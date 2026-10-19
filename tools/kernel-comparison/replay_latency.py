"""Matched single-operation replay latency, separate from ordinary Python calls."""

import argparse
import contextlib
import hashlib
import json
import os
import statistics
import subprocess
import sys
import time
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

import torch

from baselines import arguments, check, load_values, native_function, torch_function, triton_function
from summarize import lower_keys


VARIANTS = ["torch-eager", "torch-native", "torch-compile-default", "torch-compile-max-autotune", "sol-triton"]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def worker(root, output):
    output.mkdir(parents=True)
    fixtures = root / "fixtures"
    manifest = json.loads((fixtures / "manifest.json").read_text())
    for name, expected in manifest["files"].items():
        if digest(fixtures / name) != expected:
            raise ValueError("Fixture hash mismatch: " + name)
    torch.backends.cuda.matmul.allow_tf32 = False
    torch.backends.cudnn.allow_tf32 = False
    torch.set_float32_matmul_precision("highest")
    evidence = dict(accepted=False, manifestSha256=digest(fixtures / "manifest.json"),
        torch=torch.__version__, device=torch.cuda.get_device_name(), measurements=[], checks=[],
        boundary="one captured operation; Python graph.replay through cuda.synchronize; no transfer or timestamp retrieval")
    path = output / "cuda.json"

    def save():
        path.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")

    save()
    with torch.inference_mode():
        for case in manifest["cases"]:
            eager = torch_function(case)
            functions = [
                eager, native_function(case),
                torch.compile(eager, fullgraph=True, dynamic=False),
                torch.compile(eager, fullgraph=True, dynamic=False, mode="max-autotune-no-cudagraphs"),
                triton_function(case),
            ]
            states = {}
            for name, function in zip(VARIANTS, functions):
                values, _ = load_values(fixtures, case, "normal")
                args = arguments(values)
                for _ in range(manifest["warmups"]):
                    function(*args)
                torch.cuda.synchronize()
                graph = torch.cuda.CUDAGraph()
                with torch.cuda.graph(graph):
                    result = function(*args)
                for distribution in manifest["distributions"]:
                    changed, expected = load_values(fixtures, case, distribution)
                    for key in values:
                        values[key].copy_(changed[key])
                    graph.replay()
                    errors = check(result, expected, case)
                    evidence["checks"].append(dict(case=case["id"], variant=name, distribution=distribution, **errors))
                changed, _ = load_values(fixtures, case, "normal")
                for key in values:
                    values[key].copy_(changed[key])
                states[name] = (graph, values, result, function)
            if case["split"] == "timed":
                start = time.perf_counter()
                while time.perf_counter() - start < .250:
                    for graph, _, _, _ in states.values():
                        graph.replay()
                    torch.cuda.synchronize()
                for sample in range(manifest["samples"]):
                    order = VARIANTS[sample % len(VARIANTS):] + VARIANTS[:sample % len(VARIANTS)]
                    if sample % 2:
                        order.reverse()
                    for name in order:
                        graph = states[name][0]
                        torch.cuda.synchronize()
                        started = datetime.now(timezone.utc).isoformat()
                        start = time.perf_counter()
                        graph.replay()
                        torch.cuda.synchronize()
                        elapsed = (time.perf_counter() - start) * 1000
                        evidence["measurements"].append(dict(case=case["id"], variant=name, sample=sample,
                                                              wallMilliseconds=elapsed, startedUtc=started))
            save()
            print(case["id"] + ": single-operation CUDA replays passed", flush=True)
            del states
            torch.cuda.empty_cache()
    evidence["accepted"] = True
    save()


def run(root):
    repository = Path(__file__).resolve().parents[2]
    output = root / "replay-latency"
    output.mkdir()
    base_protocol = json.loads((root / "protocol.json").read_text(encoding="utf-8-sig"))
    source_hashes = dict(base_protocol["sourceSha256"])
    source_hashes[Path(__file__).resolve().relative_to(repository).as_posix()] = digest(Path(__file__))

    def verify_sources():
        for name, expected in source_hashes.items():
            if digest(repository / name) != expected:
                raise ValueError("Source changed: " + name)

    verify_sources()
    manifest = json.loads((root / "fixtures" / "manifest.json").read_text())
    manifest_hash = digest(root / "fixtures" / "manifest.json")
    binary = repository / "tools/Aurelian.KernelComparison/bin/Release/net10.0/Aurelian.KernelComparison.dll"
    (output / "protocol.json").write_text(json.dumps(dict(sourceSha256=source_hashes,
        manifestSha256=manifest_hash, nativeBinarySha256=digest(binary), runs=3), indent=2), encoding="utf-8")
    os.environ["TORCHINDUCTOR_CACHE_DIR"] = str(root / "inductor-cache")
    os.environ["TRITON_CACHE_DIR"] = str(root / "triton-cache")
    records = []
    for index in range(1, 4):
        folder = output / f"run{index}"
        folder.mkdir()
        for backend in (["vulkan", "cuda"] if index % 2 else ["cuda", "vulkan"]):
            if backend == "vulkan":
                command = ["dotnet", str(binary), str(root / "fixtures"), str(folder / "vulkan")]
            else:
                command = [sys.executable, str(Path(__file__).resolve()), str(root), "--worker", str(folder / "cuda")]
            with (folder / f"{backend}.log").open("w", encoding="utf-8") as log:
                subprocess.run(command, check=True, stdout=log, stderr=subprocess.STDOUT)
            evidence = lower_keys(json.loads((folder / backend / f"{backend}.json").read_text()))
            if not evidence["accepted"] or evidence["manifestSha256"] != manifest_hash:
                raise ValueError("Rejected replay evidence: " + str(folder))
            variants = VARIANTS if backend == "cuda" else [
                "existing-unfused", "sol-fused-specialized", "sol-prepared-blocking", "sol-prepared"]
            expected = Counter((case["id"], variant, sample) for case in manifest["cases"] if case["split"] == "timed"
                               for variant in variants for sample in range(manifest["samples"]))
            actual = Counter((item["case"], item["variant"], item["sample"]) for item in evidence["measurements"])
            if actual != expected:
                raise ValueError("Incomplete replay timing grid")
            expected_checks = Counter((case["id"], variant, distribution) for case in manifest["cases"]
                                      for variant in variants for distribution in manifest["distributions"])
            actual_checks = Counter((item["case"], item["variant"], item["distribution"]) for item in evidence["checks"])
            if actual_checks != expected_checks:
                raise ValueError("Incomplete replay correctness grid")
            records.extend(dict(run=index, **item) for item in evidence["measurements"])
            print(f"Single-operation replay round {index}: {backend} accepted", flush=True)
    verify_sources()
    groups = {}
    for item in records:
        groups.setdefault((item["case"], item["variant"]), []).append(item)
    rows = []
    for (case, variant), values in groups.items():
        samples = sorted(item["wallMilliseconds"] * 1000 for item in values)
        rows.append(dict(case=case, variant=variant, medianMicroseconds=statistics.median(samples),
                         p10Microseconds=samples[int((len(samples) - 1) * .1)],
                         p90Microseconds=samples[int((len(samples) - 1) * .9)], samples=len(samples)))
    lookup = {(item["case"], item["variant"]): item["medianMicroseconds"] for item in rows}
    timed_cases = [case["id"] for case in manifest["cases"] if case["split"] == "timed"]
    effects = []
    for baseline in VARIANTS:
        ratios = [lookup[case, baseline] / lookup[case, "sol-prepared"] for case in timed_cases]
        effects.append(dict(candidate="sol-prepared", baseline=baseline,
            geometricMeanSpeedup=statistics.geometric_mean(ratios),
            fasterByFivePercent=sum(value >= 1.05 for value in ratios),
            slowerByFivePercent=sum(value <= 1 / 1.05 for value in ratios)))
    result = dict(accepted=True, manifestSha256=manifest_hash, rows=rows, effects=effects,
        boundary="single-operation replay through GPU completion; Vulkan additionally reads its timestamp query")
    (output / "summary.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    lines = ["# Single-operation replay latency", "", "Median microseconds through GPU completion; three independent rounds.", "",
             "| Case | Vulkan prepared | Eager graph | Native graph | Inductor default graph | Inductor tuned graph | Sol Triton graph |",
             "| --- | ---: | ---: | ---: | ---: | ---: | ---: |"]
    for case in timed_cases:
        values = [lookup[case, variant] for variant in ["sol-prepared"] + VARIANTS]
        lines.append("| " + case + " | " + " | ".join(f"{value:.2f}" for value in values) + " |")
    (output / "RESULTS.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=Path)
    parser.add_argument("--worker", type=Path)
    parsed = parser.parse_args()
    if parsed.worker:
        worker(parsed.root.resolve(), parsed.worker.resolve())
    else:
        run(parsed.root.resolve())
