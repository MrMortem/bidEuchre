import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import torch
from hybrid_training.export import export_model
from hybrid_training.models import HybridNet, BoundaryNet
from hybrid_training.registry import Registry, digest, canonical

class RegistryTests(unittest.TestCase):
    def setUp(self):
        torch.set_num_threads(1)
        self.tmp=tempfile.TemporaryDirectory(); self.addCleanup(self.tmp.cleanup)
        self.root=Path(self.tmp.name); self.registry_root=self.root/'registry'; self.registry_root.mkdir()
        binary=self.root/'engine'; binary.write_bytes(b'fixture executable'); binary.chmod(0o755)
        source=self.root/'source.cpp'; source.write_text('// frozen baseline fixture')
        config={'baseline':{'executable':str(binary),'arguments':[],'source_files':[str(source)]},
                'hybrid':{'executable':str(binary),'arguments':['--decision-ms','3000','--stop-ms','3500','--max-simulations','0']},
                'arena':str(binary),'budget':{'DecisionMs':3000,'StopMs':3500,'ProcessMs':5000,'StartupMs':5000},
                'conditions':[{'Id':'baseline','PartnerId':'baseline','Opponent0Id':'baseline','Opponent1Id':'baseline'}]}
        (self.registry_root/'config.json').write_text(json.dumps(config))
        self.registry=Registry(self.registry_root)
        self.artifact=export_model(HybridNet(),BoundaryNet(),{'purpose':'registry unit fixture'},self.root/'model')
        self.candidate=self.registry.register_candidate(self.artifact)

    def test_frozen_baseline_and_canonical_candidate(self):
        self.assertEqual('baseline',self.registry.current()['kind'])
        model=json.loads(self.artifact.read_text())
        self.assertEqual(digest(canonical(model)),self.candidate)
        self.artifact.write_text(json.dumps(model,indent=2))
        self.assertEqual(self.candidate,self.registry.register_candidate(self.artifact))
        (self.artifact.parent/'weights.bin').write_bytes(b'damaged')
        self.assertEqual(model,self.registry.record(self.candidate)['artifact'])
        with self.assertRaises(ValueError): self.registry.register_candidate(self.artifact)

    def test_test_provenance_and_corruption_rejected(self):
        m=json.loads(self.artifact.read_text()); m['provenance']['metadata']['test_only']=True
        self.artifact.write_text(json.dumps(m))
        with self.assertRaises(ValueError): self.registry.register_candidate(self.artifact)
        path=self.registry_root/'candidates'/self.candidate/'weights.bin'; path.write_bytes(b'bad')
        with self.assertRaises(ValueError): self.registry.record(self.candidate)

    def test_atomic_pointer_and_rollback_history(self):
        original=self.registry.current()['id']
        with patch('hybrid_training.registry.os.replace',side_effect=OSError('crash before rename')):
            with self.assertRaises(OSError): self.registry._point(self.candidate,'synthetic test pointer')
        self.assertEqual(original,self.registry.current()['id'])
        original_fsync=__import__('os').fsync
        def interrupted_directory(fd):
            import os, stat
            if stat.S_ISDIR(os.fstat(fd).st_mode) and (self.registry_root/'champion.json').exists() and json.loads((self.registry_root/'champion.json').read_text())['id']==self.candidate:
                raise OSError('crash after pointer rename')
            original_fsync(fd)
        with patch('hybrid_training.registry.os.fsync',side_effect=interrupted_directory):
            with self.assertRaises(OSError): self.registry._point(self.candidate,'synthetic post-rename failure')
        self.assertEqual(self.candidate,self.registry.current()['id'])
        self.registry._point(self.candidate,'synthetic test pointer')
        self.registry.rollback(original)
        self.assertEqual(original,self.registry.current()['id'])
        self.assertGreaterEqual(len(list((self.registry_root/'history').glob('*.json'))),3)
        with self.assertRaises(ValueError): self.registry.rollback('missing')

    def test_references_publish_only_after_copied_files_and_directories_are_durable(self):
        import os
        other=(self.root/'durable-registry').resolve();other.mkdir()
        (other/'config.json').write_bytes((self.registry_root/'config.json').read_bytes())
        events=[]; real_fsync=os.fsync;real_replace=os.replace;real_rename=Path.rename
        def synced(fd):
            info=os.fstat(fd)
            for path in [other,*other.rglob('*')]:
                if path.exists() and (path.stat().st_dev,path.stat().st_ino)==(info.st_dev,info.st_ino):
                    events.append(('sync',str(path.relative_to(other))));break
            return real_fsync(fd)
        def replaced(source,target):
            events.append(('publish',str(Path(target).relative_to(other))))
            return real_replace(source,target)
        def renamed(source,target):
            events.append(('rename',str(source.relative_to(other)),str(target.relative_to(other))))
            return real_rename(source,target)
        with patch('hybrid_training.registry.os.fsync',side_effect=synced), patch('hybrid_training.registry.os.replace',side_effect=replaced), patch.object(Path,'rename',renamed):
            registry=Registry(other)
            initial=list(events)
            candidate=registry.register_candidate(self.artifact)
            published=list(events)
            registry._point(candidate,'test-only durability pointer')
        freeze=initial.index(('publish','frozen.json'))
        baseline=registry.frozen['baseline']
        binary=Path(baseline['engine']['Executable']).relative_to(other)
        self.assertIn(('sync',str(binary)),initial[:freeze])
        self.assertIn(('sync',str(binary.parent)),initial[:freeze])
        self.assertIn(('sync','binaries'),initial[:freeze])
        for sha in baseline['source_files'].values(): self.assertIn(('sync','sources/'+sha),initial[:freeze])
        self.assertIn(('sync','sources'),initial[:freeze])
        rename=next(i for i,event in enumerate(published) if event[0]=='rename')
        temporary=published[rename][1]
        self.assertIn(('sync',temporary+'/weights.bin'),published[:rename])
        self.assertIn(('sync',temporary),published[:rename])
        self.assertIn(('sync','candidates'),published[rename+1:])
        pointer=max(i for i,event in enumerate(events) if event==('publish','champion.json'))
        self.assertLess(rename,pointer)
        self.assertIn(('sync','candidates'),events[rename+1:pointer])

    def test_failed_artifact_sync_cannot_publish_and_retry_finishes_durability(self):
        import os
        real_fsync=os.fsync
        candidate_root=self.registry_root/'candidates'
        artifact=export_model(HybridNet(),BoundaryNet(),{'purpose':'durability crash test'},self.root/'crash-model')
        identity=digest(canonical(json.loads(artifact.read_text())))
        old=self.registry.current()['id']
        def fail_weights(fd):
            info=os.fstat(fd)
            if any(path.stat().st_ino==info.st_ino for path in candidate_root.glob('.candidate-*/weights.bin')):
                raise OSError('injected weights sync failure')
            real_fsync(fd)
        with patch('hybrid_training.registry.os.fsync',side_effect=fail_weights):
            with self.assertRaisesRegex(OSError,'weights sync'):self.registry.register_candidate(artifact)
        self.assertFalse((candidate_root/identity).exists());self.assertEqual(old,self.registry.current()['id'])
        def fail_rename_commit(fd):
            if (candidate_root/identity).exists() and os.fstat(fd).st_ino==candidate_root.stat().st_ino:
                raise OSError('injected directory sync failure')
            real_fsync(fd)
        with patch('hybrid_training.registry.os.fsync',side_effect=fail_rename_commit):
            with self.assertRaisesRegex(OSError,'directory sync'):self.registry.register_candidate(artifact)
        self.assertEqual(old,self.registry.current()['id'])
        # A retry must sync a complete-but-not-yet-durable existing directory too.
        with patch('hybrid_training.registry.os.fsync',side_effect=fail_rename_commit):
            with self.assertRaisesRegex(OSError,'directory sync'):self.registry.register_candidate(artifact)
        self.assertEqual(identity,self.registry.register_candidate(artifact))
        self.assertEqual(old,self.registry.current()['id'])

    def test_atomic_launch_descriptor_matches_complete_frozen_engine(self):
        self.registry._point(self.candidate,'unit launch descriptor')
        pointer=json.loads((self.registry_root/'champion.json').read_text())
        self.assertEqual(self.registry.current()['engine'],pointer['engine'])
        self.assertEqual('root',self.registry.record(self.candidate)['artifact']['search_mode'])
        self.assertEqual('policy',self.registry.record(self.candidate)['artifact']['leaf_mode'])

    def test_mode_mismatch_rejected_before_publication(self):
        m=json.loads(self.artifact.read_text());m['search_mode']='information-tree'
        self.artifact.write_text(json.dumps(m))
        with self.assertRaisesRegex(ValueError,'mode'):self.registry.register_candidate(self.artifact)

    def test_native_registry_launch_exact_arguments_cwd_and_hash(self):
        import subprocess,hashlib,os
        native=Path(__file__).resolve().parents[2]/'build/bideuchre-hybrid'
        script=self.root/'probe';script.write_text('#!/bin/sh\nprintf "cwd=%s\\n" "$PWD"\nprintf "arg=%s\\n" "$@"\n');script.chmod(0o755)
        descriptor={'Executable':str(script),'ExecutableSha256':hashlib.sha256(script.read_bytes()).hexdigest(),
                    'WorkingDirectory':str(self.root),'Arguments':['--seed','731','--max-simulations','7','literal space']}
        (self.root/'champion.json').write_text(json.dumps({'id':'fixture','engine':descriptor}))
        result=subprocess.run([native,'--registry',self.root],capture_output=True,text=True)
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertEqual(['cwd='+str(self.root.resolve()),*['arg='+x for x in descriptor['Arguments']]],result.stdout.splitlines())
        result=subprocess.run([native,'--registry',self.root,'--seed','9'],capture_output=True,text=True)
        self.assertNotEqual(0,result.returncode)
        script.write_text(script.read_text()+'# changed\n')
        result=subprocess.run([native,'--registry',self.root],capture_output=True,text=True)
        self.assertNotEqual(0,result.returncode);self.assertIn('hash',result.stderr)

    def test_native_registry_keeps_loaded_a_after_pointer_b(self):
        import subprocess
        binary=Path(__file__).resolve().parents[2]/'build/bideuchre-hybrid'
        config=json.loads((self.registry_root/'config.json').read_text())
        config['hybrid']['executable']=str(binary)
        config['hybrid']['arguments']=['--seed','731','--max-simulations','0','--search-mode','root','--leaf-mode','policy']
        root=self.root/'live-registry';root.mkdir();(root/'config.json').write_text(json.dumps(config))
        registry=Registry(root);a=registry.register_candidate(self.artifact);registry._point(a,'unit A')
        process=subprocess.Popen([binary,'--registry',root],stdin=subprocess.PIPE,stdout=subprocess.PIPE,text=True)
        def greeting():
            process.stdin.write('beuci\n');process.stdin.flush();lines=[]
            while True:
                line=process.stdout.readline();self.assertTrue(line);lines.append(line)
                if line.strip()=='beuciok':return ''.join(lines)
        try:
            sha=registry.current()['artifact']['model_sha256'];self.assertIn(sha,greeting())
            other=export_model(HybridNet(),BoundaryNet(),{'purpose':'B'},self.root/'B')
            b=registry.register_candidate(other);registry._point(b,'unit B')
            self.assertEqual(b,registry.current()['id']);self.assertIn(sha,greeting())
            process.communicate('quit\n',timeout=3);self.assertEqual(0,process.returncode)
        finally:
            if process.poll() is None:process.kill();process.wait()
            process.stdin.close();process.stdout.close()
