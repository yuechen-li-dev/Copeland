"""Compare two Aurelian projection kernels with llama.cpp on fixed GPT-2 tokens."""

import argparse
import hashlib
import json
from pathlib import Path
from statistics import median
import subprocess


MODES = ("tiled", "llama", "gemv")
CHECKPOINT_SHA256 = "d96baac3127df932b743507d957b88308ee2735dda3f8661284fc5c6cc1ab744"


def run(args):
    digest = hashlib.sha256()
    with args.checkpoint.open("rb") as checkpoint:
        for chunk in iter(lambda: checkpoint.read(1024 * 1024), b""):
            digest.update(chunk)
    if digest.hexdigest() != CHECKPOINT_SHA256:
        raise ValueError("Expected the pinned GPT-2 F32 GGUF checkpoint.")
    args.output.mkdir(parents=True, exist_ok=True)
    measurements = {mode: [] for mode in MODES}
    reference = None
    adapter = None
    maximum_error = 0
    maximum_gemv_error = 0
    for round_index in range(args.rounds):
        offset = round_index % len(MODES)
        for mode in MODES[offset:] + MODES[:offset]:
            print(f"Round {round_index + 1} / {mode}", flush=True)
            name = args.output / f"round-{round_index + 1}-{mode}"
            log_path = name.with_suffix(".log")
            result_path = name.with_suffix(".json")
            common_args = [str(args.checkpoint), str(result_path), str(args.samples)]
            if mode == "llama":
                command = [str(args.reference), *common_args]
            else:
                command = ["dotnet", str(args.aurelian), *common_args, mode]
            with log_path.open("w", encoding="utf-8") as log:
                status = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, check=False, timeout=120)
            if status.returncode:
                raise RuntimeError(f"{mode} exited with status {status.returncode}. See {log_path}.")
            result = json.loads(result_path.read_text())
            if adapter is None:
                adapter = result["Device"]
            if (result["Device"] != adapter or len(result["Samples"]) != args.samples
                    or result.get("Validation")):
                raise ValueError(f"Unexpected device or sample count in {result_path}.")
            if mode == "llama" and "offloaded 13/13 layers to GPU" not in log_path.read_text(encoding="utf-8"):
                raise ValueError(f"Incomplete llama.cpp Vulkan model offload. See {log_path}.")
            for sample in result["Samples"]:
                predictions = sample["Predictions"]
                if len(predictions) != 33:
                    raise ValueError(f"Unexpected number of logits in {result_path}.")
                if reference is None:
                    reference = predictions
                for actual, expected in zip(predictions, reference):
                    if actual["Token"] != expected["Token"]:
                        raise ValueError(f"Top token differs in {result_path}: {actual} vs {expected}")
                    delta = abs(actual["Score"] - expected["Score"])
                    maximum_error = max(maximum_error, delta)
                    if mode == "gemv":
                        maximum_gemv_error = max(maximum_gemv_error, delta)
                measurements[mode].append(sample)
    if maximum_error > 0.20 or maximum_gemv_error > 0.075:
        raise ValueError(f"Unexpected top-logit drift: across runtimes {maximum_error}, Aurelian GEMV {maximum_gemv_error}.")
    results = {}
    for mode, samples in measurements.items():
        phases = ("PrefillMs", "DecodeMs")
        if mode != "llama":
            phases += ("PrefillGpuMs", "DecodeGpuMs")
        row = {}
        for phase in phases:
            values = [sample[phase] for sample in samples]
            row[phase] = {"P50": median(values), "Min": min(values), "Max": max(values)}
        row["DecodeTokensPerSecond"] = 32000 / row["DecodeMs"]["P50"]
        row["Samples"] = len(samples)
        results[mode] = row
        print(f"{mode:<8} {row['DecodeTokensPerSecond']:.1f} tokens/s")
    summary = {
        "Device": adapter,
        "CheckpointSha256": digest.hexdigest(),
        "PromptTokenIds": list(range(100, 132)),
        "DecodeTokenIds": list(range(500, 532)),
        "TopTokens": [prediction["Token"] for prediction in reference],
        "MaximumTopLogitDeltaFromTiled": maximum_error,
        "MaximumGemvTopLogitDeltaFromTiled": maximum_gemv_error,
        "Results": results,
    }
    (args.output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for argument in ("checkpoint", "aurelian", "reference", "output"):
        parser.add_argument(argument, type=lambda value: Path(value).resolve())
    parser.add_argument("--rounds", type=int, default=3, choices=range(1, 11))
    parser.add_argument("--samples", type=int, default=3, choices=range(1, 21))
    run(parser.parse_args())
