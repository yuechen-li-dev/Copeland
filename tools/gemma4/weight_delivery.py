"""Summarize calibrated warm decode deliveries from GemmaProof --trace-deliveries."""
import argparse
import json
import statistics
from collections import defaultdict
from pathlib import Path


def intersect(start, end, other_start, other_end):
    return max(0.0, min(end, other_end) - max(start, other_start))


def measure(trace, frequency):
    start = trace["Start"]
    end = trace["End"]
    host_ms = 1000 / frequency
    device_span = end["DeviceTicks"] - start["DeviceTicks"]
    host_span_ms = (end["HostTicks"] - start["HostTicks"]) * host_ms
    if device_span <= 0 or host_span_ms <= 0:
        raise ValueError("Nonmonotonic calibration endpoints")
    device_ms = host_span_ms / device_span

    def gpu(ticks):
        return (ticks - start["DeviceTicks"]) * device_ms

    def cpu(ticks):
        return (ticks - start["HostTicks"]) * host_ms

    totals = defaultdict(float)
    groups = defaultdict(lambda: defaultdict(float))
    totals["wall_ms"] = host_span_ms
    totals["calibration_us"] = max(start["MaxDeviationNs"], end["MaxDeviationNs"]) / 1000
    for row in trace["Deliveries"]:
        profile = row["Profile"]
        copy = profile["TransferInterval"]
        compute = profile["ComputeInterval"]
        if copy is None or compute is None:
            raise ValueError("Missing GPU interval for a weight delivery")
        copy_start, ready = gpu(copy["StartTicks"]), gpu(copy["EndTicks"])
        used = gpu(compute["StartTicks"])
        requested = cpu(row["Requested"])
        submit = cpu(profile["SubmitStartTimestamp"])
        tolerance_ms = totals["calibration_us"] / 1000
        if copy_start < submit - tolerance_ms or ready < copy_start or used < ready - tolerance_ms:
            raise ValueError("Inconsistent submission, transfer or consumer timestamps")
        role = row["Matrix"].split(".")[-2]
        sample = {
            "deliveries": 1, "MiB": profile["UploadBytes"] / 2**20,
            "copy_ms": ready - copy_start, "staging_ms": profile["StagingMs"],
            "request_to_use_ms": used - requested,
        }
        previous = profile["PreviousComputeEndTicks"]
        if previous is not None:
            # Earliest possible need assuming the next compute batch is otherwise
            # ready as soon as its predecessor ends. A measured gap is an upper bound
            # on weight starvation. Within this single queue, no other batch intervenes.
            need = gpu(previous)
            sample["bounded_deliveries"] = 1
            sample["gap_ms"] = max(0, used - need)
            sample["copy_in_gap_ms"] = intersect(need, used, copy_start, ready)
            pre_copy_end = min(used, copy_start)
            sample["before_copy_ms"] = max(0, pre_copy_end - need)
            sample["before_request_ms"] = max(0, min(requested, pre_copy_end) - need)
            sample["before_submit_ms"] = max(0, min(submit, pre_copy_end) - need)
            sample["staging_in_gap_ms"] = 0
            if profile["StagingMs"] > 0:
                stage = cpu(profile["StagingStartTimestamp"])
                sample["staging_in_gap_ms"] = intersect(need, pre_copy_end, stage, stage + profile["StagingMs"])
            sample["after_ready_ms"] = max(0, used - max(need, ready))
        for key, value in sample.items():
            totals[key] += value
            groups[role][key] += value
    for aggregate in [totals, *groups.values()]:
        if aggregate["deliveries"]:
            aggregate["mean_request_to_use_ms"] = aggregate.pop("request_to_use_ms") / aggregate["deliveries"]
    return totals, groups


def medians(rows):
    return {key: round(statistics.median(row.get(key, 0) for row in rows), 3)
            for key in sorted(set().union(*rows))}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("evidence", type=Path)
    args = parser.parse_args()
    evidence = json.loads(args.evidence.read_text(encoding="utf-8-sig"))
    rows = []
    roles = defaultdict(list)
    for trace in evidence["DeliveryTraces"]:
        totals, groups = measure(trace, evidence["DeliveryClockFrequency"])
        rows.append(totals)
        for role, values in groups.items():
            roles[role].append(values)
    if not rows:
        raise ValueError("No traced decode steps")
    print(json.dumps({"steps": len(rows), "per_token_medians": medians(rows),
                      "by_projection": {role: medians(values) for role, values in roles.items()}}, indent=2))
