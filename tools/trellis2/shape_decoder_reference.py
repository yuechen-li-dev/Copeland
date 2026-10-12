"""Offline NumPy qualification of the complete pinned sparse decoder.

Uses recorded native subdivision guides to isolate learned arithmetic from
threshold-induced topology differences. Child-logit signs are checked separately.
No part of this oracle is called by C#/Vulkan inference.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import time

os.environ["OPENBLAS_NUM_THREADS"] = "4"
import numpy as np
from shape_decoder_stage_reference import weights_from_file, half, affine, neighbors_from_coordinates, relative_rms


def convolve(values, neighbors, weights, prefix):
    channels = values.shape[1]
    count = weights[prefix + ".weight"].shape[0]
    weight = np.asarray(weights[prefix + ".weight"], dtype=np.float32).reshape(count, 27 * channels)
    bias = np.asarray(weights[prefix + ".bias"], dtype=np.float32)
    output = np.empty((values.shape[0], count), dtype=np.float32)
    for first in range(0, len(values), 256):
        indices = neighbors[first:first + 256]
        columns = values[np.maximum(indices, 0)].copy()
        columns[indices < 0] = 0
        output[first:first + 256] = half(columns.reshape(-1, 27 * channels) @ weight.T + bias)
    return output


def normalize(values, weights=None, prefix=None, epsilon=1e-6, rounded=True):
    output = np.empty_like(values)
    for first in range(0, len(values), 8192):
        page = values[first:first + 8192].astype(np.float64)
        centered = page - page.mean(axis=1, keepdims=True)
        result = (centered / np.sqrt(np.mean(centered ** 2, axis=1, keepdims=True) + epsilon)).astype(np.float32)
        if prefix is not None:
            result = result * np.asarray(weights[prefix + ".weight"], dtype=np.float32) + np.asarray(weights[prefix + ".bias"], dtype=np.float32)
        output[first:first + 8192] = half(result) if rounded else result
    return output


def silu(values):
    with np.errstate(over="ignore"):
        return half(values / (1 + np.exp(-values)))


def coordinates_from_rows(rows):
    return np.array([[row["X"], row["Y"], row["Z"]] for row in rows], dtype=np.int32)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--weights", type=Path, required=True)
    parser.add_argument("--sample", type=Path, required=True)
    parser.add_argument("--native", type=Path, required=True)
    parser.add_argument("--material", action="store_true")
    args = parser.parse_args()
    evidence_path = args.native / "decoder-reference-evidence.json"
    evidence_path.write_text(json.dumps({"Accepted": False}))
    weights = weights_from_file(args.weights, expected_count=284 if args.material else 292)
    coordinates = coordinates_from_rows(json.loads((args.sample / "coordinates.json").read_text()))
    latent_bytes = (args.sample / "decoder-latent.f32").read_bytes()
    latent = np.frombuffer(latent_bytes, dtype="<f4").reshape(len(coordinates), 32)
    guides = json.loads((args.native / "subdivisions.json").read_text())
    started = time.monotonic()
    values = affine(latent, weights, "from_latent")
    checks = []
    for level, blocks in enumerate([4, 16, 8, 4, 0]):
        neighbors = neighbors_from_coordinates(coordinates)
        for block in range(blocks):
            prefix = f"blocks.{level}.{block}"
            hidden = normalize(convolve(values, neighbors, weights, prefix + ".conv"), weights, prefix + ".norm")
            output = np.empty_like(values)
            for first in range(0, len(values), 8192):
                page = affine(hidden[first:first + 8192], weights, prefix + ".mlp.0")
                page = affine(silu(page), weights, prefix + ".mlp.2")
                output[first:first + 8192] = half(page + values[first:first + 8192])
            values = output
            print(f"{prefix}: {len(values)} rows, {time.monotonic() - started:.1f}s", flush=True)
        if level < 4:
            prefix = f"blocks.{level}.{blocks}"
            guide = guides[level]
            parents = np.array(guide["ParentRows"], dtype=np.int32)
            slots = np.array(guide["ChildSlots"], dtype=np.int32)
            if not args.material:
                expected_mask = np.zeros((len(values), 8), dtype=bool)
                expected_mask[parents, slots] = True
                predicted = affine(values, weights, prefix + ".to_subdiv") > 0
                checks.append({"Level": level, "ChildSignAgreement": float(np.mean(predicted == expected_mask))})
            hidden = silu(normalize(values, weights, prefix + ".norm1"))
            expanded = convolve(hidden, neighbors, weights, prefix + ".conv1")
            out_channels = expanded.shape[1] // 8
            gathered = expanded.reshape(len(values), 8, out_channels)[parents, slots]
            coordinates = coordinates_from_rows(guide["Coordinates"])
            gathered = silu(normalize(gathered))
            hidden = convolve(gathered, neighbors_from_coordinates(coordinates), weights, prefix + ".conv2")
            skip = values.reshape(len(values), 8, values.shape[1] // 8)[parents, slots]
            skip = np.repeat(skip, out_channels // skip.shape[1], axis=1)
            values = half(hidden + skip)
            print(f"upsample {level}: {len(values)} rows", flush=True)
    values = normalize(values, epsilon=1e-5, rounded=False)
    expected = values @ np.asarray(weights["output_layer.weight"], dtype=np.float32).T + np.asarray(weights["output_layer.bias"], dtype=np.float32)
    actual_bytes = (args.native / "features.f32").read_bytes()
    actual = np.frombuffer(actual_bytes, dtype="<f4").reshape(expected.shape)
    error = relative_rms(actual, expected)
    agreement = 1.0 if args.material else float(np.mean((actual[:, 3:6] > 0) == (expected[:, 3:6] > 0)))
    accepted = bool(np.isfinite(expected).all() and error < .005 and agreement > .995
                    and all(item["ChildSignAgreement"] > .995 for item in checks))
    evidence = {"Accepted": accepted, "Scope": "All learned decoder arithmetic on recorded native subdivision guides; topology decisions checked by sign agreement",
                "MaterialDecoder": args.material,
                "FullGenerationQualified": False, "LatentSha256": hashlib.sha256(latent_bytes).hexdigest(),
                "FeatureSha256": hashlib.sha256(actual_bytes).hexdigest(), "FeatureRelativeRms": error,
                "IntersectionSignAgreement": agreement, "Subdivisions": checks,
                "OracleWallSeconds": time.monotonic() - started,
                "Thresholds": {"RelativeRmsLessThan": .005, "SignAgreementGreaterThan": .995}}
    evidence_path.write_text(json.dumps(evidence, indent=2))
    print(json.dumps(evidence, indent=2), flush=True)
    if not accepted:
        raise SystemExit("Full sparse decoder arithmetic qualification failed")


if __name__ == "__main__":
    main()
