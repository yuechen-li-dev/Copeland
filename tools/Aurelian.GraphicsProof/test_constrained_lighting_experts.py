"""Numerical and rejection witnesses for the bounded continuity compiler."""

import sys
import unittest

sys.dont_write_bytecode = True

import numpy as np

from constrained_lighting_experts import (
    constraint_matrix, shared_edges, solve, training_system, validate_contract,
)
from local_lighting_experts import coordinates, polynomial, routes


def fixture():
    return {
        "planes": np.array([[1, 0, 0], [0, 1, 0], [0, 1, 0], [1, 0, .5],
                            [1, 0, .5], [1, 0, -.5], [1, 0, -.5]], dtype=np.float64),
        "children": np.array([[1, 2], [3, 4], [5, 6], [-1, -2], [-3, -4], [-5, -6], [-7, -8]]),
        "transforms": np.tile([0, 0, 1, 1], (8, 1)),
    }


class ConstraintCompilerTests(unittest.TestCase):
    def test_a_continuous_linear_field_remains_representable(self):
        arrays = fixture()
        points = coordinates(24)
        labels = (1 + points[:, 0] * .2 + points[:, 1] * .3)[:, None] * np.ones((1, 6))
        augmented, right = training_system(arrays, points, labels)
        matrix = constraint_matrix(arrays, shared_edges(arrays), [])
        # Remove ridge rows for this representability witness. The production fit
        # deliberately adds a small regularization bias, evaluated independently.
        exact, _, residual = solve(augmented[:len(points)], right[:len(points)], matrix, "exact", 10)
        self.assertLess(residual, 1e-10)
        leaf_ids = routes(points, arrays)
        for leaf in range(8):
            mask = leaf_ids == leaf
            actual = polynomial(points[mask], arrays["transforms"][leaf]) @ exact[leaf]
            self.assertLess(np.max(np.abs(actual - labels[mask])), 1e-10)

    def test_exact_constraints_hold_between_collocation_points(self):
        arrays = fixture()
        points = coordinates(24)
        # Intentionally discontinuous labels expose the accuracy cost of a wrong contract.
        labels = (routes(points, arrays) * .1 + points[:, 0] * .02)[:, None] * np.ones((1, 6))
        augmented, right = training_system(arrays, points, labels)
        edges = shared_edges(arrays)
        matrix = constraint_matrix(arrays, edges, [])
        independent, _, unconstrained = solve(augmented, right, matrix, "independent", 10)
        soft, _, softened = solve(augmented, right, matrix, "soft", 10)
        exact, rank, constrained = solve(augmented, right, matrix, "exact", 10)
        self.assertGreater(rank, 0)
        self.assertGreater(unconstrained, softened)
        self.assertGreater(softened, constrained)
        self.assertLess(constrained, 1e-10)
        for edge in edges:
            start, end = np.array(edge["start"]), np.array(edge["end"])
            probes = start + np.linspace(0, 1, 101)[:, None] * (end - start)
            first = polynomial(probes, arrays["transforms"][edge["first"]]) @ exact[edge["first"]]
            second = polynomial(probes, arrays["transforms"][edge["second"]]) @ exact[edge["second"]]
            self.assertLess(np.max(np.abs(first - second)), 1e-9)
        independent_error = np.linalg.norm(augmented @ independent.reshape(80, 6) - right)
        exact_error = np.linalg.norm(augmented @ exact.reshape(80, 6) - right)
        self.assertGreater(exact_error, independent_error)

    def test_authored_discontinuities_are_not_smoothed(self):
        arrays = fixture()
        edges = shared_edges(arrays)
        exceptions = [edge["name"] for edge in edges]
        matrix = constraint_matrix(arrays, edges, exceptions)
        self.assertEqual((0, 80), matrix.shape)
        points = coordinates(24)
        labels = routes(points, arrays)[:, None] * np.ones((1, 6))
        augmented, right = training_system(arrays, points, labels)
        exact, rank, residual = solve(augmented, right, matrix, "exact", 10)
        independent, _, _ = solve(augmented, right, matrix, "independent", 10)
        np.testing.assert_allclose(exact, independent, atol=1e-10)
        self.assertEqual(0, rank)
        self.assertEqual(0, residual)

    def test_invalid_contracts_and_unknown_relationships_fail_closed(self):
        profile = {"basis": "basis", "domain": "domain"}
        contract = {"schema": "aurelian.lighting-fit-contract/1", "name": "floor", "receiver": "floor",
                    "degree": 3, "continuity": "C0", "basis": "basis", "domain": "domain",
                    "softPenalty": 10, "discontinuousSeams": []}
        validate_contract(contract, profile)
        for key, value in (("degree", 4), ("receiver", "wall"), ("softPenalty", float("nan")),
                           ("continuity", "guess"), ("discontinuousSeams", ["a", "a"])):
            with self.assertRaisesRegex(ValueError, "lighting-constraint"):
                validate_contract(dict(contract, **{key: value}), profile)
        arrays = fixture()
        with self.assertRaisesRegex(ValueError, "unknown-seam"):
            constraint_matrix(arrays, shared_edges(arrays), ["absent"])
        arrays["children"][0, 0] = 0
        with self.assertRaisesRegex(ValueError, "invalid-tree"):
            shared_edges(arrays)


if __name__ == "__main__":
    unittest.main()
