import copy
import unittest

from summarize_research import CASES, expected_grid, validate
from summarize_clocks import parse_clock, parse_timestamp


def report_for(backend):
    samples = [dict(Case=case, Variant=variant, Name=name, Sample=sample,
        Repetitions=1 if name == "single" else 16, GpuMicroseconds=1.0, WallMicroseconds=10.0)
        for case, variant, name, sample in expected_grid(backend)]
    checks = []
    for case, variant, name in {key[:3] for key in expected_grid(backend)}:
        for distribution in ("normal", "zero", "extreme", "alternating"):
            for output_index in range(16 if name.startswith("independent") else 1):
                checks.append(dict(Case=case, Variant=variant, Name=name,
                    Distribution=distribution, OutputIndex=output_index, MaximumToleranceRatio=0.1))
    queue = [dict(Case=case, Mode=mode, PreparationMicroseconds=preparation, Sample=sample,
        WallMicroseconds=100, Submissions=128, metrics=dict(Submissions=128))
        for case in CASES[:2] for mode in ("fixed1", "fixed2", "fixed4", "adaptive")
        for preparation in (0, 25, 100) for sample in range(12)]
    return dict(Accepted=True, Samples=samples, Checks=checks, Queue=queue)


class ResearchIntegrityTests(unittest.TestCase):
    def test_dotnet_timestamp_precision_is_supported(self):
        for digits in range(1, 10):
            fraction = "869920123"[:digits]
            parsed = parse_timestamp("2026-10-20T01:22:43." + fraction + "+00:00")
            self.assertEqual(parsed.microsecond, int(fraction[:6].ljust(6, "0")))
            self.assertEqual(parsed.utcoffset().total_seconds(), 0)

    def test_nvidia_csv_clocks_with_units_are_supported(self):
        self.assertEqual(parse_clock(" 7201 MHz"), 7201)
        self.assertEqual(parse_clock("5001"), 5001)

    def test_complete_grids_qualify(self):
        for backend in ("vulkan", "cuda"):
            validate(report_for(backend), backend)

    def test_duplicate_and_missing_samples_fail(self):
        report = report_for("vulkan")
        report["Samples"].append(copy.deepcopy(report["Samples"][0]))
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            validate(report, "vulkan")
        report["Samples"] = report["Samples"][2:]
        with self.assertRaisesRegex(ValueError, "Incomplete"):
            validate(report, "vulkan")

    def test_unchecked_independent_output_fails(self):
        report = report_for("cuda")
        index = next(index for index, row in enumerate(report["Checks"]) if row["Name"] == "independent")
        report["Checks"].pop(index)
        with self.assertRaisesRegex(ValueError, "Missing output"):
            validate(report, "cuda")

    def test_invalid_timing_and_oracle_fail(self):
        report = report_for("cuda")
        report["Samples"][0]["GpuMicroseconds"] = float("nan")
        with self.assertRaisesRegex(ValueError, "Invalid timing"):
            validate(report, "cuda")
        report = report_for("cuda")
        report["Checks"][0]["MaximumToleranceRatio"] = 1.01
        with self.assertRaisesRegex(ValueError, "Invalid numerical"):
            validate(report, "cuda")

    def test_missing_physical_submissions_fail(self):
        report = report_for("vulkan")
        report["Queue"][0]["metrics"]["Submissions"] = 127
        with self.assertRaisesRegex(ValueError, "Incomplete physical"):
            validate(report, "vulkan")


if __name__ == "__main__":
    unittest.main()
