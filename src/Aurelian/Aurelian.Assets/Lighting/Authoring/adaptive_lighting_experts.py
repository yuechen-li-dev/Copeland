"""Training-only conforming receiver refinement at a declared numeric budget."""

import hashlib
from pathlib import Path

import numpy as np

from continuous_lighting_experts import (
    barycentric_matrices, compiler_key as continuous_compiler_key,
    design_matrix, triangle_nodes,
)


def interior_edges(triangles):
    incidence = {}
    for triangle in triangles:
        for first, second in ((0, 1), (1, 2), (2, 0)):
            edge = tuple(sorted((int(triangle[first]), int(triangle[second]))))
            incidence[edge] = incidence.get(edge, 0) + 1
    if any(count not in (1, 2) for count in incidence.values()):
        raise ValueError("adaptive-fit-nonmanifold-edge")
    return sorted(edge for edge, count in incidence.items() if count == 2)


def bisect_edge(vertices, triangles, edge):
    """Split both incident triangles, retaining orientation and original leaf IDs."""
    if edge not in interior_edges(triangles):
        raise ValueError("adaptive-fit-edge-must-be-interior")
    midpoint = ((vertices[edge[0]] + vertices[edge[1]]) / 2).astype(np.float32).astype(np.float64)
    new_vertices = np.vstack((vertices, midpoint))
    midpoint_id = len(vertices)
    new_triangles = []
    for triangle in triangles:
        if not all(vertex in triangle[:3] for vertex in edge):
            new_triangles.append(triangle.tolist())
            continue
        for first, second, third in ((0, 1, 2), (1, 2, 0), (2, 0, 1)):
            if tuple(sorted((int(triangle[first]), int(triangle[second])))) == edge:
                new_triangles.append([int(triangle[first]), midpoint_id, int(triangle[third]), int(triangle[3])])
                new_triangles.append([midpoint_id, int(triangle[second]), int(triangle[third]), int(triangle[3])])
                break
    new_triangles = np.asarray(new_triangles, dtype=np.int32)
    return new_vertices, new_triangles, barycentric_matrices(new_vertices, new_triangles)


def numeric_bytes(vertices, triangles):
    _, count = triangle_nodes(vertices, triangles, 2, False)
    # Same accounting as ContinuousLightingModel: six output channels and retained routing.
    return 4 * (vertices.size + triangles.size + count * 6 + 21 + 14)


def fit(points, target, arrays, vertices, triangles, matrices, ridge):
    design = design_matrix(points, arrays, vertices, triangles, matrices, 2, False)
    penalty = np.eye(design.shape[1]) * len(target) * ridge
    weights = np.linalg.solve(design.T @ design + penalty, design.T @ target)
    residual = design @ weights - target
    return float(np.mean(residual ** 2)), design


def refine(points, target, arrays, vertices, triangles, matrices, contract, profile):
    if profile["boundaryDegree"] != 2 or profile["interiorBubble"]:
        raise ValueError("adaptive-fit-requires-quadratic-boundaries")
    policy = profile["refinement"]
    if policy not in ("none", "longest-edge", "seeded-random", "training-residual"):
        raise ValueError("adaptive-fit-policy-invalid")
    steps = 0 if policy == "none" else contract["interiorEdgeSplits"]
    budget = contract["numericBudgetBytes"]
    if not isinstance(steps, int) or steps < 0 or not isinstance(budget, int) or budget <= 0:
        raise ValueError("adaptive-fit-budget-invalid")
    if numeric_bytes(vertices, triangles) + steps * 136 > budget:
        raise ValueError("adaptive-fit-budget-exceeded")
    random = np.random.default_rng(contract["randomSeed"])
    history = []
    loss, _ = fit(points, target, arrays, vertices, triangles, matrices, contract["ridge"])
    for step in range(steps):
        edges = interior_edges(triangles)
        if not edges:
            raise ValueError("adaptive-fit-no-refinement-candidates")
        candidates_evaluated = 0
        if policy == "training-residual":
            best = None
            for edge in edges:
                candidate = bisect_edge(vertices, triangles, edge)
                candidate_loss, _ = fit(points, target, arrays, *candidate, contract["ridge"])
                candidates_evaluated += 1
                if best is None or candidate_loss < best[0]:
                    best = (candidate_loss, edge, candidate)
            new_loss, edge, refined = best
            if not np.isfinite(new_loss) or new_loss >= loss:
                raise ValueError("adaptive-fit-no-positive-training-gain")
        else:
            if policy == "longest-edge":
                edge = min(edges, key=lambda value: (-np.sum((vertices[value[0]] - vertices[value[1]]) ** 2), value))
            else:
                edge = edges[int(random.integers(len(edges)))]
            refined = bisect_edge(vertices, triangles, edge)
            new_loss, _ = fit(points, target, arrays, *refined, contract["ridge"])
        history.append({
            "Step": step + 1, "Edge": list(edge), "TrainingMseBefore": loss,
            "TrainingMseAfter": new_loss, "CandidatesEvaluated": candidates_evaluated,
        })
        vertices, triangles, matrices = refined
        loss = new_loss
        if numeric_bytes(vertices, triangles) > budget:
            raise ValueError("adaptive-fit-budget-exceeded")
    return vertices, triangles, matrices, {
        "RefinementPolicy": policy, "RefinementHistory": history,
        "NumericBudgetBytes": budget, "NumericPayloadBytes": numeric_bytes(vertices, triangles),
        "TrainingMse": loss,
        "MeshConstruction": "retained conforming fan; paired interior-edge midpoint bisection",
        "SelectionInputs": ["training.bin", "retained partition", "training receiver positions"],
    }


def compiler_key():
    return hashlib.sha256(Path(__file__).read_bytes() + bytes.fromhex(continuous_compiler_key())).hexdigest()


def compile_and_save(output, contract_path):
    from continuous_lighting_experts import compile_and_save as compile_continuous

    compile_continuous(output, contract_path, mesh_refiner=refine, prefix="adaptive", compiler_identity=compiler_key())
