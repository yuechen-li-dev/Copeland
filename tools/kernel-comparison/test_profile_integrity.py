import copy
from pathlib import Path
import tempfile
import unittest

from summarize_profile import CUDA_CASES, CUDA_VARIANTS, DISTRIBUTIONS, digest, verify_cuda


class ProfileIntegrityTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.repository = Path(self.temporary.name)
        script = self.repository / "tools/kernel-comparison/cuda_profile.py"
        script.parent.mkdir(parents=True)
        script.write_text("fixture source")
        artifact = self.repository / "kernel.ptx"
        artifact.write_text("fixture PTX")
        self.data = dict(Accepted=True, ScriptSha256=digest(script),
            CompilationHashes={"kernel.ptx": digest(artifact)},
            EmptyLaunch=[dict(Sample=sample, WallMicroseconds=10.0) for sample in range(500)],
            Samples=[dict(Case=case, Variant=variant, Sample=sample,
                WallMicroseconds=10.0, GpuMicroseconds=2.0)
                for case in CUDA_CASES for variant in CUDA_VARIANTS for sample in range(100)],
            Checks=[dict(Case=case, Variant=variant, Distribution=distribution, MaximumToleranceRatio=0.1)
                for case in CUDA_CASES for variant in CUDA_VARIANTS for distribution in DISTRIBUTIONS],
            ReplayChecks=[dict(Case=case, Variant=variant, Distribution=distribution,
                Kind=kind, MaximumToleranceRatio=0.1) for case in CUDA_CASES
                for variant in CUDA_VARIANTS for distribution in DISTRIBUTIONS for kind in ("single", "batch16")])

    def verify(self, data):
        verify_cuda(data, self.repository, self.repository)

    def test_complete_grid_passes(self):
        self.verify(self.data)

    def test_duplicate_coordinate_rejected(self):
        data = copy.deepcopy(self.data)
        data["Samples"][-1] = data["Samples"][0]
        with self.assertRaisesRegex(ValueError, "timing grid"):
            self.verify(data)

    def test_missing_changed_input_replay_rejected(self):
        self.data["ReplayChecks"].pop()
        with self.assertRaisesRegex(ValueError, "changed-input"):
            self.verify(self.data)

    def test_nan_timestamp_rejected(self):
        self.data["Samples"][0]["GpuMicroseconds"] = float("nan")
        with self.assertRaisesRegex(ValueError, "Invalid CUDA timing"):
            self.verify(self.data)

    def test_empty_capture_timestamp_rejected(self):
        self.data["Samples"][0]["GpuMicroseconds"] = 0
        with self.assertRaisesRegex(ValueError, "Invalid CUDA timing"):
            self.verify(self.data)

    def test_failed_replay_correctness_rejected(self):
        self.data["ReplayChecks"][0]["MaximumToleranceRatio"] = 2
        with self.assertRaisesRegex(ValueError, "correctness failed"):
            self.verify(self.data)

    def test_changed_ptx_rejected(self):
        (self.repository / "kernel.ptx").write_text("different PTX")
        with self.assertRaisesRegex(ValueError, "Changed CUDA compilation"):
            self.verify(self.data)

    def test_nonfinite_empty_launch_rejected(self):
        self.data["EmptyLaunch"][0]["WallMicroseconds"] = float("nan")
        with self.assertRaisesRegex(ValueError, "Invalid empty CUDA timing"):
            self.verify(self.data)

    def test_missing_empty_launch_rejected(self):
        self.data["EmptyLaunch"].pop()
        with self.assertRaisesRegex(ValueError, "Incomplete empty CUDA launch grid"):
            self.verify(self.data)


if __name__ == "__main__":
    unittest.main()
