# Machina.Typography.OpenFont package

`Machina.Typography.OpenFont` is Copeland's maintained distribution of LayoutFarm's
`Typography.OpenFont`. Public namespaces remain `Typography.OpenFont`; the assembly
and NuGet identity are distinct so consumers can intentionally select this fork.

The source is pinned to LayoutFarm/Typography commit
`5877180c7c5271091379a0eaf9f03ab6ebd256b3`. The package includes the upstream
`LICENSE.md`, `UPSTREAM.md`, `PATCHES.md`, and package README. This third-party subtree
retains upstream's license rather than Copeland's repository-wide license.

The first downstream correction preserves the requested glyph ID for every empty
TrueType or WOFF2 outline. The old implementation returned a shared glyph-zero object;
metric lookup through that object consequently returned `.notdef` spacing. Package
tests verify every loaded Crimson Text glyph retains its index and that space glyph
556 has the qualified 229-font-unit advance.

Build and pack with:

```powershell
dotnet test tests/Machina.UI/Machina.Typography.OpenFont.Tests/Machina.Typography.OpenFont.Tests.csproj -m:1
dotnet pack src/ThirdParty/Machina.Typography.OpenFont/Machina.Typography.OpenFont.csproj -c Release -o artifacts/machina-typography-openfont
```

Publication uses NuGet trusted publishing through the existing
`.github/workflows/copeland-preview-release.yml` identity and the
`copeland-publication` GitHub environment. `NuGet/login@v1` exchanges the workflow's
OIDC token for a temporary API key; no long-lived NuGet secret is stored in the
repository.

The release-authoritative local/CI command is:

```powershell
./tools/Build-MachinaTypographyOpenFontRelease.ps1 -Version 1.0.1
```

To publish, dispatch `Copeland Preview Release` with
`publish_machina_typography_openfont` enabled and the desired package version. A
Machina-only dispatch skips the coordinated Copeland TS build and publishes only the
validated `.nupkg` downloaded from the build job.

## Maintenance release 1.0.1

The vendored project now enforces warnings-as-errors. Eleven never-assigned fields
from unimplemented bitmap stubs were removed without suppressions or public API
changes. Its internal big-endian reader now fills each numeric field exactly,
handles fragmented streams, rejects truncation with `EndOfStreamException`, and
assembles values independently of host byte order. It keeps the eight-byte buffer;
no new runtime dependencies or target framework requirements were introduced.

Six of the seven new reader regression cases failed before the correction.
The two existing identity tests plus all seven reader cases pass afterward.
SDK [package validation](https://learn.microsoft.com/en-us/dotnet/fundamentals/apicompat/package-validation/overview)
checks public API compatibility against the already-published 1.0.0 package during pack.

The release validator performs a strict rebuild, tests, packing and archive
inspection, then runs a fresh consumer using the actual `.nupkg`. Its font fixture
now comes from the authoritative shared font asset directory rather than an obsolete
test-fixture source path. Read `PATCHES.md` in the package for the exact scope.

### 1.0.1 release evidence (2026-11-16)

- Source commit: `f68e43f9329db72619b8937f3d0cf033b6f4988a`.
- [Machina-only release workflow](https://github.com/yuechen-li-dev/Copeland/actions/runs/43964812192):
  strict rebuild (zero warnings/errors), nine package tests, compatibility validation,
  archive inspection, clean package consumer and trusted NuGet publication all succeeded.
- Downstream current-source tests: Machina.Fonts 311, Aurelian.Machina 53, Valencia 300;
  no failures/skips. RTX 3070 typography readback passed with zero Vulkan validation errors.
- The exact CI archive also parsed a real 718-glyph font on .NET 8 through a stream
  restricted to three-byte reads. Space glyph 556 retained advance 229.
- NuGet returned HTTP 201 Created for the package at 21:56:28 UTC and symbols at
  21:56:29 UTC. At 22:11:34 UTC the public flat-container index still listed only
  1.0.0, and downloading 1.0.1 returned 404. **Upload was accepted; public indexing/
  availability remains unverified.** A public-feed restore has consequently not passed.

The source-commit-matched CI archives (not the earlier local candidates), manifest
and SHA-256 checksums are retained under
`artifacts/releases/machina-typography-openfont/1.0.1/`.
Detailed observations are in `validation/typography-openfont-1.0.1.json`.
The CI workflow's artifact-download action also emits an upstream Node.js deprecation
notice; the zero-warning claim refers to the vendored C# build, not that action.
