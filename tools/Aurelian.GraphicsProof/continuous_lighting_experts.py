"""Conforming shared boundary values plus optional edge-vanishing local detail."""

import hashlib
import json
from pathlib import Path

import numpy as np

from constrained_lighting_experts import leaf_halfspaces
from local_lighting_experts import coordinates, footprint_mask, routes


def cell_vertices(halfspaces):
    vertices = []
    for first in range(len(halfspaces)):
        for second in range(first + 1, len(halfspaces)):
            matrix = halfspaces[[first, second], :2]
            if abs(np.linalg.det(matrix)) < 1e-10:
                continue
            point = np.linalg.solve(matrix, -halfspaces[[first, second], 2])
            if np.max(halfspaces[:, :2] @ point + halfspaces[:, 2]) > 1e-8:
                continue
            if not any(np.linalg.norm(point - previous) < 1e-8 for previous in vertices):
                vertices.append(point)
    if len(vertices) < 3:
        raise ValueError("continuous-fit-degenerate-cell")
    centre = np.mean(vertices, axis=0)
    return sorted(vertices, key=lambda point: np.arctan2(point[1] - centre[1], point[0] - centre[0]))


def mesh_from_partition(arrays):
    cells = leaf_halfspaces(arrays)
    polygons = [cell_vertices(cell) for cell in cells]
    vertices = []

    def index_of(point):
        for index, previous in enumerate(vertices):
            if np.linalg.norm(point - previous) < 1e-8:
                return index
        vertices.append(np.array(point))
        return len(vertices) - 1

    for polygon in polygons:
        for point in polygon:
            index_of(point)
    boundary_vertices = list(vertices)
    triangles = []
    for leaf, polygon in enumerate(polygons):
        ring = []
        # Insert all retained T-junction vertices into both incident cell edges.
        # A visually coincident but disconnected edge would not establish C0.
        for first, second in zip(polygon, polygon[1:] + polygon[:1]):
            direction = second - first
            length_squared = direction @ direction
            candidates = []
            for point in boundary_vertices:
                fraction = (point - first) @ direction / length_squared
                nearest = first + fraction * direction
                if -1e-8 <= fraction < 1 - 1e-8 and np.linalg.norm(point - nearest) < 1e-8:
                    candidates.append((fraction, index_of(point)))
            ring.extend(index for _, index in sorted(candidates))
        centre = index_of(np.mean(polygon, axis=0))
        for first, second in zip(ring, ring[1:] + ring[:1]):
            triangles.append([centre, first, second, leaf])
    # Round once before fitting so CPU/GPU and native USD share the same mesh.
    vertices = np.asarray(vertices, dtype=np.float32).astype(np.float64)
    triangles = np.asarray(triangles, dtype=np.int32)
    matrices = barycentric_matrices(vertices, triangles)
    return vertices, triangles, matrices


def barycentric_matrices(vertices, triangles):
    result = []
    for triangle in triangles:
        points = vertices[triangle[:3]]
        matrix = np.vstack((points.T, np.ones(3)))
        if np.linalg.det(matrix) <= 1e-10:
            raise ValueError("continuous-fit-triangle-orientation")
        result.append(np.linalg.inv(matrix))
    return np.asarray(result)


def canonical_edges(triangles):
    result = set()
    for triangle in triangles:
        for first, second in ((0, 1), (1, 2), (2, 0)):
            result.add(tuple(sorted((int(triangle[first]), int(triangle[second])))))
    return sorted(result)


def triangle_nodes(vertices, triangles, degree, bubble):
    if degree not in (1, 2) or not isinstance(bubble, bool):
        raise ValueError("continuous-fit-profile-invalid")
    edges = canonical_edges(triangles)
    edge_ids = {edge: len(vertices) + index for index, edge in enumerate(edges)}
    boundary_count = len(vertices) + (len(edges) if degree == 2 else 0)
    nodes = []
    for index, triangle in enumerate(triangles):
        current = triangle[:3].tolist()
        if degree == 2:
            for first, second in ((0, 1), (1, 2), (2, 0)):
                edge = tuple(sorted((int(triangle[first]), int(triangle[second]))))
                current.append(edge_ids[edge])
        if bubble:
            current.append(boundary_count + index)
        nodes.append(current)
    coefficient_count = boundary_count + (len(triangles) if bubble else 0)
    return np.asarray(nodes, dtype=np.int32), coefficient_count


def local_features(barycentric, degree, bubble):
    first, second, third = barycentric.T
    if degree == 1:
        features = [first, second, third]
    else:
        features = [first * (2 * first - 1), second * (2 * second - 1), third * (2 * third - 1),
                    4 * first * second, 4 * second * third, 4 * third * first]
    if bubble:
        features.append(27 * first * second * third)
    return np.column_stack(features)


def design_matrix(points, arrays, vertices, triangles, matrices, degree, bubble):
    nodes, count = triangle_nodes(vertices, triangles, degree, bubble)
    leaves = routes(points, arrays)
    selected = np.full(len(points), -1, dtype=np.int32)
    design = np.zeros((len(points), count))
    homogeneous = np.column_stack((points, np.ones(len(points))))
    for index, triangle in enumerate(triangles):
        candidates = np.flatnonzero((leaves == triangle[3]) & (selected < 0))
        barycentric = homogeneous[candidates] @ matrices[index].T
        inside = np.min(barycentric, axis=1) >= -1e-6
        receivers = candidates[inside]
        features = local_features(barycentric[inside], degree, bubble)
        design[np.ix_(receivers, nodes[index])] = features
        selected[receivers] = index
    if np.any(selected < 0):
        raise ValueError("continuous-fit-uncovered-receiver")
    return design


def compiler_key():
    files = [Path(__file__), Path(__file__).with_name("constrained_lighting_experts.py"),
             Path(__file__).with_name("local_lighting_experts.py")]
    return hashlib.sha256(b"".join(path.read_bytes() for path in files)).hexdigest()


def payload_hash(vertices, triangles, weights):
    digest = hashlib.sha256()
    for values, dtype in ((vertices, "<f4"), (triangles, "<i4"), (weights, "<f4")):
        digest.update(np.asarray(values, dtype=dtype).reshape(-1).tobytes())
    return digest.hexdigest()


def compile_and_save(output, contract_path, mesh_refiner=None, prefix="continuous", compiler_identity=None):
    from pxr import Sdf, Usd, UsdGeom, Vt

    contract_bytes = Path(contract_path).read_bytes()
    contract = json.loads(contract_bytes)
    base = json.loads((output / "loaded-local-expert.json").read_text())
    for name, value in (("schema", "aurelian.continuous-fit-contract/1"), ("receiver", "floor"),
                        ("continuity", "C0"), ("domain", base["domain"]), ("basis", base["basis"])):
        if contract.get(name) != value:
            raise ValueError("continuous-fit-contract-mismatch: " + name)
    if not np.isfinite(contract["ridge"]) or contract["ridge"] <= 0:
        raise ValueError("continuous-fit-ridge-invalid")
    names = [profile["name"] for profile in contract["profiles"]]
    if not names or len(names) != len(set(names)):
        raise ValueError("continuous-fit-profile-names-invalid")
    arrays = {"planes": np.array(base["planes"]).reshape(7, 3),
              "children": np.array(base["children"], dtype=np.int32).reshape(7, 2)}
    base_vertices, base_triangles, base_matrices = mesh_from_partition(arrays)
    authoring = json.loads((output / "partition-authoring.json").read_text())
    points = coordinates(48)
    visible = footprint_mask(points, np.array(authoring["OccluderBoundsMetres"]))
    target = np.fromfile(output / "training.bin", dtype="<f4").reshape(-1, 6)[visible].astype(np.float64)
    for profile in contract["profiles"]:
        name, degree, bubble = profile["name"], profile["boundaryDegree"], profile["interiorBubble"]
        vertices, triangles, matrices = base_vertices, base_triangles, base_matrices
        refinement = {}
        if mesh_refiner is not None:
            vertices, triangles, matrices, refinement = mesh_refiner(
                points[visible], target, arrays, vertices, triangles, matrices, contract, profile)
        design = design_matrix(points[visible], arrays, vertices, triangles, matrices, degree, bubble)
        penalty = np.eye(design.shape[1]) * len(target) * contract["ridge"]
        # Positive ridge also resolves any hidden/unobserved interior coefficient.
        weights = np.linalg.solve(design.T @ design + penalty, design.T @ target)
        if not np.isfinite(weights).all():
            raise ValueError("continuous-fit-nonfinite-weights")
        retained = {
            "Contract": contract, "ContractKey": hashlib.sha256(contract_bytes).hexdigest(),
            "CompilerKey": compiler_identity or compiler_key(), "TrainingKey": hashlib.sha256((output / "training.bin").read_bytes()).hexdigest(),
            "PartitionKey": base["weightsSha256"], "Profile": name,
            "BoundaryDegree": degree, "InteriorBubble": bubble,
            "CoefficientCount": design.shape[1], "ObservedRank": int(np.linalg.matrix_rank(design)),
            "ReferenceInputs": ["training.bin"], "MeshConstruction": "retained convex cells, conforming T junctions, centroid fans",
        }
        retained.update(refinement)
        artifact_path = output / (prefix + "-" + name + ".usda")
        artifact = Usd.Stage.CreateNew(str(artifact_path))
        artifact.GetRootLayer().subLayerPaths = ["local-lighting.usda"]
        prim = UsdGeom.Scope.Define(artifact, "/AurelianContinuousLighting").GetPrim()
        strings = {
            "schema": "aurelian.continuous-expert/1", "sceneKey": base["sceneKey"],
            "decoderKey": hashlib.sha256(Path("tools/Aurelian.GraphicsProof/Assets/ContinuousExpertDecoder.v.ts").read_bytes()).hexdigest(),
            "weightsSha256": payload_hash(vertices, triangles, weights),
            "compilation": json.dumps(retained), "basis": base["basis"], "domain": base["domain"],
            "colourSpace": base["colourSpace"],
        }
        for key, value in strings.items():
            prim.CreateAttribute("aurelian:" + key, Sdf.ValueTypeNames.String).Set(value)
        for key, values in (("vertices", vertices), ("weights", weights)):
            prim.CreateAttribute("aurelian:" + key, Sdf.ValueTypeNames.FloatArray).Set(
                Vt.FloatArray(np.asarray(values, dtype=np.float32).reshape(-1).tolist()))
        prim.CreateAttribute("aurelian:triangles", Sdf.ValueTypeNames.IntArray).Set(Vt.IntArray(triangles.reshape(-1).tolist()))
        artifact.GetRootLayer().Save()
        reopened = Usd.Stage.Open(str(artifact_path))
        if reopened is None or reopened.GetCompositionErrors():
            raise ValueError("continuous-fit-usd-composition-failed")
        prim = reopened.GetPrimAtPath("/AurelianContinuousLighting")
        manifest = {key: prim.GetAttribute("aurelian:" + key).Get() for key in strings}
        for key in ("vertices", "triangles", "weights"):
            manifest[key] = list(prim.GetAttribute("aurelian:" + key).Get())
        if payload_hash(manifest["vertices"], manifest["triangles"], manifest["weights"]) != strings["weightsSha256"]:
            raise ValueError("continuous-fit-usd-checksum-failed")
        manifest["compilation"] = json.loads(manifest["compilation"])
        if manifest["compilation"] != retained:
            raise ValueError("continuous-fit-usd-provenance-changed")
        (output / ("loaded-" + prefix + "-" + name + ".json")).write_text(json.dumps(manifest, indent=2))
    print("AURELIAN_" + prefix.upper() + "_USD_EXPERT_ROUNDTRIP_PASSED", flush=True)
