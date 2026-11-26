"""Compare full post-softcap logits and matched inference timings."""

import argparse
import json
import statistics
from pathlib import Path

import numpy as np


VOCABULARY = 262144


def median_step(rows, measure):
    return round(statistics.median(
        statistics.mean(measure(step) for step in run) for run in rows
    ), 3)


def print_hotspots(rows):
    sections = ("Body", "Head")
    sample = rows[0][0]
    if sample["Body"].get("FeedForwardMs"):
        phases = {phase for run in rows for step in run for phase in step["Body"]["FeedForwardMs"]}
        feed_forward = {
            phase: median_step(rows, lambda step: step["Body"]["FeedForwardMs"].get(phase, 0))
            for phase in sorted(phases)
        }
        print(f"    Feed-forward GPU intervals (ms) {feed_forward}")
    if sample["Body"].get("BatchCpu"):
        def batches(step):
            return [batch for section in sections for batch in step[section]["BatchCpu"]]

        phases = ("ValidationMs", "DescriptorsMs", "RecordingMs", "TimestampReadMs")
        cpu = {
            phase: median_step(rows, lambda step: sum(batch[phase] for batch in batches(step)))
            for phase in phases
        }
        cpu["FenceWaitMs"] = median_step(rows, lambda step: sum(
            batch["Submission"]["CompletionWaitMicroseconds"] for batch in batches(step)
        ) / 1000)
        cpu["OutsideBodyAndHeadGpuMs"] = median_step(
            rows, lambda step: step["WallMs"] - sum(step[section]["GpuMs"] for section in sections)
        )
        cpu["ManagedKiB"] = median_step(rows, lambda step: step["ManagedBytes"] / 1024)
        print(f"    Host intervals {cpu}")
    if sample["Body"].get("ShaderMs"):
        shaders = {
            shader for run in rows for step in run for section in sections
            for shader in step[section]["ShaderMs"]
        }
        intervals = {
            shader: median_step(rows, lambda step: sum(
                step[section]["ShaderMs"].get(shader, 0) for section in sections
            ))
            for shader in shaders
        }
        top = sorted(intervals.items(), key=lambda item: (-item[1], item[0]))[:6]
        print(f"    GPU shader intervals (ms) {dict(top)}")


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
            print_hotspots(rows)


def max_kl_divergence(expected, actual):
    """Maximum KL(reference || candidate) in nats across teacher-forced positions."""
    maximum = 0.0
    for reference_row, candidate_row in zip(expected, actual):
        reference_log = reference_row.astype(np.float64)
        candidate_log = candidate_row.astype(np.float64)
        reference_log -= reference_log.max()
        candidate_log -= candidate_log.max()
        reference_weights = np.exp(reference_log)
        reference_mass = reference_weights.sum()
        reference_log -= np.log(reference_mass)
        candidate_log -= np.log(np.exp(candidate_log).sum())
        divergence = np.sum(reference_weights * (reference_log - candidate_log)) / reference_mass
        maximum = max(maximum, float(divergence))
    return maximum


def compare(reference, candidate, tolerance, kl_tolerance=None):
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
    if kl_tolerance is not None:
        divergence = max_kl_divergence(expected, actual)
        print(f"  max KL(reference || candidate) {divergence:.9g} nats")
        if divergence > kl_tolerance:
            raise SystemExit(f"{candidate.name}: KL tolerance exceeded")
    if tolerance is not None and (relative.max() > tolerance or not matches.all()):
        raise SystemExit(f"{candidate.name}: logit parity tolerance exceeded")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reference", type=Path)
    parser.add_argument("candidates", nargs="+", type=Path)
    parser.add_argument("--tolerance", type=float, help="Maximum relative RMS (also require exact argmax)")
    parser.add_argument("--kl-tolerance", type=float, help="Maximum reference-to-candidate KL divergence in nats")
    args = parser.parse_args()
    if args.kl_tolerance is not None and (not np.isfinite(args.kl_tolerance) or args.kl_tolerance < 0):
        parser.error("--kl-tolerance must be finite and nonnegative")
    summary(args.reference)
    for candidate in args.candidates:
        summary(candidate)
        compare(args.reference, candidate, args.tolerance, args.kl_tolerance)
