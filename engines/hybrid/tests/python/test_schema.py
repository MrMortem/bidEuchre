"""Contracts shared by generated C++ and Python observation layouts."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[4]
HYBRID = ROOT / "engines/hybrid"
sys.path.insert(0, str(HYBRID / "python"))
from hybrid_training import schema


class SchemaTests(unittest.TestCase):
    def setUp(self):
        self.source = json.loads((HYBRID / "schema/observation-v1.json").read_bytes())
        spec = importlib.util.spec_from_file_location("generate_schema", HYBRID / "scripts/generate_schema.py")
        self.generator = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.generator)

    def test_fixed_contracts(self):
        self.assertEqual((schema.FEATURE_COUNT, schema.BOUNDARY_FEATURE_COUNT, schema.ACTION_COUNT, schema.OUTPUT_COUNT), (1188, 38, 61, 165))
        self.assertEqual(len(set(schema.ACTION_COMMANDS)), 61)
        self.assertEqual(schema.CARD_CODES, tuple(r+s for s in "CDHS" for r in "9TJQKA"))
        self.assertEqual(schema.ACTION_COMMANDS[60], "bestaction play AS")

    def test_blocks_cover_every_input_once(self):
        for blocks, count in [(schema.MAIN_BLOCKS, 1188), (schema.BOUNDARY_BLOCKS, 38)]:
            covered = [i for block in blocks for i in range(block["offset"], block["offset"] + block["size"])]
            self.assertEqual(covered, list(range(count)))
        self.generator.validate_schema(self.source)

    def test_duplicate_missing_or_reordered_actions_rejected(self):
        for mutation in (lambda actions: actions.__setitem__(1, actions[0]), lambda actions: actions.pop(), lambda actions: actions.reverse()):
            bad = copy.deepcopy(self.source)
            mutation(bad["actions"])
            with self.assertRaises(ValueError):
                self.generator.validate_schema(bad)

    def test_subfield_layout_gap_rejected(self):
        bad = copy.deepcopy(self.source)
        bad["layouts"]["play"]["fields"][0]["offset"] = 1
        with self.assertRaises(ValueError):
            self.generator.validate_schema(bad)

    def test_schema_and_ordered_rule_hashes(self):
        self.assertEqual(schema.SCHEMA_SHA256, hashlib.sha256((HYBRID / "schema/observation-v1.json").read_bytes()).hexdigest())
        digest = hashlib.sha256()
        expected = ["bid-euchre-final-rules.md", "src/BidEuchre.Core/Domain.cs", "src/BidEuchre.Core/GameRules.cs", "src/BidEuchre.Core/GameEngine.cs"]
        self.assertEqual(self.source["fingerprints"]["rule_paths"], expected)
        for name in expected:
            digest.update(name.encode() + b"\0" + (ROOT / name).read_bytes() + b"\0")
        self.assertEqual(schema.RULE_SHA256, digest.hexdigest())

    def test_generation_check_detects_stale_outputs_and_rules(self):
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory)
            for name in ["engines/hybrid/schema/observation-v1.json", "engines/hybrid/native/include/hybrid/schema.hpp", "engines/hybrid/python/hybrid_training/schema.py", *self.source["fingerprints"]["rule_paths"]]:
                dest = target / name
                dest.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(ROOT / name, dest)
            command = [sys.executable, str(HYBRID / "scripts/generate_schema.py"), "--root", str(target), "--check"]
            result = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            for name in ["engines/hybrid/native/include/hybrid/schema.hpp", "engines/hybrid/python/hybrid_training/schema.py", self.source["fingerprints"]["rule_paths"][0]]:
                path = target / name
                original = path.read_bytes()
                path.write_bytes(original + b"\n")
                result = subprocess.run(command, capture_output=True, text=True)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn("stale", result.stderr.lower())
                path.write_bytes(original)


if __name__ == "__main__":
    unittest.main()
