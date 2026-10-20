"""Fail-closed integrity checks and compact tables for the Vulkan profiling controls."""

import argparse
from collections import Counter
import hashlib
import json
import math
from pathlib import Path
import statistics


CASES = ["bias_silu-4x128x0", "bias_silu-1024x4096x0", "softmax-1024x4096x0",
    "rmsnorm-128x1024x0", "gemm-512x512x1024", "gemm-17x67x131"]
COMPILERS = ["dxc", "dxc-spirv-opt", "glslang", "dxc-O0", "dxc-static-params"]
PLANS = ["timestamped", "untimestamped", "batch16", "host-parameters", "host-batch16"]
DISTRIBUTIONS = ["normal", "uniform", "shifted", "alternating"]
CUDA_CASES = ["bias_silu-4x128x0", "bias_silu-128x1024x0", "bias_silu-1024x4096x0", "bias_silu-7x193x0"]
CUDA_VARIANTS = ["stable-expf", "stable-fast-exp", "hoisted-fast-exp"]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def compilers(case):
    return COMPILERS + (["dxc-hoisted-exp"] if case.startswith("bias_silu") else [])


def require(condition, message):
    if not condition:
        raise ValueError(message)


def positive(value):
    return isinstance(value, (int, float)) and math.isfinite(value) and value > 0


def verify_native(data, directory, repository):
    require(data["Accepted"], f"Rejected native run: {directory}")
    for name, expected in data["SourceHashes"].items():
        require(digest(repository / name) == expected, f"Changed measured source: {name}")
    expected = Counter((case, compiler, plan, spin, sample) for case in CASES
        for compiler in compilers(case) for plan in PLANS for spin in (0, 100_000) for sample in range(60))
    actual = Counter((x["Case"], x["Compiler"], x["Name"], x["SpinNanoseconds"], x["Sample"]) for x in data["Samples"])
    require(actual == expected, f"Incomplete or duplicate native timing grid: {directory}")
    expected_checks = Counter((case, compiler, plan, distribution) for case in CASES
        for compiler in compilers(case) for plan in PLANS for distribution in DISTRIBUTIONS)
    actual_checks = Counter((x["Case"], x["Compiler"], x["Name"], x["Distribution"]) for x in data["Checks"])
    require(actual_checks == expected_checks, f"Incomplete native correctness grid: {directory}")
    for check in data["Checks"]:
        require(0 <= check["MaximumToleranceRatio"] <= 1, f"Failed correctness: {check}")
    for sample in data["Samples"]:
        require(positive(sample["WallMicroseconds"]), "Invalid native wall time")
        require(sample["GpuMicroseconds"] is None if sample["Name"] == "untimestamped"
            else positive(sample["GpuMicroseconds"]), "Invalid GPU timestamp")
        profile = sample["Profile"]
        values = list(profile["Submission"].values()) + [profile["ValidationMicroseconds"], profile["TimestampReadMicroseconds"]]
        require(all(math.isfinite(value) and value >= 0 for value in values), "Invalid CPU profile")
    expected_empty = Counter((mode, sample) for mode in ("owner-blocking", "owner-spin", "raw-fence", "raw-idle") for sample in range(500))
    require(Counter((x["Mode"], x["Sample"]) for x in data["EmptySubmissions"]) == expected_empty,
        "Incomplete empty submission grid")
    for sample in data["EmptySubmissions"]:
        require(positive(sample["WallMicroseconds"]), "Invalid empty Vulkan wall time")
        require(all(math.isfinite(value) and value >= 0 for value in sample["Phases"].values()),
            "Invalid empty Vulkan CPU phase")
    for case in CASES:
        controls = [x for x in data["Compilation"] if x["Case"] == case]
        require(Counter(x["Compiler"] for x in controls) == Counter(compilers(case)), "Incomplete compiler grid")
        require(len({x["HlslSha256"] for x in controls if x["Compiler"] in COMPILERS[:4]}) == 1,
            "Compiler controls received different HLSL")
        for shader in controls:
            path = directory / "shaders" / case / shader["Compiler"] / (shader["Shader"] + ".spv")
            require(digest(path) == shader["SpirvSha256"], f"Changed SPIR-V: {path}")


def verify_cuda(data, repository, directory):
    require(data["Accepted"], "Rejected CUDA profile")
    require(data["ScriptSha256"] == digest(repository / "tools/kernel-comparison/cuda_profile.py"), "CUDA script changed")
    for name, expected in data["CompilationHashes"].items():
        require(digest(directory / name) == expected, f"Changed CUDA compilation artifact: {name}")
    expected = Counter((case, variant, sample) for case in CUDA_CASES for variant in CUDA_VARIANTS for sample in range(100))
    require(Counter((x["Case"], x["Variant"], x["Sample"]) for x in data["Samples"]) == expected, "Incomplete CUDA timing grid")
    expected_checks = Counter((case, variant, distribution) for case in CUDA_CASES for variant in CUDA_VARIANTS for distribution in DISTRIBUTIONS)
    require(Counter((x["Case"], x["Variant"], x["Distribution"]) for x in data["Checks"]) == expected_checks, "Incomplete CUDA correctness grid")
    expected_replay = Counter((case, variant, distribution, kind) for case in CUDA_CASES for variant in CUDA_VARIANTS
        for distribution in DISTRIBUTIONS for kind in ("single", "batch16"))
    require(Counter((x["Case"], x["Variant"], x["Distribution"], x["Kind"]) for x in data["ReplayChecks"]) == expected_replay,
        "Incomplete changed-input CUDA graph validation")
    for check in data["Checks"] + data["ReplayChecks"]:
        require(0 <= check["MaximumToleranceRatio"] <= 1, "CUDA correctness failed")
    for sample in data["Samples"]:
        require(positive(sample["WallMicroseconds"]) and positive(sample["GpuMicroseconds"]), "Invalid CUDA timing")
    require(Counter(x["Sample"] for x in data["EmptyLaunch"]) == Counter(range(500)), "Incomplete empty CUDA launch grid")
    require(all(positive(x["WallMicroseconds"]) for x in data["EmptyLaunch"]), "Invalid empty CUDA timing")


def summarize(root):
    repository = Path(__file__).resolve().parents[2]
    (root / "summary.json").write_text('{"Accepted":false}')
    protocol = json.loads((root / "protocol.json").read_text(encoding="utf-8-sig"))
    native, cuda, provenance = [], [], []
    for run in range(1, protocol["Rounds"] + 1):
        for mode in ("vulkan", "vulkan-cuda-context"):
            directory = root / f"run{run}" / mode
            path = directory / "profile.json"
            data = json.loads(path.read_text())
            verify_native(data, directory, repository)
            require(data["CudaContextRetained"] == (mode == "vulkan-cuda-context"), "Incorrect context control")
            native.append((run, mode, data))
            provenance.append(dict(Path=str(path.relative_to(root)), Sha256=digest(path),
                Clocks=data["Clocks"], Checks=len(data["Checks"])))
        path = root / f"run{run}" / "cuda/cuda-profile.json"
        data = json.loads(path.read_text())
        verify_cuda(data, repository, path.parent)
        cuda.append((run, data))
        provenance.append(dict(Path=str(path.relative_to(root)), Sha256=digest(path),
            Clocks=data["Clocks"],
            Checks=len(data["Checks"]) + len(data["ReplayChecks"])))
    manifest_hashes = {data["ManifestSha256"] for _, _, data in native} | {data["ManifestSha256"] for _, data in cuda}
    require(len(manifest_hashes) == 1, "Different fixture manifests")
    require(all(not data["Validation"] for _, _, data in native), "Validation instrumentation in timing run")
    require(all(data["SourceHashes"] == native[0][2]["SourceHashes"]
        and data["BinaryHashes"] == native[0][2]["BinaryHashes"] for _, _, data in native),
        "Native runs used different sources or assemblies")
    rows = []
    for mode in ("vulkan", "vulkan-cuda-context"):
        for case in CASES:
            for compiler in compilers(case):
                for plan in PLANS:
                    for spin in (0, 100_000):
                        per_run = []
                        phases = []
                        for run, current_mode, data in native:
                            if mode != current_mode:
                                continue
                            samples = [x for x in data["Samples"] if x["Case"] == case
                                and x["Compiler"] == compiler and x["Name"] == plan and x["SpinNanoseconds"] == spin]
                            gpu = [x["GpuMicroseconds"] for x in samples if x["GpuMicroseconds"] is not None]
                            per_run.append(dict(Run=run, WallMicroseconds=statistics.median(x["WallMicroseconds"] for x in samples),
                                GpuMicroseconds=statistics.median(gpu) if gpu else None))
                            phases.extend(x["Profile"] for x in samples)
                        rows.append(dict(Mode=mode, Case=case, Compiler=compiler, Plan=plan, SpinNanoseconds=spin,
                            WallMicroseconds=statistics.median(x["WallMicroseconds"] for x in per_run),
                            GpuMicroseconds=statistics.median(x["GpuMicroseconds"] for x in per_run) if per_run[0]["GpuMicroseconds"] is not None else None,
                            PerRun=per_run, CpuPhaseMedians=dict(
                                Validation=statistics.median(x["ValidationMicroseconds"] for x in phases),
                                Preparation=statistics.median(x["Submission"]["PreparationMicroseconds"] for x in phases),
                                QueueSubmit=statistics.median(x["Submission"]["QueueSubmitMicroseconds"] for x in phases),
                                CompletionWait=statistics.median(x["Submission"]["CompletionWaitMicroseconds"] for x in phases),
                                QueryRead=statistics.median(x["TimestampReadMicroseconds"] for x in phases))))
    cuda_rows = []
    for case in CUDA_CASES:
        for variant in CUDA_VARIANTS:
            per_run = []
            for run, data in cuda:
                samples = [x for x in data["Samples"] if x["Case"] == case and x["Variant"] == variant]
                per_run.append(dict(Run=run, WallMicroseconds=statistics.median(x["WallMicroseconds"] for x in samples),
                    GpuMicroseconds=statistics.median(x["GpuMicroseconds"] for x in samples)))
            cuda_rows.append(dict(Case=case, Variant=variant, PerRun=per_run,
                WallMicroseconds=statistics.median(x["WallMicroseconds"] for x in per_run),
                GpuMicroseconds=statistics.median(x["GpuMicroseconds"] for x in per_run)))
    empty_rows = []
    for context in ("vulkan", "vulkan-cuda-context"):
        for mode in ("owner-blocking", "owner-spin", "raw-fence", "raw-idle"):
            per_run = [statistics.median(x["WallMicroseconds"] for x in data["EmptySubmissions"] if x["Mode"] == mode)
                for _, current, data in native if current == context]
            empty_rows.append(dict(Context=context, Mode=mode, WallMicroseconds=statistics.median(per_run), PerRun=per_run))
    result = dict(Accepted=True, Rounds=protocol["Rounds"], Provenance=provenance,
        SummaryScriptSha256=digest(Path(__file__)),
        Native=rows, Cuda=cuda_rows, Empty=empty_rows,
        CudaEmptyMicroseconds=statistics.median(statistics.median(x["WallMicroseconds"] for x in data["EmptyLaunch"]) for _, data in cuda),
        TimingSamples=sum(len(data["Samples"]) + len(data["EmptySubmissions"]) for _, _, data in native)
            + sum(len(data["Samples"]) + len(data["EmptyLaunch"]) for _, data in cuda),
        CorrectnessChecks=sum(x["Checks"] for x in provenance))
    (root / "summary.json").write_text(json.dumps(result, indent=2) + "\n")

    def native_row(case, plan="batch16", mode="vulkan-cuda-context", compiler="dxc", spin=0):
        return next(x for x in rows if x["Case"] == case and x["Plan"] == plan and x["Mode"] == mode
            and x["Compiler"] == compiler and x["SpinNanoseconds"] == spin)

    lines = ["# Vulkan profiling controls", "", "All times below are microseconds, median of independent process medians.", "",
        "GPU timestamps cover 16 resident operations and barriers, divided by 16. CUDA event nodes are inside the graph.", "",
        "| Case | Vulkan alone, host parameters | Vulkan + CUDA refresh, host parameters | Vulkan + CUDA refresh, device parameters | CUDA C stable expf |",
        "|---|---:|---:|---:|---:|"]
    for case in CASES:
        matched = next((x for x in cuda_rows if x["Case"] == case and x["Variant"] == "stable-expf"), None)
        columns = [native_row(case, "host-batch16", "vulkan")["GpuMicroseconds"],
            native_row(case, "host-batch16")["GpuMicroseconds"], native_row(case)["GpuMicroseconds"]]
        cuda_value = f"{matched['GpuMicroseconds']:.2f}" if matched else "—"
        lines.append(f"| {case} | " + " | ".join(f"{value:.2f}" for value in columns) + f" | {cuda_value} |")
    lines += ["", "Compiler controls, device-local parameters and CUDA power-state refresh:", "",
        "| Case | DXC | DXC + spirv-opt | glslang | DXC O0 | Static parameters |",
        "|---|---:|---:|---:|---:|---:|"]
    for case in CASES:
        lines.append(f"| {case} | " + " | ".join(f"{native_row(case, compiler=compiler)['GpuMicroseconds']:.2f}" for compiler in COMPILERS) + " |")
    lines += ["", "Empty command submission plus synchronous completion (instrumented):", "",
        "| Control | Vulkan alone | Vulkan + CUDA context (before refresh loop) |", "|---|---:|---:|"]
    for mode in ("owner-blocking", "owner-spin", "raw-fence", "raw-idle"):
        values = [next(x["WallMicroseconds"] for x in empty_rows if x["Context"] == context and x["Mode"] == mode)
            for context in ("vulkan", "vulkan-cuda-context")]
        lines.append(f"| {mode} | {values[0]:.2f} | {values[1]:.2f} |")
    lines += ["", f"CUDA driver no-op launch plus context synchronization: {result['CudaEmptyMicroseconds']:.2f} µs.",
        "The CUDA control executes an empty kernel; the Vulkan command buffer contains no commands. This is a floor control, not identical GPU work.", "",
        "Native single-operation CPU phases are measured in a separate profiled replay; ordinary wall samples omit CPU phase instrumentation.",
        "Phase medians and per-process medians are retained in summary.json. All raw compiler, shader and correctness controls are retained.", "",
        f"Accepted: {result['TimingSamples']} timing samples; {result['CorrectnessChecks']} correctness checks.",
        "Unmanaged clocks and a live desktop remain limitations. Clock snapshots and the explicit completed CUDA no-op refresh outside native timing regions must accompany interpretation."]
    (root / "RESULTS.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("AURELIAN_VULKAN_PROFILE_SUMMARY_PASSED")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=Path)
    summarize(parser.parse_args().root.resolve())
