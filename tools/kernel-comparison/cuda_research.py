"""Matched NVRTC controls for the subgroup and instruction-ladder experiment.

PyTorch only allocates storage and provides graph/event infrastructure. All
timed arithmetic is the retained CUDA C source, not a PyTorch tensor operation.
"""

import argparse
import ctypes as ct
import json
from pathlib import Path
import subprocess
import time

import numpy as np
import torch

import cuda_profile
from cuda_profile import CudaCompiler, digest


def source_for(case):
    width = case["columns"]
    count = case["rows"] * width
    functions = ["struct __align__(16) ResearchFloat4 { float x, y, z, w; };\n"]
    variants = []
    if case["family"] == "bias_silu":
        for operation in ("copy", "bias", "exp", "bias_silu", "exp-expf", "silu-expf"):
            for threads in (32, 64, 128, 256):
                name = f"{operation}-{threads}"
                entry = name.replace("-", "_")
                exponential = "expf" if operation.endswith("expf") else "__expf"
                expression = {
                    "copy": "x[index]",
                    "bias": "value",
                    "exp": "exponential",
                    "exp-expf": "exponential",
                }.get(operation, "value >= 0.f ? value / (1.f + exponential) : value * exponential / (1.f + exponential)")
                functions.append(f'''
extern "C" __global__ void {entry}(const float* x, const float* bias, float* output) {{
    unsigned base = blockIdx.x * 1024;
    #pragma unroll
    for (unsigned part = 0; part < {1024 // threads}; ++part) {{
        unsigned index = base + threadIdx.x + part * {threads};
        if (index < {count}) {{
            float value = x[index] + bias[index % {width}];
            float exponential = {exponential}(-fabsf(value));
            output[index] = {expression};
        }}
    }}
}}
''')
                variants.append((name, entry, threads, (count + 1023) // 1024, operation))
        for operation in ("copy", "bias", "exp", "bias_silu"):
            name = operation + "-vector4"
            entry = name.replace("-", "_")
            assignments = []
            for component in "xyzw":
                value = f"value.{component} + weights.{component}"
                expression = {
                    "copy": f"value.{component}",
                    "bias": "combined",
                    "exp": "exponential",
                    "bias_silu": "combined >= 0.f ? combined / (1.f + exponential) : combined * exponential / (1.f + exponential)",
                }[operation]
                assignments.append(f"{{ float combined = {value}; float exponential = __expf(-fabsf(combined)); answer.{component} = {expression}; }}")
            functions.append(f'''
extern "C" __global__ void {entry}(const float* x, const float* bias, float* output) {{
    unsigned index = blockIdx.x * 256 + threadIdx.x;
    if (index < {count // 4}) {{
        ResearchFloat4 value = reinterpret_cast<const ResearchFloat4*>(x)[index];
        ResearchFloat4 weights = reinterpret_cast<const ResearchFloat4*>(bias)[index % {width // 4}];
        ResearchFloat4 answer;
        {chr(10).join(assignments)}
        reinterpret_cast<ResearchFloat4*>(output)[index] = answer;
    }}
}}
''')
            variants.append((name, entry, 256, (count + 1023) // 1024, operation))
    else:
        if case["family"] == "rmsnorm":
            body = f'''
    float total = 0;
    for (unsigned c = lane; c < {width}; c += 32) {{
        float value = x[row * {width} + c];
        total += value * value;
    }}
    for (unsigned delta = 16; delta > 0; delta /= 2)
        total += __shfl_down_sync(0xffffffff, total, delta);
    total = __shfl_sync(0xffffffff, total, 0);
    float inverse = rsqrtf(total / {width} + 1e-5f);
    for (unsigned c = lane; c < {width}; c += 32)
        output[row * {width} + c] = x[row * {width} + c] * inverse * bias[c];
'''
        else:
            body = f'''
    float maximum = -3.402823466e38f;
    for (unsigned c = lane; c < {width}; c += 32)
        maximum = fmaxf(maximum, x[row * {width} + c]);
    for (unsigned delta = 16; delta > 0; delta /= 2)
        maximum = fmaxf(maximum, __shfl_down_sync(0xffffffff, maximum, delta));
    maximum = __shfl_sync(0xffffffff, maximum, 0);
    float total = 0;
    for (unsigned c = lane; c < {width}; c += 32)
        total += __expf(x[row * {width} + c] - maximum);
    for (unsigned delta = 16; delta > 0; delta /= 2)
        total += __shfl_down_sync(0xffffffff, total, delta);
    total = __shfl_sync(0xffffffff, total, 0);
    for (unsigned c = lane; c < {width}; c += 32)
        output[row * {width} + c] = __expf(x[row * {width} + c] - maximum) / total;
'''
        for threads in (32, 128, 256):
            candidate = body.replace("c = lane", "c = threadIdx.x").replace("c += 32", f"c += {threads}")
            if threads > 32:
                candidate = candidate.replace("total = __shfl_sync(0xffffffff, total, 0);", f'''
    if (lane == 0) partialSum[threadIdx.x / 32] = total;
    __syncthreads();
    total = lane < {threads // 32} ? partialSum[lane] : 0;
    for (unsigned delta = 16; delta > 0; delta /= 2)
        total += __shfl_down_sync(0xffffffff, total, delta);
    total = __shfl_sync(0xffffffff, total, 0);
''')
                candidate = candidate.replace("maximum = __shfl_sync(0xffffffff, maximum, 0);", f'''
    if (lane == 0) partialMaximum[threadIdx.x / 32] = maximum;
    __syncthreads();
    maximum = lane < {threads // 32} ? partialMaximum[lane] : -3.402823466e38f;
    for (unsigned delta = 16; delta > 0; delta /= 2)
        maximum = fmaxf(maximum, __shfl_down_sync(0xffffffff, maximum, delta));
    maximum = __shfl_sync(0xffffffff, maximum, 0);
''')
            entry = f"wave{threads}"
            functions.append(f'extern "C" __global__ void {entry}(const float* x, const float* bias, float* output) {{\n'
                '__shared__ float partialSum[8]; __shared__ float partialMaximum[8];\n'
                'unsigned row = blockIdx.x; unsigned lane = threadIdx.x % 32;\n' + candidate + "\n}\n")
            variants.append((entry, entry, threads, case["rows"], case["family"]))
    return "\n".join(functions), variants


def expected_for(case, operation, folder):
    if operation in ("bias_silu", "silu-expf", "rmsnorm", "softmax"):
        return np.fromfile(folder / "expected.bin", dtype="<f4")
    values = np.fromfile(folder / "input.bin", dtype="<f4")
    if operation == "copy":
        return values
    bias = np.fromfile(folder / "auxiliary.bin", dtype="<f4")
    combined = values.reshape(-1, case["columns"]) + bias
    if operation == "bias":
        return combined.ravel()
    return np.exp(-np.abs(combined.astype(np.float64))).astype(np.float32).ravel()


def check(actual, expected, case):
    ratio = np.abs(actual.astype(np.float64) - expected) / (case["atol"] + case["rtol"] * np.abs(expected))
    assert np.isfinite(actual).all() and ratio.max() <= 1, (case["id"], float(ratio.max()))
    return float(ratio.max())


def run(fixtures, output):
    output.mkdir(parents=True, exist_ok=False)
    evidence = output / "research.json"
    evidence.write_text('{"Accepted": false}')
    script_hash = digest(Path(__file__))
    manifest = json.loads((fixtures / "manifest.json").read_text())
    for name, value in manifest["files"].items():
        assert digest(fixtures / name) == value, name
    context_anchor = torch.empty(1, device="cuda")
    compiler = CudaCompiler()
    samples, checks, clocks = [], [], []
    try:
        selected = ("bias_silu-4x128x0", "bias_silu-1024x4096x0", "rmsnorm-128x1024x0", "softmax-1024x4096x0")
        for case in manifest["cases"]:
            if case["id"] not in selected:
                continue
            source, variants = source_for(case)
            cuda_profile.SOURCE = source
            module = compiler.compile(output / case["id"], ["--fmad=false"])
            count = case["rows"] * case["columns"]
            values = torch.empty(count, device="cuda")
            bias = torch.empty(case["columns"], device="cuda")
            result = torch.empty_like(values)
            independent = [torch.empty_like(values) for _ in range(16)]
            plans = []
            for variant, entry, threads, groups, operation in variants:
                function = compiler.function(module, entry)
                layouts = [("single", 1, [result]), ("batch16", 16, [result])]
                if variant == "bias_silu-256":
                    layouts.append(("independent", 16, independent))
                for kind, repetitions, outputs in layouts:
                    # Argument objects must remain alive throughout graph capture.
                    words = [[ct.c_uint64(tensor.data_ptr()) for tensor in (values, bias, destination)]
                        for destination in outputs]
                    parameters = [(ct.c_void_p * 3)(*(ct.addressof(word) for word in row)) for row in words]

                    def launch():
                        for index in range(repetitions):
                            compiler.check(compiler.driver.cuLaunchKernel(function, groups, 1, 1,
                                threads, 1, 1, 0, torch.cuda.current_stream().cuda_stream,
                                parameters[index % len(parameters)], None))

                    launch()
                    torch.cuda.synchronize()
                    begin = torch.cuda.Event(enable_timing=True, external=True)
                    end = torch.cuda.Event(enable_timing=True, external=True)
                    graph = torch.cuda.CUDAGraph()
                    with torch.cuda.graph(graph):
                        begin.record()
                        launch()
                        end.record()
                    for distribution in manifest["distributions"]:
                        folder = fixtures / case["id"] / distribution
                        values.copy_(torch.from_numpy(np.fromfile(folder / "input.bin", dtype="<f4")))
                        bias.copy_(torch.from_numpy(np.fromfile(folder / "auxiliary.bin", dtype="<f4")))
                        for destination in outputs:
                            destination.fill_(float("nan"))
                        graph.replay()
                        expected = expected_for(case, operation, folder)
                        for output_index, destination in enumerate(outputs):
                            ratio = check(destination.cpu().numpy(), expected, case)
                            checks.append(dict(Case=case["id"], Variant=variant, Name=kind,
                                Distribution=distribution, OutputIndex=output_index, MaximumToleranceRatio=ratio))
                    plans.append((variant, kind, repetitions, graph, begin, end))
            folder = fixtures / case["id"] / "normal"
            values.copy_(torch.from_numpy(np.fromfile(folder / "input.bin", dtype="<f4")))
            bias.copy_(torch.from_numpy(np.fromfile(folder / "auxiliary.bin", dtype="<f4")))
            for sample in range(-10, 40):
                for variant, kind, repetitions, graph, begin, end in (plans if sample % 2 == 0 else reversed(plans)):
                    start = time.perf_counter_ns()
                    graph.replay()
                    torch.cuda.synchronize()
                    wall = (time.perf_counter_ns() - start) / 1000
                    gpu = begin.elapsed_time(end) * 1000 / repetitions
                    assert gpu > 0 and np.isfinite(gpu)
                    if sample >= 0:
                        samples.append(dict(Case=case["id"], Variant=variant, Name=kind,
                            Repetitions=repetitions, Sample=sample, WallMicroseconds=wall, GpuMicroseconds=gpu))
            clocks.append(dict(Case=case["id"], Snapshot=subprocess.run(["nvidia-smi",
                "--query-gpu=name,pstate,clocks.sm,clocks.mem,power.draw,temperature.gpu", "--format=csv"],
                capture_output=True, text=True, check=True).stdout))
            print(case["id"], "CUDA research checks and timings passed", flush=True)
            del plans, independent, values, bias, result
        assert digest(Path(__file__)) == script_hash
        evidence.write_text(json.dumps(dict(Accepted=True, Device=torch.cuda.get_device_name(),
            ScriptSha256=script_hash, CompilerScriptSha256=digest(Path(cuda_profile.__file__)),
            ManifestSha256=digest(fixtures / "manifest.json"), NvrtcSha256=digest(compiler.nvrtc_path),
            Precision="FP32; fmad disabled; __expf and expf explicitly separate; no global fast-math",
            Boundary="Event nodes inside single/16-kernel graph; wall includes replay and synchronization",
            ArtifactHashes={str(path.relative_to(output)): digest(path) for path in sorted(output.rglob("*"))
                if path.is_file() and path != evidence},
            Checks=checks, Samples=samples, Clocks=clocks), indent=2))
    finally:
        compiler.close()
        del context_anchor


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("fixtures", type=Path)
    parser.add_argument("output", type=Path)
    arguments = parser.parse_args()
    run(arguments.fixtures.resolve(), arguments.output.resolve())
