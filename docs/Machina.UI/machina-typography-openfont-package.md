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
./tools/Build-MachinaTypographyOpenFontRelease.ps1 -Version 1.0.0
```

To publish, dispatch `Copeland Preview Release` with
`publish_machina_typography_openfont` enabled and the desired package version. A
Machina-only dispatch skips the coordinated Copeland TS build and publishes only the
validated `.nupkg` downloaded from the build job.
