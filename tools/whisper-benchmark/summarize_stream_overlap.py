"""Summarize a Whisper profile-stream capture using timestamps in the device time domain."""

import argparse
import json
from pathlib import Path
from statistics import median


def tile_span(tile, kind):
    return {"StartTicks": tile[f"{kind}StartTicks"], "EndTicks": tile[f"{kind}EndTicks"],
            "NanosecondsPerTick": tile["NanosecondsPerTick"]}


def request_intervals(sample):
    batches = sample["EncoderBatches"]
    compute = [batch["Compute"] for batch in batches]
    copies = []
    consumers = [i for i, batch in enumerate(batches) if batch["Operation"] == "encoder-qkv"]
    prefetch = sample.get("PrefetchCopies") or []
    if prefetch:
        # The policy pins a prefix and streams the suffix in execution order.
        if len(prefetch) > len(consumers):
            raise ValueError("More encoder copies than consumers")
        for copy, index in zip(prefetch, consumers[-len(prefetch):]):
            copies.append((copy["Interval"], index, copy["Bytes"]))
    else:
        for index, row in zip(consumers, sample["EncoderUploads"], strict=True):
            if row["Bytes"]:
                copies.append((batches[index]["Transfer"], index, row["Bytes"]))
    uploaded = sample["Queue"]["UploadedBytes"]
    inner = sample.get("CheckpointTiles")
    if inner:
        intervals = inner["Intervals"]
        if intervals is None or len(intervals) != inner["Tiles"] or copies:
            raise ValueError("Expected a complete profile-stream capture of the checkpoint tile rings")
        uploaded += inner["Bytes"]
        tile_copies = []
        for tile in intervals:
            if not tile["Completed"]:
                raise ValueError("Unharvested tile")
            consumer = tile_span(tile, "Compute")
            compute.append(consumer)
            tile_copies.append((tile_span(tile, "Transfer"), consumer, tile["UploadBytes"]))
        compute.sort(key=lambda batch: batch["StartTicks"])
        positions = {id(batch): i for i, batch in enumerate(compute)}
        copies = [(copy, positions[id(consumer)], size) for copy, consumer, size in tile_copies]
    if sum(size for _, _, size in copies) != uploaded:
        raise ValueError("Missing timed weight copy")

    scale = compute[0]["NanosecondsPerTick"] / 1_000_000
    for span in compute + [copy for copy, _, _ in copies]:
        if (span["NanosecondsPerTick"] / 1_000_000 != scale
                or span["EndTicks"] < span["StartTicks"]):
            raise ValueError("Timestamp period mismatch or counter wrap")
    if any(right["StartTicks"] < left["EndTicks"] for left, right in zip(compute, compute[1:])):
        raise ValueError("Compute intervals must be ordered and disjoint")

    copy_ticks = 0
    overlap_ticks = 0
    gap_ticks = 0
    overlapped_by_copy = []
    for copy, index, _ in copies:
        start, end = copy["StartTicks"], copy["EndTicks"]
        if end > compute[index]["StartTicks"]:
            raise ValueError("Copy completes after first consumer starts")
        copy_ticks += end - start
        overlap = sum(max(0, min(end, batch["EndTicks"]) - max(start, batch["StartTicks"]))
                      for batch in compute)
        overlap_ticks += overlap
        overlapped_by_copy.append(overlap * scale)
        gap_ticks += max(0, end - max(start, compute[index - 1]["EndTicks"]))

    return {
        "CopyMs": copy_ticks * scale,
        "OverlapMs": overlap_ticks * scale,
        "OverlapByCopyMs": overlapped_by_copy,
        "OutsideComputeMs": (copy_ticks - overlap_ticks) * scale,
        "CopyInConsumerGapMs": gap_ticks * scale,
        "EncoderComputeMs": sum(span["EndTicks"] - span["StartTicks"] for span in compute) * scale,
        "EncoderSpanMs": (compute[-1]["EndTicks"] - compute[0]["StartTicks"]) * scale,
    }


def summarize(data):
    calibrated = {"VK_EXT_calibrated_timestamps", "VK_KHR_calibrated_timestamps"}
    if calibrated.isdisjoint(data["EnabledDeviceExtensions"]):
        raise ValueError("Comparing submissions requires a common device timestamp domain")
    rows = [request_intervals(sample) for sample in data["Samples"]]
    summary = {key: median(row[key] for row in rows) for key in rows[0] if key != "OverlapByCopyMs"}
    summary["OverlapByCopyMs"] = [median(values) for values in
                                  zip(*(row["OverlapByCopyMs"] for row in rows), strict=True)]
    total_copy = sum(row["CopyMs"] for row in rows)
    summary["OverlapPercent"] = 100 * sum(row["OverlapMs"] for row in rows) / total_copy if total_copy else None
    summary["Requests"] = len(rows)
    return summary


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    args = parser.parse_args()
    print(json.dumps(summarize(json.loads(args.capture.read_text(encoding="utf-8-sig"))), indent=2))
