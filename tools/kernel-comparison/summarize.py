"""Aggregate complete raw runs without dropping failures or selecting best samples."""

import argparse
import hashlib
import json
import math
import statistics
from collections import Counter
from pathlib import Path


def lower_keys(value):
    if isinstance(value, dict):
        return {key[0].lower() + key[1:]: lower_keys(item) for key, item in value.items()}
    if isinstance(value, list):
        return [lower_keys(item) for item in value]
    return value


def summarize(root):
    (root / "summary.json").write_text('{"accepted":false}\n', encoding="utf-8")
    manifest_path = root / "fixtures" / "manifest.json"
    manifest = json.loads(manifest_path.read_text())
    manifest_hash = hashlib.sha256(manifest_path.read_bytes()).hexdigest()
    records = []
    runs = []
    incomplete = []
    protocol = json.loads((root / "protocol.json").read_text(encoding="utf-8-sig"))
    repository = Path(__file__).resolve().parents[2]
    for name, expected in protocol["sourceSha256"].items():
        if hashlib.sha256((repository / name).read_bytes()).hexdigest() != expected:
            incomplete.append(f"Source changed during qualification: {name}")
    timed_cases = [case["id"] for case in manifest["cases"] if case["split"] == "timed"]
    vulkan_variants = ["existing-unfused", "sol-fused-specialized", "sol-prepared-blocking", "sol-prepared"]
    cuda_variants = ["torch-eager", "torch-native", "torch-compile-default", "torch-compile-max-autotune", "sol-triton"]
    for run in sorted(root.glob("run*")):
        if not run.is_dir():
            continue
        for backend in ("vulkan", "cuda"):
            path = run / backend / f"{backend}.json"
            if not path.exists():
                incomplete.append(str(path))
                continue
            evidence = lower_keys(json.loads(path.read_text()))
            if not evidence["accepted"] or evidence["manifestSha256"] != manifest_hash:
                incomplete.append(str(path))
            variants = vulkan_variants if backend == "vulkan" else cuda_variants
            expected_samples = Counter((case, variant, sample) for case in timed_cases
                                       for variant in variants for sample in range(manifest["samples"]))
            actual_samples = Counter((item["case"], item["variant"], item["sample"]) for item in evidence["measurements"])
            if actual_samples != expected_samples:
                incomplete.append(f"{path}: missing, duplicated or unexpected timed samples")
            modes = [(False, False)] if backend == "vulkan" else [(False, False), (True, False), (False, True)]
            expected_checks = Counter((case["id"], variant, distribution, reused, graph)
                                      for case in manifest["cases"] for variant in variants
                                      for distribution in manifest["distributions"] for reused, graph in modes)
            actual_checks = Counter((item["case"], item["variant"], item["distribution"],
                                     item.get("reusedAddresses", False), item.get("graphReplay", False))
                                    for item in evidence["checks"])
            if actual_checks != expected_checks:
                incomplete.append(f"{path}: missing, duplicated or unexpected correctness checks")
            for check in evidence["checks"]:
                ratio = check["maximumToleranceRatio"]
                if not math.isfinite(ratio) or ratio > 1:
                    incomplete.append(f"{path}: numerical check exceeded the frozen tolerance")
            runs.append(dict(path=str(path), accepted=evidence["accepted"],
                             device=evidence["device"], checks=len(evidence["checks"]),
                             torch=evidence.get("torch"), triton=evidence.get("triton")))
            for measurement in evidence["measurements"]:
                for metric in ("wallMilliseconds", "gpuMilliseconds", "repeatedGpuMilliseconds"):
                    if measurement[metric] is None or not math.isfinite(measurement[metric]) or measurement[metric] <= 0:
                        raise ValueError(f"{path}: unavailable or invalid {metric}")
                records.append(dict(run=run.name, **measurement))
    if not runs:
        raise ValueError("No completed runs found")
    if len(runs) != protocol["runs"] * 2:
        incomplete.append("Missing backend executions for the requested independent runs")
    groups = {}
    for record in records:
        key = record["case"], record["variant"]
        groups.setdefault(key, []).append(record)
    rows = []
    for (case, variant), values in groups.items():
        row = dict(case=case, variant=variant, samples=len(values))
        for metric in ("wallMilliseconds", "gpuMilliseconds", "repeatedGpuMilliseconds"):
            samples = [item[metric] * 1000 for item in values]
            row[metric.replace("Milliseconds", "Microseconds")] = dict(
                median=statistics.median(samples), minimum=min(samples), maximum=max(samples),
                p10=float(sorted(samples)[int((len(samples) - 1) * .1)]),
                p90=float(sorted(samples)[int((len(samples) - 1) * .9)]),
                runMedians={name: statistics.median(item[metric] * 1000 for item in values if item["run"] == name)
                            for name in sorted({item["run"] for item in values})},
            )
        rows.append(row)
    lookup = {(row["case"], row["variant"]): row for row in rows}
    comparison = []
    for case in manifest["cases"]:
        if case["split"] != "timed":
            continue
        case_id = case["id"]
        result = dict(case=case_id, family=case["family"])
        for metric in ("wallMicroseconds", "repeatedGpuMicroseconds"):
            timings = {variant: row[metric]["median"] for (key, variant), row in lookup.items() if key == case_id}
            result[metric] = timings
        comparison.append(result)
    effects = []
    for metric in ("wallMicroseconds", "repeatedGpuMicroseconds"):
        for candidate, baseline in [
            ("sol-prepared", "existing-unfused"),
            ("sol-prepared", "torch-eager"),
            ("sol-prepared", "torch-native"),
            ("sol-prepared", "torch-compile-default"),
            ("sol-prepared", "torch-compile-max-autotune"),
            ("sol-prepared", "sol-triton"),
            ("torch-compile-default", "torch-eager"),
            ("torch-compile-max-autotune", "torch-eager"),
            ("sol-prepared", "sol-prepared-blocking"),
        ]:
            ratios = [row[metric][baseline] / row[metric][candidate] for row in comparison
                      if baseline in row[metric] and candidate in row[metric]]
            if ratios:
                effects.append(dict(metric=metric, candidate=candidate, baseline=baseline,
                                    geometricMeanSpeedup=statistics.geometric_mean(ratios),
                                    fasterByFivePercent=sum(value >= 1.05 for value in ratios),
                                    slowerByFivePercent=sum(value <= 1 / 1.05 for value in ratios), cases=len(ratios)))
    summary = dict(accepted=not incomplete, manifestSha256=manifest_hash, runs=runs, effects=effects,
                   incomplete=incomplete, rows=rows, comparison=comparison)
    (root / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    lines = ["# Local kernel comparison", "", f"Complete: {not incomplete}. Runs: {len(runs)} backend executions.", "",
             "Median microseconds per operation. GPU columns use repeated batches; wall columns use one resident operation.", "",
             "| Case | Vulkan prepared GPU | Torch eager GPU | Torch native GPU | Inductor default GPU | Inductor tuned GPU | Sol Triton GPU | Vulkan prepared wall | Eager wall | Tuned wall |",
             "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |"]
    for row in comparison:
        gpu, wall = row["repeatedGpuMicroseconds"], row["wallMicroseconds"]
        gpu_values = [gpu.get(name) for name in ("sol-prepared", "torch-eager", "torch-native", "torch-compile-default", "torch-compile-max-autotune", "sol-triton")]
        wall_values = [wall.get(name) for name in ("sol-prepared", "torch-eager", "torch-compile-max-autotune")]
        formatted = ["missing" if value is None else f"{value:.2f}" for value in gpu_values + wall_values]
        lines.append("| " + row["case"] + " | " + " | ".join(formatted) + " |")
    lines.extend(["", "Raw results, ranges and individual run medians are in summary.json. Missing or rejected runs invalidate the comparison.", ""])
    (root / "RESULTS.md").write_text("\n".join(lines), encoding="utf-8")
    print("\n".join(lines))
    if incomplete:
        raise RuntimeError("Incomplete or rejected evidence: " + "; ".join(incomplete))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=Path)
    summarize(parser.parse_args().root.resolve())
