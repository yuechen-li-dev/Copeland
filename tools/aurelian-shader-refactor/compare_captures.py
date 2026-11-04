"""Require decoded-pixel equality against preserved native rendering captures."""

import hashlib
import json
from pathlib import Path
import sys

from PIL import Image, ImageChops


def compare(baseline, current):
    destination = current / "capture-comparison.json"
    destination.write_text(json.dumps({"Accepted": False}), encoding="utf-8")
    qualification = json.loads((baseline / "qualification.json").read_text(encoding="utf-8-sig"))
    if not qualification["Accepted"]:
        raise ValueError("The baseline qualification is unaccepted")
    missing = []
    changed = []
    matched = 0
    byte_identical = 0
    for reference in sorted(baseline.rglob("*.png")):
        relative = reference.relative_to(baseline)
        actual = current / relative
        if not actual.is_file():
            missing.append(str(relative))
            continue
        matched += 1
        if reference.read_bytes() == actual.read_bytes():
            byte_identical += 1
            continue
        with Image.open(reference) as source, Image.open(actual) as result:
            if source.size != result.size:
                changed.append({"Path": str(relative), "Reason": "Dimensions changed"})
                continue
            difference = ImageChops.difference(source.convert("RGBA"), result.convert("RGBA"))
            maximum = max(bounds[1] for bounds in difference.getextrema())
            if maximum != 0:
                changed.append({"Path": str(relative), "MaximumChannelDifference": maximum})
    evidence = {
        "Accepted": matched > 0 and not missing and not changed,
        "Baseline": str(baseline),
        "BaselineQualificationSha256": hashlib.sha256((baseline / "qualification.json").read_bytes()).hexdigest(),
        "MatchedCaptures": matched,
        "ByteIdenticalCaptures": byte_identical,
        "MissingCaptures": missing,
        "ChangedPixels": changed,
    }
    destination.write_text(json.dumps(evidence, indent=2), encoding="utf-8")
    print(json.dumps(evidence))
    if not evidence["Accepted"]:
        raise ValueError("Native captures differ from the preserved pre-refactor baseline")


if __name__ == "__main__":
    compare(Path(sys.argv[1]).resolve(), Path(sys.argv[2]).resolve())
