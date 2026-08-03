# MARIONETTE-AURELIAN-M1 provenance

## Chosen authority

The Skyrim-facing Aurelian projects consume the released package identities
`Dominatus.Core` and `Dominatus.OptFlow` at central version `1.0.0`.
Package-only restore/build is the release behavior and is verified by tests.
No `0.4.0` Dominatus central version remains.

Copeland's `reference/dominatus` gitlink was advanced from
`9b43e7912332856e6095d62c530f58049b1b5150` to the exact audited 1.0 release
candidate source commit:

```text
adbecd91cf1e07ca9a53c60a38fbb8356245b076
Fix trusted NuGet package publication
```

The gitlink is provenance/reference source, not a competing build authority.
The Skyrim projects contain package references, not project references. This
also avoids parent central-package-management settings leaking into the pinned
generator project. Package metadata at the pinned commit declares version
`1.0.0` for both Core and OptFlow.

## Verification

- `Directory.Packages.props` pins both package IDs to `1.0.0`;
- the transport project uses both package IDs and the runtime uses Core;
- a compiled test verifies `Dominatus.Core` loads with assembly major/minor
  `1.0`;
- a source test rejects `0.4.0` and rejects a project-reference override in the
  Skyrim transport project;
- generated `SkyrimBodyAgentFlow` compilation proves the packaged OptFlow
  incremental generator is present;
- operation-site tests prove the 1.0 typed operation API is active.

The package was restored for local build verification only. Nothing was
published, tagged, or pushed by M1.
