"""The deployed C++ loader, not a Python substitute, checks every candidate."""
import copy
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest
import numpy as np
import torch
ROOT = Path(__file__).resolve().parents[4]
HYBRID = ROOT / 'engines/hybrid'
sys.path.insert(0,str(HYBRID/'python'))
from hybrid_training.models import HybridNet, BoundaryNet, normalize_output
from hybrid_training.export import export_model, validate_artifact
CHECK = HYBRID/'build/bideuchre-hybrid-model-check'

class ExportTests(unittest.TestCase):
    def setUp(self):
        torch.manual_seed(712)
        torch.set_num_threads(1)
        self.main, self.boundary = HybridNet().eval(), BoundaryNet().eval()
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.path = export_model(self.main,self.boundary,{'purpose':'unit golden'},Path(self.tmp.name)/'candidate')

    def native(self, records, manifest=None):
        inputs = Path(self.tmp.name)/'inputs.jsonl'
        inputs.write_text(''.join(json.dumps(r)+'\n' for r in records))
        return subprocess.run([str(CHECK),'--manifest',str(manifest or self.path),'--inputs',str(inputs)],text=True,capture_output=True)

    def test_every_phase_python_native_raw_and_normalized_golden(self):
        fixtures = [json.loads(line) for line in (ROOT/'tests/fixtures/hybrid/authority-v1.jsonl').read_text().splitlines()]
        # Rare traces include both exchanges and match end; random held-out traces
        # ensure inputs were not designed around this seeded network.
        selected = [f for f in fixtures if not f['id'].startswith('random-')] + [f for f in fixtures if f['id'].startswith('random-')][:2]
        features = [0.]*1188; features[0]=1.
        result = self.native([{'features':features,'legal':[False]*61,'boundary_features':[0.]*38}] + [{'fixture':f} for f in selected])
        self.assertEqual(result.returncode,0,result.stderr)
        phases=set(); count=0; largest=0.
        for line in result.stdout.splitlines():
            row=json.loads(line); phases.add(row['phase']); count+=1
            x=torch.tensor(row['features'],dtype=torch.float32).unsqueeze(0)
            with torch.no_grad():
                raw=self.main(x); norm=normalize_output(raw,torch.tensor([row['legal']],dtype=torch.bool))
                boundary=self.boundary(torch.tensor([row['boundary_features']],dtype=torch.float32))
            for name,value in raw.items():
                expected=value.numpy().reshape(-1); actual=np.array(row[name]).reshape(-1)
                np.testing.assert_allclose(actual,expected,atol=1e-5,rtol=1e-5)
                largest=max(largest,float(np.max(np.abs(actual-expected))))
            for name,value in norm.items(): np.testing.assert_allclose(row[name],value.numpy().reshape(-1),atol=1e-5,rtol=1e-5)
            np.testing.assert_allclose(row['boundary_logit'],boundary.numpy().reshape(-1),atol=1e-5,rtol=1e-5)
            self.assertTrue(all(p==0 for p,legal in zip(row['policy'],row['legal']) if not legal))
        self.assertEqual(phases,{'NotStarted','Bidding','ChoosingContract','ExchangingBidderCard','ExchangingPartnerCard','Playing','HandComplete','GameComplete'})
        self.assertGreater(count,100)
        print(f'Golden parity: {count} observations, 8 phases, max raw absolute error {largest:.3g}')

    def test_corruptions_rejected_in_both_loaders(self):
        original=json.loads(self.path.read_text()); weights=self.path.parent/'weights.bin'; saved=weights.read_bytes()
        mutations=[('version',lambda m:m.__setitem__('version',2)),
            ('boolean version',lambda m:m.__setitem__('version',True)),
            ('schema',lambda m:m.__setitem__('schema_sha256','0'*64)),
            ('rules',lambda m:m.__setitem__('rule_sha256','0'*64)),
            ('hash',lambda m:m.__setitem__('model_sha256','0'*64)),
            ('byte order',lambda m:m.__setitem__('byte_order','big')),
            ('shape',lambda m:m['tensors'][0].__setitem__('shape',[256,1187])),
            ('offset',lambda m:m['tensors'][1].__setitem__('offset',0)),
            ('activation',lambda m:m['tensors'][0].__setitem__('activation','sigmoid')),
            ('source',lambda m:m['provenance'].__setitem__('git_revision','bad'))]
        for name,mutation in mutations:
            with self.subTest(name=name):
                m=copy.deepcopy(original);mutation(m);self.path.write_text(json.dumps(m))
                with self.assertRaises(ValueError):validate_artifact(self.path)
                result=self.native([]);self.assertNotEqual(result.returncode,0,name)
        self.path.write_text(json.dumps(original))
        for name,data in [('truncated',saved[:-4]),('NaN',struct.pack('<f',float('nan'))+saved[4:]),('Inf',struct.pack('<f',float('inf'))+saved[4:])]:
            with self.subTest(name=name):
                m=copy.deepcopy(original);m['model_sha256']=hashlib.sha256(data).hexdigest();weights.write_bytes(data);self.path.write_text(json.dumps(m))
                with self.assertRaises(ValueError):validate_artifact(self.path)
                self.assertNotEqual(self.native([]).returncode,0)
        weights.write_bytes(saved);self.path.write_text(json.dumps(original))

    def test_manifest_size_matches_native_limit(self):
        destination=Path(self.tmp.name)/'large'
        with self.assertRaises(ValueError):export_model(self.main,self.boundary,{'text':'x'*65536},destination)
        self.assertFalse(destination.exists())

    def test_explicit_row_major_little_endian_bytes(self):
        manifest=validate_artifact(self.path)
        data=(self.path.parent/'weights.bin').read_bytes()
        state={**{'main.'+k:v for k,v in self.main.state_dict().items()},**{'boundary.'+k:v for k,v in self.boundary.state_dict().items()}}
        for tensor in manifest['tensors']:
            self.assertEqual(data[tensor['offset']:tensor['offset']+tensor['byte_length']],state[tensor['name']].numpy().astype('<f4').tobytes(order='C'))

    def test_export_is_atomic_and_rejects_invalid_weights(self):
        with self.assertRaises(FileExistsError):export_model(self.main,self.boundary,{},self.path.parent)
        with torch.no_grad(): self.main.fc1.weight[0,0]=float('nan')
        destination=Path(self.tmp.name)/'invalid'
        with self.assertRaises(ValueError):export_model(self.main,self.boundary,{},destination)
        self.assertFalse(destination.exists())
        self.assertFalse(list(Path(self.tmp.name).glob('.invalid-*')))

    def test_nonfinite_inference_fails_without_emitting_scores(self):
        x=[0.]*1188; x[0]=3.4e38
        # Finite but overflowing tensors must load; inference must fail cleanly.
        with torch.no_grad(): self.main.fc1.weight.fill_(3.4e38)
        manifest=export_model(self.main,self.boundary,{},Path(self.tmp.name)/'overflow')
        result=self.native([{'features':x,'legal':[True]*61,'boundary_features':[0.]*38}],manifest)
        self.assertNotEqual(result.returncode,0)
        self.assertEqual(result.stdout,'')
        self.assertIn('nonfinite',result.stderr)
