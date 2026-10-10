"""Optional Blender/Cycles/OpenUSD authoring adapter. Runtime has no Blender/USD dependency."""

import argparse
import hashlib
import json
import sys
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).parent))

import bpy
import numpy as np
from pxr import Sdf, Usd, UsdGeom, Vt

from cycles_reference import write_scene, configure_cycles, load_scene, bake
from local_lighting_experts import coordinates, fit_tree, box_projection_planes
from continuous_lighting_experts import mesh_from_partition, design_matrix, payload_hash
from adaptive_lighting_experts import refine, numeric_bytes


def receiver_albedo(points, body, domain):
    positions = np.asarray(body["Positions"]).reshape(-1, 3)
    colours = np.asarray(body.get("FaceAlbedos") or np.tile(body["Albedo"], len(positions) // 3)).reshape(-1, 3)
    metres = np.column_stack((domain["MinimumX"] + (points[:, 0] + 1) * domain["Width"] / 2,
                              domain["MinimumZ"] + (points[:, 1] + 1) * domain["Depth"] / 2))
    result = np.zeros((len(points), 3))
    assigned = np.zeros(len(points), dtype=bool)
    homogeneous = np.column_stack((metres, np.ones(len(points))))
    for face, triangle in enumerate(positions.reshape(-1, 3, 3)):
        if np.max(np.abs(triangle[:, 1] - domain["Height"])) > .001:
            continue
        matrix = np.vstack((triangle[:, [0, 2]].T, np.ones(3)))
        if abs(np.linalg.det(matrix)) < 1e-10:
            continue
        inside = (homogeneous @ np.linalg.inv(matrix).T).min(axis=1) >= -1e-6
        result[inside & ~assigned] = colours[face]
        assigned |= inside
    if not assigned.all() or np.min(result) <= 0:
        raise ValueError("receiver-requires-covered-positive-Lambertian-albedo")
    return result


def authoring_geometry(recipe, bodies, points):
    domain = recipe["Receiver"]
    metres = np.column_stack((domain["MinimumX"] + (points[:, 0] + 1) * domain["Width"] / 2,
                              domain["MinimumZ"] + (points[:, 1] + 1) * domain["Depth"] / 2))
    visible = np.ones(len(points), dtype=bool)
    candidates = [np.array([1, 0, value]) for value in (-.5, 0, .5)]
    candidates += [np.array([0, 1, 0])]
    emitters = [body for body in bodies if max(body["Emission"]) > 0]
    for body in bodies:
        if body["Id"] == recipe["ReceiverId"]:
            continue
        positions = np.asarray(body["Positions"])
        lower, upper = positions.min(axis=0), positions.max(axis=0)
        if lower[1] > domain["Height"] + .01 or upper[1] <= domain["Height"] + .01:
            continue
        covered = ((metres[:, 0] >= lower[0]) & (metres[:, 0] <= upper[0])
                   & (metres[:, 1] >= lower[2]) & (metres[:, 1] <= upper[2]))
        visible &= ~covered
        # Reuse the geometry candidate planner in its canonical 5.4m domain, not the physical bake geometry.
        def normalized_bounds(value):
            positions = np.asarray(value["Positions"]).copy()
            positions[:, 0] = ((positions[:, 0] - domain["MinimumX"]) * 2 / domain["Width"] - 1) * 2.7
            positions[:, 2] = ((positions[:, 2] - domain["MinimumZ"]) * 2 / domain["Depth"] - 1) * 2.7
            positions[:, 1] -= domain["Height"]
            return positions.min(axis=0), positions.max(axis=0)

        for emitter in emitters:
            emitter_points = np.asarray(emitter["Positions"])
            if emitter_points[:, 1].min() <= upper[1]:
                continue
            planes, _ = box_projection_planes(normalized_bounds(body), normalized_bounds(emitter))
            candidates.extend(planes)
    return visible, np.asarray(candidates)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--recipe", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--reuse", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    document = json.loads(Path(args.recipe).read_text(encoding="utf-8-sig"))
    recipe, scene_key = document["Recipe"], document["SceneKey"]
    output = Path(args.output)
    output.mkdir(parents=True, exist_ok=True)
    bodies = []
    for body in recipe["Contributors"]:
        bodies.append(dict(body, Positions=np.asarray(body["Positions"]).reshape(-1, 3).tolist()))
    fixture = {"SceneKey": scene_key, "Sun": recipe["SunDirection"], "Bodies": bodies}
    write_scene(fixture, output / "scene.usda")
    stage = Usd.Stage.Open(str(output / "scene.usda"))
    compiler_key = hashlib.sha256(b"".join(path.read_bytes() for path in sorted(Path(__file__).parent.glob("*.py")))).hexdigest()
    acquisition_key = hashlib.sha256(Path(__file__).with_name("cycles_reference.py").read_bytes()).hexdigest()
    reference_path = output / "reference-manifest.json"
    cached = json.loads(reference_path.read_text()) if args.reuse and reference_path.exists() else {}
    reusable = cached.get("SceneKey") == scene_key and cached.get("AcquisitionKey") == acquisition_key
    reusable &= cached.get("BlenderVersion") == bpy.app.version_string
    if reusable:
        for name in ("training", "testing", "repeat"):
            path = output / (name + ".bin")
            reusable &= path.exists() and hashlib.sha256(path.read_bytes()).hexdigest() == cached.get(name)
    if not reusable:
        devices = configure_cycles()
        receiver, sun, emitters = load_scene(stage, recipe["ReceiverId"], recipe["Receiver"])
        for name, size, seed in (("training", 48, 173), ("testing", 64, 491), ("repeat", 64, 829)):
            values = np.concatenate([bake(receiver, sun, emitters, size, basis, seed + basis) for basis in range(2)], axis=2)
            values.astype("<f4").tofile(output / (name + ".bin"))
        cached = {"SceneKey": scene_key, "AcquisitionKey": acquisition_key, "BlenderVersion": bpy.app.version_string,
                  "Samples": 8192, "BounceCap": 8, "Devices": devices}
        for name in ("training", "testing", "repeat"):
            cached[name] = hashlib.sha256((output / (name + ".bin")).read_bytes()).hexdigest()
        reference_path.write_text(json.dumps(cached, indent=2))
    receiver_body = next(body for body in bodies if body["Id"] == recipe["ReceiverId"])
    points = coordinates(48)
    visible, planes = authoring_geometry(recipe, bodies, points)
    albedo = receiver_albedo(points, receiver_body, recipe["Receiver"])
    training = np.fromfile(output / "training.bin", dtype="<f4").reshape(-1, 6).astype(np.float64)
    target = training / np.tile(albedo, (1, 2))
    arrays = fit_tree(points[visible], target[visible], planes)
    vertices, triangles, matrices = mesh_from_partition(arrays)
    base_bytes = numeric_bytes(vertices, triangles)
    refinements = min(recipe["MaximumRefinements"], (recipe["NumericBudgetBytes"] - base_bytes) // 136)
    if refinements < 0:
        raise ValueError("declared-budget-cannot-store-the-base-receiver-mesh")
    contract = {"ridge": recipe["Ridge"], "numericBudgetBytes": recipe["NumericBudgetBytes"],
                "interiorEdgeSplits": refinements, "randomSeed": 419}
    profile = {"boundaryDegree": 2, "interiorBubble": False, "refinement": "training-residual"}
    vertices, triangles, matrices, provenance = refine(points[visible], target[visible], arrays,
                                                       vertices, triangles, matrices, contract, profile)
    design = design_matrix(points[visible], arrays, vertices, triangles, matrices, 2, False)
    weights = np.linalg.solve(design.T @ design + np.eye(design.shape[1]) * len(design) * recipe["Ridge"],
                              design.T @ target[visible])
    provenance.update({"Recipe": recipe, "TrainingInputs": ["training.bin"], "ObservedRank": int(np.linalg.matrix_rank(design)),
                       "CoefficientCount": len(weights), "Reference": cached})
    # Quality evaluation occurs after topology selection and fitting. It cannot choose a different model.
    queries = coordinates(64)
    mask, _ = authoring_geometry(recipe, bodies, queries)
    test_albedo = receiver_albedo(queries, receiver_body, recipe["Receiver"])
    testing = np.fromfile(output / "testing.bin", dtype="<f4").reshape(-1, 6)
    repeat = np.fromfile(output / "repeat.bin", dtype="<f4").reshape(-1, 6)
    predicted = np.maximum(design_matrix(queries, arrays, vertices, triangles, matrices, 2, False) @ weights, 0)
    predicted *= np.tile(test_albedo, (1, 2))
    rmse = float(np.sqrt(np.mean((predicted[mask] - testing[mask]) ** 2)))
    evidence = {"Accepted": rmse <= recipe["MaximumValidationRmse"] and provenance["ObservedRank"] == len(weights), "SceneKey": scene_key, "Rmse": rmse,
                "SamplingNoise": float(np.sqrt(np.mean((repeat[mask] - testing[mask]) ** 2))),
                "NumericPayloadBytes": numeric_bytes(vertices, triangles), "CompilerKey": compiler_key,
                "ValidationReceivers": int(mask.sum()), "Provenance": provenance}
    (output / "compile-evidence.json").write_text(json.dumps(evidence, indent=2))
    if not evidence["Accepted"]:
        raise ValueError("compiled-lighting-quality-or-rank-gate-failed: " + str(rmse))
    quality = {"ValidationRmse": rmse, "MaximumValidationRmse": recipe["MaximumValidationRmse"],
               "SamplingNoise": evidence["SamplingNoise"], "NumericPayloadBytes": evidence["NumericPayloadBytes"],
               "NumericBudgetBytes": recipe["NumericBudgetBytes"], "ObservedRank": provenance["ObservedRank"],
               "CoefficientCount": len(weights), "ValidationReceivers": evidence["ValidationReceivers"]}
    artifact = Usd.Stage.CreateNew(str(output / "lighting.usda"))
    artifact.GetRootLayer().subLayerPaths = ["scene.usda"]
    prim = UsdGeom.Scope.Define(artifact, "/AurelianStaticDiffuse").GetPrim()
    for name, values in (("vertices", vertices), ("weights", weights)):
        prim.CreateAttribute("aurelian:" + name, Sdf.ValueTypeNames.FloatArray).Set(Vt.FloatArray(values.astype(np.float32).reshape(-1).tolist()))
    prim.CreateAttribute("aurelian:triangles", Sdf.ValueTypeNames.IntArray).Set(Vt.IntArray(triangles.reshape(-1).tolist()))
    metadata = {"decoder": "aurelian.static-diffuse.p2/1", "sceneKey": scene_key, "receiver": recipe["Receiver"],
                "sunDirection": recipe["SunDirection"], "compilerKey": compiler_key, "trainingKey": cached["training"],
                "colourSpace": "scene-linear Rec.709", "basis": "sun-indirect; emission-total; per-unit-receiver-albedo",
                "quality": quality, "provenance": provenance}
    prim.CreateAttribute("aurelian:metadata", Sdf.ValueTypeNames.String).Set(json.dumps(metadata))
    artifact.GetRootLayer().Save()
    reopened = Usd.Stage.Open(str(output / "lighting.usda"))
    if reopened is None or reopened.GetCompositionErrors():
        raise ValueError("native-usd-lighting-composition-failed")
    prim = reopened.GetPrimAtPath("/AurelianStaticDiffuse")
    content = json.loads(prim.GetAttribute("aurelian:metadata").Get())
    for name in ("vertices", "triangles", "weights"):
        content[name] = list(prim.GetAttribute("aurelian:" + name).Get())
    if payload_hash(content["vertices"], content["triangles"], content["weights"]) != payload_hash(vertices, triangles, weights):
        raise ValueError("native-usd-lighting-numeric-roundtrip-failed")
    serialized = json.dumps(content, separators=(",", ":"))
    envelope = {"schema": "aurelian.static-diffuse.asset/1", "content": serialized,
                "sha256": hashlib.sha256(serialized.encode()).hexdigest()}
    (output / "lighting.alight").write_text(json.dumps(envelope, indent=2))
    print("AURELIAN_STATIC_DIFFUSE_ASSET_COMPILED", scene_key, rmse, flush=True)


if __name__ == "__main__":
    main()
