"""Mutation checks against real completed evidence; run after Run-Research.ps1."""

import copy
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path

from summarize import load_run


ROOT = Path(sys.argv.pop(1)) if len(sys.argv) > 1 else Path("artifacts/local/router-research/qualified")


class EvidenceIntegrityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.original = json.loads((ROOT / "round1/run.json").read_text(encoding="utf-8"))
        cls.scratch = tempfile.TemporaryDirectory(prefix="router-integrity-", dir=ROOT.parent)
        cls.folder = Path(cls.scratch.name)
        for source in (ROOT / "round1").iterdir():
            if source.name != "run.json" and source.is_file():
                # Only manifests are mutated. Hard-linked fixture bytes remain read-only here.
                os.link(source, cls.folder / source.name)

    @classmethod
    def tearDownClass(cls):
        cls.scratch.cleanup()

    def check_mutation(self, mutate, pattern):
        run = copy.deepcopy(self.original)
        mutate(run)
        (self.folder / "run.json").write_text(json.dumps(run), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, pattern):
            load_run(self.folder, False)

    def test_completed_run_is_accepted(self):
        load_run(ROOT / "round1", False)

    def test_duplicate_sample_is_rejected(self):
        self.check_mutation(lambda run: run["Samples"].append(run["Samples"][0]), "Duplicate sample")

    def test_missing_cpu_control_is_rejected(self):
        def mutate(run):
            run["Samples"] = [sample for sample in run["Samples"] if sample["Variant"] != "parallel-all"]
        self.check_mutation(mutate, "Missing or extra samples")

    def test_changed_fixture_hash_is_rejected(self):
        self.check_mutation(lambda run: run["Cases"][0].update(PacketSha256="0" * 64), "Fixture changed")

    def test_missing_correctness_check_is_rejected(self):
        def mutate(run):
            run["Checks"] = [check for check in run["Checks"]
                             if not (check["Case"] == run["Cases"][0]["Id"] and check["Variant"] == "simd")]
        self.check_mutation(mutate, "Missing full output check")

    def test_invalid_timing_is_rejected(self):
        self.check_mutation(lambda run: run["Samples"][0].update(WallMicroseconds=-1), "Invalid wall sample")

    def test_changed_experiment_coordinate_is_rejected(self):
        self.check_mutation(lambda run: run["Cases"][0].update(Id="replacement"), "Changed factorial coordinates")


if __name__ == "__main__":
    unittest.main()
