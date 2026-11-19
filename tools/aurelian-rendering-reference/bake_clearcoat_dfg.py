"""Bake the two Smith-GGX directional albedo coefficients used by clearcoat."""

import argparse
from pathlib import Path

import numpy as np


def bake(size: int = 64) -> np.ndarray:
    radius = np.sqrt((np.arange(192) + 0.5) / 192)
    angle = (np.arange(256) + 0.5) * (2 * np.pi / 256)
    disk_x = radius[:, None] * np.cos(angle)
    disk_y = radius[:, None] * np.sin(angle)
    disk_edge = np.sqrt(1 - disk_x * disk_x)
    grid = np.linspace(0, 1, size)
    result = np.empty((size, size, 2), dtype="<f4")
    for row, v in enumerate(grid):
        roughness = 0.045 + 0.955 * v
        alpha = roughness * roughness
        for column, u in enumerate(grid):
            nv = 0.001 + 0.999 * u * u
            vx = np.sqrt(1 - nv * nv)
            stretched = np.hypot(alpha * vx, nv)
            z = nv / stretched
            x = alpha * vx / stretched

            # Heitz visible-normal disk warp, then unstretch the sampled normal.
            y = 0.5 * ((1 + z) * disk_y + (1 - z) * disk_edge)
            height = np.sqrt(np.maximum(0, 1 - disk_x * disk_x - y * y))
            hx = alpha * (-z * y + x * height)
            hy = alpha * disk_x
            hz = np.maximum(0, x * y + z * height)
            length = np.sqrt(hx * hx + hy * hy + hz * hz)
            vh = np.clip((vx * hx + nv * hz) / length, 0, 1)
            nl = 2 * vh * hz / length - nv
            smith_v = np.hypot(nv, alpha * vx) / nv
            smith_l = np.sqrt(1 + alpha * alpha * (1 - nl * nl) / np.maximum(nl * nl, 1e-20))
            weight = np.where(nl > 0, (1 + smith_v) / (smith_v + smith_l), 0)
            grazing = (1 - vh) ** 5
            result[row, column] = np.mean(weight * (1 - grazing)), np.mean(weight * grazing)
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    args.output.write_bytes(bake().tobytes())
