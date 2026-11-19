"""Fit and export joint BCn latents with a small nonlinear material decoder.

Input directories hold albedo.png, normal.png (OpenGL XY), and roughness.png.
Use DirectXTex texconv and Pillow with DDS support. Outputs are research fixtures.
"""

import argparse
import json
import subprocess
from pathlib import Path

import numpy as np
from PIL import Image
from scipy.sparse import coo_matrix
from scipy.sparse.linalg import splu


def load8(path, mode):
    image = Image.open(path)
    if mode == "L":
        pixels = np.asarray(image)
        if pixels.ndim == 2 and pixels.dtype.itemsize > 1:
            return np.uint8(pixels >> 8)
    return np.asarray(image.convert(mode))


def quantize(pixels):
    return np.uint8(np.rint(np.clip(pixels, 0, 1) * 255))


def convert(texconv, directory, name, pixels, fmt):
    path = directory / f"{name}.png"
    dds = path.with_suffix(".dds")
    if not (dds.is_file() and path.is_file() and np.array_equal(pixels, np.asarray(Image.open(path)))):
        Image.fromarray(pixels).save(path)
        result = subprocess.run(
            [str(texconv), "-nologo", "-y", "-m", "1", "-dx10", "-nogpu", "-f", fmt,
             "-bc", "x", "-o", str(directory), str(path)],
            capture_output=True, text=True, check=True,
        )
        if not dds.is_file():
            raise RuntimeError(result.stdout + result.stderr)
    return np.asarray(Image.open(dds).convert("RGBA"), dtype=np.float32) / 255


def latent_channels(y, method):
    flat = y.reshape(-1, 6)
    if method == "pca":
        _, rotation = np.linalg.eigh(np.cov(flat[::8], rowvar=False))
        source = flat @ rotation[:, ::-1][:, :5].astype(np.float32)
        return source.reshape((*y.shape[:2], 5))
    albedo = flat[:, :3]
    luminance = albedo @ np.array([.2126, .7152, .0722], dtype=np.float32)
    chroma = albedo - luminance[:, None]
    _, rotation = np.linalg.eigh(np.cov(chroma[::8], rowvar=False))
    chroma = chroma @ rotation[:, -1].astype(np.float32)
    return np.column_stack([flat[:, 3:6], chroma, luminance]).reshape((*y.shape[:2], 5))


def fit_decoder(x, y, steps, preserve_normal=False):
    # The first hidden units start as an affine decoder. Remaining units
    # are learned ReLU features conditioned on the decoded latent values.
    rng = np.random.default_rng(4121)
    channels = x.shape[-1]
    x = x.reshape(-1, channels)
    y = y.reshape(-1, 6)
    features = channels + 1
    inputs = np.ones((len(x), features), dtype=np.float32)
    inputs[:, :channels] = x
    first = np.zeros((features, 16), dtype=np.float32)
    first[:, :features] = np.eye(features, dtype=np.float32)
    first[:, features:] = rng.normal(0, .8, (features, 16 - features))

    def fit_output(matrix):
        # Some latent planes are nearly collinear. Form the ridge solution in
        # FP64 so roundoff cannot swamp the regularization on a 1K material.
        matrix = matrix.astype(np.float64)
        return np.linalg.solve(matrix.T @ matrix + np.eye(16) * .02, matrix.T @ targets).astype(np.float32)

    def packed_weights(hidden, output):
        weights = np.zeros((2, 16, 16), dtype="<f2")
        weights[0, :features] = hidden
        weights[1, :, :6] = output
        if preserve_normal:
            weights[1, :, 3:5] = 0
            weights[1, 0, 3] = 1
            weights[1, 1, 4] = 1
        return weights

    def predict(values, weights):
        hidden = np.maximum(0, values @ weights[0, :features].astype(np.float32))
        return hidden.astype(np.float16).astype(np.float32) @ weights[1, :, :6].astype(np.float32)

    training = rng.choice(len(inputs), min(131072, len(inputs)), replace=False)
    design, targets = inputs[training], y[training]
    second = fit_output(np.maximum(0, design @ first))
    linear = np.linalg.lstsq(design, targets, rcond=None)[0].astype(np.float32)
    baseline = np.clip(inputs @ linear, 0, 1)
    if preserve_normal:
        baseline[:, 3:5] = inputs[:, :2]
    sample = rng.choice(len(inputs), min(32768, len(inputs)), replace=False)
    sample_inputs = inputs[sample].astype(np.float16).astype(np.float32)
    sample_targets = y[sample]
    affine_hidden = np.zeros_like(first)
    affine_hidden[:, :features] = np.eye(features, dtype=np.float32)
    affine_output = np.zeros_like(second)
    affine_output[:features] = linear
    weights = packed_weights(affine_hidden, affine_output)
    scale = np.maximum(np.mean((predict(sample_inputs, weights) - sample_targets) ** 2, axis=0), 1e-7)

    def score(candidate):
        delta = np.clip(predict(sample_inputs, candidate), 0, 1) - sample_targets
        return np.mean(np.mean(delta * delta, axis=0) / scale)

    best = score(weights)
    initial = packed_weights(first, second)
    initial_error = score(initial)
    if initial_error < best:
        weights, best = initial, initial_error
    velocity = np.zeros_like(first)
    variance = np.zeros_like(first)
    for step in range(steps):
        batch = rng.integers(0, len(design), 8192)
        stripes, expected = design[batch], targets[batch]
        before = stripes @ first
        hidden = np.maximum(0, before)
        residual = hidden @ second - expected
        if preserve_normal:
            residual[:, 3:5] = 0
        gradient = stripes.T @ ((residual @ second.T) * (before > 0)) / len(batch)
        gradient[:, :features] = 0
        velocity = .9 * velocity + .1 * gradient
        variance = .99 * variance + .01 * gradient * gradient
        lr = .002 * (1 - .8 * step / max(1, steps))
        first -= lr * (velocity / (1 - .9 ** (step + 1))) / (np.sqrt(variance / (1 - .99 ** (step + 1))) + 1e-6)
        if (step + 1) % 8 == 0:
            second = fit_output(np.maximum(0, design @ first))
            candidate = packed_weights(first, second)
            error = score(candidate)
            if error < best:
                weights, best = candidate, error

    prediction = predict(inputs.astype(np.float16).astype(np.float32), weights)
    return weights, prediction, baseline


def quality(reference, result):
    error = reference.reshape(-1, 6) - np.clip(result.reshape(-1, 6), 0, 1)
    return [-10 * float(np.log10(np.mean(error[:, at:stop] ** 2))) for at, stop in [(0, 3), (3, 5), (5, 6)]]


def bilinear_repeat(image, height, width):
    sh, sw = image.shape[:2]
    u = (np.arange(width, dtype=np.float32) + .5) * sw / width - .5
    v = (np.arange(height, dtype=np.float32) + .5) * sh / height - .5
    x, y = np.floor(u).astype(np.int32), np.floor(v).astype(np.int32)
    fx, fy = (u - x).astype(np.float32)[None, :, None], (v - y).astype(np.float32)[:, None, None]
    upper = (1 - fx) * image[y[:, None] % sh, x % sw] + fx * image[y[:, None] % sh, (x + 1) % sw]
    lower = (1 - fx) * image[(y[:, None] + 1) % sh, x % sw] + fx * image[(y[:, None] + 1) % sh, (x + 1) % sw]
    return (1 - fy) * upper + fy * lower


def fit_bilinear(image, height, width):
    result = image
    for axis, size in enumerate((height, width)):
        values = np.moveaxis(result, axis, 0)
        count = values.shape[0]
        coordinate = (np.arange(count) + .5) * size / count - .5
        base = np.floor(coordinate).astype(np.int32)
        fraction = coordinate - base
        taps = coo_matrix((np.concatenate([1 - fraction, fraction]), (
            np.tile(np.arange(count), 2), np.concatenate([base % size, (base + 1) % size])
        )), shape=(count, size)).tocsc()
        normal = splu((taps.T @ taps).tocsc())
        fitted = normal.solve(taps.T @ values.reshape(count, -1))
        result = np.moveaxis(fitted.reshape((size, *values.shape[1:])), 0, axis)
    return result


def normalize_range(image):
    low = np.min(image, axis=(0, 1))
    high = np.max(image, axis=(0, 1))
    return quantize((image - low) / np.maximum(high - low, 1e-8))


def bake(texconv, source, directory, steps, methods):
    directory.mkdir(parents=True, exist_ok=True)
    albedo = load8(source / "albedo.png", "RGB")
    normal = load8(source / "normal.png", "RGB")
    roughness = load8(source / "roughness.png", "L")
    if albedo.shape[:2] != normal.shape[:2] or albedo.shape[:2] != roughness.shape or albedo.shape[0] % 4 or albedo.shape[1] % 4:
        raise ValueError(f"{source} needs aligned source channels and complete BC blocks")
    rgba = np.dstack((albedo, roughness))
    y = np.dstack((albedo, normal[:, :, :2], roughness)).astype(np.float32) / 255
    (directory / "reference.raw").write_bytes(np.uint8(np.rint(y * 255)).tobytes())
    bc_color = convert(texconv, directory, "color-bc7", rgba, "BC7_UNORM")
    bc_normal = convert(texconv, directory, "normal-bc7", normal, "BC7_UNORM")
    bc5 = convert(texconv, directory, "normal-bc5", normal, "BC5_UNORM")
    controls = {}
    for label, normals in [("BC7", bc_normal), ("BC7+BC5", bc5)]:
        controls[label] = quality(y, np.dstack((bc_color[:, :, :3], normals[:, :, :2], bc_color[:, :, 3])))
    candidates = {}
    for method in methods:
        if method.startswith("split"):
            height, width = y.shape[:2]
            luminance = y[:, :, :3] @ np.array([.2126, .7152, .0722], dtype=np.float32)
            if method.startswith("splitfit"):
                ratio = .5 if method == "splitfit2" else .6875
                chroma = np.dstack((y[:, :, :3] - luminance[:, :, None], y[:, :, 5]))
                coarse = normalize_range(fit_bilinear(chroma, round(height * ratio / 4) * 4, round(width * ratio / 4) * 4))
                scalar_source = normalize_range(luminance)
            else:
                scale = int(method[-1])
                coarse = rgba.reshape(height // scale, scale, width // scale, scale, 4).mean(axis=(1, 3))
                coarse = np.uint8(np.rint(coarse))
                scalar_source = quantize(luminance)
            scalar = convert(texconv, directory, f"{method}-scalar", scalar_source, "BC4_UNORM")[:, :, 0]
            coarse = convert(texconv, directory, f"{method}-coarse", coarse, "BC7_UNORM")
            features = np.dstack((bc5[:, :, :2], scalar, bilinear_repeat(coarse, height, width)))
        else:
            latent = latent_channels(y, method)
            encoded = normalize_range(latent)
            compressed = convert(texconv, directory, f"{method}-latents", encoded[:, :, :4], "BC7_UNORM")
            scalar = convert(texconv, directory, f"{method}-scalar", encoded[:, :, 4], "BC4_UNORM")[:, :, 0]
            features = np.dstack((compressed, scalar))
        weights, prediction, linear = fit_decoder(features, y, steps, preserve_normal=method.startswith("split"))
        (directory / f"{method}-weights.bin").write_bytes(weights.tobytes())
        (directory / f"{method}-oracle.raw").write_bytes(quantize(prediction).tobytes())
        candidates[method] = {
            "PSNR": quality(y, prediction),
            "AffinePSNR": quality(y, linear),
            "NonlinearUnits": int(np.any(weights[1, features.shape[-1] + 1:, :6] != 0, axis=1).sum()),
        }
    data = {"Width": int(y.shape[1]), "Height": int(y.shape[0]), "Controls": controls, "Candidates": candidates}
    (directory / "offline.json").write_text(json.dumps(data, indent=2))
    print(source.name, json.dumps(data), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("texconv", type=Path)
    parser.add_argument("input", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--steps", default=96, type=int)
    parser.add_argument("--only", nargs="+")
    parser.add_argument("--methods", nargs="+", choices=["material", "pca", "split2", "split4", "splitfit2", "splitfit704"],
                        default=["material", "pca", "split2", "split4", "splitfit2", "splitfit704"])
    args = parser.parse_args()
    for source in sorted(args.input.iterdir()):
        if source.is_dir() and (args.only is None or source.name in args.only):
            bake(args.texconv.resolve(), source, args.output.resolve() / source.name, args.steps, args.methods)
