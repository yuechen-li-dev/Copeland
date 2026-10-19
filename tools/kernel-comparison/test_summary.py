"""Negative artifact-integrity checks; synthetic timings are unit-test data only."""

import contextlib
import hashlib
import io
import json
import tempfile
import unittest
from pathlib import Path

from summarize import summarize


class SummaryIntegrityTests(unittest.TestCase):
    def setUp(self):
        base = Path(__file__).resolve().parents[2] / "artifacts" / "local" / "kernel-comparison"
        base.mkdir(parents=True, exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(prefix="unit-test-", dir=base)
        self.root = Path(self.temporary.name).resolve()
        assert self.root.is_relative_to(base.resolve())
        (self.root / "fixtures").mkdir()
        manifest = dict(samples=2, distributions=["normal", "shifted"], cases=[
            dict(id="timed", family="bias_silu", split="timed"),
            dict(id="heldout", family="bias_silu", split="heldout-shape"),
        ])
        manifest_path = self.root / "fixtures" / "manifest.json"
        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
        manifest_hash = hashlib.sha256(manifest_path.read_bytes()).hexdigest()
        (self.root / "protocol.json").write_text(json.dumps(dict(runs=1, sourceSha256={})), encoding="utf-8")
        for backend, variants in [
            ("vulkan", ["existing-unfused", "sol-fused-specialized", "sol-prepared-blocking", "sol-prepared"]),
            ("cuda", ["torch-eager", "torch-native", "torch-compile-default", "torch-compile-max-autotune", "sol-triton"]),
        ]:
            folder = self.root / "run1" / backend
            folder.mkdir(parents=True)
            measurements = []
            checks = []
            for variant in variants:
                milliseconds = 2.0 if variant == "torch-eager" else 1.0
                for sample in range(2):
                    measurements.append(dict(case="timed", variant=variant, sample=sample,
                        wallMilliseconds=milliseconds, gpuMilliseconds=milliseconds, repeatedGpuMilliseconds=milliseconds))
                modes = [(False, False)] if backend == "vulkan" else [(False, False), (True, False), (False, True)]
                for case in ("timed", "heldout"):
                    for distribution in manifest["distributions"]:
                        for reused, graph in modes:
                            checks.append(dict(case=case, variant=variant, distribution=distribution,
                                reusedAddresses=reused, graphReplay=graph, maximumToleranceRatio=.25))
            evidence = dict(accepted=True, manifestSha256=manifest_hash, device="synthetic-unit-test",
                            measurements=measurements, checks=checks)
            (folder / f"{backend}.json").write_text(json.dumps(evidence), encoding="utf-8")

    def tearDown(self):
        self.temporary.cleanup()

    def run_summary(self):
        with contextlib.redirect_stdout(io.StringIO()):
            summarize(self.root)

    def change(self, mutate):
        path = self.root / "run1" / "cuda" / "cuda.json"
        evidence = json.loads(path.read_text())
        mutate(evidence)
        path.write_text(json.dumps(evidence), encoding="utf-8")

    def assert_rejected(self):
        with self.assertRaises((RuntimeError, ValueError)):
            self.run_summary()
        self.assertFalse(json.loads((self.root / "summary.json").read_text())["accepted"])

    def test_complete_grid_reports_known_ratio(self):
        self.run_summary()
        summary = json.loads((self.root / "summary.json").read_text())
        self.assertTrue(summary["accepted"])
        effect = next(item for item in summary["effects"]
                      if item["candidate"] == "sol-prepared" and item["baseline"] == "torch-eager")
        self.assertEqual(2, effect["geometricMeanSpeedup"])

    def test_duplicate_cannot_replace_a_missing_sample(self):
        self.change(lambda evidence: evidence["measurements"].__setitem__(1, evidence["measurements"][0]))
        self.assert_rejected()

    def test_missing_graph_correctness_is_rejected(self):
        self.change(lambda evidence: evidence["checks"].pop())
        self.assert_rejected()

    def test_foreign_manifest_is_rejected(self):
        self.change(lambda evidence: evidence.__setitem__("manifestSha256", "0" * 64))
        self.assert_rejected()

    def test_rejected_backend_is_not_scored_as_complete(self):
        self.change(lambda evidence: evidence.__setitem__("accepted", False))
        self.assert_rejected()

    def test_nonfinite_timing_cannot_leave_stale_success(self):
        self.run_summary()
        self.change(lambda evidence: evidence["measurements"][0].__setitem__("gpuMilliseconds", float("nan")))
        self.assert_rejected()

    def test_changed_source_is_rejected(self):
        path = self.root / "protocol.json"
        protocol = json.loads(path.read_text())
        protocol["sourceSha256"]["tools/kernel-comparison/summarize.py"] = "0" * 64
        path.write_text(json.dumps(protocol), encoding="utf-8")
        self.assert_rejected()


if __name__ == "__main__":
    unittest.main()
