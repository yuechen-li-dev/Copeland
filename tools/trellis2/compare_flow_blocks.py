"""Compare explicit diagnostic snapshots without rerunning the learned model.

This reports numerical sensitivity, not full-sampler acceptance. NumPy is an
optional offline tool and is never invoked by the C#/Vulkan inference path.
"""

import argparse
import json
from pathlib import Path

import numpy as np


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--native", type=Path, required=True)
    parser.add_argument("--reference", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--modulation-only", action="store_true")
    parser.add_argument("--require-bit-exact", action="store_true")
    parser.add_argument("--maximum-relative-rms", type=float)
    args = parser.parse_args()
    results = []
    native_metadata = args.native / "diagnostic-evidence.json"
    reference_metadata = args.reference / "diagnostic-evidence.json"
    metadata_checked = native_metadata.exists() and reference_metadata.exists()
    if metadata_checked:
        native = json.loads(native_metadata.read_text())
        reference = json.loads(reference_metadata.read_text())
        for key in ["WeightSha256", "InputSha256", "FeatureSha256"]:
            if native[key] != reference[key]:
                raise ValueError("Diagnostic provenance mismatch: " + key)
        # The original diagnostic command supported positive conditioning only.
        if native.get("NegativeConditioning", False) != reference.get("NegativeConditioning", False):
            raise ValueError("Diagnostic conditioning mismatch")
    labels = ["shared-modulation"] if args.modulation_only else [
        "positive-block-00", "positive-block-01", "positive-block-02",
        "positive-block-05", "positive-block-10", "positive-block-20",
        "positive-block-29", "positive-velocity",
    ]
    for label in labels:
        actual = np.fromfile(args.native / (label + ".f32"), dtype="<f4")
        expected = np.fromfile(args.reference / (label + ".f32"), dtype="<f4")
        if actual.shape != expected.shape:
            raise ValueError("Diagnostic shape mismatch: " + label)
        if not np.isfinite(actual).all() or not np.isfinite(expected).all():
            raise ValueError("Nonfinite diagnostic values: " + label)
        difference = actual.astype(np.float64) - expected
        energy = max(float(np.mean(expected.astype(np.float64) ** 2)), 1e-30)
        relative = float(np.sqrt(np.mean(difference ** 2) / energy))
        results.append({
            "Stage": label,
            "RelativeRmsError": relative,
            "MaximumAbsoluteError": float(np.abs(difference).max()),
            "BitIdenticalFraction": float(np.mean(actual.view(np.uint32) == expected.view(np.uint32))),
        })
    bit_exact = all(item["BitIdenticalFraction"] == 1 for item in results)
    accepted = (not args.require_bit_exact or bit_exact) and (
        args.maximum_relative_rms is None or all(
            item["RelativeRmsError"] <= args.maximum_relative_rms for item in results))
    evidence = {
        "Scope": "Explicit positive-model diagnostic snapshots; not full Euler/sampler acceptance",
        "AcceptedAsFullSampling": False,
        "NativeDiagnosticProvenanceChecked": metadata_checked,
        "BitExact": bit_exact,
        "RequireBitExact": args.require_bit_exact,
        "MaximumRelativeRms": args.maximum_relative_rms,
        "AcceptedNumericalComparison": accepted if args.require_bit_exact or args.maximum_relative_rms is not None else None,
        "Measurements": results,
    }
    args.output.write_text(json.dumps(evidence, indent=2))
    print(json.dumps(evidence, indent=2))
    if not accepted:
        raise SystemExit("Diagnostic comparison failed the requested numerical bound")


if __name__ == "__main__":
    main()
