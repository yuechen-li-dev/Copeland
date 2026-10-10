"""Compile authored continuity into a joint fit; the runtime decoder is unchanged."""

import hashlib
import json
from pathlib import Path

import numpy as np

from local_lighting_experts import (
    ARRAY_NAMES, FEATURES, LEAVES, array_hash, coordinates, footprint_mask,
    polynomial, routes,
)


def validate_contract(contract, profile):
    expected = {
        "schema": "aurelian.lighting-fit-contract/1",
        "receiver": "floor",
        "degree": 3,
        "continuity": "C0",
        "domain": profile["domain"],
        "basis": profile["basis"],
    }
    for name, value in expected.items():
        if contract.get(name) != value:
            raise ValueError("lighting-constraint-profile-mismatch: " + name)
    if not isinstance(contract.get("name"), str) or not contract["name"]:
        raise ValueError("lighting-constraint-name-missing")
    penalty = contract.get("softPenalty")
    if not isinstance(penalty, (int, float)) or not np.isfinite(penalty) or penalty <= 0:
        raise ValueError("lighting-constraint-penalty-invalid")
    exceptions = contract.get("discontinuousSeams")
    if not isinstance(exceptions, list) or any(not isinstance(item, str) for item in exceptions):
        raise ValueError("lighting-constraint-exceptions-invalid")
    if len(set(exceptions)) != len(exceptions):
        raise ValueError("lighting-constraint-exception-duplicate")


def leaf_halfspaces(arrays):
    """Retain tree-path authority as convex cells; do not infer adjacency from labels."""
    domain = np.array([[1, 0, -1], [-1, 0, -1], [0, 1, -1], [0, -1, -1]], dtype=np.float64)
    result = [None] * LEAVES
    visited = set()

    def visit(node, path):
        if node < 0:
            leaf = -node - 1
            if leaf >= LEAVES or result[leaf] is not None:
                raise ValueError("lighting-constraint-invalid-leaf")
            result[leaf] = np.vstack((domain, path))
            return
        if node in visited or node >= LEAVES - 1:
            raise ValueError("lighting-constraint-invalid-tree")
        visited.add(node)
        plane = arrays["planes"][node]
        visit(int(arrays["children"][node, 0]), path + [plane])
        visit(int(arrays["children"][node, 1]), path + [-plane])

    visit(0, [])
    if any(cell is None for cell in result):
        raise ValueError("lighting-constraint-missing-leaf")
    return result


def shared_edges(arrays):
    cells = leaf_halfspaces(arrays)
    edges = []
    for first in range(LEAVES):
        for second in range(first + 1, LEAVES):
            for plane in cells[first][4:]:
                if not any(np.max(np.abs(plane + other)) < 1e-10 for other in cells[second][4:]):
                    continue
                normal = plane[:2]
                origin = -normal * plane[2] / (normal @ normal)
                tangent = np.array([-normal[1], normal[0]]) / np.linalg.norm(normal)
                lower, upper = -np.inf, np.inf
                valid = True
                for boundary in np.vstack((cells[first], cells[second])):
                    slope = boundary[:2] @ tangent
                    intercept = boundary[:2] @ origin + boundary[2]
                    if abs(slope) < 1e-10:
                        if intercept > 1e-9:
                            valid = False
                            break
                    elif slope > 0:
                        upper = min(upper, -intercept / slope)
                    else:
                        lower = max(lower, -intercept / slope)
                if valid and np.isfinite(lower) and np.isfinite(upper) and upper - lower > 1e-8:
                    edges.append({
                        "name": f"leaf-{first}/leaf-{second}", "first": first, "second": second,
                        "start": (origin + tangent * lower).tolist(),
                        "end": (origin + tangent * upper).tolist(),
                        "provenance": "intersection of retained routing halfspaces and receiver domain",
                    })
                    break
    if not edges:
        raise ValueError("lighting-constraint-no-shared-edges")
    return edges


def constraint_matrix(arrays, edges, exceptions):
    names = {edge["name"] for edge in edges}
    if not set(exceptions).issubset(names):
        raise ValueError("lighting-constraint-unknown-seam: " + str(sorted(set(exceptions) - names)))
    rows = []
    for edge in edges:
        if edge["name"] in exceptions:
            continue
        # A cubic restricted to a straight edge is a univariate cubic. Equality
        # at four distinct points implies equality along the complete edge.
        start, end = np.array(edge["start"]), np.array(edge["end"])
        points = start + np.linspace(0, 1, 4)[:, None] * (end - start)
        first, second = edge["first"], edge["second"]
        matrix = np.zeros((4, LEAVES * FEATURES))
        matrix[:, first * FEATURES:(first + 1) * FEATURES] = polynomial(points, arrays["transforms"][first])
        matrix[:, second * FEATURES:(second + 1) * FEATURES] = -polynomial(points, arrays["transforms"][second])
        # Normalize equation rows so the declared soft penalty has consistent units.
        matrix /= np.linalg.norm(matrix, axis=1)[:, None]
        rows.extend(matrix)
    return np.asarray(rows).reshape(-1, LEAVES * FEATURES)


def training_system(arrays, points, target):
    leaf_ids = routes(points, arrays)
    design = np.zeros((len(points), LEAVES * FEATURES))
    regularizer = np.zeros(LEAVES * FEATURES)
    for leaf in range(LEAVES):
        mask = leaf_ids == leaf
        if not mask.any():
            raise ValueError("lighting-constraint-unobserved-leaf")
        section = slice(leaf * FEATURES, (leaf + 1) * FEATURES)
        design[mask, section] = polynomial(points[mask], arrays["transforms"][leaf])
        regularizer[section] = mask.sum() * 1e-6
        regularizer[leaf * FEATURES] = 0
    augmented = np.vstack((design, np.diag(np.sqrt(regularizer))))
    right = np.vstack((target, np.zeros((LEAVES * FEATURES, 6))))
    return augmented, right


def solve(augmented, right, constraints, mode, penalty):
    if not len(constraints):
        rank = 0
        nullspace = np.eye(LEAVES * FEATURES)
    else:
        _, singular, vectors = np.linalg.svd(constraints, full_matrices=True)
        rank = int(np.sum(singular > singular[0] * 1e-10))
        nullspace = vectors[rank:].T
    if mode == "independent":
        weights = np.linalg.lstsq(augmented, right, rcond=None)[0]
    elif mode == "soft":
        matrix = np.vstack((augmented, np.sqrt(penalty) * constraints))
        target = np.vstack((right, np.zeros((len(constraints), 6))))
        weights = np.linalg.lstsq(matrix, target, rcond=None)[0]
    elif mode == "exact":
        if not nullspace.shape[1]:
            raise ValueError("lighting-constraint-no-free-coefficients")
        reduced = np.linalg.lstsq(augmented @ nullspace, right, rcond=None)[0]
        weights = nullspace @ reduced
    else:
        raise ValueError("lighting-constraint-unknown-mode")
    if not np.isfinite(weights).all():
        raise ValueError("lighting-constraint-nonfinite-solution")
    residual = float(np.max(np.abs(constraints @ weights))) if len(constraints) else 0
    if mode == "exact" and residual > 1e-9:
        raise ValueError("lighting-constraint-infeasible-solution")
    return weights.reshape(LEAVES, FEATURES, 6), rank, residual


def compile_and_save(output, contract_path):
    from pxr import Sdf, Usd, UsdGeom, Vt

    contract_bytes = Path(contract_path).read_bytes()
    contract = json.loads(contract_bytes)
    stage = Usd.Stage.Open(str(output / "local-lighting.usda"))
    if stage is None or stage.GetCompositionErrors():
        raise ValueError("lighting-constraint-source-usd-invalid")
    source = stage.GetPrimAtPath("/AurelianLocalLighting")
    profile = {name: source.GetAttribute("aurelian:" + name).Get()
               for name in ("schema", "sceneKey", "decoderKey", "domain", "basis", "colourSpace",
                            "leaves", "features", "gridSize")}
    if profile["schema"] != "aurelian.local-expert/1" or (profile["leaves"], profile["features"], profile["gridSize"]) != (8, 10, 10):
        raise ValueError("lighting-constraint-source-profile-invalid")
    validate_contract(contract, profile)
    shapes = {"planes": (7, 3), "children": (7, 2), "transforms": (8, 4), "weights": (8, 10, 6),
              "uniformTransforms": (8, 4), "uniformWeights": (8, 10, 6), "grid": (10, 10, 6)}
    arrays = {}
    for name in ARRAY_NAMES:
        dtype = np.int32 if name == "children" else np.float64
        arrays[name] = np.asarray(source.GetAttribute("aurelian:" + name).Get(), dtype=dtype).reshape(shapes[name])
    authoring = json.loads((output / "partition-authoring.json").read_text())
    points = coordinates(48)
    mask = footprint_mask(points, np.array(authoring["OccluderBoundsMetres"]))
    target = np.fromfile(output / "training.bin", dtype="<f4").reshape(-1, 6)[mask].astype(np.float64)
    augmented, right = training_system(arrays, points[mask], target)
    edges = shared_edges(arrays)
    constraints = constraint_matrix(arrays, edges, contract["discontinuousSeams"])
    retained = {
        "Contract": contract, "ContractKey": hashlib.sha256(contract_bytes).hexdigest(),
        "PartitionKey": array_hash(arrays),
        "TrainingKey": hashlib.sha256((output / "training.bin").read_bytes()).hexdigest(),
        "CompilerKey": hashlib.sha256(Path(__file__).read_bytes()
                                     + Path(__file__).with_name("local_lighting_experts.py").read_bytes()).hexdigest(),
        "SharedEdges": edges, "EquationCount": len(constraints),
        "ReferenceInputs": ["training.bin"],
    }
    for mode in ("independent", "soft", "exact"):
        fitted, rank, residual = solve(augmented, right, constraints, mode, contract["softPenalty"])
        variant = dict(arrays)
        variant["weights"] = fitted
        artifact_path = output / ("constrained-" + mode + ".usda")
        artifact = Usd.Stage.CreateNew(str(artifact_path))
        artifact.GetRootLayer().subLayerPaths = ["local-lighting.usda"]
        prim = UsdGeom.Scope.Define(artifact, "/AurelianLocalLighting").GetPrim()
        for name in ARRAY_NAMES:
            if name == "children":
                prim.CreateAttribute("aurelian:" + name, Sdf.ValueTypeNames.IntArray).Set(
                    Vt.IntArray(np.asarray(variant[name], dtype=np.int32).reshape(-1).tolist()))
            else:
                prim.CreateAttribute("aurelian:" + name, Sdf.ValueTypeNames.FloatArray).Set(
                    Vt.FloatArray(np.asarray(variant[name], dtype=np.float32).reshape(-1).tolist()))
        free_coefficients = 80 - rank if mode == "exact" else 80
        evidence = dict(retained, Mode=mode, Rank=rank, FreeCoefficients=free_coefficients,
                        DoubleConstraintResidual=residual)
        float_weights = fitted.astype(np.float32).astype(np.float64).reshape(80, 6)
        evidence["FloatConstraintResidual"] = float(np.max(np.abs(constraints @ float_weights))) if len(constraints) else 0
        prim.CreateAttribute("aurelian:constraintCompilation", Sdf.ValueTypeNames.String).Set(json.dumps(evidence))
        prim.CreateAttribute("aurelian:weightsSha256", Sdf.ValueTypeNames.String).Set(array_hash(variant))
        artifact.GetRootLayer().Save()
        reopened = Usd.Stage.Open(str(artifact_path))
        if reopened is None or reopened.GetCompositionErrors():
            raise ValueError("lighting-constraint-usd-composition-failed")
        prim = reopened.GetPrimAtPath("/AurelianLocalLighting")
        manifest = {name: prim.GetAttribute("aurelian:" + name).Get() for name in profile}
        manifest.update({name: list(prim.GetAttribute("aurelian:" + name).Get()) for name in ARRAY_NAMES})
        manifest["weightsSha256"] = prim.GetAttribute("aurelian:weightsSha256").Get()
        manifest["constraintCompilation"] = json.loads(prim.GetAttribute("aurelian:constraintCompilation").Get())
        if array_hash(manifest) != manifest["weightsSha256"]:
            raise ValueError("lighting-constraint-usd-checksum-failed")
        loaded_weights = np.array(manifest["weights"]).reshape(80, 6)
        loaded_residual = float(np.max(np.abs(constraints @ loaded_weights))) if len(constraints) else 0
        if loaded_residual != evidence["FloatConstraintResidual"] or manifest["constraintCompilation"] != evidence:
            raise ValueError("lighting-constraint-usd-provenance-changed")
        (output / ("loaded-constrained-" + mode + ".json")).write_text(json.dumps(manifest, indent=2))
    print("AURELIAN_CONSTRAINED_USD_EXPERT_ROUNDTRIP_PASSED", flush=True)
