"""Compare full post-softcap logits and matched inference timings."""

import argparse
import json
import statistics
from pathlib import Path

import numpy as np


VOCABULARY = 262144


def summary(prefix):
    runs = json.loads(prefix.with_suffix(".json").read_text())["Runs"]
    prefill = statistics.median(run[0]["WallMs"] for run in runs)
    decode = statistics.median(statistics.mean(step["WallMs"] for step in run[1:]) for run in runs)
    print(f"{prefix.name}: prefill {prefill:.2f} ms, decode {decode:.2f} ms/token ({1000 / decode:.2f} tok/s)")
    if "Body" in runs[0][0]:
        for label, rows in (
            ("prefill", [[run[0]] for run in runs]),
            ("decode", [run[1:] for run in runs]),
        ):
            stats = {
                section: {
                    key: round(statistics.median(
                        statistics.mean(step[section][key] for step in row) for row in rows
                    ), 3)
                    for key in ("WallMs", "GpuMs", "InputMs", "Submissions", "Dispatches")
                }
                for section in ("Body", "Head")
            }
            print(f"  {label} {stats}")


def compare(reference, candidate, tolerance):
    expected = np.memmap(reference.with_suffix(".f32"), dtype="<f4", mode="r").reshape(-1, VOCABULARY)
    actual = np.memmap(candidate.with_suffix(".f32"), dtype="<f4", mode="r").reshape(-1, VOCABULARY)
    if actual.shape != expected.shape or not np.isfinite(actual).all() or not np.isfinite(expected).all():
        raise SystemExit(f"{candidate.name}: shape or finite-value check failed")
    difference = np.abs(expected - actual)
    relative = np.sqrt(np.mean(difference ** 2, axis=1) / np.mean(expected ** 2, axis=1))
    winner = expected.argmax(axis=1)
    matches = (actual.argmax(axis=1) == winner)
    expected_top = np.argpartition(expected, -5, axis=1)[:, -5:]
    actual_top = np.argpartition(actual, -5, axis=1)[:, -5:]
    intersection = np.sum(np.any(expected_top[:, :, None] == actual_top[:, None, :], axis=2), axis=1)
    print(f"  max relative RMS {relative.max():.5%}, max absolute {difference.max():.6f}, "
          f"top-1 {matches.sum()}/{len(matches)}, mean top-5 overlap {intersection.mean():.2f}/5")
    print(f"  per-step relative RMS %: {np.round(relative * 100, 4).tolist()}")
    if tolerance is not None and (relative.max() > tolerance or not matches.all()):
        raise SystemExit(f"{candidate.name}: logit parity tolerance exceeded")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reference", type=Path)
    parser.add_argument("candidates", nargs="+", type=Path)
    parser.add_argument("--tolerance", type=float, help="Maximum relative RMS (also require exact argmax)")
    args = parser.parse_args()
    summary(args.reference)
    for candidate in args.candidates:
        summary(candidate)
        compare(args.reference, candidate, args.tolerance)
