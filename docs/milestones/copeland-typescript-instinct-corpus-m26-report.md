# COPELAND-TYPESCRIPT-INSTINCT-CORPUS-M26

**Outcome B — major authoring and semantic improvements; native ordered maps remain missing.** The compiler now handles common increment/compound syntax, native string associated calls, closed CLR generic construction, bounded batch branches, and explicit int32 arithmetic across C# and JavaScript. Rejected corpus instincts receive one useful primary diagnostic at an authored span. A CLR dictionary makes the map kernel expressible, but does not establish native ordered-map/Option semantics.

The real motivating cases improve: all 68 instinct fixtures qualify, all six fresh-context dogfood repairs execute, and all ten benchmark twin pairs have matching checksums. The full `Copeland.slnx` suite passes **1,960 tests across eight assemblies**, with no failures or skips. The solution build has no warnings or errors. No Linux process was executed.

## Decisions and qualification

| Required report topic | Result and evidence |
| --- | --- |
| 1. Outcome | B. The native map/storage authority is absent; CLR interop provides a functional alternative. |
| 2. Source findings | The Claude account and original report identify receiver/category friction, constructor/mutation/batch restrictions, host integer ambiguity, and codegen rewrites. The actual supplied harness was rerun. See [baseline research](../research/copeland-typescript-instinct-baseline-m26.md). |
| 3. TypeScript instinct law | Accept defined Copeland semantics, give one primary exact repair, or state a missing capability and its closest supported alternative. |
| 4. Principle decision table | [principle-decisions.json](../../artifacts/copeland-typescript-instinct-m26/principle-decisions.json) records retained differences and removed friction. Typed strict equality aliases, increments, and CLR constructors buy familiarity without coercion. |
| 5. Corpus design | 68 source fixtures: 48 execute, 18 require repair, 2 state unsupported capability. Execution uses checked optimized C# and both JavaScript Production/Symbolic profiles; CLR-only cases explicitly exclude JS. Rejections assert exactly one code, authored path/offset/length, and repair text. |
| 6. Receiver diagnostics | `COPE-CALL-0021` includes type, authored method, and exact associated call, e.g. `String.Split(text, ",")`; receiver binding no longer misclassifies these as enums. One-argument receiver slice includes the receiver length as end. |
| 7. Native strings | Typed MIR Split/IndexOf/CodeAt/Slice/Join, ordinal UTF-16 rules, empty delimiters/fields, negative slicing, and bound traps. No arbitrary CLR string API exposure. |
| 8. String benchmark | Native and original detour variants have equal checksums. Native allocation falls about 10%; native elapsed time is worse in this run. No speedup claim. |
| 9. Increment/compound assignment | Named locals support prefix/postfix increment/decrement and arithmetic compounds; postfix old value is explicit in MIR. Computed receivers/indices require explicit single evaluation; records remain immutable. |
| 10. Batch structured branch | Final one-return if/else and guard/fallback normalize to the existing expression conditional. Pure bounded prefix branches use ordinary binding. Loops/general early returns remain guided restrictions. |
| 11. Expression conditional | Existing `if (condition) { value } else { otherValue }` reused; no ternary or second batch language added. |
| 12. Generic CLR instantiation | Imported closed `List<int>` and `Dictionary<string,int>` construct and execute through reflected CLR authority. Generic signatures, indexer accessors, inferred constructor locals, and malformed argument diagnostics have corpus coverage. |
| 13. new policy | `new List<int>()` is accepted CLR constructor sugar. The existing constructor-call spelling `List<int>()` also executes. No native object/prototype model is added. |
| 14. MutableMap design | Not implemented. There is no shared native hash/ordering/key equality owner or Option lookup operation. `COPE-COLLECTION-0001` gives the CLR alternative and names the portable gap once. |
| 15. Map benchmark | Same insert/read/update/remove algorithm now runs through CLR Dictionary versus JS Map with checksum parity. CLR container policy is explicitly not a native backend map contract. |
| 16. MutableArray initializer | `MutableArray<T>(n, initializer)` initializes once and fills immutable scalar/string/record/enum values. Negative length traps; freeze/copy policy retained. |
| 17. MutableList | Native list is unqualified. Fixed MutableArray or CLR List/Add/ToArray are supported alternatives. The dogfood dynamic-list case executes through the latter. |
| 18. Static member chaining | Resolve receiver type/static property/value member once. `CultureInfo.InvariantCulture.CompareInfo` executes; no dotted type-name guessing workaround. |
| 19. Unknown names | `Missing.Case(...)` gives the earliest unknown name and suppresses dependent constructor/type noise. |
| 20. Length repair | `COPE-STRING-0001` suggests `.length` at the authored member. |
| 21. Repair metadata | Optional `SuggestedReplacement` / `RepairKind` added without replacing diagnostic contracts. Native map is marked `unsupported-capability`; canonical repairs use `canonical-form`. Not every existing typed error populates optional metadata. |
| 22. Cascade prevention | Error expressions short-circuit dependent unary/binary/member/index/coalesce work; unsupported native map is deduplicated. Invalid typed declarations do not independently rebind their dependent initializer. |
| 23. Source locations | Corpus includes member calls, generic argument restrictions, batch body anchors, exponent literals, and nested template-hole unknown names. Lexer/parser diagnostics retain source path. |
| 24. Integer overflow | Signed int32 wrapping add/subtract/multiply/negation. Division truncates and minimum/-1 wraps; minimum remainder is zero; division/remainder zero trap. Static evaluator follows arithmetic policy. |
| 25. C#/JS parity | Checked-host C# emission uses explicit unchecked operations and long intermediate division; JS uses imul/int32 normalization. Production and Symbolic runtime fixtures cover overflow, negative remainder, shifts, and traps. |
| 26. Bitwise operators | Int-only `&`, `|`, `^`, `<<`, signed `>>`; shift count low five bits. Hex literals denote bounded int32 bit patterns. Float operands get explicit conversion guidance. |
| 27. Wrapping operations | No separate wrapping API needed: ordinary int arithmetic is wrapping. Explicit floor/ceil/round/truncate conversion retains range checks. |
| 28. Source rewrite removal | Module, namespace, carrier scope, exports, and generated owners selected structurally through `CSharpEmissionOptions`. MSBuild and table query identity replacements removed. |
| 29. Architecture gate | Roslyn invocation inspection checks both former rewrite owners. Only exception-message newline cleanup is allowed there. Literal/path escaping, explicit C# block newline normalization, and pre-emission identity encoding remain justified nonsemantic transforms. Custom identity and query results execute. |
| 30. Fingerprint audit | Task/compiler/C# backend/MIR name/version and bytes included; C# policy options explicitly included alongside source graph, authored C#, and contracts. Real equal-name/version/size/time different-byte assemblies change the production hash. External CLR references retain metadata identity; TSON asset content cache invalidation remains a separate gap. End-to-end binary repack/rebuild was not executed. |
| 31. Linux golden fix | MIR text owner canonicalizes LF; table and CLI byte/hash pins use the same LF expectation on every platform. Windows tests pass. Live Linux execution remains unqualified. |
| 32. Payload-less enums | C# case singleton is reused by actual repeated construction; Production JS already caches such cases. Source equality domains remain unchanged; no Symbolic allocation optimization claim. Golden updates are intentional. |
| 33. Array codegen | Int index/length paths omit redundant checked casts; real conversions retain checks. Arrays benchmark rerun succeeds, but no isolated JIT bounds-elimination gain is claimed. Existing bounds policy is preserved. |
| 34. Adaptive batch scheduling | Small inputs sequential, larger inputs Parallel.For, deterministic output order and lowest-index failure. Default 128 is a measured conservative count-only compromise, not body-cost optimization. |
| 35. Batch memory | Failure dictionary allocated only after a failure. At 8 items, current sequential median is 248 bytes versus baseline 3,344; at 32 items, 344 versus 3,888. Reflection harness overhead is included; baseline has one warmed trial per size. |
| 36. SoA documentation | [Language policy](../Copeland/language/typescript-instinct-m26.md) distinguishes scalar columns/record tables, row arrays, and CLR container costs. Do not force row materialization in column hot loops. Immutable record initializers are allowed without blanket warnings. |
| 37. Option/nullish behavior | Existing typed Option `??` and supported `?.` laws retained. Non-Option fallback has a direct typed diagnostic; null/undefined acquire no runtime values. |
| 38. Record/object behavior | Closed inferred/contextual nominal values remain immutable and closed; mutation gets with guidance. Object literals do not become dictionaries. |
| 39. type/interface/record | Preserve erased alias/shape, capability constraint, and nominal runtime identity split. No new structural runtime object engine. |
| 40. any/null/undefined | Direct profile diagnostics suggest concrete/nominal/constrained types or Option construction; dependent initializer errors are suppressed. |
| 41. Array push/map guidance | Fixed MutableArray/freeze or CLR List for growth; batch/pure helper for array map; CLR Dictionary for native-map gap. |
| 42. LLM dogfood | Initial author and two separate diagnostic-only repair contexts read no language docs/compiler source. Original inputs and every failed source/version retained. After execution: 6/6; three portable, three CLR-only. CSV only qualifies complete decimal fields, not arbitrary parseInt semantics. |
| 43. Repair turns | Completed common subset (loop, batch, hash): 8 baseline turns versus 1 after. Whole replay: baseline 18 turns with 3 blocked; after 10 LLM attempts plus 1 compiler-fix retry, 6 executed. Totals have different completion rates; successful verification reruns excluded. |
| 44. Benchmark rerun | Eight original kernels plus native strings and maps: ten same-algorithm/data-layout pairs, three full-size trials per isolated process, all checksums equal. Raw allocations/GC/process peaks retained. |
| 45. Unsupported instincts | Native ordered map and native growable list; arbitrary CLR generics/nested owners and JS CLR execution; general batch loops/early returns; computed compound mutation; optional string overloads/regex splitting; universal one-diagnostic CSV parsing. No unrelated TypeScript feature checklist work. |
| 46. Exact M27 | Add shared typed native map operations/storage with scalar key equality/hashing, insertion-order iteration, remove/reinsert rules, Option lookup, and C#/JS adversarial parity. Add native-map same-algorithm benchmark. Separately use the existing asset resolver's dependency closure for TSON cache fingerprints; do not create a regex asset scanner. |
| 47. Diff stat | [change-inventory.json](../../artifacts/copeland-typescript-instinct-m26/change-inventory.json) records tracked diff and added paths. Changes are local to Copeland: compiler/MIR/backends/MSBuild, tests/goldens, benchmark sample, proof tools, docs, and evidence. No commit or push performed. |

## Reproduction and interpretation

```powershell
dotnet build Copeland.slnx -m:1
dotnet test Copeland.slnx -m:1
dotnet build tools/copeland-typescript-instinct-m26/InstinctProof.csproj -c Release -m:1
dotnet tools/copeland-typescript-instinct-m26/bin/Release/net10.0/InstinctProof.dll tests/Copeland/Copeland.TS.Tests/TypeScriptInstinctCorpus/instinct-corpus.json artifacts/copeland-typescript-instinct-m26/instinct-corpus.json
```

The [benchmark sample](../../samples/copeland-ts/typescript-instinct-m26-benchmark/README.md) provides the Release task-build and rerun commands. The [proof tool](../../tools/copeland-typescript-instinct-m26/README.md) describes runtime measurements, baseline recreation, dogfood transcripts, and summary generation. [manifest.json](../../artifacts/copeland-typescript-instinct-m26/manifest.json) states qualified and unqualified flags and hashes recorded artifacts.

This is a single Windows host with 16 logical processors, SDK 10.0.401, and Node 26.2.0. Kernel timing uses best of three without a separate untimed warmup; CLR allocation is total GC allocation, while Node heap delta is retained memory change and can be negative. Peak working set includes process startup and all trials. Node GC counts are unavailable. The objects twin uses row arrays, so it does not establish columnar superiority. The benchmark does not rank whole languages or application concurrency.

Automatic approval review rejected removal of task-created repository-local scratch files with the stated reason `blocked by policy`. They remain under `.tmp`; no user files were removed. Scratch paths are excluded from the change inventory and delivered artifact hashes.

Native strings improve expressibility and allocation in this matched kernel, not elapsed time. CLR maps improve expressibility without specifying native map semantics. Cheap batch bodies still favor sequential execution at large sizes; the count threshold is conservative and workload-dependent. Arrays codegen is simpler, but this aggregate rerun does not isolate a JIT speedup. These limits are part of the qualification, rather than deferred success claims.

<!-- M26 measured tables -->

## Recorded measurements

| Kernel | CLR best ms | Node best ms | CLR allocated MiB | Peak CLR / Node MiB | Checksum |
| --- | ---: | ---: | ---: | ---: | --- |
| fib | 46.37 | 91.05 | 0.00 | 24.72 / 38.77 | 13584083 |
| nbody | 503.84 | 694.84 | 0.00 | 26.53 / 41.94 | -0.169077842 |
| trees | 600.90 | 615.59 | 2053.33 | 84.05 / 157.37 | 67283631 |
| sieve | 162.15 | 214.44 | 95.37 | 82.27 / 136.08 | 1270607 |
| arrays | 439.98 | 694.76 | 51.00 | 44.45 / 113.03 | 336296378656 |
| strings | 239.61 | 130.73 | 344.46 | 74.55 / 167.30 | 21777520 |
| objects | 48.16 | 172.23 | 459.53 | 49.91 / 266.76 | 749992500000 |
| closures | 237.84 | 238.20 | 0.00 | 24.89 / 39.29 | 997753 |
| native-strings | 361.33 | 128.05 | 308.90 | 87.46 / 171.53 | 21777520 |
| maps | 268.98 | 207.31 | 252.02 | 89.37 / 176.89 | -1538607552 |

CLR allocated bytes and peaks describe different quantities. Peaks are process-wide maxima across all trials; Node heap deltas and CLR GC counts remain in [the raw trial JSON](../../artifacts/copeland-typescript-instinct-m26/raw).

| Batch size | Cheap seq / par ms | Costly seq / par ms | Cheap seq / par bytes |
| ---: | ---: | ---: | ---: |
| 1 | 0.0001 / 0.0037 | 0.0020 / 0.0072 | 224 / 1992 |
| 8 | 0.0001 / 0.0024 | 0.0276 / 0.0199 | 248 / 2408 |
| 32 | 0.0003 / 0.0032 | 0.0854 / 0.0415 | 344 / 3192 |
| 128 | 0.0008 / 0.0053 | 0.3281 / 0.0707 | 728 / 4104 |
| 1024 | 0.0099 / 0.0261 | 2.6747 / 0.3440 | 4312 / 7952 |
| 16384 | 0.0806 / 0.0898 | 9.1448 / 0.7045 | 65752 / 71576 |

Tracked diff:  50 files changed, 1130 insertions(+), 475 deletions(-). 103 additional files are listed in the inventory; the tracked diff excludes these added files. Repository-local scratch files are excluded.

