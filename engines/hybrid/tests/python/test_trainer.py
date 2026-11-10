import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import numpy as np
import torch
from hybrid_training.models import HybridNet, BoundaryNet
from hybrid_training.checkpoint import load_checkpoint
from hybrid_training.trainer import (TrainConfig, train_cycle, training_losses,
    unknown_ownership_mask, teacher_eligible, seed_for, generate_burst)
from hybrid_training.replay import ReplayStore
from test_worker import job, WORKER
from hybrid_training.worker import run_job, JobResult


def batch():
    return dict(features=np.zeros((2,1188), np.float32), legal=np.ones((2,61), bool),
                policy_target=np.eye(61,dtype=np.float32)[[1,2]], policy_weight=np.ones(2,np.float32),
                teacher_eligible=np.array([True,False]), ownership_target=np.ones((2,24),np.int64),
                ownership_mask=np.ones((2,24),bool), trick_target=np.array([2,4]),
                win_target=np.array([0,1],np.float32))

class ObjectiveTests(unittest.TestCase):
    def test_value_is_terminal_victory_and_policy_eligibility(self):
        b = batch(); b['features'][:,17:19] = [[10,-5],[-2,3]]
        raw = HybridNet()(torch.from_numpy(b['features']))
        losses = training_losses(raw,b)
        expected = torch.nn.functional.binary_cross_entropy_with_logits(raw['win_logit'].flatten(),torch.tensor([0.,1.]))
        self.assertTrue(torch.equal(expected,losses['win']))
        expected = -torch.log_softmax(raw['policy_logits'][0],-1)[1]
        self.assertTrue(torch.allclose(expected,losses['policy']))
        b['win_target'][0] = .5
        with self.assertRaisesRegex(ValueError,'terminal'):
            training_losses(raw,b)

    def test_known_own_transfer_and_played_cards_ignored(self):
        b = batch(); f=b['features']; f[:,27]=1; f[:,77]=1; f[:,100]=1
        f[:,103]=1; f[:,125]=1; f[:,204]=1; f[:,209+3]=1
        f[:,320]=1; f[:,329]=1; f[:,334+4]=1
        mask=unknown_ownership_mask(f,b['ownership_mask'])
        self.assertFalse(mask[:,:5].any()); self.assertTrue(mask[:,5:].all())
        raw=HybridNet()(torch.from_numpy(f)); loss=training_losses(raw,b)['ownership']
        altered={k:v.clone() for k,v in raw.items()}; altered['ownership_logits'][:,:20]=1000
        self.assertTrue(torch.equal(loss,training_losses(altered,b)['ownership']))

    def test_teacher_requires_complete_nonexploratory_branch(self):
        base=dict(teacher_kind='baseline',exploration_selected=False,behavior_kind='baseline')
        self.assertTrue(teacher_eligible(base))
        self.assertFalse(teacher_eligible(dict(base,exploration_selected=True)))
        self.assertFalse(teacher_eligible(dict(base,teacher_kind='none')))
        self.assertFalse(teacher_eligible(dict(base,teacher_kind='search',search={'balanced':False})))
        self.assertTrue(teacher_eligible(dict(base,teacher_kind='search',behavior_kind='search',search={'balanced':True})))

    def test_seeds_are_disjoint(self):
        seeds=[{seed_for(1,i,role) for i in range(100)} for role in ('training','tuning','screening','promotion')]
        for i,a in enumerate(seeds):
            for b in seeds[i+1:]: self.assertTrue(a.isdisjoint(b))

class TrainerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp=tempfile.TemporaryDirectory(); cls.root=Path(cls.temp.name)
        cls.replay=ReplayStore(cls.root/'replay',64<<20)
        request=job(); request['seed']=seed_for(1,0,'training')
        result=run_job(request,WORKER,cls.root/'match.jsonl')
        if result.status!='complete': raise AssertionError(result)
        cls.replay.commit_match(result.spool_path)
    @classmethod
    def tearDownClass(cls): cls.temp.cleanup()

    def test_exact_cpu_resume_next_batch_and_weights(self):
        with tempfile.TemporaryDirectory() as tmp:
            a,b=Path(tmp)/'a',Path(tmp)/'b'
            c=TrainConfig(device='cpu',duration_seconds=30,max_updates=4,batch_size=8)
            train_cycle(c,self.replay,a)
            train_cycle(TrainConfig(device='cpu',duration_seconds=30,max_updates=2,batch_size=8),self.replay,b)
            train_cycle(TrainConfig(device='cpu',duration_seconds=30,max_updates=2,batch_size=8),self.replay,b)
            x,y=load_checkpoint(a/'checkpoint.pt'),load_checkpoint(b/'checkpoint.pt')
            self.assertEqual(x['replay'],y['replay'])
            self.assertEqual(x['rng']['python'],y['rng']['python'])
            for key,value in x['main'].items(): self.assertTrue(torch.equal(value,y['main'][key]),key)
            for key,value in x['boundary'].items(): self.assertTrue(torch.equal(value,y['boundary'][key]),key)
            self.assertEqual(4,y['updates'])
            self.assertEqual(x['optimizer']['param_groups'],y['optimizer']['param_groups'])
            for key,values in x['optimizer']['state'].items():
                for name,value in values.items(): self.assertTrue(torch.equal(value,y['optimizer']['state'][key][name]))

    def test_capacity_preserves_champion(self):
        with tempfile.TemporaryDirectory() as tmp:
            output=Path(tmp); champion=output/'champion'; champion.write_bytes(b'preserve')
            for limits in (dict(max_memory_bytes=1),dict(max_disk_bytes=1)):
                result=train_cycle(TrainConfig(device='cpu',**limits),self.replay,output)
                self.assertEqual('capacity',result.status)
                self.assertEqual(b'preserve',champion.read_bytes())

    def test_generation_pending_job_restarts_same_seed(self):
        with tempfile.TemporaryDirectory() as tmp:
            output=Path(tmp); replay=ReplayStore(output/'replay',64<<20)
            config=TrainConfig(device='cpu',execution_mode='fixed_seeded',duration_seconds=10,max_updates=2)
            captured=[]
            def interrupt(request,worker,spool):
                captured.append(copy.deepcopy(request)); raise KeyboardInterrupt
            with patch('hybrid_training.trainer.run_job',side_effect=interrupt):
                self.assertEqual('interrupted',generate_burst(config,replay,output,WORKER))
            before=load_checkpoint(output/'checkpoint.pt')['pending_job']
            self.assertEqual(captured[0],before)
            self.assertEqual('complete',generate_burst(config,replay,output,WORKER))
            after=load_checkpoint(output/'checkpoint.pt')
            self.assertEqual(1,after['next_job']); self.assertIsNone(after['pending_job'])
            shard=next(p for p in replay.root.iterdir() if p.is_dir())
            self.assertEqual(before['seed'],json.loads((shard/'manifest.json').read_text())['job']['seed'])

    def test_memory_reduces_initial_batch_and_persists_partition(self):
        with tempfile.TemporaryDirectory() as tmp:
            output=Path(tmp); config=TrainConfig(device='cpu',max_memory_bytes=64<<20,max_updates=2,batch_size=64)
            # Reserve is 32 MiB, leaving transient space for exactly two rows.
            with patch('hybrid_training.trainer.memory_bytes',return_value=(32<<20)-2*65536):
                result=train_cycle(config,self.replay,output)
            self.assertEqual('complete',result.status)
            saved=load_checkpoint(output/'checkpoint.pt')
            self.assertEqual(2,saved['resources']['actual_batch_size'])
            self.assertEqual(4,saved['rows_seen'])
            with patch('hybrid_training.trainer.memory_bytes',return_value=(32<<20)-65536):
                result=train_cycle(config,self.replay,output)
            self.assertEqual('capacity',result.status)
            self.assertEqual(saved['replay'],load_checkpoint(output/'checkpoint.pt')['replay'])

    def test_promotion_seed_is_never_training_data(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); replay=ReplayStore(root/'replay',64<<20)
            source=self.root/'match.jsonl'
            rows=[json.loads(line) for line in source.read_text().splitlines()]
            rows[0]['job']['seed']=seed_for(1,0,'promotion')
            spool=root/'promotion.jsonl'; spool.write_text(''.join(json.dumps(row)+'\n' for row in rows))
            spool.with_name(spool.name+'.status.json').write_text(json.dumps(dict(version=1,status='complete',job_id=rows[0]['job']['job_id'])))
            replay.commit_match(spool)
            with self.assertRaisesRegex(ValueError,'namespace'):
                train_cycle(TrainConfig(device='cpu',max_updates=1),replay,root/'out')

    def test_interrupt_mid_step_rolls_back_cursor_rng_and_optimizer(self):
        with tempfile.TemporaryDirectory() as tmp:
            a,b=Path(tmp)/'a',Path(tmp)/'b'; config=TrainConfig(device='cpu',max_updates=1,batch_size=8)
            train_cycle(config,self.replay,a)
            train_cycle(config,self.replay,b)
            original=load_checkpoint(b/'checkpoint.pt')
            with patch('torch.optim.AdamW.step',side_effect=KeyboardInterrupt):
                result=train_cycle(config,self.replay,b)
            self.assertEqual('interrupted',result.status)
            saved=load_checkpoint(b/'checkpoint.pt')
            self.assertEqual(original['replay'],saved['replay'])
            train_cycle(config,self.replay,a); train_cycle(config,self.replay,b)
            x,y=load_checkpoint(a/'checkpoint.pt'),load_checkpoint(b/'checkpoint.pt')
            for key,value in x['main'].items(): self.assertTrue(torch.equal(value,y['main'][key]))

    def test_interrupted_burst_retains_remaining_job_count(self):
        with tempfile.TemporaryDirectory() as tmp:
            output=Path(tmp); replay=ReplayStore(output/'replay',64<<20)
            config=TrainConfig(device='cpu',execution_mode='fixed_seeded',duration_seconds=10,generation_matches=2)
            seen=[]
            def completed_fixture(request,worker,spool):
                # Exercise scheduler/packing independently of a seed's full-game length.
                rows=[json.loads(line) for line in (self.root/'match.jsonl').read_text().splitlines()]
                rows[0]['job']=request; rows[-1]['job_id']=request['job_id']
                spool.write_text(''.join(json.dumps(row)+'\n' for row in rows))
                spool.with_name(spool.name+'.status.json').write_text(json.dumps(dict(version=1,status='complete',job_id=request['job_id'])))
                return JobResult('complete',request['job_id'],spool,1)
            def stop_second(request,worker,spool):
                seen.append(request['seed'])
                if len(seen)==2: raise KeyboardInterrupt
                return completed_fixture(request,worker,spool)
            with patch('hybrid_training.trainer.run_job',side_effect=stop_second):
                self.assertEqual('interrupted',generate_burst(config,replay,output,WORKER))
            state=load_checkpoint(output/'checkpoint.pt')
            self.assertEqual(1,state['burst_remaining'])
            with patch('hybrid_training.trainer.run_job',side_effect=completed_fixture):
                self.assertEqual('complete',generate_burst(config,replay,output,WORKER))
            self.assertEqual(2,load_checkpoint(output/'checkpoint.pt')['next_job'])
            self.assertEqual(2,len([p for p in replay.root.iterdir() if p.is_dir()]))
