"""Join scoped NVIDIA telemetry to identical Vulkan replay windows."""

import argparse
import csv
from datetime import datetime, timedelta, timezone
import hashlib
import json
import math
from pathlib import Path
import re
import statistics


def parse_timestamp(value):
    # System.Text.Json emits 100-ns fractions; Python 3.10 requires microseconds.
    normalized = value.replace("Z", "+00:00")
    normalized = re.sub(r"\.(\d+)(?=[+-]\d{2}:\d{2}$)",
        lambda match: "." + match.group(1)[:6].ljust(6, "0"), normalized)
    return datetime.fromisoformat(normalized)


def parse_clock(value):
    # csv includes units unless nounits is explicitly selected at collection.
    return int(value.strip().split()[0])


def summarize(root):
    path = root / "clock.json"
    report = json.loads(path.read_text())
    if report.get("Accepted") is not True:
        raise ValueError("Unaccepted clock report")
    clock_file = root / "clocks.csv"
    if hashlib.sha256(clock_file.read_bytes()).hexdigest() != report["ClockCsvSha256"]:
        raise ValueError("Clock telemetry changed")
    seen = set()
    samples = {}
    for row in report["Samples"]:
        key = (row["Variant"], row["Mode"], row["Round"], row["Sample"])
        if key in seen or not math.isfinite(row["GpuMicroseconds"]) or row["GpuMicroseconds"] <= 0:
            raise ValueError("Invalid clock sample")
        seen.add(key)
        samples[key] = row
    expected = {(variant, mode, iteration, sample) for variant in ("copy-256", "bias_silu-256")
        for mode in ("idle", "soak16", "soak512") for iteration in range(3) for sample in range(40)}
    if seen != expected:
        raise ValueError("Incomplete clock-control grid")
    checks = {(row["Variant"], row["Repetitions"], row["Distribution"]) for row in report["Checks"]}
    if len(checks) != 24 or len(report["Checks"]) != 24:
        raise ValueError("Incomplete clock numerical checks")
    if any(not math.isfinite(row["MaximumToleranceRatio"]) or row["MaximumToleranceRatio"] > 1 for row in report["Checks"]):
        raise ValueError("Invalid clock numerical result")
    local_timezone = timezone(timedelta(minutes=report["LocalUtcOffsetMinutes"]))
    clocks = []
    for row in csv.DictReader(clock_file.open()):
        try:
            timestamp = datetime.strptime(row["timestamp"].strip(), "%Y/%m/%d %H:%M:%S.%f").replace(tzinfo=local_timezone)
            sm = parse_clock(row[" clocks.current.sm [MHz]"])
            memory = parse_clock(row[" clocks.current.memory [MHz]"])
        except (ValueError, KeyError, AttributeError):
            # A killed telemetry process can leave one trailing partial line.
            continue
        clocks.append((timestamp, sm, memory))
    result = []
    for window in report["Windows"]:
        start = parse_timestamp(window["MeasuredStartUtc"])
        end = parse_timestamp(window["CompletedUtc"])
        matched = [row for row in clocks if start <= row[0] <= end]
        if not matched:
            raise ValueError("No telemetry inside measurement window")
        values = [samples[(window["Variant"], window["Mode"], window["Round"], index)]["GpuMicroseconds"] for index in range(40)]
        gpu = statistics.median(values)
        result.append(dict(Variant=window["Variant"], Mode=window["Mode"], Round=window["Round"],
            GpuMicroseconds=gpu, FirstFiveMedian=statistics.median(values[:5]), LastFiveMedian=statistics.median(values[-5:]),
            SmClockRange=[min(row[1] for row in matched), max(row[1] for row in matched)],
            MemoryClockRange=[min(row[2] for row in matched), max(row[2] for row in matched)],
            MemoryClockMedian=statistics.median(row[2] for row in matched), TelemetrySamples=len(matched),
            MinimumTrafficGigabytesPerSecond=1024 * 4096 * 8 / gpu / 1000))
    summary = dict(Accepted=True, Validation=report["Validation"], SourceReportSha256=hashlib.sha256(path.read_bytes()).hexdigest(),
        Windows=result)
    (root / "summary.json").write_text(json.dumps(summary, indent=2))
    for row in result:
        print(row["Variant"], row["Mode"], row["Round"], round(row["GpuMicroseconds"], 3),
            "SM", row["SmClockRange"], "memory", row["MemoryClockRange"])
    return summary


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=Path)
    summarize(parser.parse_args().root.resolve())
