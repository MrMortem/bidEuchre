"""Trainable shapes and normalization are part of the frozen contract."""
import sys
from pathlib import Path
import unittest
ROOT = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(ROOT / 'engines/hybrid/python'))
import torch
from hybrid_training.models import HybridNet, BoundaryNet, normalize_output

class ModelTests(unittest.TestCase):
    def test_shapes_and_gradients(self):
        model = HybridNet()
        result = model(torch.zeros(3, 1188))
        self.assertEqual({k: tuple(v.shape) for k,v in result.items()},
                         {'policy_logits': (3,61), 'win_logit': (3,1), 'trick_logits': (3,7), 'ownership_logits': (3,96)})
        sum(v.sum() for v in result.values()).backward()
        self.assertTrue(all(p.grad is not None for p in model.parameters()))
        self.assertEqual(tuple(BoundaryNet()(torch.zeros(3,38)).shape), (3,1))
        for model, width in [(model,1187),(BoundaryNet(),39)]:
            with self.assertRaises(ValueError): model(torch.zeros(2,width))

    def test_mask_and_stable_normalization(self):
        raw = {'policy_logits': torch.full((2,61),10000.), 'win_logit': torch.tensor([[10000.],[-10000.]]),
               'trick_logits': torch.zeros(2,7), 'ownership_logits': torch.zeros(2,96)}
        legal = torch.zeros(2,61,dtype=torch.bool); legal[0,[1,4]]=True
        p = normalize_output(raw,legal)
        self.assertEqual(float(p['policy'][0,0]),0.)
        self.assertEqual(float(p['policy'][0,1]),.5)
        self.assertEqual(float(p['policy'][1].sum()),0.)
        torch.testing.assert_close(p['trick'].sum(-1),torch.ones(2))
        torch.testing.assert_close(p['ownership'].reshape(2,24,4).sum(-1),torch.ones(2,24))
        self.assertEqual(p['win'].tolist(),[[1.],[0.]])
        raw['policy_logits'][0,0]=float('nan')
        with self.assertRaises(ValueError): normalize_output(raw,legal)
