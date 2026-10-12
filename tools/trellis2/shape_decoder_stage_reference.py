"""Offline qualification of the actual first-resolution learned sparse decoder.

Independent CPU coordinate lookup, NumPy convolution/affines and FP16 operation
boundaries. No Torch/CUDA; never invoked from the C#/Vulkan inference path.
Equations from pinned Microsoft TRELLIS.2 sc_vaes/sparse_unet_vae.py and sparse
Channel2Spatial conventions. See TRELLIS-NOTICE.txt.
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


def weights_from_file(path):
    with path.open("rb") as stream:
        length = struct.unpack("<Q", stream.read(8))[0]
        header = json.loads(stream.read(length))
    weights = {}
    for name, item in header.items():
        dtype = {"F16": "<f2", "F32": "<f4"}[item["dtype"]]
        weights[name] = np.memmap(path, dtype=dtype, mode="r", offset=8 + length + item["data_offsets"][0], shape=tuple(item["shape"]))
    if len(weights) != 292:
        raise ValueError("Unexpected learned decoder inventory")
    return weights


def half(values):
    return values.astype(np.float16).astype(np.float32)


def affine(values, weights, prefix):
    return half(values @ np.asarray(weights[prefix + ".weight"], dtype=np.float32).T
                + np.asarray(weights[prefix + ".bias"], dtype=np.float32))


def convolution(values, neighbors, weights, prefix):
    started = time.monotonic()
    channels = values.shape[1]
    weight = np.asarray(weights[prefix + ".weight"], dtype=np.float32).reshape((channels, 27 * channels))
    bias = np.asarray(weights[prefix + ".bias"], dtype=np.float32)
    output = np.empty_like(values)
    for first in range(0, values.shape[0], 512):
        indices = neighbors[first:first + 512]
        columns = values[np.maximum(indices, 0)].copy()
        columns[indices < 0] = 0
        projected = columns.reshape((-1, 27 * channels)) @ weight.T + bias
        output[first:first + 512] = half(projected)
    print(f"{prefix}: {time.monotonic() - started:.2f}s", flush=True)
    return output


def normalize(values, weights, prefix):
    centered = values.astype(np.float64) - values.mean(axis=-1, keepdims=True, dtype=np.float64)
    norm = (centered / np.sqrt(np.mean(centered ** 2, axis=-1, keepdims=True) + 1e-6)).astype(np.float32)
    return half(norm * np.asarray(weights[prefix + ".weight"], dtype=np.float32)
                + np.asarray(weights[prefix + ".bias"], dtype=np.float32))


def neighbors_from_coordinates(coordinates):
    lookup = {tuple(coordinate): row for row, coordinate in enumerate(coordinates)}
    offsets = [(x, y, z) for x in [-1, 0, 1] for y in [-1, 0, 1] for z in [-1, 0, 1]]
    return np.array([[lookup.get(tuple(coordinate + offset), -1) for offset in offsets]
                     for coordinate in coordinates], dtype=np.int32)


def relative_rms(actual, expected):
    difference = actual.astype(np.float64) - expected
    return float(np.sqrt(np.mean(difference ** 2)) / np.sqrt(np.mean(expected.astype(np.float64) ** 2)))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--weights", type=Path, required=True)
    parser.add_argument("--latent", type=Path, required=True)
    parser.add_argument("--coordinates", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    evidence_path = args.output / "decoder-stage-reference-evidence.json"
    evidence_path.write_text(json.dumps({"Accepted": False}))
    rows = json.loads(args.coordinates.read_text())
    coordinates = np.array([[row["X"], row["Y"], row["Z"]] for row in rows], dtype=np.int32)
    latent_bytes = args.latent.read_bytes()
    latent = np.frombuffer(latent_bytes, dtype="<f4").reshape((len(rows), 32))
    weights = weights_from_file(args.weights)
    neighbors = neighbors_from_coordinates(coordinates)
    started = time.monotonic()
    values = affine(latent, weights, "from_latent")
    for index in range(4):
        prefix = f"blocks.0.{index}"
        hidden = convolution(values, neighbors, weights, prefix + ".conv")
        hidden = normalize(hidden, weights, prefix + ".norm")
        hidden = affine(hidden, weights, prefix + ".mlp.0")
        with np.errstate(over="ignore"):
            hidden = half(hidden / (1 + np.exp(-hidden)))
        hidden = affine(hidden, weights, prefix + ".mlp.2")
        values = half(values + hidden)
    subdivision = affine(values, weights, "blocks.0.4.to_subdiv")
    values.astype("<f4").tofile(args.output / "reference-features.f32")
    subdivision.astype("<f4").tofile(args.output / "reference-subdivision-logits.f32")
    actual_features_bytes = (args.output / "features.f32").read_bytes()
    actual_logits_bytes = (args.output / "subdivision-logits.f32").read_bytes()
    actual_features = np.frombuffer(actual_features_bytes, dtype="<f4").reshape(values.shape)
    actual_subdivision = np.frombuffer(actual_logits_bytes, dtype="<f4").reshape(subdivision.shape)
    feature_error = relative_rms(actual_features, values)
    subdivision_error = relative_rms(actual_subdivision, subdivision)
    agreement = float(np.mean((actual_subdivision > 0) == (subdivision > 0)))
    accepted = bool(np.isfinite(values).all() and np.isfinite(subdivision).all()
                    and feature_error < .005 and subdivision_error < .005 and agreement > .995)
    evidence = {
        "Accepted": accepted,
        "Scope": "Actual from_latent, four sparse ConvNeXt blocks and learned child logits; later decoder and mesh extraction not covered",
        "LatentSha256": hashlib.sha256(latent_bytes).hexdigest(),
        "FeatureSha256": hashlib.sha256(actual_features_bytes).hexdigest(),
        "SubdivisionLogitSha256": hashlib.sha256(actual_logits_bytes).hexdigest(),
        "FeatureRelativeRmsError": feature_error,
        "SubdivisionRelativeRmsError": subdivision_error,
        "SubdivisionSignAgreement": agreement,
        "ReferenceChildren": int(np.count_nonzero(subdivision > 0)),
        "OracleWallSeconds": time.monotonic() - started,
        "Thresholds": {"RelativeRmsErrorLessThan": .005, "SignAgreementGreaterThan": .995},
    }
    evidence_path.write_text(json.dumps(evidence, indent=2))
    print(json.dumps(evidence, indent=2), flush=True)
    if not accepted:
        raise SystemExit("Sparse decoder stage reference failed")


if __name__ == "__main__":
    main()
