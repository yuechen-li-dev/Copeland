"""Freeze a custom diagnostic suite before timing; this is not a KernelBench score."""

import argparse
import hashlib
import json
from pathlib import Path

import numpy as np


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def reference(case, values):
    x = values["input"].astype(np.float64)
    auxiliary = values["auxiliary"].astype(np.float64)
    family = case["family"]
    if family in ("gemm", "gemm_bias_silu"):
        result = x @ auxiliary
        if family == "gemm":
            return result
        x = result
        auxiliary = values["bias"].astype(np.float64)
    if family in ("bias_silu", "gemm_bias_silu"):
        x = x + auxiliary
        return x * np.exp(-np.logaddexp(0, -x))
    if family == "softmax":
        exponential = np.exp(x - np.max(x, axis=1, keepdims=True))
        return exponential / np.sum(exponential, axis=1, keepdims=True)
    if family == "rmsnorm":
        return x / np.sqrt(np.mean(x * x, axis=1, keepdims=True) + 1e-5) * auxiliary
    raise ValueError(family)


def generate(output):
    output.mkdir(parents=True, exist_ok=True)
    cases = []
    for family in ("bias_silu", "softmax", "rmsnorm", "gemm", "gemm_bias_silu"):
        matrix = family.startswith("gemm")
        shapes = [(16, 64, 128), (128, 256, 512), (512, 512, 1024)] if matrix else [
            (4, 128, 0), (128, 1024, 0), (1024, 4096, 0)
        ]
        shapes.append((17, 67, 131) if matrix else (7, 193, 0))
        for index, (rows, columns, inner) in enumerate(shapes):
            cases.append(dict(
                id=f"{family}-{rows}x{columns}x{inner}", family=family,
                rows=rows, columns=columns, inner=inner,
                split="heldout-shape" if index == 3 else "timed",
                atol=2e-4 if matrix else 2e-5,
                rtol=2e-4 if matrix else 2e-5,
            ))
    manifest = dict(
        schema="aurelian.kernel-comparison.v1", officialKernelBench=False,
        precision="fp32-ieee; TF32 disabled; no reduced-precision matmul",
        seed=20261019, warmups=8, samples=40, repetitions=16,
        cachePolicy="warm resident inputs, no deliberate cache flush",
        distributions=["normal", "uniform", "shifted", "alternating"], cases=cases,
        files={},
    )
    rng = np.random.default_rng(manifest["seed"])
    for case in cases:
        for distribution in manifest["distributions"]:
            matrix = case["family"].startswith("gemm")
            rows, columns, inner = case["rows"], case["columns"], case["inner"]
            shape = (rows, inner if matrix else columns)
            x = rng.standard_normal(shape).astype("<f4")
            if distribution == "uniform":
                x = rng.uniform(-3, 3, shape).astype("<f4")
            elif distribution == "shifted":
                x = (x * 0.1 + (100 if not matrix else 2)).astype("<f4")
            elif distribution == "alternating":
                x.flat[::2] *= -8
                x.flat[1::2] *= 0.01
            auxiliary = rng.standard_normal((inner, columns) if matrix else (1, columns)).astype("<f4")
            if matrix:
                auxiliary *= np.float32(1 / np.sqrt(inner))
            values = dict(input=x, auxiliary=auxiliary)
            if case["family"] == "gemm_bias_silu":
                values["bias"] = rng.uniform(-1, 1, (1, columns)).astype("<f4")
            expected = reference(case, values).astype("<f4")
            values["expected"] = expected
            folder = output / case["id"] / distribution
            folder.mkdir(parents=True, exist_ok=True)
            for name, array in values.items():
                path = folder / f"{name}.bin"
                array.tofile(path)
                manifest["files"][path.relative_to(output).as_posix()] = digest(path)
    target = output / "manifest.json"
    target.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Frozen {len(cases)} cases, {len(manifest['files'])} files: {digest(target)}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path)
    generate(parser.parse_args().output)
