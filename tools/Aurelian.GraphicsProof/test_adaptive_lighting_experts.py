import sys
import unittest

sys.dont_write_bytecode = True
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src/Aurelian/Aurelian.Assets/Lighting/Authoring"))

import numpy as np

from adaptive_lighting_experts import bisect_edge, interior_edges, numeric_bytes, refine
from continuous_lighting_experts import design_matrix, local_features, mesh_from_partition, triangle_nodes
from local_lighting_experts import coordinates
from test_constrained_lighting_experts import fixture


class AdaptiveCompilerTests(unittest.TestCase):
    def setUp(self):
        self.arrays = fixture()
        self.vertices, self.triangles, self.matrices = mesh_from_partition(self.arrays)
        self.points = coordinates(24)
        field = np.exp(-90 * ((self.points[:, 0] - .35) ** 2 + (self.points[:, 1] + .28) ** 2))
        self.target = np.repeat(field[:, None], 6, axis=1)
        self.contract = {
            "interiorEdgeSplits": 2, "numericBudgetBytes": numeric_bytes(self.vertices, self.triangles) + 272,
            "randomSeed": 419, "ridge": 1e-6,
        }

    def profile(self, policy):
        return {"boundaryDegree": 2, "interiorBubble": False, "refinement": policy}

    def test_paired_split_preserves_domain_leaf_membership_and_arbitrary_weight_continuity(self):
        # Exercise an edge between different original tree leaves, not only a fan spoke.
        edge = next(edge for edge in interior_edges(self.triangles)
                    if len({int(triangle[3]) for triangle in self.triangles
                            if all(vertex in triangle[:3] for vertex in edge)}) == 2)
        vertices, triangles, matrices = bisect_edge(self.vertices, self.triangles, edge)
        self.assertEqual(len(self.vertices) + 1, len(vertices))
        self.assertEqual(len(self.triangles) + 2, len(triangles))
        self.assertEqual(numeric_bytes(self.vertices, self.triangles) + 136, numeric_bytes(vertices, triangles))
        self.assertAlmostEqual(4, sum(np.linalg.det(np.vstack((vertices[triangle[:3]].T, np.ones(3)))) / 2
                                     for triangle in triangles), places=6)
        design = design_matrix(self.points, self.arrays, vertices, triangles, matrices, 2, False)
        np.testing.assert_allclose(design.sum(axis=1), 1, atol=1e-10)
        nodes, count = triangle_nodes(vertices, triangles, 2, False)
        weights = np.random.default_rng(17).normal(size=(count, 6))
        for shared in interior_edges(triangles):
            adjacent = [index for index, triangle in enumerate(triangles)
                        if all(vertex in triangle[:3] for vertex in shared)]
            first, second = vertices[list(shared)]
            points = first + np.linspace(0, 1, 31)[:, None] * (second - first)
            homogeneous = np.column_stack((points, np.ones(len(points))))
            left = local_features(homogeneous @ matrices[adjacent[0]].T, 2, False) @ weights[nodes[adjacent[0]]]
            right = local_features(homogeneous @ matrices[adjacent[1]].T, 2, False) @ weights[nodes[adjacent[1]]]
            np.testing.assert_allclose(left, right, atol=1e-10)

    def test_training_selection_is_deterministic_and_improves_training_fit_with_equal_storage(self):
        first = refine(self.points, self.target, self.arrays, self.vertices, self.triangles, self.matrices,
                       self.contract, self.profile("training-residual"))
        second = refine(self.points, self.target, self.arrays, self.vertices, self.triangles, self.matrices,
                        self.contract, self.profile("training-residual"))
        np.testing.assert_array_equal(first[1], second[1])
        self.assertEqual(first[3], second[3])
        for step in first[3]["RefinementHistory"]:
            self.assertLess(step["TrainingMseAfter"], step["TrainingMseBefore"])
        for policy in ("longest-edge", "seeded-random"):
            control = refine(self.points, self.target, self.arrays, self.vertices, self.triangles, self.matrices,
                             self.contract, self.profile(policy))
            self.assertEqual(numeric_bytes(*first[:2]), numeric_bytes(*control[:2]))
        self.assertEqual(self.contract["numericBudgetBytes"], first[3]["NumericPayloadBytes"])

    def test_refined_basis_still_reproduces_global_quadratic_without_fitting(self):
        vertices, triangles, matrices = bisect_edge(self.vertices, self.triangles, interior_edges(self.triangles)[0])
        from continuous_lighting_experts import canonical_edges

        edge_points = [(vertices[a] + vertices[b]) / 2 for a, b in canonical_edges(triangles)]
        nodes = np.vstack((vertices, edge_points))
        weights = 1 + nodes[:, 0] ** 2 - .4 * nodes[:, 0] * nodes[:, 1]
        design = design_matrix(self.points, self.arrays, vertices, triangles, matrices, 2, False)
        expected = 1 + self.points[:, 0] ** 2 - .4 * self.points[:, 0] * self.points[:, 1]
        np.testing.assert_allclose(design @ weights, expected, atol=1e-10)

    def test_over_budget_and_boundary_edge_requests_fail_before_refinement(self):
        self.contract["numericBudgetBytes"] -= 1
        with self.assertRaisesRegex(ValueError, "budget-exceeded"):
            refine(self.points, self.target, self.arrays, self.vertices, self.triangles, self.matrices,
                   self.contract, self.profile("training-residual"))
        from continuous_lighting_experts import canonical_edges

        boundary = next(edge for edge in canonical_edges(self.triangles) if edge not in interior_edges(self.triangles))
        with self.assertRaisesRegex(ValueError, "must-be-interior"):
            bisect_edge(self.vertices, self.triangles, boundary)


if __name__ == "__main__":
    unittest.main()
