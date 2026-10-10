"""Bounded Cycles -> OpenUSD -> deterministic decoder experiment. No downloads."""

import argparse
import hashlib
import json
import math
import sys
import time
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector
from pxr import Gf, Sdf, Usd, UsdGeom, UsdLux, UsdShade, Vt


ARRAY_NAMES = ("features", "polynomial", "network", "grid", "residual")
TRAIN_SIZE = 48
TEST_SIZE = 64
WIDTH = 32
GRID_SIZE = 6


def array_hash(arrays):
    digest = hashlib.sha256()
    for name in ARRAY_NAMES:
        values = np.asarray(arrays[name], dtype="<f4").reshape(-1)
        digest.update(values.tobytes())
    return digest.hexdigest()


sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src/Aurelian/Aurelian.Assets/Lighting/Authoring"))
from cycles_reference import write_scene, configure_cycles, load_scene, bake


def coordinates(size):
    axis = (np.arange(size, dtype=np.float64) + .5) / size * 2 - 1
    x, z = np.meshgrid(axis, axis)
    return np.column_stack((x.reshape(-1), z.reshape(-1)))


def valid_receivers(points):
    x = points[:, 0] * 2.7
    z = points[:, 1] * 2.7
    return ~((x >= .3) & (x <= 1.3) & (z >= -.8) & (z <= .2))


def features(points, parameters):
    x, z = points.T
    polynomial = np.column_stack((np.ones(len(x)), x, z, x * x, x * z, z * z))
    waves = np.maximum(points @ parameters[6:, :2].T + parameters[6:, 2], 0)
    return np.column_stack((polynomial, waves))


def solve(matrix, target, regularization):
    gram = matrix.T @ matrix
    penalty = np.eye(matrix.shape[1]) * regularization * len(matrix)
    penalty[0, 0] = 0
    return np.linalg.solve(gram + penalty, matrix.T @ target)


def grid_lookup(points, grid):
    coordinate = np.clip((points + 1) * .5 * GRID_SIZE - .5, 0, GRID_SIZE - 1)
    lower = np.floor(coordinate).astype(int)
    upper = np.minimum(lower + 1, GRID_SIZE - 1)
    fraction = coordinate - lower
    x, z = lower.T
    right, top = upper.T
    fx, fz = fraction.T
    return ((grid[z, x] * (1 - fx[:, None]) + grid[z, right] * fx[:, None]) * (1 - fz[:, None])
            + (grid[top, x] * (1 - fx[:, None]) + grid[top, right] * fx[:, None]) * fz[:, None])


def fit(reference):
    points = coordinates(TRAIN_SIZE)
    mask = valid_receivers(points)
    target = reference.reshape(-1, 6).astype(np.float64)
    generator = np.random.default_rng(41073)
    parameters = np.zeros((WIDTH, 3), dtype=np.float64)
    parameters[6:, :2] = generator.normal(0, 4, (WIDTH - 6, 2))
    parameters[6:, 2] = generator.uniform(-math.pi, math.pi, WIDTH - 6)
    matrix = features(points, parameters)
    polynomial = solve(matrix[mask, :6], target[mask], 1e-5)
    network = solve(matrix[mask], target[mask], 2e-4)
    grid = np.zeros((GRID_SIZE, GRID_SIZE, 6), dtype=np.float64)
    for row in range(GRID_SIZE):
        for column in range(GRID_SIZE):
            centre = np.array([(column + .5) / GRID_SIZE * 2 - 1, (row + .5) / GRID_SIZE * 2 - 1])
            distances = np.sum((points[mask] - centre) ** 2, axis=1)
            nearest = np.argsort(distances)[:12]
            grid[row, column] = target[mask][nearest].mean(axis=0)
    residual = solve(matrix[mask], target[mask] - grid_lookup(points[mask], grid), 5e-4)
    return {"features": parameters, "polynomial": polynomial, "network": network, "grid": grid, "residual": residual}


def save_artifact(scene_path, output_path, fixture, arrays, decoder_key):
    stage = Usd.Stage.CreateNew(str(output_path))
    stage.GetRootLayer().subLayerPaths = [scene_path.name]
    prim = UsdGeom.Scope.Define(stage, "/AurelianLighting").GetPrim()
    strings = {
        "schema": "aurelian.scene-expert/1",
        "sceneKey": fixture["SceneKey"],
        "decoderKey": decoder_key,
        "weightsSha256": array_hash(arrays),
        "colourSpace": "scene-linear Rec.709",
        "domain": "visible floor, Y=0, X/Z in [-2.7,2.7] metres",
        "basis": "unit sun indirect RGB; unit lamp direct-plus-indirect RGB",
        "training": "fixed ReLU hidden features; ridge-trained output weights",
    }
    for name, value in strings.items():
        prim.CreateAttribute("aurelian:" + name, Sdf.ValueTypeNames.String).Set(value)
    prim.CreateAttribute("aurelian:width", Sdf.ValueTypeNames.Int).Set(WIDTH)
    prim.CreateAttribute("aurelian:gridSize", Sdf.ValueTypeNames.Int).Set(GRID_SIZE)
    for name in ARRAY_NAMES:
        values = np.asarray(arrays[name], dtype=np.float32).reshape(-1).tolist()
        prim.CreateAttribute("aurelian:" + name, Sdf.ValueTypeNames.FloatArray).Set(Vt.FloatArray(values))
    stage.GetRootLayer().Save()
    # Extract runtime data only from a reopened native OpenUSD stage.
    reopened = Usd.Stage.Open(str(output_path))
    if reopened is None or reopened.GetCompositionErrors():
        raise RuntimeError("USD artifact failed composition.")
    prim = reopened.GetPrimAtPath("/AurelianLighting")
    loaded = {name: list(prim.GetAttribute("aurelian:" + name).Get()) for name in ARRAY_NAMES}
    if array_hash(loaded) != strings["weightsSha256"]:
        raise RuntimeError("USD float-array round trip changed weights.")
    manifest = {name: prim.GetAttribute("aurelian:" + name).Get() for name in strings}
    manifest["width"] = prim.GetAttribute("aurelian:width").Get()
    manifest["gridSize"] = prim.GetAttribute("aurelian:gridSize").Get()
    manifest.update(loaded)
    return manifest


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--fixture", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--decoder", required=True)
    parser.add_argument("--reuse", action="store_true")
    parser.add_argument("--local-decoder")
    parser.add_argument("--constraints")
    parser.add_argument("--continuous-contract")
    parser.add_argument("--adaptive-contract")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    if args.constraints and not args.local_decoder:
        parser.error("--constraints requires --local-decoder")
    if args.continuous_contract and not args.constraints:
        parser.error("--continuous-contract requires --constraints")
    if args.adaptive_contract and not args.continuous_contract:
        parser.error("--adaptive-contract requires --continuous-contract")
    output = Path(args.output)
    fixture = json.loads(Path(args.fixture).read_text(encoding="utf-8"))
    scene_path = output / "room.usda"
    write_scene(fixture, scene_path)
    stage = Usd.Stage.Open(str(scene_path))
    reference_manifest = output / "reference-manifest.json"
    reusable = args.reuse and reference_manifest.exists()
    if reusable:
        previous = json.loads(reference_manifest.read_text())
        reusable = (previous["SceneKey"] == fixture["SceneKey"]
                    and previous["Samples"] == 8192
                    and previous["BlenderVersion"] == bpy.app.version_string)
        if reusable:
            previous["Scope"] = "Cycles sun indirect and lamp diffuse direct-plus-indirect; finite bounce cap, raster mesh representation"
            reference_manifest.write_text(json.dumps(previous, indent=2))
    if not reusable:
        devices = configure_cycles()
        receiver, sun, emitters = load_scene(stage)
        training = np.concatenate([bake(receiver, sun, emitters, TRAIN_SIZE, basis, 173 + basis) for basis in range(2)], axis=2)
        testing = np.concatenate([bake(receiver, sun, emitters, TEST_SIZE, basis, 491 + basis) for basis in range(2)], axis=2)
        repeat = np.concatenate([bake(receiver, sun, emitters, TEST_SIZE, basis, 829 + basis) for basis in range(2)], axis=2)
        for name, values in (("training", training), ("testing", testing), ("repeat", repeat)):
            values.astype("<f4").tofile(output / (name + ".bin"))
        reference_manifest.write_text(json.dumps({
            "SceneKey": fixture["SceneKey"], "Devices": devices, "TrainingSize": TRAIN_SIZE,
            "TestingSize": TEST_SIZE, "Samples": 8192, "BounceCap": 8,
            "BlenderVersion": bpy.app.version_string,
            "Denoising": False, "AdaptiveSampling": False, "Clamping": False,
            "Scope": "Cycles sun indirect and lamp diffuse direct-plus-indirect; finite bounce cap, raster mesh representation",
        }, indent=2))
    training = np.fromfile(output / "training.bin", dtype="<f4").reshape(TRAIN_SIZE, TRAIN_SIZE, 6)
    arrays = fit(training)
    decoder_key = hashlib.sha256(Path(args.decoder).read_bytes()).hexdigest()
    manifest = save_artifact(scene_path, output / "lighting.usda", fixture, arrays, decoder_key)
    (output / "loaded-expert.json").write_text(json.dumps(manifest, indent=2))
    if args.local_decoder:
        sys.dont_write_bytecode = True
        sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src/Aurelian/Aurelian.Assets/Lighting/Authoring"))
        from local_lighting_experts import fit_and_save
        fit_and_save(stage, output, args.local_decoder, fixture["SceneKey"])
        if args.constraints:
            from constrained_lighting_experts import compile_and_save
            compile_and_save(output, args.constraints)
        if args.continuous_contract:
            from continuous_lighting_experts import compile_and_save as compile_continuous
            compile_continuous(output, args.continuous_contract)
        if args.adaptive_contract:
            from adaptive_lighting_experts import compile_and_save as compile_adaptive
            compile_adaptive(output, args.adaptive_contract)
    print("AURELIAN_USD_EXPERT_ROUNDTRIP_PASSED", flush=True)


if __name__ == "__main__":
    main()
