"""Geometry-derived partitions; training-only split selection; no new dependencies."""

import hashlib
import json
from pathlib import Path

import numpy as np


LEAVES = 8
FEATURES = 10
GRID_SIZE = 10
ARRAY_NAMES = ("planes", "children", "transforms", "weights", "uniformTransforms", "uniformWeights", "grid")


def coordinates(size):
    axis = (np.arange(size, dtype=np.float64) + .5) / size * 2 - 1
    x, z = np.meshgrid(axis, axis)
    return np.column_stack((x.reshape(-1), z.reshape(-1)))


def body_bounds(bodies, name):
    points = np.asarray(next(body["Positions"] for body in bodies if body["Id"] == name))
    return points.min(axis=0), points.max(axis=0)


def footprint_mask(points, bounds):
    lower, upper = bounds
    metres = points * 2.7
    covered = ((metres[:, 0] >= lower[0]) & (metres[:, 0] <= upper[0])
               & (metres[:, 1] >= lower[2]) & (metres[:, 1] <= upper[2]))
    return ~covered


def convex_hull(points):
    points = sorted(set(map(tuple, points)))

    def cross(a, b, c):
        return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])

    lower = []
    for point in points:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], point) <= 0:
            lower.pop()
        lower.append(point)
    upper = []
    for point in reversed(points):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], point) <= 0:
            upper.pop()
        upper.append(point)
    return lower[:-1] + upper[:-1]


def geometry_planes(bodies):
    occluder = body_bounds(bodies, "blue-block")
    lamp = body_bounds(bodies, "lamp")
    cuts = []

    def add(a, b, c):
        length = np.hypot(a, b)
        if length < 1e-9:
            return
        plane = np.array([a, b, c]) / length
        if plane[0] < -1e-9 or (abs(plane[0]) < 1e-9 and plane[1] < 0):
            plane = -plane
        if not any(np.max(np.abs(plane - previous)) < 1e-6 for previous in cuts):
            cuts.append(plane)

    for axis, coordinate in ((0, occluder[0][0]), (0, occluder[1][0]),
                             (1, occluder[0][2]), (1, occluder[1][2])):
        add(1 if axis == 0 else 0, 1 if axis == 1 else 0, -coordinate / 2.7)
    # Project authored box corners from the emitter centre and four area corners.
    # These silhouette lines are candidate split metadata, not exact area-light visibility.
    light_points = [(lamp[0] + lamp[1]) * .5]
    for x in (lamp[0][0], lamp[1][0]):
        for y in (lamp[0][1], lamp[1][1]):
            light_points.append(np.array([x, y, (lamp[0][2] + lamp[1][2]) * .5]))
    for light in light_points:
        projected = []
        for x in (occluder[0][0], occluder[1][0]):
            for y in (occluder[0][1], occluder[1][1]):
                for z in (occluder[0][2], occluder[1][2]):
                    point = np.array([x, y, z])
                    if light[1] <= point[1]:
                        raise ValueError("This experiment requires the entire emitter above the box.")
                    floor = light + (point - light) * light[1] / (light[1] - point[1])
                    projected.append(floor[[0, 2]] / 2.7)
        hull = convex_hull(projected)
        for index, first in enumerate(hull):
            second = hull[(index + 1) % len(hull)]
            a = first[1] - second[1]
            b = second[0] - first[0]
            add(a, b, -(a * first[0] + b * first[1]))
    return np.asarray(cuts), occluder


def polynomial(points, transform):
    normalized = (points - transform[:2]) * transform[2:]
    x, z = normalized.T
    return np.column_stack((np.ones(len(x)), x, z, x * x, x * z, z * z,
                            x * x * x, x * x * z, x * z * z, z * z * z))


def fit_leaf(points, target):
    lower = points.min(axis=0)
    upper = points.max(axis=0)
    transform = np.concatenate(((lower + upper) * .5, 2 / np.maximum(upper - lower, .05)))
    matrix = polynomial(points, transform)
    penalty = np.eye(FEATURES) * len(points) * 1e-6
    penalty[0, 0] = 0
    weights = np.linalg.solve(matrix.T @ matrix + penalty, matrix.T @ target)
    residual = matrix @ weights - target
    return transform, weights, np.sum(residual * residual)


def fit_tree(points, target, planes):
    leaves = [{"indices": np.arange(len(points)), "node": -1}]
    nodes = []
    while len(leaves) < LEAVES:
        best = None
        best_gain = -np.inf
        for leaf_index, leaf in enumerate(leaves):
            indices = leaf["indices"]
            _, _, parent_loss = fit_leaf(points[indices], target[indices])
            for plane in planes:
                side = points[indices] @ plane[:2] + plane[2] <= 0
                if min(side.sum(), (~side).sum()) < 40:
                    continue
                first, second = indices[side], indices[~side]
                first_loss = fit_leaf(points[first], target[first])[2]
                second_loss = fit_leaf(points[second], target[second])[2]
                gain = parent_loss - first_loss - second_loss
                if gain > best_gain:
                    best_gain = gain
                    best = (leaf_index, plane, first, second)
        if best is None:
            raise ValueError("Geometry candidates cannot produce the declared eight-leaf profile.")
        leaf_index, plane, first, second = best
        leaf = leaves.pop(leaf_index)
        node_index = len(nodes)
        if leaf["node"] >= 0:
            nodes[leaf["node"]]["children"][leaf["side"]] = node_index
        nodes.append({"plane": plane, "children": [None, None]})
        leaves.extend([{"indices": first, "node": node_index, "side": 0},
                       {"indices": second, "node": node_index, "side": 1}])
    transforms, weights = [], []
    for index, leaf in enumerate(leaves):
        transform, fitted, _ = fit_leaf(points[leaf["indices"]], target[leaf["indices"]])
        transforms.append(transform)
        weights.append(fitted)
        nodes[leaf["node"]]["children"][leaf["side"]] = -index - 1
    return {
        "planes": np.array([node["plane"] for node in nodes]),
        "children": np.array([node["children"] for node in nodes], dtype=np.int32),
        "transforms": np.array(transforms),
        "weights": np.array(weights),
    }


def fit(reference, bodies):
    points = coordinates(reference.shape[0])
    target = reference.reshape(-1, 6).astype(np.float64)
    planes, occluder = geometry_planes(bodies)
    mask = footprint_mask(points, occluder)
    arrays = fit_tree(points[mask], target[mask], planes)
    uniform_transforms, uniform_weights = [], []
    cells = np.minimum(((points + 1) * np.array([2, 1])).astype(int), np.array([3, 1]))
    for row in range(2):
        for column in range(4):
            region = mask & (cells[:, 0] == column) & (cells[:, 1] == row)
            transform, fitted, _ = fit_leaf(points[region], target[region])
            uniform_transforms.append(transform)
            uniform_weights.append(fitted)
    arrays["uniformTransforms"] = np.array(uniform_transforms)
    arrays["uniformWeights"] = np.array(uniform_weights)
    grid = np.zeros((GRID_SIZE, GRID_SIZE, 6))
    for row in range(GRID_SIZE):
        for column in range(GRID_SIZE):
            centre = np.array([(column + .5) / GRID_SIZE * 2 - 1, (row + .5) / GRID_SIZE * 2 - 1])
            nearest = np.argsort(np.sum((points[mask] - centre) ** 2, axis=1))[:12]
            grid[row, column] = target[mask][nearest].mean(axis=0)
    arrays["grid"] = grid
    return arrays, planes, occluder


def routes(points, arrays):
    result = np.zeros(len(points), dtype=np.int32)
    for index, point in enumerate(points):
        node = 0
        for _ in range(LEAVES):
            if node < 0:
                break
            plane = arrays["planes"][node]
            side = 0 if point @ plane[:2] + plane[2] <= 0 else 1
            node = arrays["children"][node, side]
        if node >= 0:
            raise ValueError("Partition traversal did not reach a leaf.")
        result[index] = -node - 1
    return result


def predict(points, arrays, representation):
    if representation == "MatchedGrid":
        position = np.clip((points + 1) * .5 * GRID_SIZE - .5, 0, GRID_SIZE - 1)
        lower = np.floor(position).astype(int)
        upper = np.minimum(lower + 1, GRID_SIZE - 1)
        fraction = position - lower
        x, z = lower.T
        right, top = upper.T
        fx, fz = fraction.T
        grid = arrays["grid"]
        return ((grid[z, x] * (1 - fx[:, None]) + grid[z, right] * fx[:, None]) * (1 - fz[:, None])
                + (grid[top, x] * (1 - fx[:, None]) + grid[top, right] * fx[:, None]) * fz[:, None])
    if representation == "Local":
        leaf_ids = routes(points, arrays)
        transforms, weights = arrays["transforms"], arrays["weights"]
    else:
        cells = np.minimum(((points + 1) * np.array([2, 1])).astype(int), np.array([3, 1]))
        leaf_ids = cells[:, 1] * 4 + cells[:, 0]
        transforms, weights = arrays["uniformTransforms"], arrays["uniformWeights"]
    result = np.empty((len(points), 6))
    for leaf in range(LEAVES):
        mask = leaf_ids == leaf
        result[mask] = polynomial(points[mask], transforms[leaf]) @ weights[leaf]
    return np.maximum(result, 0)


def array_hash(arrays):
    digest = hashlib.sha256()
    for name in ARRAY_NAMES:
        dtype = "<i4" if name == "children" else "<f4"
        digest.update(np.asarray(arrays[name], dtype=dtype).reshape(-1).tobytes())
    return digest.hexdigest()


def fit_and_save(stage, output, decoder_path, scene_key):
    from pxr import Sdf, Usd, UsdGeom, Vt

    bodies = []
    for prim in stage.Traverse():
        if prim.IsA(UsdGeom.Mesh):
            bodies.append({"Id": prim.GetAttribute("aurelian:bodyId").Get(),
                           "Positions": list(map(tuple, UsdGeom.Mesh(prim).GetPointsAttr().Get()))})
    training = np.fromfile(output / "training.bin", dtype="<f4").reshape(48, 48, 6)
    arrays, candidates, occluder = fit(training, bodies)
    artifact = Usd.Stage.CreateNew(str(output / "local-lighting.usda"))
    artifact.GetRootLayer().subLayerPaths = ["lighting.usda"]
    prim = UsdGeom.Scope.Define(artifact, "/AurelianLocalLighting").GetPrim()
    strings = {
        "schema": "aurelian.local-expert/1",
        "sceneKey": scene_key,
        "decoderKey": hashlib.sha256(Path(decoder_path).read_bytes()).hexdigest(),
        "weightsSha256": array_hash(arrays),
        "basis": "unit sun indirect RGB; unit lamp direct-plus-indirect RGB",
        "colourSpace": "scene-linear Rec.709",
        "domain": "visible floor, Y=0, X/Z in [-2.7,2.7] metres",
        "training": "eight local cubic experts; geometry-derived cuts; greedy training-only split selection",
    }
    for name, value in strings.items():
        prim.CreateAttribute("aurelian:" + name, Sdf.ValueTypeNames.String).Set(value)
    for name, value in (("leaves", LEAVES), ("features", FEATURES), ("gridSize", GRID_SIZE)):
        prim.CreateAttribute("aurelian:" + name, Sdf.ValueTypeNames.Int).Set(value)
    for name in ARRAY_NAMES:
        if name == "children":
            values = np.asarray(arrays[name], dtype=np.int32).reshape(-1).tolist()
            prim.CreateAttribute("aurelian:" + name, Sdf.ValueTypeNames.IntArray).Set(Vt.IntArray(values))
        else:
            values = np.asarray(arrays[name], dtype=np.float32).reshape(-1).tolist()
            prim.CreateAttribute("aurelian:" + name, Sdf.ValueTypeNames.FloatArray).Set(Vt.FloatArray(values))
    artifact.GetRootLayer().Save()
    reopened = Usd.Stage.Open(str(output / "local-lighting.usda"))
    if reopened is None or reopened.GetCompositionErrors():
        raise ValueError("Local expert USD failed composition.")
    prim = reopened.GetPrimAtPath("/AurelianLocalLighting")
    loaded = {name: list(prim.GetAttribute("aurelian:" + name).Get()) for name in ARRAY_NAMES}
    if array_hash(loaded) != strings["weightsSha256"]:
        raise ValueError("Local USD round trip changed coefficients or routing.")
    manifest = {name: prim.GetAttribute("aurelian:" + name).Get() for name in strings}
    for name in ("leaves", "features", "gridSize"):
        manifest[name] = prim.GetAttribute("aurelian:" + name).Get()
    manifest.update(loaded)
    (output / "loaded-local-expert.json").write_text(json.dumps(manifest, indent=2))
    (output / "partition-authoring.json").write_text(json.dumps({
        "CandidateCount": len(candidates), "CandidatePlanes": candidates.tolist(),
        "OccluderBoundsMetres": [bound.tolist() for bound in occluder],
        "SplitSelection": "training loss only; min 40 samples per leaf; fixed eight-leaf budget",
        "ReferenceInputs": ["training.bin"],
    }, indent=2))
    print("AURELIAN_LOCAL_USD_EXPERT_ROUNDTRIP_PASSED", flush=True)
