"""Compare two Tiny.en benchmark JSON results recorded with the capture option."""

import argparse
import json
from pathlib import Path

import numpy as np


def load_capture(result):
    samples = json.loads(result.read_text(encoding="utf-8-sig"))["Samples"]
    tokens = samples[-1]["Tokens"]
    directory = result.with_suffix(result.suffix + ".capture")
    encoder = np.fromfile(directory / "encoder.f32", dtype="<f4").reshape(1500, 384)
    logits = []
    # The two prompt evaluations precede the continuation through end-of-text.
    for position in range(len(tokens) + 1):
        row = np.fromfile(directory / f"logits-{position:03}.f32", dtype="<f4")
        logits.append(row.reshape(51864))
    return tokens, encoder, np.stack(logits)


def errors(reference, observed):
    left = reference.ravel().astype(np.float64)
    right = observed.ravel().astype(np.float64)
    if not np.isfinite(left).all() or not np.isfinite(right).all():
        raise ValueError("Non-finite values in a captured tensor")
    delta = right - left
    return {
        "Elements": left.size,
        "MeanAbs": float(np.mean(np.abs(delta))),
        "Rms": float(np.sqrt(np.mean(delta * delta))),
        "MaxAbs": float(np.max(np.abs(delta))),
        "Cosine": float(np.dot(left, right) / (np.linalg.norm(left) * np.linalg.norm(right))),
    }


def compare(reference, variant):
    tokens, encoder, logits = load_capture(reference)
    new_tokens, new_encoder, new_logits = load_capture(variant)
    # A changed prediction first affects the logits on the following evaluation.
    rows = min(len(logits), len(new_logits))
    for index, (expected, observed) in enumerate(zip(tokens, new_tokens)):
        if observed != expected:
            rows = min(rows, index + 2)
            break
    left, right = logits[:rows], new_logits[:rows]
    return {
        "SameTranscriptTokens": tokens == new_tokens,
        "MatchedInputPositions": rows,
        "Encoder": errors(encoder, new_encoder),
        "Logits": errors(left, right),
        "VocabTop1Agreements": int(np.sum(left.argmax(axis=1) == right.argmax(axis=1))),
        "GreedyTop1Agreements": int(np.sum(left[:, :50257].argmax(axis=1) == right[:, :50257].argmax(axis=1))),
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reference", type=Path)
    parser.add_argument("variant", type=Path)
    args = parser.parse_args()
    print(json.dumps(compare(args.reference, args.variant), indent=2))
