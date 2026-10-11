"""Offline qualification only. Never called by the C#/Vulkan inference path.

An independent NumPy implementation of the pinned Hugging Face DINOv3 graph,
with TRELLIS.2's non-affine final normalization. No Torch or CUDA dependency.
See TRELLIS-NOTICE.txt for primary sources and attribution.
"""

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import struct
import time

os.environ.setdefault("OPENBLAS_NUM_THREADS", "8")
import numpy as np
from PIL import Image


def load_weights(path):
    with path.open("rb") as stream:
        header_size = struct.unpack("<Q", stream.read(8))[0]
        header = json.loads(stream.read(header_size))
    weights = {}
    for name, metadata in header.items():
        if name == "__metadata__":
            continue
        if metadata["dtype"] != "F32":
            raise ValueError("Reference requires the pinned F32 checkpoint")
        weights[name] = np.memmap(
            path, dtype="<f4", mode="r",
            offset=8 + header_size + metadata["data_offsets"][0],
            shape=tuple(metadata["shape"]),
        )
    return weights


def normalize(values, weights=None, prefix=None):
    # Double reductions avoid sharing the GPU reduction's summation algorithm.
    centered = values.astype(np.float64) - values.mean(axis=-1, keepdims=True, dtype=np.float64)
    normalized = (centered / np.sqrt(np.mean(centered * centered, axis=-1, keepdims=True) + 1e-5)).astype(np.float32)
    if prefix is not None:
        normalized = normalized * weights[prefix + ".weight"] + weights[prefix + ".bias"]
    return normalized


def affine(values, weights, prefix):
    weight = weights[prefix + ".weight"].reshape((-1, values.shape[-1]))
    result = values @ weight.T
    bias = weights.get(prefix + ".bias")
    if bias is not None:
        result += bias
    return result


def rotary(values, cos, sin):
    heads = values.reshape((1029, 16, 64)).transpose((1, 0, 2)).copy()
    patches = heads[:, 5:, :]
    rotated = np.concatenate((-patches[:, :, 32:], patches[:, :, :32]), axis=-1)
    heads[:, 5:, :] = patches * cos + rotated * sin
    return heads


def upstream_preprocess(image_path, output):
    image = Image.open(image_path).convert("RGBA")
    scale = min(1, 1024 / max(image.size))
    if scale < 1:
        image = image.resize((int(image.width * scale), int(image.height * scale)), Image.Resampling.LANCZOS)
    pixels = np.asarray(image)
    foreground = np.argwhere(pixels[:, :, 3] > 0.8 * 255)
    min_y, min_x = np.min(foreground, axis=0)
    max_y, max_x = np.max(foreground, axis=0)
    center_x = (min_x + max_x) / 2
    center_y = (min_y + max_y) / 2
    size = int(max(max_x - min_x, max_y - min_y))
    image = image.crop((center_x - size // 2, center_y - size // 2,
                        center_x + size // 2, center_y + size // 2))
    pixels = np.asarray(image).astype(np.float32) / 255
    pixels = pixels[:, :, :3] * pixels[:, :, 3:4]
    image = Image.fromarray((pixels * 255).astype(np.uint8))
    image = image.resize((512, 512), Image.Resampling.LANCZOS)
    image.save(output / "pillow-encoder-input.png")
    return np.asarray(image)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--weights", type=Path, required=True)
    parser.add_argument("--image", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    evidence_path = args.output / "reference-evidence.json"
    evidence_path.write_text(json.dumps({"Accepted": False}), encoding="utf-8")
    checkpoint_hash = hashlib.sha256()
    with args.weights.open("rb") as checkpoint:
        for chunk in iter(lambda: checkpoint.read(8 * 1024 * 1024), b""):
            checkpoint_hash.update(chunk)
    if checkpoint_hash.hexdigest() != "dcb2e45127cccbf1601e5f42fef165eea275c8e5213197e8dcf3f48822718179":
        raise ValueError("Reference checkpoint differs from the published Meta digest")
    weights = load_weights(args.weights)
    rgb = np.fromfile(args.output / "encoder-input.rgb", dtype=np.uint8).reshape((512, 512, 3))
    pillow_rgb = upstream_preprocess(args.image, args.output)
    pixel_error = np.abs(rgb.astype(np.int16) - pillow_rgb.astype(np.int16))
    # Independently repack the actual runtime RGB input, rather than consuming its
    # already normalized patch buffer and thereby hiding a channel/layout error.
    normalized = (rgb.astype(np.float32) / 255 - np.array([.485, .456, .406], np.float32)) / np.array([.229, .224, .225], np.float32)
    patches = normalized.reshape((32, 16, 32, 16, 3)).transpose((0, 2, 4, 1, 3)).reshape((1024, 768))
    runtime_patches = np.fromfile(args.output / "patches.f32", dtype="<f4").reshape(patches.shape)
    patch_error = float(np.max(np.abs(patches - runtime_patches)))
    hidden = np.concatenate((weights["embeddings.cls_token"].reshape((1, 1024)),
                             weights["embeddings.register_tokens"].reshape((4, 1024)),
                             affine(patches, weights, "embeddings.patch_embeddings")), axis=0)
    coordinates = np.stack(np.meshgrid(np.arange(32, dtype=np.float32), np.arange(32, dtype=np.float32), indexing="ij"), axis=-1).reshape((1024, 2))
    coordinates = (coordinates + .5) / 32 * 2 - 1
    inverse_frequency = 1 / np.power(np.float32(100), np.arange(16, dtype=np.float32) / 16)
    angles = (np.float32(2 * math.pi) * coordinates[:, :, None] * inverse_frequency).reshape((1024, 32))
    angles = np.tile(angles, (1, 2))
    cos = np.cos(angles)
    sin = np.sin(angles)
    erf = np.frompyfunc(math.erf, 1, 1)
    timer = time.perf_counter()
    for layer in range(24):
        prefix = f"layer.{layer}"
        x = normalize(hidden, weights, prefix + ".norm1")
        q = rotary(affine(x, weights, prefix + ".attention.q_proj"), cos, sin)
        k = rotary(affine(x, weights, prefix + ".attention.k_proj"), cos, sin)
        v = affine(x, weights, prefix + ".attention.v_proj").reshape((1029, 16, 64)).transpose((1, 0, 2))
        scores = (q @ k.transpose((0, 2, 1))) / np.float32(8)
        scores -= np.max(scores, axis=-1, keepdims=True)
        probabilities = np.exp(scores)
        probabilities /= np.sum(probabilities, axis=-1, keepdims=True)
        attended = (probabilities @ v).transpose((1, 0, 2)).reshape((1029, 1024))
        hidden += affine(attended, weights, prefix + ".attention.o_proj") * weights[prefix + ".layer_scale1.lambda1"]
        x = affine(normalize(hidden, weights, prefix + ".norm2"), weights, prefix + ".mlp.up_proj")
        # True error-function GELU, deliberately not the GPU polynomial.
        activated = (.5 * x.astype(np.float64) * (1 + erf(x.astype(np.float64) / math.sqrt(2)).astype(np.float64))).astype(np.float32)
        hidden += affine(activated, weights, prefix + ".mlp.down_proj") * weights[prefix + ".layer_scale2.lambda1"]
        print(f"NumPy DINO layer {layer + 1}/24", flush=True)
    expected = normalize(hidden)
    expected.tofile(args.output / "reference-features.f32")
    actual = np.fromfile(args.output / "features.f32", dtype="<f4").reshape(expected.shape)
    error = actual.astype(np.float64) - expected.astype(np.float64)
    maximum = float(np.max(np.abs(error)))
    rms = float(np.sqrt(np.mean(error * error)))
    relative_rms = rms / float(np.sqrt(np.mean(expected.astype(np.float64) ** 2)))
    accepted = bool(np.isfinite(actual).all() and maximum <= .002 and relative_rms <= .0002
                    and patch_error == 0 and pixel_error.max() <= 1 and pixel_error.mean() <= .001)
    evidence = {
        "Accepted": accepted,
        "FullEncoderQualified": accepted,
        "FullGenerationQualified": False,
        "Reference": "Independent NumPy BLAS, double LayerNorm reductions and math.erf; no Torch/CUDA",
        "WeightSha256": checkpoint_hash.hexdigest(),
        "ImageSha256": hashlib.sha256(args.image.read_bytes()).hexdigest(),
        "Scope": "All 24 learned layers, all 1029 tokens, 1024 channels, actual C# preprocessed RGB",
        "MaximumAbsoluteError": maximum,
        "RootMeanSquareError": rms,
        "RelativeRootMeanSquareError": relative_rms,
        "Limits": {"MaximumAbsoluteError": .002, "RelativeRootMeanSquareError": .0002,
                   "PillowPreprocessingMaximumByteError": 1, "PillowPreprocessingMeanByteError": .001},
        "PatchLayoutMaximumError": patch_error,
        "PillowPreprocessingMaximumByteError": int(pixel_error.max()),
        "PillowPreprocessingMeanByteError": float(pixel_error.mean()),
        "PillowPreprocessingExactByteParity": bool(np.array_equal(rgb, pillow_rgb)),
        "ReferenceWallSeconds": time.perf_counter() - timer,
        "FeatureSha256": hashlib.sha256((args.output / "features.f32").read_bytes()).hexdigest(),
    }
    evidence_path.write_text(json.dumps(evidence, indent=2), encoding="utf-8")
    print(json.dumps(evidence, indent=2))
    if not accepted:
        raise RuntimeError("Full DINO encoder disagrees with the independent reference")


if __name__ == "__main__":
    main()
