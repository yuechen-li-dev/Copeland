"""Validate complete matched router runs before deriving any performance claim."""

import csv
import hashlib
import json
import math
import statistics
import sys
from pathlib import Path


VARIANTS = {"scalar", "simd", "parallel-simd", "parallel-all", "gpu-resident", "gpu-transfer-commit", "gpu-original-readback"}
CPU_VARIANTS = {"scalar", "simd", "parallel-simd", "parallel-all"}
SEMANTIC_VARIANTS = CPU_VARIANTS | {"gpu-transfer-commit"}
EXPECTED_CASES = {f"n{count}-c{candidates}-r{rules}-mixed"
                  for count in [1, 64, 1024, 8192, 65536]
                  for candidates in [1, 8] for rules in [0, 64, 512, 2048]}
EXPECTED_CASES.update({"n8192-c8-r2048-early", "n8192-c8-r2048-late", "n4099-c8-r512-mixed"})


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def percentile(values, fraction):
    ordered = sorted(values)
    return ordered[max(0, math.ceil(len(ordered) * fraction) - 1)]


def load_run(folder, validation):
    run = json.loads((folder / "run.json").read_text(encoding="utf-8-sig"))
    require(run["Accepted"] and not run["Quick"], f"Unqualified run: {folder}")
    require(run["Validation"] == validation and run["SampleCount"] == 80, "Changed run protocol")
    require(not run["NicForwardingQualified"], "Unexpected NIC qualification claim")
    require(run["Vector256Accelerated"], "SIMD control was unavailable")
    require(len(run["Cases"]) == 43, "Incomplete factorial/diagnostic cases")
    require({case["Id"] for case in run["Cases"]} == EXPECTED_CASES, "Changed factorial coordinates")
    if validation:
        require("VK_LAYER_KHRONOS_validation" in run["PlantFacts"]["EnabledValidationLayers"], "No validation layer")
    coordinates = {}
    for sample in run["Samples"]:
        key = (sample["Case"], sample["Variant"], sample["Sample"])
        require(key not in coordinates, f"Duplicate sample {key}")
        require(sample["Variant"] in VARIANTS and 0 <= sample["Sample"] < 80, "Invalid sample coordinate")
        require(math.isfinite(sample["WallMicroseconds"]) and sample["WallMicroseconds"] > 0, "Invalid wall sample")
        expected_repetitions = 64 if sample["Case"].startswith("n1-") and sample["Variant"] in CPU_VARIANTS else 1
        require(sample["Repetitions"] == expected_repetitions, "Changed timing repetition protocol")
        if sample["Variant"].startswith("gpu"):
            require(math.isfinite(sample["GpuMicroseconds"]) and sample["GpuMicroseconds"] > 0, "Invalid GPU sample")
        coordinates[key] = sample
    require(len(coordinates) == 43 * len(VARIANTS) * 80, "Missing or extra samples")
    checks = {(check["Case"], check["Variant"]): check for check in run["Checks"]}
    require(len(checks) == len(run["Checks"]), "Duplicate correctness check")
    for case in run["Cases"]:
        require(case["Id"] == f"n{case['Count']}-c{case['Candidates']}-r{case['RuleCount']}-{case['Distribution']}",
                "Case metadata disagrees with its coordinate")
        for filename, expected in [(case["PacketFile"], case["PacketSha256"]),
                                   (case["TableFile"], case["TableSha256"]),
                                   (case["ExpectedFile"], case["OutputSha256"])]:
            require(sha256(folder / filename) == expected, f"Fixture changed: {filename}")
        require(sum(case["Outcomes"]) == case["Count"], "Invalid outcome accounting")
        for variant in VARIANTS - {"gpu-resident"}:
            check = checks.get((case["Id"], variant))
            require(check is not None and check["Words"] == case["Count"] * 5, "Missing full output check")
        require((case["Id"], "post-timing") in checks, "Missing post-timing check")
        for variant in VARIANTS:
            for sample in range(80):
                require((case["Id"], variant, sample) in coordinates, "Missing sample")
    for variant in SEMANTIC_VARIANTS:
        require(("known-outcomes", variant) in checks, "Missing known specimen check")
    for seed in [7, 19, 53]:
        require((f"edge-seed{seed}", "stale-rejection") in checks, "Missing stale rejection")
        for generation in [1, 2, 3]:
            for variant in SEMANTIC_VARIANTS:
                require((f"edge-seed{seed}-generation{generation}", variant) in checks, "Missing state transition check")
    require(sha256(folder / "Router.spv") == run["SpirvSha256"], "SPIR-V changed")
    return run, coordinates


def summarize(root):
    loaded = [load_run(root / "validation", True)]
    loaded += [load_run(root / f"round{index}", False) for index in [1, 2, 3]]
    authority = loaded[0][0]
    for run, _ in loaded[1:]:
        for field in ["SourceHashes", "BinaryHashes", "Cases", "HlslSha256", "SpirvSha256", "PlantFacts"]:
            # Validation layer/instance extension lists deliberately differ between lanes.
            if field == "PlantFacts":
                for name in ["PhysicalDeviceName", "DriverVersion", "DeviceId"]:
                    require(run[field][name] == authority[field][name], f"Changed device: {name}")
            elif field == "Cases":
                exclude = {"FixtureSetupMicroseconds", "GpuSetupMicroseconds"}
                strip = lambda cases: [{key: value for key, value in case.items() if key not in exclude} for case in cases]
                require(strip(run[field]) == strip(authority[field]), "Unmatched case fixtures")
            else:
                require(run[field] == authority[field], f"Unmatched run: {field}")
    rows = []
    for case in authority["Cases"]:
        results = {}
        for variant in sorted(VARIANTS):
            rounds = []
            for _, coordinates in loaded[1:]:
                samples = [coordinates[(case["Id"], variant, index)] for index in range(80)]
                wall = [sample["WallMicroseconds"] for sample in samples]
                device = [sample["GpuMicroseconds"] for sample in samples if sample["GpuMicroseconds"] is not None]
                rounds.append({"MedianUs": statistics.median(wall), "P95Us": percentile(wall, 0.95),
                               "GpuMedianUs": statistics.median(device) if device else None})
            results[variant] = rounds
        speedups = [min(results[variant][index]["MedianUs"] for variant in CPU_VARIANTS)
                    / results["gpu-transfer-commit"][index]["MedianUs"] for index in range(3)]
        gpu_median = statistics.median(result["MedianUs"] for result in results["gpu-transfer-commit"])
        gpu_p95 = max(result["P95Us"] for result in results["gpu-transfer-commit"])
        useful = (case["Count"] <= 8192 and gpu_p95 <= 1000
                  and all(case["Count"] / result["MedianUs"] >= 1 for result in results["gpu-transfer-commit"]))
        row = {"Case": case["Id"], "Packets": case["Count"], "Candidates": case["Candidates"],
               "Rules": case["RuleCount"], "Distribution": case["Distribution"],
               "GpuHostMedianUs": gpu_median, "GpuWorstRoundP95Us": gpu_p95,
               "GpuInputMpps": case["Count"] / gpu_median,
               "SpeedupByRound": speedups, "MinimumSpeedup": min(speedups),
               "HostUsefulnessGate": useful, "GpuAdvantageGate": useful and min(speedups) >= 1.25,
               "Measurements": results}
        rows.append(row)
    summary = {"Accepted": True, "NicForwardingQualified": False, "Cases": rows,
               "PassingGpuAdvantageCases": [row["Case"] for row in rows if row["GpuAdvantageGate"]]}
    (root / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    with (root / "summary.csv").open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(["case", "packets", "rules", "gpu_host_us", "gpu_p95_us", "gpu_input_mpps", "min_speedup", "gpu_gate"])
        for row in rows:
            writer.writerow([row["Case"], row["Packets"], row["Rules"], row["GpuHostMedianUs"],
                             row["GpuWorstRoundP95Us"], row["GpuInputMpps"], row["MinimumSpeedup"], row["GpuAdvantageGate"]])
    lines = ["# Router research measured results", "",
             "Three independent performance processes; medians of process medians. Validation is separate.",
             "GPU host service includes whole-frame upload, completion, readback and CPU commit.",
             "Input Mpps counts all accepted/rejected/queued inputs; it is not transmitted Mpps.", "",
             "| Case | GPU host us | Worst round p95 us | Input Mpps | Minimum speedup vs best CPU | GPU gate |",
             "|---|---:|---:|---:|---:|---|"]
    for row in rows:
        lines.append(f"| {row['Case']} | {row['GpuHostMedianUs']:.2f} | {row['GpuWorstRoundP95Us']:.2f} | "
                     f"{row['GpuInputMpps']:.2f} | {row['MinimumSpeedup']:.2f}x | {row['GpuAdvantageGate']} |")
    (root / "RESULTS.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"Integrity accepted: {len(rows)} cases; {len(summary['PassingGpuAdvantageCases'])} pass GPU advantage gate.")


if __name__ == "__main__":
    summarize(Path(sys.argv[1]))
