# HOLLOWFLUX-PEARL-MINING-M20 report

## 1. Outcome

**Outcome B — major pearls landed; the remaining seam is production field ownership.** Combat is on a reusable real path and TinyFarm's existing sword intent now resolves contacts through it. Field2D, reactive waves, connected disturbances, charge, combat-to-field synthesis, gameplay query, native shader compilation, and deterministic inspection artifacts work as bounded proofs. The field is not yet stored in the default TinyFarm session/save envelope or wired into a live Oblivion Notebook surface; claiming Outcome A would overstate that integration.

## 2–5. Inputs, map, cross-check, classification

The complete JS bundle, Claude audit, and CSS were read. SHA-256 values, sizes, exact offsets, and bounded fragments are recorded in `semantic-map.json` and `semantic-anchors.json`. The latter is explicitly a human-authored mapping with machine-verified anchors, not decompiler name recovery. The Claude audit served as the intent-reconstruction map and was independently checked in [hollowflux-claude-audit-crosscheck-m20.md](../research/hollowflux-claude-audit-crosscheck-m20.md). The A–E subsystem map and rejection decisions are in [hollowflux-semantic-map-m20.md](../research/hollowflux-semantic-map-m20.md).

## 6–10. Semantic reconstruction and MIR

Four islands were selected: action phase construction/advancement, weapon move records, connected disturbances, and the disturbance falloff kernel. Minifier names were replaced before import. A bounded semantic Copeland program uses records, enums, numeric expressions, conditionals, and match; ordinary `CopelandCompiler.CompileToMir`, `MirTextWriter`, and `CSharpBackend` produce real MIR and C#. No MIR extension and no JavaScript-specific MIR were needed.

For these islands, semantic recovery is better than source reconstruction: browser ownership, bundler boundaries, and minifier symbols are irrelevant, while stable move fields and transition behavior are directly representable. The exact extracted `Ba` function is executed with Node for the 3/5/10 sword fixture and compared to the Aurelian result: both complete in 18 ticks with startup → active → recovery → complete. The complete source, MIR, lowered C#, diagnostics, original-JS trace, and parity result are in `cope-mir-recovery-proof.json` and `combat-parity.json`.

## 11–16. Combat

The prior TinyFarm attack was an atomic, target-specific one-damage reduction guarded by ownership, scene, lifecycle, and proximity. Hollowflux contributed explicit move data, phase timing, hit-once tracking, shape semantics, impulse, and presentation facts. `Aurelian.Combat` now owns those reusable mechanisms. It does not mutate world state.

Phase boundaries are not a second state-machine stack. They are an immutable `Dominatus.OptFlow.Transition.For` definition with three typed rules and stable inspection identities. Tick accumulation stays ordinary combat data. Contacts are only proposed during Active; targets are stable-sorted; per-action IDs prevent repeat hits unless a move opts in. Arc, line, ring, and point are bounded geometry, while displacement remains an impulse proposal for the existing world owner to validate.

At M20, TinyFarm's atomic `AttackIntent` path retained its prior validation but spun the generic sword move to completion inside one resolution. That proved adapter compatibility, not observable phased gameplay: the reusable mechanism had landed, but its timing pearl was still on the shelf. M21 removes that spin-loop and advances the default hosted combat action one tick at a time through the existing scheduler. Direct resolver use remains an explicitly atomic safe-boundary operation. Spear thrust and hammer smash provide the two additional profiles. Twin strike and sweeping hoe are fresh-context, data-only extensions with no resolver or Spatial2D rewrite.

## 17–28. Field2D and synthesis

`Field2D<T>` is a bounded flat typed array with cell/world transform, nearest sampling, spans, and explicit snapshots. `ReactiveFluid2D` composes columnar channels rather than a large cell object: height, velocity, foam, charge, flow X/Y, liquid mask, and conductive mask. This is the measured minimum; blood, memory, emission, vorticity, and theme behavior were rejected from the generic substrate.

The field advances at 60 Hz with at most six catch-up steps. Its wave kernel uses the useful bank law: a dry neighbor contributes the center height, reflecting rather than draining the wave. Resident next buffers avoid per-step allocation. Point, line, arc, and ring disturbances traverse only connected liquid using a reusable queue and visit-generation array. Charge traverses only conductive liquid, diffuses locally, decays, and is queryable.

The TinyFarm synthesis fixture uses a hammer contact to create a semantic ring disturbance and energize a pond. The field's charge is then queried as a one-point gameplay damage decision. This proves the field is not shader decoration, but it remains a bounded TinyFarm consumer rather than default-session state—the precise Outcome B seam.

Persistence uses an explicit `ReactiveFluidSnapshot` for the bounded proof. For a large deterministic authored world, the recommended production boundary is regenerated masks/base currents plus sparse semantic deltas; active combat action, equipment, and any non-reconstructable channel values must be explicit versioned state. M20 does not add generic serializer magic.

## 29–36. Graphics, presentation, optional pearls, inspection

The adopted shader pearl is liquid-mask-safe reconstruction: continuous channel deviation is divided by a lower-bounded wet coverage, while the wet mask remains authoritative. It was re-authored in `samples/Aurelian/HollowfluxFieldM20.v.ts`, lowered through VD-MIR to HLSL, compiled to SPIR-V, and validated. The RGBA choice—height, foam, wet mask, charge—is realization metadata, not a public field API.

Browser WebGL plumbing, duplicated renderer classes, exact caustic style, and Canvas character paint code were rejected. Equipment-driven composition maps to existing Copeland.Profile semantic layers and did not justify a second asset system. Loot and procedural-audio recipe layers were audited and deferred. The confirmed blood-scent gradient was also triaged: it is a strong future field-to-AI feedback pearl, but adding a Blood channel without a TinyFarm production mechanic would violate the channel-pressure gate, so it is explicitly deferred rather than silently omitted.

The evidence tool emits field and combat inspector PNGs with machine-readable JSON. These make channels, phase bands, and contacts directly inspectable. Actual Oblivion Notebook registration is deliberately marked unqualified and belongs with production field ownership.

## 37. Performance

`performance.json` records elapsed time and thread allocations for combat with 1, 32, and 256 candidates and for 16×16 field update, disturbance, charge, and RGBA projection batches. Measurements are generated on the current machine and are evidence, not universal benchmarks. The field update itself uses resident buffers; projections intentionally allocate an output payload.

## 38–39. Rejections and compounding benefit

Rejected mechanisms include the browser renderer, DOM/mobile glue, exact art and content, giant theme cell, exact loot/balance tables, renderer fork, general JS frontend, general RPG framework, material graph, compute fluid, animation graph, and audio DAW. The retained stack raises the next application from “invent phase/contact/field semantics” to “supply typed move data, disturbances, and application-owned consequences.”

## 40. Exact next milestone recommendation

Productionize one TinyFarm pond in the default native presenter: add a versioned session-owned field snapshot/regeneration recipe, advance it from the existing simulation cadence, route accepted combat events into disturbances, apply one resolver-owned charged-water consequence, upload the existing RGBA projection, and register the two compact inspectors in Oblivion. Do not add new channels until that path creates pressure.

## 41. Diff and validation

The working change contains 42 files: 5 tracked files modified and 37 new files, including 5 PNG evidence images and 4,271 lines across text/code/generated JSON. Required machine-readable artifacts live under `artifacts/hollowflux-pearl-mining-m20/`. Optional equipment proof is absent and explicitly unqualified.

Validation on 2026-09-06:

- `dotnet test TinyFarm.slnx -v:minimal`: 27 Spatial2D tests and 315 TinyFarm tests passed.
- `dotnet build Aurelian.slnx -v:minimal`: succeeded with zero warnings and zero errors.
- `dotnet test Aurelian.slnx --no-restore -v:minimal`: all 795 tests passed.
- Mining tool rerun: complete inputs read; Cope MIR/C# emission, Node parity, Visual TS/VD-MIR/HLSL/SPIR-V validation, deterministic hashes, JSON, and PNG generation succeeded.
- `git diff --check`: clean apart from Git's informational CRLF normalization warning for `TinyFarm.slnx`.
