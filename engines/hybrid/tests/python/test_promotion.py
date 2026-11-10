import copy
import json
from unittest.mock import patch
import test_registry
from hybrid_training.registry import canonical, digest, file_hash, publish, read
from hybrid_training import schema
from hybrid_training.promotion import create_evaluation, validate_gate

class PromotionTests(test_registry.RegistryTests):
    def seed_manifest(self,mode='screening',run_seed=100):
        path=self.root/(mode+'.json')
        path.write_text(json.dumps({'registry_root':str(self.registry_root),'run_seed':run_seed,'start_index':0,
            'primary_blocks':1000 if mode=='promotion' else 1,'replacement_blocks':500 if mode=='promotion' else 1,'bootstrap_seed':7}))
        return path

    def test_seed_reservation_resume_namespace_and_int32(self):
        source=self.seed_manifest('promotion')
        path=create_evaluation(self.candidate,self.registry.current()['id'],source,'promotion')
        frozen=json.loads(path.read_text()); seeds=[x['Seed'] for x in frozen['Legs'][::2]]
        self.assertEqual(1500,len(set(seeds))); self.assertTrue(all(s%4==3 for s in seeds))
        with self.assertRaises(ValueError):create_evaluation(self.candidate,self.registry.current()['id'],source,'promotion')
        screen=create_evaluation(self.candidate,self.registry.current()['id'],self.seed_manifest(),'screening')
        self.assertTrue(all(x['Seed']%4==2 for x in json.loads(screen.read_text())['Legs']))
        with self.assertRaises(ValueError):create_evaluation(self.candidate,self.registry.current()['id'],self.seed_manifest('screening',2**31),'screening')
        self.assertEqual('baseline',self.registry.current()['kind'])

    @staticmethod
    def passing():
        return {'Gate':1,'CanPromote':True,'Complete':True,'RegisteredLegs':3000,'CompletedLegs':3000,
            'RegisteredPrimaryBlocks':1000,'CompletedPrimaryBlocks':1000,'RegisteredReplacementBlocks':500,
            'CompletedReplacementBlocks':500,'PrimaryLower95':.51,'ReplacementLower95':-.019,'GateErrors':[],
            'Conditions':[{'Id':'baseline','RegisteredBlocks':500,'CompletedBlocks':500}]}

    def test_strict_gate_rejects_thresholds_smoke_incomplete_errors(self):
        validate_gate(self.passing())
        for key,value in [('Gate',0),('Complete',False),('CompletedLegs',2999),('PrimaryLower95',.50),
                          ('ReplacementLower95',-.02),('GateErrors',['fallback']),('CanPromote',False),
                          ('RegisteredPrimaryBlocks',999),('CompletedReplacementBlocks',499),('PrimaryLower95',float('nan'))]:
            with self.subTest(key=key):
                r=self.passing();r[key]=value
                with self.assertRaises(ValueError):validate_gate(r)

    def test_untrusted_report_never_changes_champion(self):
        original=self.registry.current()['id']; report=self.root/'report.json';report.write_text(json.dumps(self.passing()))
        verification=self.root/'verification.json'; verification.write_text('{}')
        with self.assertRaises(ValueError): self.registry.promote(self.candidate,report,verification)
        self.assertEqual(original,self.registry.current()['id'])
        self.assertTrue(list((self.registry_root/'evidence').glob('*.json')))

    def prepare_verified_trial(self, synthetic_at=None, run_seed=100):
        candidate=self.registry.record(self.candidate)
        artifact=candidate['artifact']
        bindings={'candidate_id':self.candidate,'model_sha256':artifact['model_sha256'],'manifest_sha256':self.candidate,
            'schema_sha256':schema.SCHEMA_SHA256,'rule_sha256':schema.RULE_SHA256,'source_sha256':artifact['source_sha256'],
            'native_source':self.registry.frozen['source'],'binary_sha256':candidate['engine']['ExecutableSha256'],
            'authority':self.registry.frozen['authority']}
        checks={}
        for name in ('privacy','simulator','export'):
            log=self.root/(name+'.log'); log.write_text('synthetic test-only verification fixture')
            checks[name]={'passed':True,'path':str(log),'sha256':file_hash(log)}
        content={'bindings':bindings,'checks':checks}
        if synthetic_at=='report':content['synthetic']=True
        elif synthetic_at is not None:checks[synthetic_at]['synthetic']=True
        verification=self.root/'verification.json';publish(verification,content)
        source=self.seed_manifest('promotion'); seeds=read(source);seeds['run_seed']=run_seed;seeds['verification_report']=str(verification);publish(source,seeds)
        manifest=create_evaluation(self.candidate,self.registry.current()['id'],source,'promotion')
        report=manifest.parent/'run/report.json';report.parent.mkdir()
        publish(report,self.passing())
        return manifest,report,verification

    def test_synthetic_authority_seam_passes_only_bound_complete_evidence(self):
        # Only the subprocess authority is stubbed: production has no synthetic
        # flag/bypass. Registry, verification, schedules, hashes and reservations
        # execute unchanged. This fixture is never registered as production play.
        manifest,report,verification=self.prepare_verified_trial()
        original=self.registry.current()['id']
        descriptor=read(manifest.parent/'evaluation.json')
        changed=dict(descriptor,verification_sha256='0'*64);publish(manifest.parent/'evaluation.json',changed)
        with self.assertRaisesRegex(ValueError,'frozen evaluation differs'):self.registry.promote(self.candidate,report,verification)
        publish(manifest.parent/'evaluation.json',descriptor)
        def synthetic_audit(*args,**kwargs):
            publish(report.parent/'manifest.json',read(manifest))
            result=self.passing();result['ManifestFingerprint']=file_hash(report.parent/'manifest.json');publish(report,result)
            return report
        with patch('hybrid_training.promotion._arena',side_effect=synthetic_audit) as audit:
            saved=verification.read_bytes()
            for mutation in ('model','privacy','parity','source','binary','test'):
                broken=json.loads(saved)
                if mutation=='model':broken['bindings']['model_sha256']='0'*64
                if mutation=='source':broken['bindings']['source_sha256']='0'*64
                if mutation=='binary':broken['bindings']['binary_sha256']='0'*64
                if mutation=='privacy':broken['checks']['privacy']['passed']=False
                if mutation=='parity':broken['checks']['export']['passed']=False
                if mutation=='test':broken['test_only']=True
                publish(verification,broken)
                with self.assertRaises(ValueError):self.registry.promote(self.candidate,report,verification)
                self.assertEqual(original,self.registry.current()['id'])
            verification.write_bytes(saved)
            (self.root/'privacy.log').write_text('tampered')
            with self.assertRaises(ValueError):self.registry.promote(self.candidate,report,verification)
            (self.root/'privacy.log').write_text('synthetic test-only verification fixture')
            self.registry.promote(self.candidate,report,verification)
            self.assertEqual(self.candidate,self.registry.current()['id']);self.assertEqual(1,audit.call_count)
            self.registry.rollback(original)
            with self.assertRaises(ValueError):self.registry.promote(self.candidate,report,verification)
            self.assertEqual(original,self.registry.current()['id'])

    def test_live_process_keeps_loaded_model_after_pointer_change(self):
        import base64, subprocess
        from pathlib import Path
        from hybrid_training.export import export_model
        from hybrid_training.models import HybridNet,BoundaryNet
        binary=Path(__file__).resolve().parents[2]/'build/bideuchre-hybrid'
        original=self.registry.record(self.candidate)
        self.registry._point(self.candidate,'test-only startup pointer')
        engine=subprocess.Popen([str(binary),'--model',original['manifest'],'--max-simulations','0'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,text=True)
        try:
            engine.stdin.write('beuci\n');engine.stdin.flush()
            greeting=[]
            while True:
                line=engine.stdout.readline();greeting.append(line)
                if line.strip()=='beuciok':break
            self.assertIn(original['artifact']['model_sha256'],''.join(greeting))
            other=export_model(HybridNet(),BoundaryNet(),{'purpose':'second test model'},self.root/'second-model')
            new_id=self.registry.register_candidate(other)
            self.registry._point(new_id,'test-only pointer swap')
            fixtures=Path(__file__).resolve().parents[4]/'tests/fixtures/hybrid/authority-v1.jsonl'
            game=json.loads(fixtures.read_text().splitlines()[0])['expectedInitial']['privateViews'][0]
            game.pop('legal');game.pop('handResults');game['events']=[]
            suits=['Clubs','Diamonds','Hearts','Spades']; ranks=['Nine','Ten','Jack','Queen','King','Ace']
            game['players'][0]['cards']=[{'suit':suits[x//6],'rank':ranks[x%6],'code':'9TJQKA'[x%6]+'CDHS'[x//6]} for x in game['players'][0]['cards']]
            game['legalActions']={'canPass':True,'bids':['Three','Four','Five','Six','PartnersBest','Alone'],'contractModes':[],'trumpSuits':[],'cards':[]}
            payload=base64.urlsafe_b64encode(json.dumps({'seat':0,'game':game}).encode()).decode().rstrip('=')
            output,_=engine.communicate('position '+payload+'\ngo\nquit\n',timeout=10)
            self.assertIn('backend=hybrid-policy',output)
            self.assertIn('model_sha256='+original['artifact']['model_sha256'],output)
            self.assertNotIn('model_sha256='+read(other)['model_sha256'],output)
        finally:
            if engine.poll() is None: engine.kill();engine.wait()
            engine.stdin.close();engine.stdout.close()

    def test_capacity_before_launch_and_owned_process_group_cleanup(self):
        import os,sys
        from hybrid_training.registry import Registry
        from hybrid_training.promotion import evaluate_and_report
        arena=self.root/'fake-arena'
        child_pid=self.root/'child.pid'
        arena.write_text('#!/usr/bin/env python3\nimport subprocess,sys,signal,time\n'
            + 'child=subprocess.Popen([sys.executable,"-c","import time;time.sleep(30)"])\n'
            + 'open('+repr(str(child_pid))+',"w").write(str(child.pid))\n'
            + 'try: time.sleep(30)\nfinally: child.wait(timeout=2)\n')
        arena.chmod(0o755)
        config=read(self.registry_root/'config.json');config['arena']=str(arena)
        other_root=self.root/'resource-registry';other_root.mkdir();publish(other_root/'config.json',config)
        registry=Registry(other_root);candidate=registry.register_candidate(self.artifact);old=registry.current()['id']
        source=self.seed_manifest();data=read(source);data['registry_root']=str(other_root);publish(source,data)
        manifest=create_evaluation(candidate,old,source,'screening')
        with patch('hybrid_training.promotion.resource_usage',return_value=(3<<30,0)):
            with self.assertRaisesRegex(ValueError,'before launch'):evaluate_and_report(manifest,arena,1)
        self.assertFalse(child_pid.exists());self.assertEqual('capacity',read(manifest.parent/'invocation.json')['status'])
        with patch('hybrid_training.promotion.resource_usage',side_effect=lambda *_: ((3<<30) if child_pid.exists() else 0,0)):
            with self.assertRaisesRegex(ValueError,'resource capacity'):evaluate_and_report(manifest,arena,1)
        self.assertEqual('resource capacity',read(manifest.parent/'invocation.json')['reason'])
        self.assertEqual(old,registry.current()['id'])
        self.assertTrue(child_pid.exists())
        with self.assertRaises(ProcessLookupError):os.kill(int(child_pid.read_text()),0)

    def test_frozen_synthetic_report_and_each_check_are_rejected(self):
        old=self.registry.current()['id']
        for index,location in enumerate(('report','privacy','simulator','export')):
            with self.subTest(location=location):
                manifest,report,verification=self.prepare_verified_trial(location,100+index*2000)
                descriptor=read(manifest.parent/'evaluation.json')
                self.assertEqual(file_hash(verification),descriptor['verification_sha256'])
                with patch('hybrid_training.promotion._arena',side_effect=AssertionError('synthetic verification reached authority audit')) as audit:
                    with self.assertRaisesRegex(ValueError,'test provenance'):self.registry.promote(self.candidate,report,verification)
                    audit.assert_not_called()
                self.assertEqual(old,self.registry.current()['id'])
