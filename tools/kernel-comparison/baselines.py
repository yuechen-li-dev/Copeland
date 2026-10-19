"""CUDA controls with independent correctness and matched timing boundaries."""

import argparse
import hashlib
import json
import os
import platform
import subprocess
import time
import traceback
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
import torch
import triton

from triton_kernels import bias_silu, matmul, row_operation


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_values(root, case, distribution):
    folder = root / case["id"] / distribution
    matrix = case["family"].startswith("gemm")
    shapes = {
        "input": (case["rows"], case["inner"] if matrix else case["columns"]),
        "auxiliary": (case["inner"] if matrix else 1, case["columns"]),
    }
    if case["family"] == "gemm_bias_silu":
        shapes["bias"] = (1, case["columns"])
    values = {
        name: torch.from_numpy(np.fromfile(folder / f"{name}.bin", dtype="<f4").reshape(shape)).cuda()
        for name, shape in shapes.items()
    }
    expected = np.fromfile(folder / "expected.bin", dtype="<f4").reshape(case["rows"], case["columns"])
    return values, expected


def torch_function(case):
    family = case["family"]
    if family == "bias_silu":
        return lambda x, a, b: torch.nn.functional.silu(x + a)
    if family == "softmax":
        return lambda x, a, b: torch.softmax(x, dim=1)
    if family == "rmsnorm":
        return lambda x, a, b: x * torch.rsqrt(torch.mean(x * x, dim=1, keepdim=True) + 1e-5) * a
    if family == "gemm":
        return lambda x, a, b: x @ a
    return lambda x, a, b: torch.nn.functional.silu(x @ a + b)


def native_function(case):
    if case["family"] == "rmsnorm":
        return lambda x, a, b: torch.nn.functional.rms_norm(x, (case["columns"],), a.flatten(), 1e-5)
    if case["family"] == "gemm_bias_silu":
        return lambda x, a, b: torch.nn.functional.silu(torch.addmm(b, x, a))
    return torch_function(case)


def triton_function(case):
    rows, columns, inner = case["rows"], case["columns"], case["inner"]
    result = torch.empty((rows, columns), device="cuda", dtype=torch.float32)

    def execute(x, auxiliary, bias):
        family = case["family"]
        if family == "bias_silu":
            bias_silu[(triton.cdiv(rows * columns, 256),)](
                x, auxiliary, result, rows * columns, columns, 256
            )
        elif family in ("rmsnorm", "softmax"):
            block = triton.next_power_of_2(columns)
            row_operation[(rows,)](x, auxiliary, result, columns, family == "rmsnorm", block,
                                  num_warps=4 if block < 2048 else 8)
        else:
            matmul[(triton.cdiv(rows, 32), triton.cdiv(columns, 32))](
                x, auxiliary, bias, result, rows, columns, inner, family == "gemm_bias_silu",
                32, 32, 32, num_warps=4,
            )
        return result

    return execute


def arguments(values):
    return values["input"], values["auxiliary"], values.get("bias", values["auxiliary"])


def check(actual, expected, case):
    actual = actual.detach().cpu().numpy()
    difference = np.abs(actual.astype(np.float64) - expected)
    ratios = difference / (case["atol"] + case["rtol"] * np.abs(expected))
    if not np.isfinite(actual).all() or not np.all(ratios <= 1):
        index = np.unravel_index(np.argmax(ratios), ratios.shape)
        raise AssertionError(f"{case['id']}: {index}: actual={actual[index]}, expected={expected[index]}, ratio={ratios[index]}")
    return dict(maximumAbsoluteError=float(difference.max()), maximumToleranceRatio=float(ratios.max()))


def run(fixtures, output, selected_case=None):
    output.mkdir(parents=True, exist_ok=True)
    manifest_path = fixtures / "manifest.json"
    manifest = json.loads(manifest_path.read_text())
    for name, expected in manifest["files"].items():
        if sha256(fixtures / name) != expected:
            raise ValueError(f"Fixture hash mismatch: {name}")
    if not torch.cuda.is_available():
        raise RuntimeError("CUDA device unavailable; no CPU fallback permitted")
    torch.backends.cuda.matmul.allow_tf32 = False
    torch.backends.cudnn.allow_tf32 = False
    torch.set_float32_matmul_precision("highest")
    torch.manual_seed(manifest["seed"])
    evidence = dict(
        accepted=False, manifestSha256=sha256(manifest_path), torch=torch.__version__,
        triton=triton.__version__, cuda=torch.version.cuda, os=platform.platform(),
        device=torch.cuda.get_device_name(), precision=manifest["precision"],
        tritonDistribution="community triton-windows on Windows; upstream triton on Linux",
        precisionSettings=dict(matmulPrecision=torch.get_float32_matmul_precision(),
                               allowTf32Matmul=torch.backends.cuda.matmul.allow_tf32,
                               allowTf32Cudnn=torch.backends.cudnn.allow_tf32),
        measurements=[], checks=[], compilation=[], failures=[], profiles=[],
        sources={path.name: sha256(path) for path in (Path(__file__), Path(__file__).with_name("triton_kernels.py"))},
        boundaries=dict(
            gpu="CUDA events around resident single call; may include GPU idle gaps while host enqueues",
            repeatedGpu="CUDA timing event nodes captured inside one graph containing repetitions operations, divided by repetitions",
            wall="resident single call plus device synchronization; no transfers",
            cold="first invocation after construction; Inductor compile or Triton JIT plus first execution; disk caches may be warm",
        ),
    )
    (output / "pip-freeze.txt").write_text(subprocess.check_output(
        [os.sys.executable, "-m", "pip", "freeze"], text=True), encoding="utf-8")
    evidence_path = output / "cuda.json"

    def save():
        evidence_path.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")

    save()
    with torch.inference_mode():
        for case in manifest["cases"]:
            if selected_case is not None and case["id"] != selected_case:
                continue
            variants = {}
            eager = torch_function(case)
            candidates = [
                ("torch-eager", lambda: eager),
                ("torch-native", lambda: native_function(case)),
                ("torch-compile-default", lambda: torch.compile(eager, fullgraph=True, dynamic=False)),
                ("torch-compile-max-autotune", lambda: torch.compile(
                    eager, fullgraph=True, dynamic=False, mode="max-autotune-no-cudagraphs")),
                ("sol-triton", lambda: triton_function(case)),
            ]
            for name, construct in candidates:
                try:
                    function = construct()
                    inputs, _ = load_values(fixtures, case, "normal")
                    args = arguments(inputs)
                    torch.cuda.synchronize()
                    start = time.perf_counter()
                    function(*args)
                    torch.cuda.synchronize()
                    evidence["compilation"].append(dict(
                        case=case["id"], variant=name, firstCallMilliseconds=(time.perf_counter() - start) * 1000))
                    for distribution in manifest["distributions"]:
                        changed, expected = load_values(fixtures, case, distribution)
                        errors = check(function(*arguments(changed)), expected, case)
                        evidence["checks"].append(dict(case=case["id"], variant=name,
                                                       distribution=distribution, **errors))
                    # Reuse the original device addresses with changed contents: catches
                    # stale results and verifies the actual pointers used during timing.
                    for distribution in manifest["distributions"]:
                        changed, expected = load_values(fixtures, case, distribution)
                        for key in inputs:
                            inputs[key].copy_(changed[key])
                        errors = check(function(*args), expected, case)
                        evidence["checks"].append(dict(case=case["id"], variant=name,
                                                       distribution=distribution, reusedAddresses=True, **errors))
                    changed, _ = load_values(fixtures, case, "normal")
                    for key in inputs:
                        inputs[key].copy_(changed[key])
                    for _ in range(manifest["warmups"]):
                        function(*args)
                    torch.cuda.synchronize()
                    if case["rows"] == 128:
                        # Outside timed regions. Preserve the actual launched kernels
                        # instead of inferring fusion from a speedup alone.
                        with torch.profiler.profile(acc_events=True, activities=[
                            torch.profiler.ProfilerActivity.CPU, torch.profiler.ProfilerActivity.CUDA
                        ]) as profile:
                            function(*args)
                            torch.cuda.synchronize()
                        evidence["profiles"].append(dict(case=case["id"], variant=name,
                            coverageQualified=False, observedCudaKernelNames=[
                            event.name for event in profile.events()
                            if event.device_type == torch.autograd.DeviceType.CUDA
                        ]))
                    graph = torch.cuda.CUDAGraph()
                    graph_begin = torch.cuda.Event(enable_timing=True, external=True)
                    graph_end = torch.cuda.Event(enable_timing=True, external=True)
                    graph_begin.record()
                    graph_end.record()
                    torch.cuda.synchronize()
                    with torch.cuda.graph(graph):
                        graph_begin.record()
                        for _ in range(manifest["repetitions"]):
                            captured_result = function(*args)
                        graph_end.record()
                    # Validate captured execution with changed resident inputs too.
                    for distribution in manifest["distributions"]:
                        changed, expected = load_values(fixtures, case, distribution)
                        for key in inputs:
                            inputs[key].copy_(changed[key])
                        graph.replay()
                        errors = check(captured_result, expected, case)
                        evidence["checks"].append(dict(case=case["id"], variant=name,
                                                       distribution=distribution, graphReplay=True, **errors))
                    changed, _ = load_values(fixtures, case, "normal")
                    for key in inputs:
                        inputs[key].copy_(changed[key])
                    for _ in range(manifest["warmups"]):
                        graph.replay()
                    torch.cuda.synchronize()
                    variants[name] = (function, args, inputs, graph, captured_result, graph_begin, graph_end)
                    print(f"{case['id']} {name}: correctness and capture passed", flush=True)
                except Exception:
                    evidence["failures"].append(dict(case=case["id"], variant=name, error=traceback.format_exc()))
                    print(evidence["failures"][-1]["error"], flush=True)
                save()
            if case["split"] == "timed":
                names = list(variants)
                precondition = time.perf_counter()
                while variants and time.perf_counter() - precondition < .250:
                    for _, _, _, graph, _, _, _ in variants.values():
                        graph.replay()
                    torch.cuda.synchronize()
                for sample in range(manifest["samples"]):
                    # Rotate and reverse to spread clock drift across all candidates.
                    order = names[sample % len(names):] + names[:sample % len(names)] if names else []
                    if sample % 2:
                        order.reverse()
                    for name in order:
                        sample_started = datetime.now(timezone.utc).isoformat()
                        function, args, _, graph, _, graph_begin, graph_end = variants[name]
                        begin = torch.cuda.Event(enable_timing=True)
                        end = torch.cuda.Event(enable_timing=True)
                        torch.cuda.synchronize()
                        start = time.perf_counter()
                        function(*args)
                        torch.cuda.synchronize()
                        wall = (time.perf_counter() - start) * 1000
                        begin.record()
                        function(*args)
                        end.record()
                        torch.cuda.synchronize()
                        gpu = begin.elapsed_time(end)
                        graph.replay()
                        graph_end.synchronize()
                        repeated = graph_begin.elapsed_time(graph_end) / manifest["repetitions"]
                        evidence["measurements"].append(dict(case=case["id"], variant=name, sample=sample,
                            startedUtc=sample_started,
                            wallMilliseconds=wall, gpuMilliseconds=gpu, repeatedGpuMilliseconds=repeated,
                            repetitions=manifest["repetitions"]))
            save()
            del variants
            torch.cuda.empty_cache()
    if torch.backends.cuda.matmul.allow_tf32 or torch.get_float32_matmul_precision() != "highest":
        evidence["failures"].append(dict(error="FP32 precision policy changed during execution"))
    evidence["accepted"] = not evidence["failures"]
    cache = Path(os.environ.get("TORCHINDUCTOR_CACHE_DIR", output / "inductor-cache"))
    evidence["inductorSources"] = [dict(path=str(path), sha256=sha256(path)) for path in cache.rglob("*.py")]
    save()
    if evidence["failures"]:
        raise RuntimeError(f"{len(evidence['failures'])} baseline failures retained in {evidence_path}")
    print("CUDA_KERNEL_COMPARISON_PASSED", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("fixtures", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--case")
    parsed = parser.parse_args()
    run(parsed.fixtures.resolve(), parsed.output.resolve(), parsed.case)
