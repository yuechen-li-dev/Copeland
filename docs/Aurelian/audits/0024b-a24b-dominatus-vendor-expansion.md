# A24b Dominatus Vendor Expansion

## 1. Files changed

- Added build-linked vendor modules under `vendor/Dominatus/src/Ariadne.OptFlow/` and `vendor/Dominatus/src/Dominatus.UtilityLite/`.
- Added reference-only sample material under `vendor/Dominatus/samples/Ariadne.Console/`, `vendor/Dominatus/samples/Dominatus.Fishtank/`, `vendor/Dominatus/samples/Dominatus.TinyTown/`, and `vendor/Dominatus/samples/Dominatus.RTSBenchmark/`.
- Added selected upstream reference docs under `vendor/Dominatus/docs/`.
- Updated `Aurelian.slnx` to link the two new build modules only.
- Updated `README.md`, `docs/architecture/vendor-strategy.md`, and `docs/architecture/mvp-roadmap.md` with the A24b vendor boundary.

## 2. Task scope

A24b was a vendor/dependency maintenance milestone before continuing Vulkan work. The scope was limited to:

- vendor `Ariadne.OptFlow` and `Dominatus.UtilityLite` as build-linked modules;
- vendor selected Dominatus/Ariadne samples as reference-only material for Codex authors;
- link only the two build modules in `Aurelian.slnx`;
- avoid production dependencies on samples;
- avoid `CodeReferences/*`, graphics/Vulkan implementation, renderer/windowing, physics, navigation, and architecture changes.

## 3. Modules vendored

The following upstream modules were copied from `https://github.com/yuechen-li-dev/Dominatus` at commit `a21d5ab646632f29d3399d79852bdb22e68a001c`:

```text
vendor/Dominatus/src/Ariadne.OptFlow/
vendor/Dominatus/src/Dominatus.UtilityLite/
```

`Ariadne.OptFlow` references the vendored `Dominatus.Core` and `Dominatus.OptFlow` projects. `Dominatus.UtilityLite` also references the vendored `Dominatus.Core` and `Dominatus.OptFlow` projects. No package-reference additions were needed for these modules.

## 4. Samples vendored as reference-only

The following samples were copied as reference material only:

```text
vendor/Dominatus/samples/Ariadne.Console/
vendor/Dominatus/samples/Dominatus.Fishtank/
vendor/Dominatus/samples/Dominatus.TinyTown/
vendor/Dominatus/samples/Dominatus.RTSBenchmark/
```

The upstream fish sample directory is named `Dominatus.FishTank`; it was copied to the requested Aurelian reference path `vendor/Dominatus/samples/Dominatus.Fishtank/` while preserving the upstream `Dominatus.Fishtank.csproj` project name.

Selected upstream reference docs were also copied under `vendor/Dominatus/docs/`:

```text
vendor/Dominatus/docs/user/AUTHORING_GUIDE.md
vendor/Dominatus/docs/user/ARCHITECTURE.md
vendor/Dominatus/docs/user/ONBOARDING_TEMPLATES.md
vendor/Dominatus/docs/samples/SAMPLE_TINYTOWN.md
vendor/Dominatus/docs/samples/SAMPLE_RTS_BENCHMARK.md
vendor/Dominatus/docs/benchmarks/RTS_BENCHMARK_REPORT.md
```

## 5. Project references and solution linkage

`Aurelian.slnx` now links these Dominatus build modules:

```text
vendor/Dominatus/src/Ariadne.OptFlow/Ariadne.OptFlow.csproj
vendor/Dominatus/src/Dominatus.Core/Dominatus.Core.csproj
vendor/Dominatus/src/Dominatus.OptFlow/Dominatus.OptFlow.csproj
vendor/Dominatus/src/Dominatus.UtilityLite/Dominatus.UtilityLite.csproj
```

The sample projects were deliberately not added to `Aurelian.slnx`.

No Aurelian production project was changed to reference `Ariadne.OptFlow`, `Dominatus.UtilityLite`, or any sample project. The existing `Aurelian.Runtime` reference to `Dominatus.Core` remains unchanged from the earlier runtime smoke boundary.

## 6. Package/reference adjustments

No central package versions were added. The two build-linked modules use only project references to already-vendored Dominatus modules.

The moved `Ariadne.Console` reference sample had its project references adjusted from its original upstream `src/` location to the Aurelian vendor layout so that the sample remains understandable from its new reference-only path:

```text
vendor/Dominatus/samples/Ariadne.Console/ -> vendor/Dominatus/src/Ariadne.OptFlow/
vendor/Dominatus/samples/Ariadne.Console/ -> vendor/Dominatus/src/Dominatus.Core/
```

Other sample projects are kept as copied reference material. Some reference samples may mention upstream modules or packages that are not part of Aurelian's build-linked vendor subset; that does not affect Aurelian build/test because samples are not linked.

## 7. Boundary checks

Boundary checks confirmed:

- `Ariadne.OptFlow` and `Dominatus.UtilityLite` are present under `vendor/Dominatus/src/`.
- `Aurelian.slnx` links only the two new build modules plus the existing Dominatus core modules.
- The selected sample projects are not linked in `Aurelian.slnx`.
- Aurelian production projects do not reference the sample projects.
- No `bin/` or `obj/` files remain under `vendor/Dominatus/` after cleanup.
- `CodeReferences/*` was not modified.

## 8. Validation results

Commands run:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
rg -n "Ariadne.Console|Dominatus.Fishtank|Dominatus.TinyTown|Dominatus.RTSBenchmark" Aurelian.slnx src tests -g '*.slnx' -g '*.csproj' -g '*.cs' || true
rg -n "ProjectReference" Aurelian.slnx vendor/Dominatus/src src tests -g '*.slnx' -g '*.csproj'
find vendor/Dominatus -type f | grep -E '/(bin|obj)/' || true
git status --short
```

`dotnet build` and `dotnet test` both passed in Debug. Boundary checks showed no linked sample projects and no vendor `bin/obj` artifacts after cleanup.

## 9. Deferred follow-ups

- Preferred Aurelian-specific Dominatus authoring style guidance remains a future documentation task if the human provides or requests project-specific guidance.
- Reference samples are intentionally not production dependencies and should not be used to drive architecture changes unless a later scoped milestone explicitly promotes a pattern.
- Some reference samples mention upstream modules outside the A24b build-linked subset; keep them reference-only unless a later vendor milestone deliberately broadens the linked set.

## 10. Next recommendation

Return to **A25 — Timeline fences and resource pool M0** now that the Dominatus/Ariadne vendor expansion is complete and the solution remains green.
