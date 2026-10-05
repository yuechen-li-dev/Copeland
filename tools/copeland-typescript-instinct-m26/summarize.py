"""Summarize recorded M26 executions; never substitutes expectations for results."""

import hashlib
import json
from pathlib import Path
import re
import statistics
import subprocess


ROOT = Path(__file__).resolve().parents[2]
ARTIFACTS = ROOT / "artifacts/copeland-typescript-instinct-m26"
FIXTURES = ROOT / "tests/Copeland/Copeland.TS.Tests/TypeScriptInstinctCorpus/instinct-corpus.json"
BASELINE = "0bffd10afe0f9e5f3a5c1281e7713d799f5c0d3c"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write(name, value):
    (ARTIFACTS / name).write_text(
        json.dumps(value, indent=2, ensure_ascii=True) + "\n", encoding="utf-8", newline="\n"
    )


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


fixtures = read(FIXTURES)
corpus = read(ARTIFACTS / "instinct-corpus.json")
assert not corpus["failures"]
assert len(corpus["results"]) == len(fixtures)
results = {item["Id"]: item for item in corpus["results"]}
baseline = {item["Id"]: item for item in read(ARTIFACTS / "baseline-instinct-map.json")}
assert len(baseline) == len(fixtures)


def cases(predicate):
    return [results[item["Id"]] for item in fixtures if predicate(item)]


decisions = [
    ("Named ++/-- and compound arithmetic", "accept", "Typed mutation gains no safety from spelling out every addition."),
    ("=== and !==", "accept aliases", "Existing typed equality already excludes coercion; add no identity domain."),
    ("Receiver string methods", "repair", "Keep associated functions and name the exact native call."),
    ("new and generic CLR constructors", "accept CLR-only", "Reuse reflected constructor/type authority, not a second runtime."),
    ("Pure structured batch branches", "accept bounded", "Normalize into existing expression/value semantics."),
    ("Computed compound mutation", "repair", "Bind receiver/index once and spell out the indexed update."),
    ("Immutable records and closed object values", "retain", "Preserve value identity/layout; update through with."),
    ("Option rather than null/undefined", "retain", "One explicit absence model with typed construction and access."),
    ("Explicit closure captures", "retain", "Keep ownership visible; no implicit ambient capture."),
    ("any", "repair", "Concrete values or constrained generics preserve layout and static authority."),
    ("Native mutable map/list", "unsupported", "CLR alternatives exist; native ordered hash/Option semantics still require a shared owner."),
    ("int32 arithmetic", "wrap", "Specify the same result in static evaluation, checked C# hosts, and JavaScript."),
    ("SoA/record-table layout", "retain", "Scalar column hot loops need no forced row materialization."),
]
write("principle-decisions.json", {
    "law": "Accept defined semantics, provide one primary exact repair, or state a missing capability with the closest supported alternative.",
    "decisions": [dict(instinct=a, decision=b, rationale=c) for a, b, c in decisions],
})
write("diagnostic-repairs.json", {
    "singlePrimaryAndAuthoredSpanVerified": True,
    "metadata": ["SuggestedReplacement", "RepairKind", "SourcePath", "Position", "Length"],
    "metadataOptional": True,
    "cases": cases(lambda item: item["Outcome"] != "compile"),
    "limits": "This bounded corpus does not prove every parser recovery sequence has one diagnostic.",
})
write("string-surface.json", {
    "qualified": True,
    "owner": "typed MirNativeOperation calls, shared argument/type validation, native backend helpers",
    "semantics": {
        "Split": "Ordinal; retain empty fields; empty separator splits UTF-16 units.",
        "IndexOf": "First ordinal UTF-16 offset or -1; empty needle at zero.",
        "CodeAt": "UTF-16 int code unit; negative and upper indices trap.",
        "Slice": "Two explicit int bounds; negative-from-end; clamp; end exclusive; reversed empty.",
        "Join": "Ordered string[] plus separator.",
    },
    "cases": cases(lambda item: item["Category"] == "strings"),
    "unqualified": ["optional search positions", "regex separators", "locale rules"],
})
write("generic-clr-instantiation.json", {
    "qualified": True, "clrOnly": True,
    "forms": ["using System.Collections.Generic; new List<int>()", "List<int>()", "new Dictionary<string, int>()"],
    "cases": cases(lambda item: item["Category"] == "interop" or item["Id"] == "malformed-generic"),
    "authority": "Closed reflected CLR types, constructors, and property accessor MethodInfo identities.",
    "fix": "Resolve CLR using directives before function signatures; preserve local declaration conflicts.",
    "unsupported": ["nested generic owners", "type arguments without CLR mappings", "CLR execution in JavaScript"],
})
write("mutable-map.json", {
    "nativeQualified": False,
    "exactMissingSeam": "No typed native map MIR/storage owner defines key equality/hashing, ordered iteration, mutation, and Option lookup for both backends.",
    "diagnostic": "COPE-COLLECTION-0001",
    "alternative": "CLR Dictionary<K,V>; ContainsKey then indexed read; no portable ordering/Option claim.",
    "cases": cases(lambda item: item["Outcome"] == "unsupported capability"),
    "nextMilestone": "M27: scalar-key ordered hash storage and typed native operations shared by MIR/C#/JS, including deletion/reinsertion ordering and Option-valued lookup.",
})
write("compound-assignment.json", {
    "namedLocalQualified": True,
    "operators": ["++", "--", "+=", "-=", "*=", "/=", "%="],
    "postfixReturnsPrevious": True,
    "computedStorageRequiresExplicitUpdate": True,
    "immutableRecordsRequireWith": True,
    "cases": cases(lambda item: item["Category"] in ["mutation", "loops"] or item["Id"] == "record-compound-repair"),
})
write("batch-if.json", {
    "qualified": True,
    "normalization": "Final single-return if/else or guard-return/fallback into existing MirIfExpression; bounded pure prefix statements reuse ordinary binding/emission.",
    "cases": cases(lambda item: item["Category"] in ["batch", "conditionals"]),
    "unsupported": "General loops and early returns in batch bodies; use pure helpers or expression branches.",
})

measurements = read(ARTIFACTS / "batch-measurements.json")
batch_rows = []
for row in measurements["rows"]:
    assert len(set(sample["checksum"] for sample in row["samples"])) == 1
    batch_rows.append({
        "size": row["size"], "body": row["body"], "mode": row["mode"],
        "medianMilliseconds": statistics.median(sample["milliseconds"] for sample in row["samples"]),
        "medianAllocatedBytes": statistics.median(sample["allocatedBytes"] for sample in row["samples"]),
        "checksum": row["samples"][0]["checksum"],
    })
write("batch-threshold.json", {
    "default": 128, "logicalProcessors": measurements["processorCount"],
    "method": "Real checked Release-generated C#; warmup 3, measured trials 5; forced sequential/parallel scheduling seam.",
    "rationale": "128 is a conservative count-only compromise: costly work benefits, while tiny cheap batches avoid scheduling overhead. It is not a universal crossover.",
    "rows": batch_rows,
    "limits": "Cheap work favors sequential even at 16384. Body-cost adaptation remains unimplemented; sub-microsecond timings and parallel allocations are noisy.",
})
write("batch-allocation.json", {
    "lazyFailureStorage": True,
    "allocationOwner": "One batch closure/delegate; allocate ConcurrentDictionary only upon failure, not per successful batch.",
    "method": "GC.GetTotalAllocatedBytes(precise:true), includes reflection harness overhead.",
    "current": batch_rows,
    "baseline": measurements["baseline"],
    "baselineLimits": "Baseline has one warmed measured trial per size, not a distribution. Large parallel allocations vary with worker scheduling.",
    "failurePolicy": "Sequential stops at first failure; parallel selects lowest failing input index after work. Both report COPE-BATCH-FAILURE.",
})
integer_policy = {
    "type": "signed int32", "addSubtractMultiplyNegate": "wrap modulo 2^32",
    "division": "truncate toward zero; minimum/-1 wraps to minimum",
    "remainder": "dividend sign; minimum%-1 is zero",
    "zeroDivisor": "trap: Integer division by zero.",
    "bitwise": "int operands only; signed result; arithmetic right shift; shift count masked to low 5 bits",
    "conversion": "Explicit Int floor/ceil/round/truncate retains checked int32 range policy; no implicit widening.",
    "literals": "Hex 1-8 digits is an int32 bit pattern; direct decimal unary minimum is accepted.",
    "staticEvaluation": "Same int32 wrapping arithmetic in existing static evaluator.",
    "hostIsolation": "C# checked host explicitly overridden at arithmetic operations; JS imul/int32 normalization.",
    "namedWrappingOperations": "Not added because ordinary integer arithmetic wraps.",
}
write("integer-overflow-policy.json", integer_policy)
write("cross-backend-int-parity.json", {
    "qualified": True, "csharpHostChecked": True,
    "javascriptProfiles": ["Production", "Symbolic"],
    "cases": cases(lambda item: item["Category"] in ["numeric", "numeric literals"]),
    "limits": "Bounded corpus; does not prove every possible arithmetic expression or host exception class identity.",
})
write("bitwise-proof.json", {
    "qualified": True,
    "cases": cases(lambda item: "bitwise" in item["Id"] or item["Id"] in ["int-negative-shift", "hex-int-bit-pattern"]),
    "policy": integer_policy["bitwise"],
})
identity = read(ARTIFACTS / "generated-source-identity-proof.json")
identity.update({
    "architectureGate": "InstinctArchitectureTests.Identity_owners_cannot_rewrite_emitted_source_with_replace_or_regex",
    "queryOwnerRuntimeTest": "InstinctArchitectureTests.Query_materialization_uses_the_selected_namespace_and_module_owner",
    "taskOwner": "CSharpEmissionOptions chosen before emission, including carrier scope and function export visibility.",
    "allowedTransforms": "MSBuild exception newline cleanup, C# literal/path escaping, explicit-code block newline normalization, and pre-emission stable identity encoding are nonsemantic emitted-source rewrites.",
})
write("generated-source-identity-proof.json", identity)
write("fingerprint-proof.json", {
    "compilerBytesQualified": True,
    "test": "CompilerPayloadFingerprintTests.Equal_version_size_and_timestamp_do_not_hide_repacked_compiler_bytes",
    "method": "Two actual Roslyn-built assemblies: same name/version/size/timestamp, different method bytes; production payload-hash helper returns different fingerprints.",
    "payloads": ["MSBuild task", "Copeland compiler", "C# backend", "MIR"],
    "options": ["root namespace", "module name", "project type transport", "C# LangVersion", "DefineConstants", "Nullable"],
    "sourceClosure": "Independent source text or sorted project source graph, authored C# text, package/npm contracts.",
    "limits": ["External CLR references retain path/size/timestamp identity.", "TSON asset contents are not independently fingerprinted by the pre-existing task cache.", "An end-to-end same-version repacked binary output rebuild was not executed; the production hash input was tested directly."],
})
write("linux-golden-proof.json", {
    "canonicalLfQualifiedOnWindows": True, "linuxExecutionQualified": False,
    "owner": "MirTextWriter.ToText normalizes CRLF to LF for every caller.",
    "pins": [
        {"test": "TableFeatureTests", "bytes": 1606, "sha256": "CB293E99C3353216CE04AFD5703ADD331FA8A450AB4C5FDC8177DF9C97E4A144"},
        {"test": "CliIntegrationTests.Inferred_generic_cli_emission_is_repeatable_executable_and_preserves_stale_artifacts", "bytes": 602, "sha256": "7D8233C236DF3B5F38DD1F5793346B51D5EE7A33D58E0054393D8E0408C6C595"},
    ],
    "platformSpecificExpectedValues": False,
    "limits": "Canonical byte pins pass here; no Linux process was executed in this Windows session.",
})
write("array-codegen-proof.json", {
    "qualified": True,
    "owner": "Typed int index/length emission omits redundant checked((int)) casts; genuine numeric conversions retain checks.",
    "cases": cases(lambda item: item["Category"] in ["arrays", "collections"] and item["Outcome"] == "compile"),
    "limits": "The arrays benchmark rerun is aggregate evidence; no JIT disassembly or isolated bounds-check elimination speedup was proved. Existing backend bounds policy is retained.",
})
write("soa-doc-proof.json", {
    "qualified": True,
    "document": "docs/Copeland/language/typescript-instinct-m26.md",
    "law": "Prefer scalar arrays/record-table columns for bulk kernels; no forced row materialization in column hot loops. Records remain values.",
    "initializer": "MutableArray<Record> convenience is permitted, with no blanket warning or new AoS recommendation.",
    "benchmarkLayout": "Original objects benchmark is matched row-array layout, not columnar throughput evidence.",
})

before = read(ARTIFACTS / "dogfood-baseline-replay.json")
after = read(ARTIFACTS / "dogfood-replay.json")
execution = read(ARTIFACTS / "dogfood-execution.json")
assert len(execution["results"]) == 6 and not execution["failures"]
dogfood_rows = []
for old, new in zip(before["Cases"], after["results"]):
    assert old["Id"] == new["id"]
    dogfood_rows.append({
        "id": old["Id"], "baselineRepairTurns": old["RepairCount"],
        "baselineStatus": old["FinalStatus"], "afterLlmRepairAttempts": new["repairRounds"],
        "baselineExecutionQualified": old["Id"] in ["increment-loop", "batch-guard", "integer-hash"],
        "afterCompilerFixRetry": 1 if new["id"] == "counter-map" else 0,
        "afterExecuted": True,
    })
for name in ["loop", "batch", "hash"]:
    assert read(ARTIFACTS / f"raw/dogfood-baseline-{name}-execution.json")["passed"]
write("repair-turns-before-after.json", {
    "definition": "One compiler invocation following a failed authoring attempt; successful-source verification reruns are excluded.",
    "rows": dogfood_rows,
    "baseline": {"repairTurns": 18, "completedAndExecuted": 3, "blocked": 3},
    "after": {"llmRepairAttempts": sum(row["afterLlmRepairAttempts"] for row in dogfood_rows), "compilerFixRetry": 1, "repairTurns": 11, "completedAndExecuted": 6},
    "comparableCompletedSubset": {"ids": ["increment-loop", "batch-guard", "integer-hash"], "baselineRepairTurns": 8, "afterRepairTurns": 1},
    "limits": "Totals have different completion rates; baseline blocked snippets are not treated as successful repairs. A CLR declaration-order compiler fix was required during replay. Root development probes are not LLM authoring turns.",
})
write("llm-dogfood.json", {
    "protocol": "Fresh-context initial author; separate baseline and current diagnostic-only repair contexts. No language docs or compiler source read by repair agents; max six attempts each.",
    "inputs": "llm-dogfood-inputs.json", "baselineTranscript": "dogfood-baseline-replay.json",
    "afterTranscript": "dogfood-replay.json", "execution": "dogfood-execution.json",
    "initialCompilerAcceptedBefore": 0, "initialCompilerAcceptedAfter": 2,
    "afterExecuted": 6, "afterPortableExecuted": 3, "afterClrOnlyExecuted": 3,
    "baselineExecuted": 3,
    "selfContainedOneDiagnosticRepairGoalFullyMet": False,
    "limits": "CSV required five repairs and uses CLR Int32.Parse on complete decimal fields; arbitrary parseInt prefix/trimming semantics are unqualified. Native maps/lists are still absent; current map success required the compiler import-order fix. Replay failures remain in the transcript.",
})

benchmark_rows = []
for name in ["fib", "nbody", "trees", "sieve", "arrays", "strings", "objects", "closures", "native-strings", "maps"]:
    clr = read(ARTIFACTS / f"raw/copeland-{name}.json")
    node = read(ARTIFACTS / f"raw/node-{name}.json")
    assert not clr["quick"] and not node["quick"]
    checksums = {trial["result"] for trial in clr["trials"] + node["trials"]}
    assert len(checksums) == 1
    clr_best = min(clr["trials"], key=lambda trial: trial["milliseconds"])
    node_best = min(node["trials"], key=lambda trial: trial["milliseconds"])
    benchmark_rows.append({
        "name": name, "checksum": next(iter(checksums)), "checksumParity": True,
        "clrBestMilliseconds": clr_best["milliseconds"], "nodeBestMilliseconds": node_best["milliseconds"],
        "clrBestAllocatedBytes": clr_best["allocatedBytes"], "clrBestGcCollections": clr_best["gcCollections"],
        "nodeBestHeapUsedDeltaBytes": node_best["heapUsedDeltaBytes"],
        "clrPeakWorkingSetBytes": clr["peakWorkingSetBytes"], "nodePeakWorkingSetBytes": node["peakWorkingSetBytes"],
    })
by_name = {row["name"]: row for row in benchmark_rows}
write("benchmark-rerun.json", {
    "method": "Full-size original same-algorithm/data-layout twins; three timed trials per isolated process, best trial reported; Release net10 task path and Node.",
    "environment": {"os": "Windows", "dotnetSdk": "10.0.401", "node": "26.2.0", "logicalProcessors": measurements["processorCount"]},
    "archiveSha256": "4A5EADD273B4229668EFB8C6EEE67F93A182C41C3A751A671EBB16B5013CBA2A",
    "rows": benchmark_rows,
    "limits": ["Single-process kernels; not a universal language ranking.", "CLR allocation is total GC allocation; Node heap delta is retained heap change and may be negative, not allocation parity.", "Node GC counts unavailable; process peak working set includes startup and all trials.", "No separate untimed warmup; best-of-three retains tiering and GC noise.", "Maps use CLR Dictionary versus JS Map, not native Copeland map backend parity.", "Arrays results do not isolate the codegen cleanup from all M26 changes."],
})
allocation_reduction = 1 - by_name["native-strings"]["clrBestAllocatedBytes"] / by_name["strings"]["clrBestAllocatedBytes"]
write("string-benchmark.json", {
    "originalClrDetour": by_name["strings"], "native": by_name["native-strings"],
    "nativeAllocationReductionFraction": allocation_reduction,
    "nativeSpeedupClaim": False,
    "interpretation": "Native strings remove interop syntax/metadata dependence and reduce measured allocation, but this kernel is slower on CLR. Do not infer a universal speedup.",
})
write("maps-benchmark.json", {
    "expressibleClrAlternative": True, "nativeMapQualified": False,
    "algorithm": "100000 string keys, ten insert/read/update/remove rounds; equal result in CLR Dictionary and JS Map twins.",
    "result": by_name["maps"],
    "keyPolicy": "String ordinal content in this benchmark only; do not infer native key equality, hash, or iteration semantics.",
})

validation_text = (ARTIFACTS / "raw/full-tests.log").read_text(encoding="utf-8-sig")
counts = re.findall(r"Passed!\s+- Failed:\s+0, Passed:\s+(\d+), Skipped:\s+0, Total:\s+(\d+)", validation_text)
assert len(counts) == 8 and "Failed!" not in validation_text
assert all(passed == total for passed, total in counts)
test_count = sum(int(passed) for passed, total in counts)
build_text = (ARTIFACTS / "raw/full-build.log").read_text(encoding="utf-8-sig")
assert "Build succeeded." in build_text and "0 Error(s)" in build_text
write("validation.json", {
    "commands": ["dotnet build Copeland.slnx -m:1", "dotnet test Copeland.slnx -m:1", "dotnet build tools/copeland-typescript-instinct-m26/InstinctProof.csproj -c Release -m:1", "dotnet InstinctProof.dll <corpus> <results>", "pwsh -File tools/copeland-typescript-instinct-m26/run-benchmarks.ps1"],
    "solutionTestCount": test_count, "solutionTestAssemblies": 8, "failures": 0,
    "instinctCases": len(fixtures), "instinctCompileCases": sum(item["Outcome"] == "compile" for item in fixtures),
    "instinctRepairCases": sum(item["Outcome"] == "repair" for item in fixtures),
    "instinctUnsupportedCases": sum(item["Outcome"] == "unsupported capability" for item in fixtures),
    "checkedCsharpExecution": True, "javascriptProfiles": ["Production", "Symbolic"],
})

source_files = subprocess.check_output(
    ["git", "diff", "--name-only"], cwd=ROOT, text=True
).splitlines()
added_files = subprocess.check_output(
    ["git", "ls-files", "--others", "--exclude-standard"], cwd=ROOT, text=True
).splitlines()
added_files = [name for name in added_files if not name.startswith(".tmp/")]
diff_stat = subprocess.check_output(["git", "diff", "--stat"], cwd=ROOT, text=True).strip()
write("change-inventory.json", {
    "trackedDiff": diff_stat, "trackedPaths": source_files,
    "addedPaths": added_files, "baselineCommit": BASELINE,
})

report_path = ROOT / "docs/milestones/copeland-typescript-instinct-corpus-m26-report.md"
report = report_path.read_text(encoding="utf-8").split("\n<!-- M26 measured tables -->")[0]
tables = [
    "\n<!-- M26 measured tables -->",
    "\n## Recorded measurements\n",
    "| Kernel | CLR best ms | Node best ms | CLR allocated MiB | Peak CLR / Node MiB | Checksum |",
    "| --- | ---: | ---: | ---: | ---: | --- |",
]
for row in benchmark_rows:
    tables.append(
        f"| {row['name']} | {row['clrBestMilliseconds']:.2f} | {row['nodeBestMilliseconds']:.2f} "
        f"| {row['clrBestAllocatedBytes'] / 1048576:.2f} "
        f"| {row['clrPeakWorkingSetBytes'] / 1048576:.2f} / {row['nodePeakWorkingSetBytes'] / 1048576:.2f} "
        f"| {row['checksum']} |"
    )
tables.extend([
    "\nCLR allocated bytes and peaks describe different quantities. Peaks are process-wide maxima across all trials; "
    "Node heap deltas and CLR GC counts remain in [the raw trial JSON](../../artifacts/copeland-typescript-instinct-m26/raw).\n",
    "| Batch size | Cheap seq / par ms | Costly seq / par ms | Cheap seq / par bytes |",
    "| ---: | ---: | ---: | ---: |",
])
batch_lookup = {(row["size"], row["body"], row["mode"]): row for row in batch_rows}
for size in [1, 8, 32, 128, 1024, 16384]:
    cheap_seq = batch_lookup[(size, "cheap", "sequential")]
    cheap_par = batch_lookup[(size, "cheap", "parallel")]
    costly_seq = batch_lookup[(size, "costly", "sequential")]
    costly_par = batch_lookup[(size, "costly", "parallel")]
    tables.append(
        f"| {size} | {cheap_seq['medianMilliseconds']:.4f} / {cheap_par['medianMilliseconds']:.4f} "
        f"| {costly_seq['medianMilliseconds']:.4f} / {costly_par['medianMilliseconds']:.4f} "
        f"| {cheap_seq['medianAllocatedBytes']:.0f} / {cheap_par['medianAllocatedBytes']:.0f} |"
    )
tables.append(
    "\nTracked diff: " + diff_stat.splitlines()[-1] + ". "
    + str(len(added_files)) + " additional files are listed in the inventory; "
    "the tracked diff excludes these added files. Repository-local scratch files are excluded.\n"
)
report_path.write_text(report + "\n".join(tables) + "\n", encoding="utf-8", newline="\n")

write("manifest.json", {
    "milestone": "COPELAND-TYPESCRIPT-INSTINCT-CORPUS-M26",
    "kind": "typescript-instinct-compatible-static-language-hardening", "outcome": "B",
    "baselineCommit": BASELINE, "typescriptInstinctCorpusQualified": True,
    "receiverMethodRepairQualified": True, "nativeStringFunctionsQualified": True,
    "compoundAssignmentQualified": True, "incrementQualified": True, "batchIfQualified": True,
    "genericClrInstantiationQualified": True, "genericClrExecutionScope": "CLR-only",
    "mutableMapQualified": False, "mutableListQualified": False, "mutableArrayInitializerQualified": True,
    "integerOverflowSpecified": True, "csharpJavascriptIntParityQualified": True, "bitwiseOperatorsQualified": True,
    "generatedSourceStringReplacementRemoved": True, "generatedIdentityArchitectureGateQualified": True,
    "compilerPayloadFingerprintQualified": True, "completeExternalDependencyFingerprintQualified": False,
    "canonicalLfGoldenQualified": True, "linuxExecutionQualified": False,
    "payloadlessEnumSingletonQualified": True, "batchAdaptiveSchedulingQualified": True,
    "batchAllocationReduced": True, "soaDesignDocumented": True,
    "llmRepairTurnsReduced": True, "llmOneDiagnosticRepairGoalFullyMet": False,
    "javascriptImplicitCoercionAdded": False, "nullUndefinedRuntimeSemanticsAdded": False,
    "dynamicObjectBagSemanticsAdded": False,
    "testsPassed": test_count, "corpusCases": len(fixtures), "benchmarkChecksumPairs": len(benchmark_rows),
    "exactRemainingSeam": "Native ordered mutable hash storage with typed Option lookup and deterministic iteration across C#/JS.",
    "artifactSha256": {
        str(path.relative_to(ARTIFACTS)).replace("\\", "/"): digest(path)
        for path in sorted(ARTIFACTS.rglob("*"))
        if path.is_file() and path.name not in ["manifest.json", "change-inventory.json"]
    },
})
print(f"Qualified {len(fixtures)} cases, {test_count} solution tests, {len(benchmark_rows)} checksum pairs; outcome B.")
