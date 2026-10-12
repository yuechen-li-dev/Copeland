"""Separate branch error from CFG amplification using explicit native diagnostics.
Optional offline arithmetic only; retains the strict failed full-step result.
"""

import argparse
import json
from pathlib import Path

import numpy as np
from occupancy_flow_reference import guided


def relative(actual, expected):
    difference = actual.astype(np.float64) - expected
    return float(np.sqrt(np.mean(difference ** 2)) / np.sqrt(np.mean(expected.astype(np.float64) ** 2)))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--native-positive", type=Path, required=True)
    parser.add_argument("--native-negative", type=Path, required=True)
    parser.add_argument("--reference", type=Path, required=True)
    parser.add_argument("--sample-dir", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    shape = (-1, 32)
    sample = np.fromfile(args.sample_dir / "noise.f32", dtype="<f4").reshape(shape)
    after = np.fromfile(args.sample_dir / "latent-step-00.f32", dtype="<f4").reshape(shape)
    positive = np.fromfile(args.native_positive / "positive-velocity.f32", dtype="<f4").reshape(shape)
    negative = np.fromfile(args.native_negative / "negative-velocity.f32", dtype="<f4").reshape(shape)
    reference_positive = np.fromfile(args.reference / "positive-velocity.f32", dtype="<f4").reshape(shape)
    reference_negative = np.fromfile(args.reference / "negative-velocity.f32", dtype="<f4").reshape(shape)
    reconstructed = (sample - after) / np.float32(1 / 34)
    native_guided = guided(sample, positive, negative, 1, guidance_rescale=.5)
    reference_guided = guided(sample, reference_positive, reference_negative, 1, guidance_rescale=.5)
    error = reconstructed.astype(np.float64) - native_guided
    evidence = {
        "Scope": "Branch error and CFG arithmetic diagnosis from separately replayed native predictions; does not override strict full-step failure",
        "AcceptedAsFullSampling": False,
        "PositiveVelocityRelativeRmsError": relative(positive, reference_positive),
        "NegativeVelocityRelativeRmsError": relative(negative, reference_negative),
        "GuidedVelocityRelativeRmsError": relative(native_guided, reference_guided),
        "NativeEulerReconstructionVersusNativeGuidedRelativeRmsError": relative(reconstructed, native_guided),
        "NativeEulerReconstructionVersusNativeGuidedMaximumError": float(np.abs(error).max()),
        "GuidanceStrength": 7.5, "GuidanceRescale": .5,
    }
    args.output.write_text(json.dumps(evidence, indent=2))
    print(json.dumps(evidence, indent=2))


if __name__ == "__main__":
    main()
