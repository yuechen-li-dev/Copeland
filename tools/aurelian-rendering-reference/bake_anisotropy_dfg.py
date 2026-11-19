"""Bake directional Schlick/Smith albedo for the two anisotropic GGX widths."""

import argparse
from pathlib import Path

import numpy as np


WIDTH_COUNT = 25
VIEW_COUNT = 41
AZIMUTH_COUNT = 9
MIN_WIDTH = 0.002025
MAX_WIDTH = 2**0.5
MIN_VIEW = 0.001


def integrate(width_t, width_b, view, azimuth, radial_count=32, azimuth_count=64):
    radius = np.sqrt((np.arange(radial_count, dtype=np.float32) + 0.5) / radial_count)
    angle = (np.arange(azimuth_count, dtype=np.float32) + 0.5) * (2 * np.pi / azimuth_count)
    disk_x = (radius[:, None] * np.cos(angle)).ravel()[None, :]
    disk_y = (radius[:, None] * np.sin(angle)).ravel()[None, :]
    disk_edge = np.sqrt(1 - disk_x * disk_x)
    view = np.asarray(view, dtype=np.float32).reshape(-1, 1)
    azimuth = np.asarray(azimuth, dtype=np.float32).reshape(-1, 1)

    # Heitz visible-normal sampling. Work in the principal tangent/bitangent frame.
    radial_view = np.sqrt(np.maximum(0, 1 - view * view))
    vx = radial_view * np.cos(azimuth)
    vy = radial_view * np.sin(azimuth)
    svx = width_t * vx
    svy = width_b * vy
    view_scale = np.sqrt(svx * svx + svy * svy + view * view)
    svx, svy, svz = svx / view_scale, svy / view_scale, view / view_scale
    xy = np.hypot(svx, svy)
    dx = np.where(xy > 1e-8, -svy / np.maximum(xy, 1e-8), 1)
    dy = np.where(xy > 1e-8, svx / np.maximum(xy, 1e-8), 0)
    warp = (1 + svz) / 2
    y = (1 - warp) * disk_edge + warp * disk_y
    z = np.sqrt(np.maximum(0, 1 - disk_x * disk_x - y * y))
    hx = width_t * (dx * disk_x - svz * dy * y + svx * z)
    hy = width_b * (dy * disk_x + svz * dx * y + svy * z)
    hz = np.maximum(0, (svx * dy - svy * dx) * y + svz * z)
    norm = np.sqrt(hx * hx + hy * hy + hz * hz)
    hx, hy, hz = hx / norm, hy / norm, hz / norm
    vh = np.clip(vx * hx + vy * hy + view * hz, 0, 1)
    lx = 2 * vh * hx - vx
    ly = 2 * vh * hy - vy
    nl = 2 * vh * hz - view
    light_scale = np.sqrt((width_t * lx) ** 2 + (width_b * ly) ** 2 + nl * nl)
    denom = np.maximum(nl, 0) * view_scale + view * light_scale
    masking = np.where(nl > 0, (view_scale + view) * nl / np.maximum(denom, 1e-12), 0)
    grazing = (1 - vh) ** 5
    return np.stack([np.mean(masking * (1 - grazing), axis=1), np.mean(masking * grazing, axis=1)], axis=1)


def bake():
    widths = np.geomspace(MIN_WIDTH, MAX_WIDTH, WIDTH_COUNT, dtype=np.float32)
    view = MIN_VIEW + (1 - MIN_VIEW) * np.linspace(0, 1, VIEW_COUNT, dtype=np.float32) ** 2
    angle = np.linspace(0, 1, AZIMUTH_COUNT, dtype=np.float32)
    azimuth = (1 - (1 - angle) ** 2) * (np.pi / 2)
    nv, phi = np.meshgrid(view, azimuth)
    result = np.empty((WIDTH_COUNT, WIDTH_COUNT, AZIMUTH_COUNT, VIEW_COUNT, 2), dtype="<f4")
    for bitangent in range(WIDTH_COUNT):
        for tangent in range(bitangent, WIDTH_COUNT):
            result[bitangent, tangent] = integrate(widths[tangent], widths[bitangent], nv, phi).reshape(AZIMUTH_COUNT, VIEW_COUNT, 2)
    # The runtime never queries alpha_t < alpha_b.
    for bitangent in range(WIDTH_COUNT):
        result[bitangent, :bitangent] = result[bitangent, bitangent]
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.parse_args().output.write_bytes(bake().tobytes())
