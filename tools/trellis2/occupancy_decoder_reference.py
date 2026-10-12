"""Optional offline oracle for the learned occupancy decoder.

Independent NumPy volume convolution/indexing; no Torch/CUDA, and never called
by the C#/Vulkan inference pipeline. Equations/layout follow the pinned Microsoft
TRELLIS sparse_structure_vae.py, norm.py and spatial.py; see TRELLIS-NOTICE.txt.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import struct
import time

os.environ.setdefault("OPENBLAS_NUM_THREADS", "8")
import numpy as np


def load_weights(path):
    with path.open("rb") as stream:
        header_size = struct.unpack("<Q", stream.read(8))[0]
        header = json.loads(stream.read(header_size))
    weights = {}
    for name, metadata in header.items():
        if name == "__metadata__":
            continue
        dtype = {"F32": "<f4", "F16": "<f2"}[metadata["dtype"]]
        weights[name] = np.memmap(
            path, dtype=dtype, mode="r",
            offset=8 + header_size + metadata["data_offsets"][0],
            shape=tuple(metadata["shape"]),
        )
    if len(weights) != 74:
        raise ValueError("Unexpected decoder inventory")
    return weights


def half(values):
    # FP32 storage with the same binary16 operation boundary as upstream.
    return values.astype(np.float16).astype(np.float32)


def normalize(values, weights, prefix):
    centered = values.astype(np.float64) - values.mean(axis=-1, keepdims=True, dtype=np.float64)
    normalized = (centered / np.sqrt(np.mean(centered * centered, axis=-1, keepdims=True) + 1e-5)).astype(np.float32)
    return normalized * weights[prefix + ".weight"] + weights[prefix + ".bias"]


def silu(values):
    with np.errstate(over="ignore"):
        return values / (1 + np.exp(-values))


def convolution(values, weights, prefix, rounded=True):
    started = time.monotonic()
    resolution, _, _, channels = values.shape
    weight = np.asarray(weights[prefix + ".weight"], dtype=np.float32)
    output_channels = weight.shape[0]
    weight = weight.reshape((output_channels, channels * 27))
    bias = np.asarray(weights[prefix + ".bias"], dtype=np.float32)
    padded = np.pad(values, ((1, 1), (1, 1), (1, 1), (0, 0)))
    # View is XYZ/channel/kX/kY/kZ; convolution weights are O/channel/kX/kY/kZ.
    windows = np.lib.stride_tricks.sliding_window_view(padded, (3, 3, 3), axis=(0, 1, 2))
    output = np.empty((resolution ** 3, output_channels), dtype=np.float32)
    plane = resolution * resolution
    # Copy slabs rather than materializing the whole 64^3 im2col volume.
    slab_width = max(1, 4096 // plane)
    for first_x in range(0, resolution, slab_width):
        stop_x = min(resolution, first_x + slab_width)
        columns = windows[first_x:stop_x].reshape((-1, channels * 27))
        projected = columns @ weight.T + bias
        if rounded:
            projected = half(projected)
        output[first_x * plane:stop_x * plane] = projected
    print(f"{prefix}: {resolution}^3 {channels}->{output_channels}, {time.monotonic() - started:.2f}s", flush=True)
    return output.reshape((resolution, resolution, resolution, output_channels))


def residual(values, weights, prefix):
    hidden = half(normalize(values, weights, prefix + ".norm1"))
    hidden = half(silu(hidden))
    hidden = convolution(hidden, weights, prefix + ".conv1")
    hidden = half(normalize(hidden, weights, prefix + ".norm2"))
    hidden = half(silu(hidden))
    hidden = convolution(hidden, weights, prefix + ".conv2")
    return half(values + hidden)


def upsample(values, weights, prefix):
    expanded = convolution(values, weights, prefix)
    resolution = expanded.shape[0]
    channels = expanded.shape[-1] // 8
    # Independent reshape/transpose form matching the upstream child-axis layout.
    expanded = expanded.reshape((resolution, resolution, resolution, channels, 2, 2, 2))
    return expanded.transpose((0, 4, 1, 5, 2, 6, 3)).reshape((resolution * 2,) * 3 + (channels,))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--weights", type=Path, required=True)
    parser.add_argument("--latent", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    evidence_path = args.output / "decoder-reference-evidence.json"
    evidence_path.write_text(json.dumps({"Accepted": False}))
    latent_bytes = args.latent.read_bytes()
    values = np.frombuffer(latent_bytes, dtype="<f4").reshape((16, 16, 16, 8)).copy()
    if not np.isfinite(values).all():
        raise ValueError("Nonfinite latent")
    weights = load_weights(args.weights)
    started = time.monotonic()
    values = convolution(values, weights, "input_layer")
    for prefix in ["middle_block.0", "middle_block.1", "blocks.0", "blocks.1"]:
        values = residual(values, weights, prefix)
    values = upsample(values, weights, "blocks.2.conv")
    for prefix in ["blocks.3", "blocks.4"]:
        values = residual(values, weights, prefix)
    values = upsample(values, weights, "blocks.5.conv")
    for prefix in ["blocks.6", "blocks.7"]:
        values = residual(values, weights, prefix)
    values = normalize(values, weights, "out_layer.0")
    values = silu(values)
    values = convolution(values, weights, "out_layer.2", rounded=False).reshape((-1,))
    values.astype("<f4").tofile(args.output / "reference-logits.f32")
    actual_bytes = (args.output / "logits.f32").read_bytes()
    actual = np.frombuffer(actual_bytes, dtype="<f4")
    difference = actual.astype(np.float64) - values
    relative_rms = float(np.sqrt(np.mean(difference ** 2)) / np.sqrt(np.mean(values.astype(np.float64) ** 2)))
    maximum = float(np.abs(difference).max())
    agreement = float(np.mean((actual > 0) == (values > 0)))
    accepted = bool(np.isfinite(values).all() and np.isfinite(actual).all() and relative_rms < .01 and agreement > .995)
    evidence = {
        "Accepted": accepted,
        "Scope": "Independent full 74-tensor FP16-boundary decoder; same sampled latent, not an independent full flow oracle",
        "LatentSha256": hashlib.sha256(latent_bytes).hexdigest(),
        "LogitSha256": hashlib.sha256(actual_bytes).hexdigest(),
        "MaximumAbsoluteError": maximum,
        "RelativeRmsError": relative_rms,
        "Occupancy64Agreement": agreement,
        "ReferenceActiveVoxels64": int(np.count_nonzero(values > 0)),
        "OracleWallSeconds": time.monotonic() - started,
        "Thresholds": {"RelativeRmsErrorLessThan": .01, "Occupancy64AgreementGreaterThan": .995},
    }
    evidence_path.write_text(json.dumps(evidence, indent=2))
    print(json.dumps(evidence, indent=2), flush=True)
    if not accepted:
        raise SystemExit("Decoder reference qualification failed")


if __name__ == "__main__":
    main()
