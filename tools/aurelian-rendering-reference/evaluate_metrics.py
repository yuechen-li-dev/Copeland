"""Offline NVIDIA LDR-FLIP over native, fixed-exposure sRGB captures.

The pinned evaluator is BSD-3-Clause: https://github.com/NVlabs/flip.
This tool is never imported or launched by the game runtime.
"""

import argparse
import hashlib
import importlib.metadata
import json
from pathlib import Path

import flip_evaluator as flip
import numpy as np
from PIL import Image
import PIL


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load(path):
    with Image.open(path) as image:
        value = np.asarray(image.convert("RGB"), dtype=np.float32) / 255.0
    if value.ndim != 3 or value.shape[2] != 3 or not np.isfinite(value).all():
        raise ValueError(f"Invalid display input: {path}")
    return np.ascontiguousarray(value)


def evaluate(manifest_path):
    root = manifest_path.parent
    result_path = root / "flip-evidence.json"
    result_path.write_text(json.dumps({"Accepted": False}), encoding="utf-8")
    version = importlib.metadata.version("flip-evaluator")
    if version != "1.7":
        raise ValueError(f"Expected pinned FLIP 1.7, found {version}")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    if manifest["DynamicRange"] != "LDR" or manifest["InputColor"] != "sRGB":
        raise ValueError("This qualification requires explicitly tone-mapped sRGB inputs")
    results = []
    groups = {}
    edge_groups = {}
    cache = {}
    pairs = list(manifest["Pairs"])
    for sequence in ["camera-pan", "moving-occluder"]:
        frames = [pair for pair in pairs if pair["Group"] == sequence + "-taa"]
        for pair in frames:
            pairs.append(dict(pair, Name=pair["Name"] + "-frozen", Test=frames[0]["Test"],
                              Group=sequence + "-frozen-native"))
    for pair in pairs:
        reference_path = root / pair["Reference"]
        test_path = root / pair["Test"]
        if reference_path not in cache:
            cache[reference_path] = load(reference_path)
        reference = cache[reference_path]
        test = load(test_path)
        if test.shape != reference.shape:
            raise ValueError(f"FLIP image dimensions differ: {pair['Name']}")
        error_map, mean, parameters = flip.evaluate(
            reference, test, "LDR", inputsRGB=True, applyMagma=False,
            computeMeanError=True, parameters={"ppd": float(manifest["Ppd"])})
        error_map = np.asarray(error_map, dtype=np.float32)
        if not np.isfinite(error_map).all() or not 0 <= mean <= 1:
            raise ValueError(f"Invalid FLIP output for {pair['Name']}")
        error_map.tofile(root / (pair["Name"] + ".flip32f"))
        display = np.clip(error_map.squeeze() * 255, 0, 255).astype(np.uint8)
        Image.fromarray(display).save(root / (pair["Name"] + "-flip.png"))
        edges = np.zeros(reference.shape[:2], dtype=bool)
        horizontal = np.max(np.abs(reference[:, 1:] - reference[:, :-1]), axis=2) > .05
        vertical = np.max(np.abs(reference[1:] - reference[:-1]), axis=2) > .05
        edges[:, 1:] |= horizontal
        edges[:, :-1] |= horizontal
        edges[1:] |= vertical
        edges[:-1] |= vertical
        edge_mean = float(error_map.squeeze()[edges].mean()) if edges.any() else None
        if edge_mean is not None:
            edge_groups.setdefault(pair["Group"], []).append(edge_mean)
        row = dict(pair, MeanFlip=float(mean), P95Flip=float(np.quantile(error_map, .95)),
                   MaximumFlip=float(error_map.max()), ReferenceSha256=sha256(reference_path),
                   TestSha256=sha256(test_path), Parameters=parameters,
                   ReferenceEdgePixels=int(edges.sum()), ReferenceEdgeMeanFlip=edge_mean)
        results.append(row)
        groups.setdefault(pair["Group"], []).append(float(mean))
    means = {name: float(np.mean(values)) for name, values in groups.items()}
    edge_means = {name: float(np.mean(values)) for name, values in edge_groups.items()}
    if means["identity"] != 0:
        raise ValueError("Identical native output produced nonzero FLIP")
    if manifest.get("Qualification") == "local-ray-shadows":
        for scene in ["hall-0", "hall-1", "hall-2", "point"]:
            if means[scene + "-ray"] >= means[scene + "-unshadowed"]:
                raise ValueError(f"Local ray shadows failed to improve FLIP: {scene}")
            if edge_means[scene + "-ray"] >= edge_means[scene + "-unshadowed"]:
                raise ValueError(f"Local ray shadows failed to improve reference-edge FLIP: {scene}")
    elif manifest.get("Qualification") == "restir":
        for scene in ["static", "camera-pan", "moving-receiver"]:
            if means[scene + "-SpatiotemporalReservoir"] >= means[scene + "-Reservoir"]:
                raise ValueError(f"Spatial/temporal reuse failed final-frame FLIP: {scene}")
        if means["camera-pan-ReprojectedReservoir"] >= means["camera-pan-TemporalReservoir"]:
            raise ValueError("Motion reprojection did not improve camera-pan FLIP")
    elif manifest.get("Qualification") == "local-light-sampling":
        for scene in ["hall", "key-fill"]:
            if means[scene + "-Importance"] >= means[scene + "-Uniform"]:
                raise ValueError(f"Importance sampling did not improve mean-image FLIP: {scene}")
            if edge_means[scene + "-Importance"] >= edge_means[scene + "-Uniform"]:
                raise ValueError(f"Importance sampling did not improve reference-edge FLIP: {scene}")
            if means[scene + "-Importance"] >= means[scene + "-UniformEligible"]:
                raise ValueError(f"Weighted proposal did not improve eligible-uniform FLIP: {scene}")
            if edge_means[scene + "-Importance"] >= edge_means[scene + "-UniformEligible"]:
                raise ValueError(f"Weighted proposal did not improve eligible-uniform edge FLIP: {scene}")
        if means["key-fill-Hybrid"] >= means["key-fill-Importance"]:
            raise ValueError("Exact-few plus sampled-tail failed to improve key/fill FLIP")
    else:
        if means["importance"] >= means["uniform"]:
            raise ValueError("Environment importance sampling did not improve LDR-FLIP")
        if means["static-taa"] >= means["static-none"]:
            raise ValueError("TAA failed to improve static reference LDR-FLIP")
        if "constant-adaptive" in means and means["constant-adaptive"] >= means["constant-uniform"]:
            raise ValueError("Adaptive constant environment did not improve FLIP over unnecessary light sampling")
        if "compiled-environment" in means and means["compiled-environment"] >= means["environment-off"]:
            raise ValueError("Real-time environment lighting did not improve FLIP against the reference")
        for sequence in ["static", "camera-pan", "moving-occluder"]:
            if means[sequence + "-taa"] >= means[sequence + "-none"]:
                raise ValueError(f"TAA did not improve mean FLIP: {sequence}")
            if edge_means[sequence + "-taa"] >= edge_means[sequence + "-none"]:
                raise ValueError(f"TAA did not improve reference-edge FLIP: {sequence}")
            if sequence != "static" and means[sequence + "-taa"] >= means[sequence + "-frozen-native"]:
                raise ValueError(f"Frozen native-frame control was not distinguished: {sequence}")
    binary_paths = list(Path(flip.__file__).parent.glob("*.pyd")) + list(Path(flip.__file__).parent.glob("*.so"))
    evidence = dict(Accepted=True, Evaluator="NVIDIA flip-evaluator", Version=version,
                    ManifestSha256=sha256(manifest_path), Metric="LDR-FLIP on native tone-mapped sRGB",
                    NumpyVersion=np.__version__, PillowVersion=PIL.__version__,
                    EvaluatorBinaries={path.name: sha256(path) for path in binary_paths},
                    ReferenceEdgeMask="Either adjacent pixel of a horizontal/vertical reference sRGB channel difference > 0.05",
                    Groups=means, ReferenceEdgeGroups=edge_means, Pairs=results)
    result_path.write_text(json.dumps(evidence, indent=2), encoding="utf-8")
    print("AURELIAN_FLIP_PASSED", json.dumps(means, sort_keys=True))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("manifest", type=Path)
    evaluate(parser.parse_args().manifest.resolve())
