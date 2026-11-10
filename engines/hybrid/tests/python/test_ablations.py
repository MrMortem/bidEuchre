import json
from pathlib import Path
import tempfile
import unittest
import torch
from hybrid_training.export import export_model, validate_artifact
from hybrid_training.models import HybridNet, BoundaryNet
from hybrid_training.ablations import make_ablation_manifests
class AblationTests(unittest.TestCase):
 def test_runtime_equal_budgets(self):
  with tempfile.TemporaryDirectory() as d:
   torch.set_num_threads(1)
   artifact=export_model(HybridNet(),BoundaryNet(),{},Path(d)/'model')
   base={'artifact':str(artifact),'seed':731,'decision_ms':200,'stop_ms':250,'max_simulations':7}
   paths=make_ablation_manifests(base,Path(d)/'arms')
   arms=[json.loads(p.read_text()) for p in paths]
   self.assertEqual(6,len(arms))
   for a in arms:
    for key,value in base.items():
     if key!='artifact':self.assertEqual(value,a[key])
    frozen=validate_artifact(a['artifact'])
    self.assertEqual(validate_artifact(artifact)['model_sha256'],frozen['model_sha256'])
    self.assertEqual(a['search_mode'],frozen['search_mode']);self.assertEqual(a['leaf_mode'],frozen['leaf_mode'])
    self.assertEqual('runtime',a['ablation_kind']);self.assertFalse(a['can_promote'])
    self.assertIn(a['search_mode'],('root','information-tree'));self.assertIn(a['leaf_mode'],('policy','double-dummy'))
   with self.assertRaises(FileExistsError):make_ablation_manifests(base,Path(d)/'arms')
