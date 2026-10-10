import sys
import unittest

sys.dont_write_bytecode = True
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src/Aurelian/Aurelian.Assets/Lighting/Authoring"))

import numpy as np

from continuous_lighting_experts import (
    canonical_edges, design_matrix, local_features, mesh_from_partition, triangle_nodes,
)
from local_lighting_experts import coordinates
from test_constrained_lighting_experts import fixture


class ContinuousCompilerTests(unittest.TestCase):
    def test_t_junctions_form_a_conforming_mesh(self):
        arrays = fixture()
        arrays["planes"][5] = [0, 1, .5]
        vertices, triangles, matrices = mesh_from_partition(arrays)
        edges = {}
        total_area = 0
        for triangle in triangles:
            points = vertices[triangle[:3]]
            total_area += np.linalg.det(np.vstack((points.T, np.ones(3)))) / 2
            for first, second in ((0, 1), (1, 2), (2, 0)):
                edge = tuple(sorted((int(triangle[first]), int(triangle[second]))))
                edges[edge] = edges.get(edge, 0) + 1
        self.assertAlmostEqual(4, total_area, places=6)
        for edge, count in edges.items():
            points = vertices[list(edge)]
            on_boundary = any(np.all(np.abs(points[:, axis] - side) < 1e-8)
                              for axis in (0, 1) for side in (-1, 1))
            self.assertEqual(1 if on_boundary else 2, count)
        design = design_matrix(coordinates(32), arrays, vertices, triangles, matrices, 2, True)
        self.assertTrue(np.isfinite(design).all())

    def test_shared_boundaries_remain_equal_with_arbitrary_interior_detail(self):
        arrays = fixture()
        arrays["planes"][5] = [0, 1, .5]
        vertices, triangles, matrices = mesh_from_partition(arrays)
        edges = {}
        for index, triangle in enumerate(triangles):
            for first, second in ((0, 1), (1, 2), (2, 0)):
                edge = tuple(sorted((int(triangle[first]), int(triangle[second]))))
                edges.setdefault(edge, []).append(index)
        for degree in (1, 2):
            nodes, count = triangle_nodes(vertices, triangles, degree, True)
            weights = np.random.default_rng(417).normal(size=(count, 6))
            for edge, adjacent in edges.items():
                if len(adjacent) != 2:
                    continue
                first, second = vertices[list(edge)]
                points = first + np.linspace(0, 1, 37)[:, None] * (second - first)
                homogeneous = np.column_stack((points, np.ones(len(points))))
                values = []
                for triangle in adjacent:
                    barycentric = homogeneous @ matrices[triangle].T
                    values.append(local_features(barycentric, degree, True) @ weights[nodes[triangle]])
                np.testing.assert_allclose(values[0], values[1], atol=1e-10)

    def test_quadratic_boundary_field_and_local_bubble_are_independent(self):
        arrays = fixture()
        vertices, triangles, matrices = mesh_from_partition(arrays)
        nodes, count = triangle_nodes(vertices, triangles, 2, True)
        edge_points = [(vertices[first] + vertices[second]) / 2 for first, second in canonical_edges(triangles)]
        points = np.vstack((vertices, edge_points))
        weights = np.zeros((count, 6))
        expected = 1 + points[:, 0] ** 2 + .2 * points[:, 0] * points[:, 1]
        weights[:len(points)] = expected[:, None]
        queries = coordinates(32)
        design = design_matrix(queries, arrays, vertices, triangles, matrices, 2, True)
        actual = design @ weights
        np.testing.assert_allclose(actual[:, 0], 1 + queries[:, 0] ** 2 + .2 * queries[:, 0] * queries[:, 1], atol=1e-10)
        bubble_id = nodes[0, -1]
        weights[bubble_id] = 10
        centre = vertices[triangles[0, :3]].mean(axis=0)
        barycentric = matrices[0] @ np.append(centre, 1)
        features = local_features(barycentric[None, :], 2, True)
        self.assertAlmostEqual(1, features[0, -1], places=10)
        for barycentric in (np.array([[0, .3, .7]]), np.array([[.3, 0, .7]]), np.array([[.3, .7, 0]])):
            self.assertEqual(0, local_features(barycentric, 2, True)[0, -1])

    def test_invalid_profile_and_degenerate_partition_fail_closed(self):
        arrays = fixture()
        vertices, triangles, _ = mesh_from_partition(arrays)
        with self.assertRaisesRegex(ValueError, "profile-invalid"):
            triangle_nodes(vertices, triangles, 3, True)
        arrays["children"][0, 0] = 0
        with self.assertRaisesRegex(ValueError, "invalid-tree"):
            mesh_from_partition(arrays)


if __name__ == "__main__":
    unittest.main()
