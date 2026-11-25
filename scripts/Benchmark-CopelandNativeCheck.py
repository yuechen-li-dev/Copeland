"""Measure standalone tscl check and JsDiff's pinned TypeScript compilers on Windows."""

import argparse
from datetime import datetime, timezone
import importlib.util
import json
import os
from pathlib import Path
import platform
import statistics


REPO = Path(__file__).resolve().parent.parent


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native", type=Path, required=True)
    parser.add_argument("--managed", type=Path, help="Framework-dependent Copeland.Cli.dll")
    parser.add_argument("--baseline-native", type=Path)
    parser.add_argument("--jsdiff", type=Path, default=REPO.parent / "jsdiff")
    parser.add_argument("--output", type=Path, default=REPO / "artifacts/local/native-check")
    arguments = parser.parse_args()
    if os.name != "nt" or platform.python_implementation() != "CPython":
        parser.error("The harness requires Windows CPython for process memory counters.")

    harness_path = arguments.jsdiff.resolve() / "copeland-port/benchmark/benchmark.py"
    spec = importlib.util.spec_from_file_location("jsdiff_benchmark", harness_path)
    harness = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(harness)

    run_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    run = arguments.output.resolve() / run_id
    run.mkdir(parents=True)
    port = run / "m6-port"
    harness.copy_port(port)

    check_args = ["check", "--standalone", str(port)]
    ts_flags = ["-p", "tsconfig.json", "--incremental", "false", "--pretty", "false", "--noEmit"]
    tsgo = harness.TOOLS / "@typescript/typescript-win32-x64/lib/tsc.exe"
    tsrs = harness.TOOLS / "@tsrs/win32-x64/tsrs.exe"
    commands = {
        "tsgo": [str(tsgo), *ts_flags],
        "tsrs": [str(tsrs), *ts_flags],
        "copeland-native": [str(arguments.native.resolve()), *check_args],
    }
    if arguments.managed:
        commands["copeland-managed"] = ["dotnet", str(arguments.managed.resolve()), *check_args]
    if arguments.baseline_native:
        commands["copeland-baseline-native"] = [str(arguments.baseline_native.resolve()), *check_args]

    environment = os.environ.copy()
    for key in list(environment):
        if key.upper().startswith(("TSRS_", "COMPLUS_")) or key.upper() in (
            "NODE_OPTIONS", "GOMAXPROCS", "GOGC", "GOMEMLIMIT",
        ):
            environment.pop(key)

    warmups = 3
    sample_count = 21
    samples = harness.sample_processes(
        commands, harness.process_memory(), environment, sample_count, warmups
    )
    result = {
        "copeland_commit": harness.output(["git", "rev-parse", "HEAD"], cwd=REPO),
        "copeland_worktree": harness.output(["git", "status", "--short"], cwd=REPO),
        "jsdiff_commit": harness.output(["git", "rev-parse", "HEAD"], cwd=harness.REPO),
        "tsgo_version": harness.output([str(tsgo), "--version"], env=environment),
        "tsrs_version": harness.output([str(tsrs), "--version"], env=environment),
        "dotnet_sdk": harness.output(["dotnet", "--version"], cwd=harness.PORT, env=environment),
        "platform": platform.platform(),
        "commands": commands,
        "warmups": warmups,
        "samples_per_compiler": sample_count,
        "samples": samples,
    }
    measurements = run / "measurements.json"
    measurements.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    for name, rows in samples.items():
        time = statistics.median(row["wall_milliseconds"] for row in rows)
        peak = statistics.median(row["peak_working_set_bytes"] for row in rows) / 2**20
        print(f"{name:<28} {time:7.2f} ms {peak:6.2f} MiB peak working set")
    print(measurements)


if __name__ == "__main__":
    main()
