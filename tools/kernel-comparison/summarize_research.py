"""Validate complete research grids and summarize fresh-process medians."""

import argparse
from collections import defaultdict
import hashlib
import json
import math
from pathlib import Path
import statistics


CASES = ("bias_silu-4x128x0", "bias_silu-1024x4096x0", "rmsnorm-128x1024x0", "softmax-1024x4096x0")


def expected_grid(backend):
    grid = set()
    for case in CASES:
        if case.startswith("bias_silu"):
            operations = ["copy", "bias", "exp", "bias_silu"]
            if backend == "cuda":
                operations += ["exp-expf", "silu-expf"]
            variants = [f"{operation}-{threads}" for operation in operations for threads in (32, 64, 128, 256)]
            variants += [f"{operation}-vector4" for operation in ("copy", "bias", "exp", "bias_silu")]
            if backend == "vulkan":
                variants.append("ts-baseline")
        else:
            variants = ["wave32", "wave128", "wave256"]
            if backend == "vulkan":
                variants.append("ts-baseline")
        for variant in variants:
            names = ["single", "batch16"]
            if variant == "bias_silu-256":
                names += ["independent-full", "independent-elided"] if backend == "vulkan" else ["independent"]
            for name in names:
                for sample in range(40):
                    grid.add((case, variant, name, sample))
    return grid


def validate(report, backend):
    if report.get("Accepted") is not True:
        raise ValueError("Unaccepted report")
    seen = set()
    for row in report["Samples"]:
        key = (row["Case"], row["Variant"], row["Name"], row["Sample"])
        if key in seen:
            raise ValueError("Duplicate sample: " + str(key))
        seen.add(key)
        for field in ("GpuMicroseconds", "WallMicroseconds"):
            if not math.isfinite(row[field]) or row[field] <= 0:
                raise ValueError("Invalid timing")
        if row["Repetitions"] != (1 if row["Name"] == "single" else 16):
            raise ValueError("Invalid repetition count")
    if seen != expected_grid(backend):
        raise ValueError("Incomplete or extra sample coordinates")
    # Distribution spelling belongs to the frozen manifest; compare the complete
    # set across every plan.
    distributions = {row["Distribution"] for row in report["Checks"]}
    if len(distributions) != 4:
        raise ValueError("Expected four distributions")
    counts = defaultdict(int)
    checked_outputs = set()
    for row in report["Checks"]:
        key = (row["Case"], row["Variant"], row["Name"], row["Distribution"])
        output_key = key + (row["OutputIndex"],)
        if output_key in checked_outputs:
            raise ValueError("Duplicate output check")
        checked_outputs.add(output_key)
        counts[key] += 1
        ratio = row["MaximumToleranceRatio"]
        if not math.isfinite(ratio) or not 0 <= ratio <= 1:
            raise ValueError("Invalid numerical check")
    plans = {coordinate[:3] for coordinate in seen}
    expected_checks = set()
    for case, variant, name in plans:
        for distribution in distributions:
            key = (case, variant, name, distribution)
            expected_checks.add(key)
            if counts[key] != (16 if name.startswith("independent") else 1):
                raise ValueError("Missing output check: " + str(key))
            if {item[-1] for item in checked_outputs if item[:-1] == key} != set(range(counts[key])):
                raise ValueError("Incorrect output index")
    if set(counts) != expected_checks:
        raise ValueError("Extra correctness coordinate")
    if backend == "vulkan":
        queue_seen = set()
        for row in report["Queue"]:
            key = (row["Case"], row["Mode"], row["PreparationMicroseconds"], row["Sample"])
            if key in queue_seen:
                raise ValueError("Duplicate queue coordinate")
            queue_seen.add(key)
            if not math.isfinite(row["WallMicroseconds"]) or row["WallMicroseconds"] <= 0:
                raise ValueError("Invalid queue timing")
            if row["metrics"]["Submissions"] != row["Submissions"]:
                raise ValueError("Incomplete physical submissions")
        expected_queue = {(case, mode, preparation, sample) for case in CASES[:2]
            for mode in ("fixed1", "fixed2", "fixed4", "adaptive") for preparation in (0, 25, 100) for sample in range(12)}
        if queue_seen != expected_queue:
            raise ValueError("Incomplete queue grid")


def summarize(root):
    process_values = defaultdict(list)
    process_queue = defaultdict(list)
    reports = []
    manifest_hash = None
    source_hashes = None
    for directory in sorted(root.glob("run*")):
        for backend in ("vulkan", "cuda"):
            path = directory / backend / "research.json"
            report = json.loads(path.read_text())
            validate(report, backend)
            if backend == "vulkan" and report["Validation"]:
                raise ValueError("Validation-enabled run is not a performance control")
            if manifest_hash is not None and manifest_hash != report["ManifestSha256"]:
                raise ValueError("Fixture identity differs")
            manifest_hash = report["ManifestSha256"]
            if backend == "vulkan":
                if source_hashes is not None and source_hashes != report["SourceHashes"]:
                    raise ValueError("Native sources differ between processes")
                source_hashes = report["SourceHashes"]
                for item in report["Compilations"]:
                    artifact = path.parent / "shaders" / item["Case"] / item["Variant"] / "kernel.spv"
                    if hashlib.sha256(artifact.read_bytes()).hexdigest() != item["SpirvSha256"]:
                        raise ValueError("SPIR-V artifact changed")
            else:
                for name, value in report["ArtifactHashes"].items():
                    if hashlib.sha256((path.parent / name).read_bytes()).hexdigest() != value:
                        raise ValueError("CUDA artifact changed")
            values = defaultdict(list)
            for row in report["Samples"]:
                key = (backend, row["Case"], row["Variant"], row["Name"])
                values[key].append(row["GpuMicroseconds"])
            for key, timings in values.items():
                process_values[key].append(statistics.median(timings))
            values = defaultdict(list)
            for row in report.get("Queue", []):
                key = (row["Case"], row["Mode"], row["PreparationMicroseconds"])
                values[key].append(row["WallMicroseconds"] / row["Submissions"])
            for key, timings in values.items():
                process_queue[key].append(statistics.median(timings))
            reports.append(dict(Path=str(path.relative_to(root)), Sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                Checks=len(report["Checks"]), Samples=len(report["Samples"]), Clocks=report["Clocks"]))
    if not reports or len(reports) % 2:
        raise ValueError("No complete backend pair")
    result = dict(Accepted=True, Reports=reports, Gpu=[dict(Backend=key[0], Case=key[1], Variant=key[2], Name=key[3],
        MedianMicroseconds=statistics.median(values), ProcessMedians=values) for key, values in sorted(process_values.items())],
        Queue=[dict(Case=key[0], Mode=key[1], PreparationMicroseconds=key[2], MedianPerSubmissionMicroseconds=statistics.median(values),
            ProcessMedians=values) for key, values in sorted(process_queue.items())])
    (root / "summary.json").write_text(json.dumps(result, indent=2))
    for case in CASES:
        print("\n" + case)
        for row in result["Gpu"]:
            if row["Case"] == case and row["Name"] != "single":
                print(f'{row["Backend"]:7} {row["Variant"]:19} {row["Name"]:20} {row["MedianMicroseconds"]:9.3f} us')
    print("\nQueue wall per submission, including recording and scope allocation:")
    for row in result["Queue"]:
        print(row["Case"], row["PreparationMicroseconds"], row["Mode"], round(row["MedianPerSubmissionMicroseconds"], 3))
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=Path)
    summarize(parser.parse_args().root.resolve())
