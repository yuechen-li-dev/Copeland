"""Keep pipeline throughput, in-flight latency and process CPU cost distinct."""

import hashlib
import json
import math
import statistics
import sys
from pathlib import Path


VARIANTS = {"scalar", "simd", "parallel-simd", "parallel-all", "gpu-depth1", "gpu-depth2", "gpu-depth4", "gpu-depth8"}
CPU = {"scalar", "simd", "parallel-simd", "parallel-all"}
CASES = {f"n{count}-c8-r{rules}-mixed" for count in [1024, 8192] for rules in [0, 2048]}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def p95(values):
    return sorted(values)[math.ceil(len(values) * 0.95) - 1]


def summarize(root, primary):
    primary_run = json.loads((primary / "round1/run.json").read_text())
    primary_cases = {case["Id"]: case for case in primary_run["Cases"]}
    runs = [json.loads((root / name / "stream.json").read_text())
            for name in ["validation", "round1", "round2", "round3"]]
    for index, run in enumerate(runs):
        require(run["Accepted"] and run["Validation"] == (index == 0), "Rejected or wrong streaming lane")
        require(run["SampleCount"] == 16 and run["BatchesPerSample"] == 32, "Changed stream protocol")
        require(run["TieredCompilation"] == "0", "CPU tiering must be disabled for steady optimized controls")
        require({case["Id"] for case in run["Cases"]} == CASES and len(run["Cases"]) == 4, "Changed cases")
        require(run["SourceHashes"] == runs[0]["SourceHashes"] and run["BinarySha256"] == runs[0]["BinarySha256"], "Changed stream code")
        require(run["SpirvSha256"] == primary_run["SpirvSha256"], "Changed packet shader")
        require(hashlib.sha256((root / ["validation", "round1", "round2", "round3"][index] / "Router.spv").read_bytes()).hexdigest()
                == run["SpirvSha256"], "Changed SPIR-V artifact")
        for case in run["Cases"]:
            for key in ["PacketSha256", "TableSha256", "OutputSha256"]:
                require(case[key] == primary_cases[case["Id"]][key], "Changed workload from the primary matrix")
        checks = {(check["Case"], check["Variant"]): check for check in run["Checks"]}
        require(len(checks) == 32 and len(run["Checks"]) == 32, "Missing output checks")
        coordinates = set()
        for sample in run["Samples"]:
            key = (sample["Case"], sample["Variant"], sample["Sample"])
            require(key not in coordinates, "Duplicate stream sample")
            coordinates.add(key)
            require(sample["Case"] in CASES and sample["Variant"] in VARIANTS and 0 <= sample["Sample"] < 16, "Invalid coordinate")
            require(sample["Batches"] == 32 and len(sample["BatchLatenciesMicroseconds"]) == 32, "Missing batch observations")
            total = sample["TotalWallMicroseconds"]
            require(math.isfinite(total) and total > 0, "Invalid stream time")
            for latency in sample["BatchLatenciesMicroseconds"]:
                require(math.isfinite(latency) and 0 < latency <= total, "Invalid in-flight latency")
        require(len(coordinates) == 512, "Missing stream samples")
        resources = {(row["Case"], row["Variant"]): row for row in run["Resources"]}
        require(len(resources) == 32 and len(run["Resources"]) == 32, "Missing resource profiles")
        for case in run["Cases"]:
            for variant in VARIANTS:
                check = checks[(case["Id"], variant)]
                slots = int(variant[9:]) if variant.startswith("gpu") else 1
                require(check["Slots"] == slots and check["WordsPerSlot"] == case["Count"] * 5, "Partial output check")
                resource = resources[(case["Id"], variant)]
                require(resource["WallMilliseconds"] >= 250 and resource["ProcessCpuMilliseconds"] > 0, "Insufficient resource observation")
                require(resource["Packets"] == resource["Groups"] * 32 * case["Count"], "Invalid resource accounting")
    results = []
    for case in runs[0]["Cases"]:
        measurements = {}
        for variant in sorted(VARIANTS):
            rounds = []
            for run in runs[1:]:
                samples = [row for row in run["Samples"] if row["Case"] == case["Id"] and row["Variant"] == variant]
                batch_service = [row["TotalWallMicroseconds"] / 32 for row in samples]
                latencies = [value for row in samples for value in row["BatchLatenciesMicroseconds"]]
                resource = next(row for row in run["Resources"] if row["Case"] == case["Id"] and row["Variant"] == variant)
                rounds.append({"AmortizedBatchUs": statistics.median(batch_service),
                               "BatchCompletionP95Us": p95(latencies),
                               "ProcessCpuMsPerMillionInputs": resource["ProcessCpuMilliseconds"] * 1_000_000 / resource["Packets"],
                               "AverageProcessCpuCores": resource["ProcessCpuMilliseconds"] / resource["WallMilliseconds"]})
            measurements[variant] = rounds
        gpu = {}
        for depth in [1, 2, 4, 8]:
            rows = measurements[f"gpu-depth{depth}"]
            gpu[depth] = {"InputMpps": case["Count"] / statistics.median(row["AmortizedBatchUs"] for row in rows),
                          "WorstRoundBatchP95Us": max(row["BatchCompletionP95Us"] for row in rows),
                          "SpeedupByRound": [min(measurements[cpu][index]["AmortizedBatchUs"] for cpu in CPU)
                                             / rows[index]["AmortizedBatchUs"] for index in range(3)]}
        results.append({"Case": case["Id"], "Packets": case["Count"], "Rules": case["RuleCount"],
                        "Measurements": measurements, "Gpu": gpu})
    result = {"Accepted": True, "NicForwardingQualified": False,
              "SourceHashes": runs[0]["SourceHashes"], "BinarySha256": runs[0]["BinarySha256"],
              "RawRunHashes": {name: hashlib.sha256((root / name / "stream.json").read_bytes()).hexdigest()
                               for name in ["validation", "round1", "round2", "round3"]}, "Cases": results}
    (root / "summary.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print("Streaming integrity accepted: four matched workloads, depths 1/2/4/8, three performance rounds.")
    for case in results:
        print(case["Case"], json.dumps(case["Gpu"]))


if __name__ == "__main__":
    summarize(Path(sys.argv[1]), Path(sys.argv[2]))
