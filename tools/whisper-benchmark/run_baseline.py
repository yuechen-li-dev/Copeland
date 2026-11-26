"""Run the matched Vulkan comparison and reject mismatched work."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import statistics
import subprocess

from prepare_mel import prepare


MODES = ("aurelian-per-head", "whisper-flash", "aurelian-batched", "whisper-standard")
ACCELERATION_MODES = ("aurelian-batched", "aurelian-reuse", "whisper-flash",
                      "aurelian-direct", "aurelian-gemv", "whisper-standard")
PHASES = ("TotalMs", "EncodeMs", "PrefillMs", "DecodeMs")
METRICS = ("WallMs", "GpuMs", "ComputeSubmissions", "TransferCalls", "Dispatches", "CompletionWaits")
MODEL_SHA256 = "921e4cf8686fdd993dcd081a5da5b6c365bfde1162e72b08d75ac75289920b1f"


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def reference_device(log):
    selected = re.search(r"whisper_backend_init_gpu: using Vulkan(\d+) backend", log)
    if selected is None:
        raise ValueError("whisper.cpp did not select a Vulkan GPU")
    adapter = re.search(rf"^ggml_vulkan: {selected[1]} = (.+?) \(.+?\) \|", log, re.MULTILINE)
    if adapter is None:
        raise ValueError("whisper.cpp selected a Vulkan GPU without a matching device name")
    return adapter[1]


def describe(values):
    return {"P50": statistics.median(values), "Min": min(values), "Max": max(values)}


def run(args):
    modes = ACCELERATION_MODES if args.acceleration else MODES
    args.output.mkdir(parents=True, exist_ok=True)
    mel = args.output / "input.mel.f32"
    if sha256(args.checkpoint) != MODEL_SHA256:
        raise ValueError("Checkpoint SHA-256 differs from the pinned Tiny.en F16 weights")
    duration = prepare(args.checkpoint, args.wav, mel)
    measurements = {mode: [] for mode in modes}
    warmups = {mode: [] for mode in modes}
    initialization = {mode: [] for mode in modes}
    reference_tokens = None
    transcript = None
    device_name = None
    events = []
    for index in range(args.rounds):
        offset = index % len(modes)
        order = modes[offset:] + modes[:offset]
        if index % 2:
            order = order[::-1]
        for mode in order:
            prefix = args.output / f"round-{index + 1}-{mode}"
            log_path = prefix.with_suffix(".log")
            json_path = prefix.with_suffix(".json")
            model_args = [str(args.checkpoint), str(mel), str(json_path), str(args.samples)]
            aurelian = mode.startswith("aurelian-")
            if aurelian:
                command = ["dotnet", str(args.aurelian), *model_args, mode.removeprefix("aurelian-")]
            else:
                command = [str(args.reference), *model_args, mode.removeprefix("whisper-")]
            print(f"Round {index + 1} / {mode}", flush=True)
            with log_path.open("w", encoding="utf-8") as log:
                result = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, check=False, timeout=180)
            if result.returncode:
                raise RuntimeError(f"{mode} exited with status {result.returncode}. See {log_path}.")
            run_result = json.loads(json_path.read_text(encoding="utf-8"))
            device = run_result["Device"] if aurelian else reference_device(log_path.read_text(encoding="utf-8"))
            if device_name is None:
                device_name = device
            if device != device_name:
                raise ValueError(f"Vulkan device mismatch: {device_name!r} and {device!r}")
            samples = run_result["Samples"]
            if len(samples) != args.samples or (aurelian and run_result["Validation"]):
                raise ValueError(f"Incomplete samples or validation enabled in {json_path}")
            if reference_tokens is None:
                reference_tokens = samples[0]["Tokens"]
            for sample in samples:
                if sample["Tokens"] != reference_tokens or sample["Tokens"][-1] != 50256:
                    raise ValueError(f"Token sequence differs in {json_path}: {sample['Tokens']}")
                if aurelian and sample["Readbacks"] != len(sample["Tokens"]) + 1:
                    raise ValueError(f"Unexpected decoder readbacks in {json_path}")
            if aurelian:
                transcript = run_result["Transcript"]
                warmup = run_result["WarmupMs"]
            else:
                warmup = [sample["TotalMs"] for sample in run_result["Warmup"]]
            warmups[mode].append(warmup)
            initialization[mode].append(run_result["InitializationMs"])
            measurements[mode].extend(samples)
            events.append({"Mode": mode, "Samples": json_path.name, "Log": log_path.name})

    if args.acceleration:
        allocation_difference = {
            original["Allocations"] - reused["Allocations"]
            for original, reused in zip(measurements["aurelian-batched"], measurements["aurelian-reuse"])
        }
        if allocation_difference != {6 * 4 * (len(reference_tokens) + 1)}:
            raise ValueError(f"Unexpected cross-attention allocation change: {allocation_difference}")

    summary = {}
    for mode, samples in measurements.items():
        result = {phase: describe([sample[phase] for sample in samples]) for phase in PHASES}
        result.update({
            "Samples": len(samples),
            "RealtimeFactor": result["TotalMs"]["P50"] / (duration * 1000),
            "InitializationMs": initialization[mode],
            "WarmupMs": warmups[mode],
        })
        if mode.startswith("aurelian-"):
            operations = [sample["Operations"] for sample in samples]
            if any(operation["TimedSubmissions"] != operation["ComputeSubmissions"]
                   for sample in operations for operation in sample.values()):
                raise ValueError("Aurelian GPU timestamp coverage is incomplete")
            result["Operations"] = {
                name: {field: describe([sample[name][field] for sample in operations]) for field in METRICS}
                for name in operations[0]
            }
            for field in METRICS:
                result[field] = describe([sum(item[field] for item in sample.values()) for sample in operations])
            result["Readbacks"] = describe([sample["Readbacks"] for sample in samples])
            result["Allocations"] = describe([sample["Allocations"] for sample in samples])
        summary[mode] = result

    comparisons = [("aurelian-per-head", "aurelian-batched")]
    if args.acceleration:
        comparisons = [("aurelian-batched", "aurelian-reuse"), ("aurelian-direct", "aurelian-gemv")]
    for first, second in comparisons:
        for field in ("Dispatches", "ComputeSubmissions"):
            if field == "ComputeSubmissions" and not args.acceleration:
                continue
            if summary[first][field] != summary[second][field]:
                raise ValueError(f"Expected matching {field} in {first} and {second}")

    output = {
        "Device": device_name,
        "AudioSeconds": duration,
        "CheckpointSha256": MODEL_SHA256,
        "AudioSha256": sha256(args.wav),
        "MelSha256": sha256(mel),
        "Tokens": reference_tokens,
        "Transcript": transcript,
        "Results": summary,
        "Runs": events,
    }
    (args.output / "summary.json").write_text(json.dumps(output, indent=2) + "\n", encoding="utf-8")
    for mode, result in summary.items():
        print(f"{mode:<22} {result['TotalMs']['P50']:.2f} ms median")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for argument in ("checkpoint", "wav", "aurelian", "reference", "output"):
        parser.add_argument(argument, type=lambda value: Path(value).resolve())
    parser.add_argument("--rounds", type=int, default=3, choices=range(1, 11))
    parser.add_argument("--samples", type=int, default=3, choices=range(1, 21))
    parser.add_argument("--acceleration", action="store_true")
    run(parser.parse_args())
