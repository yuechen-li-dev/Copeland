"""Optional full-flow/first-Euler-step oracle, independent of Vulkan kernels.

NumPy only; no Torch/CUDA and never called by the inference executable.
Uses all 30 distinct blocks from the pinned Microsoft checkpoint. See
TRELLIS-NOTICE.txt for source attribution and the upstream operation boundaries.
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


class Weights:
    def __init__(self, path):
        self.path = path
        with path.open("rb") as stream:
            length = struct.unpack("<Q", stream.read(8))[0]
            self.header = json.loads(stream.read(length))
        self.start = length + 8
        if len(self.header) != 640:
            raise ValueError("Unexpected flow inventory")

    def get(self, name):
        item = self.header[name]
        if item["dtype"] != "BF16":
            raise ValueError("Expected pinned BF16 checkpoint")
        view = np.memmap(self.path, dtype="<u2", mode="r",
                         offset=self.start + item["data_offsets"][0], shape=tuple(item["shape"]))
        return (view.astype(np.uint32) << 16).view(np.float32)


def bf16(values):
    bits = np.ascontiguousarray(values, dtype=np.float32).view(np.uint32)
    return ((bits + np.uint32(32767) + ((bits >> 16) & 1)) & np.uint32(0xffff0000)).view(np.float32)


def affine(values, weights, prefix, rounded=True):
    projected = values @ weights.get(prefix + ".weight").T + weights.get(prefix + ".bias")
    return bf16(projected) if rounded else projected


def normalize(values, weights=None, prefix=None, rounded=True, epsilon=1e-6):
    centered = values.astype(np.float64) - values.mean(axis=-1, keepdims=True, dtype=np.float64)
    output = (centered / np.sqrt(np.mean(centered ** 2, axis=-1, keepdims=True) + epsilon)).astype(np.float32)
    if prefix is not None:
        output = output * weights.get(prefix + ".weight") + weights.get(prefix + ".bias")
    return bf16(output) if rounded else output


def silu(values):
    with np.errstate(over="ignore"):
        return values / (1 + np.exp(-values))


def conditioning(weights, timestep):
    frequencies = np.exp(-np.float32(np.log(10000)) * np.arange(128, dtype=np.float32) / 128)
    phases = np.float32(timestep) * frequencies
    value = np.concatenate((np.cos(phases), np.sin(phases))).reshape((1, 256))
    value = affine(value, weights, "t_embedder.mlp.0", rounded=False)
    value = affine(silu(value), weights, "t_embedder.mlp.2", rounded=False)
    return affine(silu(value), weights, "adaLN_modulation.1")


def head_normalize(values, weights, prefix):
    shaped = values.reshape((-1, 12, 128))
    length = np.sqrt(np.sum(shaped.astype(np.float64) ** 2, axis=-1, keepdims=True)).astype(np.float32)
    normalized = shaped / np.maximum(length, np.float32(1e-12))
    normalized = normalized * weights.get(prefix + ".gamma") * np.float32(np.sqrt(128))
    return bf16(normalized.reshape((-1, 1536)))


def rope(values, coordinates=None):
    coordinate = (np.indices((16, 16, 16)).reshape((3, -1)).T.astype(np.float32)
                  if coordinates is None else coordinates.astype(np.float32))
    tokens = coordinate.shape[0]
    frequencies = np.float32(1) / np.power(np.float32(10000), np.arange(21, dtype=np.float32) / 21)
    phases = (coordinate[:, :, None] * frequencies).reshape((tokens, 63))
    phases = np.pad(phases, ((0, 0), (0, 1)))[:, None, :]
    pairs = values.reshape((tokens, 12, 64, 2))
    output = np.empty_like(pairs)
    output[..., 0] = pairs[..., 0] * np.cos(phases) - pairs[..., 1] * np.sin(phases)
    output[..., 1] = pairs[..., 0] * np.sin(phases) + pairs[..., 1] * np.cos(phases)
    return bf16(output.reshape((tokens, 1536)))


def attention(q, k, v):
    qh = np.ascontiguousarray(q.reshape((-1, 12, 128)).transpose((1, 0, 2)))
    kh = np.ascontiguousarray(k.reshape((-1, 12, 128)).transpose((1, 2, 0)))
    vh = np.ascontiguousarray(v.reshape((-1, 12, 128)).transpose((1, 0, 2)))
    output = np.empty_like(qh)
    for first in range(0, q.shape[0], 256):
        scores = (qh[:, first:first + 256] @ kh) / np.float32(np.sqrt(128))
        scores -= scores.max(axis=-1, keepdims=True)
        probabilities = np.exp(scores)
        probabilities /= probabilities.sum(axis=-1, keepdims=True, dtype=np.float64).astype(np.float32)
        output[:, first:first + 256] = probabilities @ vh
    return bf16(output.transpose((1, 0, 2)).reshape((-1, 1536)))


def modulate(values, modulation, offset):
    shift = modulation[:, offset:offset + 1536]
    scale = bf16(1 + modulation[:, offset + 1536:offset + 3072])
    return bf16(bf16(values * scale) + shift)


def model(sample, context, weights, timestep, label, coordinates=None, gelu_policy="tanh", dump_directory=None):
    values = affine(sample, weights, "input_layer")
    shared = conditioning(weights, timestep)
    if dump_directory is not None:
        shared.astype("<f4").tofile(dump_directory / "shared-modulation.f32")
    for index in range(30):
        started = time.monotonic()
        prefix = f"blocks.{index}"
        modulation = bf16(shared + weights.get(prefix + ".modulation"))
        hidden = modulate(normalize(values), modulation, 0)
        qkv = affine(hidden, weights, prefix + ".self_attn.to_qkv")
        q, k, v = np.split(qkv, 3, axis=-1)
        q = rope(head_normalize(q, weights, prefix + ".self_attn.q_rms_norm"), coordinates)
        k = rope(head_normalize(k, weights, prefix + ".self_attn.k_rms_norm"), coordinates)
        hidden = affine(attention(q, k, v), weights, prefix + ".self_attn.to_out")
        values = bf16(values + bf16(hidden * modulation[:, 3072:4608]))
        hidden = normalize(values, weights, prefix + ".norm2")
        q = affine(hidden, weights, prefix + ".cross_attn.to_q")
        kv = affine(context, weights, prefix + ".cross_attn.to_kv")
        k, v = np.split(kv, 2, axis=-1)
        q = head_normalize(q, weights, prefix + ".cross_attn.q_rms_norm")
        k = head_normalize(k, weights, prefix + ".cross_attn.k_rms_norm")
        hidden = affine(attention(q, k, v), weights, prefix + ".cross_attn.to_out")
        values = bf16(values + hidden)
        hidden = modulate(normalize(values), modulation, 4608)
        hidden = affine(hidden, weights, prefix + ".mlp.mlp.0")
        if gelu_policy == "logistic":
            scaled = np.float32(.7978845608028654) * (hidden + np.float32(.044715) * hidden * hidden * hidden)
            with np.errstate(over="ignore"):
                activated = hidden / (1 + np.exp(-2 * scaled))
        else:
            scaled = np.float32(.7978845608028654) * (hidden + np.float32(.044715) * hidden ** 3)
            activated = hidden * .5 * (1 + np.tanh(scaled))
        hidden = affine(bf16(activated), weights, prefix + ".mlp.mlp.2")
        values = bf16(values + bf16(hidden * modulation[:, 7680:9216]))
        if dump_directory is not None and index in [0, 1, 2, 5, 10, 20, 29]:
            values.astype("<f4").tofile(dump_directory / f"{label}-block-{index:02d}.f32")
        print(f"{label}.{index}: {time.monotonic() - started:.2f}s", flush=True)
    return affine(normalize(values, rounded=False, epsilon=1e-5), weights, "out_layer", rounded=False)


def guided(sample, positive, negative, timestep, guidance_rescale=.7):
    retained = np.float32(1 - 1e-5)
    coefficient = np.float32(1e-5 + (1 - 1e-5) * timestep)
    clean_positive = retained * sample - coefficient * positive
    clean_guided = retained * sample - coefficient * (np.float32(7.5) * positive - np.float32(6.5) * negative)
    scale = np.float32(np.std(clean_positive, dtype=np.float64, ddof=1)
                       / np.std(clean_guided, dtype=np.float64, ddof=1))
    clean = np.float32(guidance_rescale) * (clean_guided * scale) + np.float32(1 - guidance_rescale) * clean_guided
    return (retained * sample - clean) / coefficient


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--weights", type=Path, required=True)
    parser.add_argument("--features", type=Path, required=True)
    parser.add_argument("--sample-dir", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--step", type=int, default=0)
    parser.add_argument("--coordinates", type=Path, help="Actual sparse XYZ coordinates; selects pinned 512 shape flow settings")
    parser.add_argument("--gelu-policy", choices=["tanh", "logistic"], default="tanh", help="Logistic is a sensitivity experiment, not a replacement of failed upstream-formula evidence")
    parser.add_argument("--dump-blocks", action="store_true", help="Explicit offline diagnostic checkpoints")
    parser.add_argument("--positive-only", action="store_true", help="Diagnostic model evaluation without Euler/CFG qualification")
    parser.add_argument("--compare-native-diagnostics", type=Path, help="Compare selected block snapshots; retains failed Euler evidence separately")
    parser.add_argument("--conditioning-only", action="store_true", help="Explicit small shared-timestep diagnostic without running transformer blocks")
    args = parser.parse_args()
    if args.step < 0 or args.step >= 12:
        raise ValueError("Step must be 0..11")
    args.output.mkdir(parents=True, exist_ok=True)
    evidence_path = args.output / "flow-reference-evidence.json"
    evidence_path.write_text(json.dumps({"Accepted": False}))
    sample_path = args.sample_dir / ("noise.f32" if args.step == 0 else f"latent-step-{args.step - 1:02d}.f32")
    sample_bytes = sample_path.read_bytes()
    coordinates = None
    if args.coordinates is not None:
        rows = json.loads(args.coordinates.read_text())
        coordinates = np.array([[row["X"], row["Y"], row["Z"]] for row in rows], dtype=np.float32)
    tokens = 4096 if coordinates is None else len(coordinates)
    channels = 8 if coordinates is None else 32
    rescale = 5 if coordinates is None else 3
    guidance_rescale = .7 if coordinates is None else .5
    sample = np.frombuffer(sample_bytes, dtype="<f4").reshape((tokens, channels)).copy()
    context = bf16(np.fromfile(args.features, dtype="<f4").reshape((1029, 1024)))
    weights = Weights(args.weights)
    linear = 1 - args.step / 12
    timestep = rescale * linear / (1 + (rescale - 1) * linear)
    previous_linear = 1 - (args.step + 1) / 12
    previous = rescale * previous_linear / (1 + (rescale - 1) * previous_linear)
    if args.conditioning_only:
        conditioning(weights, timestep * 1000).astype("<f4").tofile(args.output / "shared-modulation.f32")
        print("Shared conditioning diagnostic completed; no sampler acceptance asserted.", flush=True)
        return
    started = time.monotonic()
    positive = model(sample, context, weights, timestep * 1000, "positive", coordinates, args.gelu_policy,
                     args.output if args.dump_blocks else None)
    positive.astype("<f4").tofile(args.output / "positive-velocity.f32")
    if args.positive_only:
        if args.compare_native_diagnostics is not None:
            comparisons = []
            for block in [0, 1, 2, 5, 10, 20, 29]:
                name = f"positive-block-{block:02d}.f32"
                expected = np.fromfile(args.output / name, dtype="<f4")
                actual = np.fromfile(args.compare_native_diagnostics / name, dtype="<f4")
                difference = actual.astype(np.float64) - expected
                relative = float(np.sqrt(np.mean(difference ** 2)) / np.sqrt(np.mean(expected.astype(np.float64) ** 2)))
                comparisons.append({"Block": block, "RelativeRmsError": relative,
                                    "MaximumAbsoluteError": float(np.abs(difference).max()),
                                    "BitIdenticalFraction": float(np.mean(actual.view(np.uint32) == expected.view(np.uint32)))})
            actual_velocity = np.fromfile(args.compare_native_diagnostics / "positive-velocity.f32", dtype="<f4").reshape(positive.shape)
            velocity_error = actual_velocity.astype(np.float64) - positive
            diagnostic = {
                "Scope": "Selected-block numerical sensitivity; not a complete-sampler acceptance result",
                "AcceptedAsFullSampling": False, "GeluPolicy": args.gelu_policy,
                "Blocks": comparisons,
                "PositiveVelocityRelativeRmsError": float(np.sqrt(np.mean(velocity_error ** 2))
                    / np.sqrt(np.mean(positive.astype(np.float64) ** 2))),
            }
            (args.output / "block-comparison.json").write_text(json.dumps(diagnostic, indent=2))
            print(json.dumps(diagnostic, indent=2), flush=True)
        print("Positive model diagnostic completed; no Euler-step acceptance asserted.", flush=True)
        return
    velocity = positive
    if .6 <= timestep <= 1:
        negative = model(sample, np.zeros_like(context), weights, timestep * 1000, "negative", coordinates, args.gelu_policy,
                         args.output if args.dump_blocks else None)
        negative.astype("<f4").tofile(args.output / "negative-velocity.f32")
        velocity = guided(sample, positive, negative, timestep, guidance_rescale)
    predicted = sample - np.float32(timestep - previous) * velocity
    predicted.astype("<f4").tofile(args.output / "reference-latent.f32")
    actual = np.fromfile(args.sample_dir / f"latent-step-{args.step:02d}.f32", dtype="<f4").reshape((tokens, channels))
    actual_velocity = (sample - actual) / np.float32(timestep - previous)
    difference = actual.astype(np.float64) - predicted
    velocity_error = actual_velocity.astype(np.float64) - velocity
    relative = float(np.sqrt(np.mean(velocity_error ** 2)) / np.sqrt(np.mean(velocity.astype(np.float64) ** 2)))
    accepted = bool(np.isfinite(predicted).all() and relative < .01)
    evidence = {
        "Accepted": accepted, "Step": args.step,
        "Scope": "All 30 blocks and actual image-conditioned Euler step; other steps not independently replayed",
        "Tokens": tokens, "Channels": channels,
        "Model": "sparse structure" if coordinates is None else "512 shape SLat",
        "GeluPolicy": args.gelu_policy,
        "InputSha256": hashlib.sha256(sample_bytes).hexdigest(),
        "FeatureSha256": hashlib.sha256(args.features.read_bytes()).hexdigest(),
        "MaximumLatentAbsoluteError": float(np.abs(difference).max()),
        "LatentRmsError": float(np.sqrt(np.mean(difference ** 2))),
        "VelocityRelativeRmsError": relative,
        "VelocityMaximumAbsoluteError": float(np.abs(velocity_error).max()),
        "ModelEvaluations": 2 if .6 <= timestep <= 1 else 1,
        "OracleWallSeconds": time.monotonic() - started,
        "Threshold": {"VelocityRelativeRmsErrorLessThan": .01},
    }
    evidence_path.write_text(json.dumps(evidence, indent=2))
    print(json.dumps(evidence, indent=2), flush=True)
    if not accepted:
        raise SystemExit("Full learned-flow step qualification failed")


if __name__ == "__main__":
    main()
