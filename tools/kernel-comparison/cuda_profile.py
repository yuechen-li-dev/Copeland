"""Matched CUDA C controls compiled by NVRTC, using the same FP32 lane mapping.

PyTorch supplies allocations, events and graph capture only. Kernel code, compilation,
module loading and launch use NVRTC and the CUDA driver directly.
"""

import argparse
import ctypes as ct
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time

import numpy as np
import torch


SOURCE = r'''
extern "C" __global__ void empty_kernel() {}
extern "C" __global__ void bias_silu(const float* x, const float* bias,
    float* output, unsigned count, unsigned width) {
    unsigned base = blockIdx.x * 1024;
    #pragma unroll
    for (unsigned part = 0; part < 4; ++part) {
        unsigned index = base + threadIdx.x + part * 256;
        if (index < count) {
            float value = x[index] + bias[index % width];
            #ifdef HOIST_EXP
                float exponential = EXP(-fabsf(value));
                output[index] = value >= 0.f ? value / (1.f + exponential)
                    : value * exponential / (1.f + exponential);
            #else
                if (value >= 0.f) {
                    output[index] = value / (1.f + EXP(-value));
                } else {
                    float exponential = EXP(value);
                    output[index] = value * exponential / (1.f + exponential);
                }
            #endif
        }
    }
}
'''


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


class CudaCompiler:
    def __init__(self):
        library = Path(torch.__file__).parent / "lib"
        self.library_handle = os.add_dll_directory(str(library))
        self.nvrtc_path = library / "nvrtc64_120_0.dll"
        self.nvrtc = ct.CDLL(str(self.nvrtc_path))
        self.driver = ct.WinDLL("nvcuda.dll")
        self.modules = []
        self.nvrtc.nvrtcCreateProgram.argtypes = [ct.POINTER(ct.c_void_p), ct.c_char_p,
            ct.c_char_p, ct.c_int, ct.c_void_p, ct.c_void_p]
        self.nvrtc.nvrtcCompileProgram.argtypes = [ct.c_void_p, ct.c_int, ct.POINTER(ct.c_char_p)]
        for name in ("nvrtcGetPTXSize", "nvrtcGetProgramLogSize"):
            getattr(self.nvrtc, name).argtypes = [ct.c_void_p, ct.POINTER(ct.c_size_t)]
        for name in ("nvrtcGetPTX", "nvrtcGetProgramLog"):
            getattr(self.nvrtc, name).argtypes = [ct.c_void_p, ct.c_void_p]
        self.nvrtc.nvrtcDestroyProgram.argtypes = [ct.POINTER(ct.c_void_p)]
        self.driver.cuModuleLoadData.argtypes = [ct.POINTER(ct.c_void_p), ct.c_void_p]
        self.driver.cuModuleUnload.argtypes = [ct.c_void_p]
        self.driver.cuModuleGetFunction.argtypes = [ct.POINTER(ct.c_void_p), ct.c_void_p, ct.c_char_p]
        self.driver.cuLaunchKernel.argtypes = [ct.c_void_p] + [ct.c_uint] * 7 + [
            ct.c_void_p, ct.POINTER(ct.c_void_p), ct.c_void_p]
        self.driver.cuCtxSynchronize.argtypes = []

    @staticmethod
    def check(result):
        if result != 0:
            raise RuntimeError(f"CUDA/NVRTC status {result}")

    def compile(self, destination, options):
        destination.mkdir()
        (destination / "kernel.cu").write_text(SOURCE)
        program = ct.c_void_p()
        self.check(self.nvrtc.nvrtcCreateProgram(ct.byref(program), SOURCE.encode(), b"kernel.cu", 0, None, None))
        try:
            options = ["--gpu-architecture=compute_86", "--std=c++17"] + options
            arguments = (ct.c_char_p * len(options))(*(option.encode() for option in options))
            status = self.nvrtc.nvrtcCompileProgram(program, len(options), arguments)
            size = ct.c_size_t()
            self.check(self.nvrtc.nvrtcGetProgramLogSize(program, ct.byref(size)))
            log = ct.create_string_buffer(size.value)
            self.check(self.nvrtc.nvrtcGetProgramLog(program, log))
            (destination / "compile.log").write_bytes(log.value)
            (destination / "options.json").write_text(json.dumps(options))
            self.check(status)
            self.check(self.nvrtc.nvrtcGetPTXSize(program, ct.byref(size)))
            ptx = ct.create_string_buffer(size.value)
            self.check(self.nvrtc.nvrtcGetPTX(program, ptx))
            (destination / "kernel.ptx").write_bytes(ptx.value)
            module = ct.c_void_p()
            self.check(self.driver.cuModuleLoadData(ct.byref(module), ptx))
            self.modules.append(module)
            return module
        finally:
            self.check(self.nvrtc.nvrtcDestroyProgram(ct.byref(program)))

    def function(self, module, name):
        function = ct.c_void_p()
        self.check(self.driver.cuModuleGetFunction(ct.byref(function), module, name.encode()))
        return function

    def launch(self, function, groups, stream, parameters=None):
        self.check(self.driver.cuLaunchKernel(function, groups, 1, 1, 256, 1, 1, 0,
            stream, parameters, None))

    def close(self):
        torch.cuda.synchronize()
        for module in self.modules:
            self.check(self.driver.cuModuleUnload(module))
        self.library_handle.close()


def run(fixtures, output):
    if output.exists():
        raise FileExistsError(output)
    output.mkdir(parents=True)
    evidence = output / "cuda-profile.json"
    evidence.write_text('{"Accepted":false}')
    manifest = json.loads((fixtures / "manifest.json").read_text())
    for name, expected in manifest["files"].items():
        assert digest(fixtures / name) == expected, name
    torch.cuda.init()
    context_anchor = torch.empty(1, device="cuda")
    compiler = CudaCompiler()
    measurements, checks, replay_checks, empty, clocks = [], [], [], [], []

    def record_clocks(phase):
        clocks.append(dict(Phase=phase, NvidiaSmi=subprocess.check_output(["nvidia-smi",
            "--query-gpu=name,pci.bus_id,pstate,clocks.sm,clocks.mem,power.draw,temperature.gpu", "--format=csv"], text=True)))

    try:
        configurations = [("stable-expf", ["-DEXP=expf"]),
            ("stable-fast-exp", ["-DEXP=__expf"]),
            ("hoisted-fast-exp", ["-DEXP=__expf", "-DHOIST_EXP"])]
        functions = []
        for name, options in configurations:
            module = compiler.compile(output / name, options)
            functions.append((name, compiler.function(module, "bias_silu")))
        stream = torch.cuda.current_stream().cuda_stream
        record_clocks("before-empty")
        noop = compiler.function(compiler.modules[0], "empty_kernel")
        for sample in range(-50, 500):
            start = time.perf_counter_ns()
            compiler.launch(noop, 1, stream)
            compiler.check(compiler.driver.cuCtxSynchronize())
            elapsed = (time.perf_counter_ns() - start) / 1000
            if sample >= 0:
                empty.append(dict(Sample=sample, WallMicroseconds=elapsed))
        cases = [case for case in manifest["cases"] if case["family"] == "bias_silu"]
        for case in cases:
            count, width = case["rows"] * case["columns"], case["columns"]
            x = torch.empty(count, device="cuda")
            bias = torch.empty(width, device="cuda")
            result = torch.empty_like(x)
            words = [ct.c_uint64(tensor.data_ptr()) for tensor in (x, bias, result)]
            words += [ct.c_uint(count), ct.c_uint(width)]
            parameters = (ct.c_void_p * len(words))(*(ct.addressof(word) for word in words))
            groups = (count + 1023) // 1024
            graphs = []
            for name, function in functions:
                def launch():
                    compiler.launch(function, groups, torch.cuda.current_stream().cuda_stream, parameters)

                for distribution in manifest["distributions"]:
                    folder = fixtures / case["id"] / distribution
                    x.copy_(torch.from_numpy(np.fromfile(folder / "input.bin", dtype="<f4")))
                    bias.copy_(torch.from_numpy(np.fromfile(folder / "auxiliary.bin", dtype="<f4")))
                    launch()
                    actual = result.cpu().numpy()
                    expected = np.fromfile(folder / "expected.bin", dtype="<f4")
                    ratio = np.abs(actual.astype(np.float64) - expected) / (case["atol"] + case["rtol"] * np.abs(expected))
                    assert np.isfinite(actual).all() and ratio.max() <= 1, (case["id"], name, ratio.max())
                    checks.append(dict(Case=case["id"], Variant=name, Distribution=distribution,
                        MaximumToleranceRatio=float(ratio.max())))
                folder = fixtures / case["id"] / "normal"
                x.copy_(torch.from_numpy(np.fromfile(folder / "input.bin", dtype="<f4")))
                bias.copy_(torch.from_numpy(np.fromfile(folder / "auxiliary.bin", dtype="<f4")))
                for _ in range(20):
                    launch()
                torch.cuda.synchronize()
                single = torch.cuda.CUDAGraph()
                with torch.cuda.graph(single):
                    launch()
                repeated = torch.cuda.CUDAGraph()
                begin = torch.cuda.Event(enable_timing=True, external=True)
                end = torch.cuda.Event(enable_timing=True, external=True)
                with torch.cuda.graph(repeated):
                    begin.record()
                    for _ in range(16):
                        launch()
                    end.record()
                for distribution in manifest["distributions"]:
                    folder = fixtures / case["id"] / distribution
                    x.copy_(torch.from_numpy(np.fromfile(folder / "input.bin", dtype="<f4")))
                    bias.copy_(torch.from_numpy(np.fromfile(folder / "auxiliary.bin", dtype="<f4")))
                    expected = np.fromfile(folder / "expected.bin", dtype="<f4")
                    for kind, graph in (("single", single), ("batch16", repeated)):
                        result.fill_(float("nan"))
                        graph.replay()
                        actual = result.cpu().numpy()
                        ratio = np.abs(actual.astype(np.float64) - expected) / (case["atol"] + case["rtol"] * np.abs(expected))
                        assert np.isfinite(actual).all() and ratio.max() <= 1, (case["id"], name, kind, ratio.max())
                        replay_checks.append(dict(Case=case["id"], Variant=name, Kind=kind,
                            Distribution=distribution, MaximumToleranceRatio=float(ratio.max())))
                folder = fixtures / case["id"] / "normal"
                x.copy_(torch.from_numpy(np.fromfile(folder / "input.bin", dtype="<f4")))
                bias.copy_(torch.from_numpy(np.fromfile(folder / "auxiliary.bin", dtype="<f4")))
                graphs.append((name, single, repeated, begin, end))
            for sample in range(100):
                for name, single, repeated, begin, end in (graphs if sample % 2 == 0 else reversed(graphs)):
                    start = time.perf_counter_ns()
                    single.replay()
                    torch.cuda.synchronize()
                    wall = (time.perf_counter_ns() - start) / 1000
                    repeated.replay()
                    torch.cuda.synchronize()
                    gpu = begin.elapsed_time(end) * 1000 / 16
                    assert gpu > 0
                    measurements.append(dict(Case=case["id"], Variant=name, Sample=sample,
                        WallMicroseconds=wall, GpuMicroseconds=gpu))
            print(case["id"], "CUDA C controls passed", flush=True)
            record_clocks(case["id"])
        assert len(checks) == 4 * 3 * 4
        assert len(replay_checks) == 4 * 3 * 4 * 2
        evidence.write_text(json.dumps(dict(Accepted=True, Device=torch.cuda.get_device_name(),
            ManifestSha256=digest(fixtures / "manifest.json"), ScriptSha256=digest(Path(__file__)),
            NvrtcSha256=digest(compiler.nvrtc_path), TorchVersion=torch.__version__,
            CompilationHashes={str(path.relative_to(output)): digest(path)
                for path in sorted(output.rglob("*")) if path.is_file() and path != evidence},
            Precision="FP32; expf and __expf variants qualified separately; no fast-math flag",
            Boundary="Single kernel CUDA graph replay to synchronization; event nodes inside 16-operation graph",
            ClockPolicy="Unmanaged; active desktop", Clocks=clocks, EmptyLaunch=empty, Checks=checks,
            ReplayChecks=replay_checks, Samples=measurements), indent=2))
    finally:
        compiler.close()
        del context_anchor


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("fixtures", type=Path)
    parser.add_argument("output", type=Path)
    arguments = parser.parse_args()
    run(arguments.fixtures.resolve(), arguments.output.resolve())
