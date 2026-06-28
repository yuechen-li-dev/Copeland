# Reference Source

This folder contains source references that are useful for development and auditing but are not part of the active build.

## Dominatus

`reference/dominatus` is a Git submodule pointing to the Dominatus repository:

- Remote: `https://github.com/yuechen-li-dev/Dominatus.git`
- Pinned commit: `0d60cba322dfb4e4f5f61c72867d24d4da2fe33d`

Copeland and Machina builds use NuGet packages:

- `Dominatus.Core` `0.4.0`
- `Dominatus.OptFlow` `0.4.0`

The submodule is present only so humans and Codex can inspect source.

Important areas for current Machina work:

- `src/Dominatus.Assets.Toml`
- `src/Dominatus.SpriteForge`

There is no clear `0.4.0` Dominatus source tag in the upstream repository at the time this submodule was added, so the submodule is pinned to a specific `master` commit. NuGet remains the build authority.

Do not add `ProjectReference` entries to the submodule unless intentionally changing the build model.

The active build must continue to work even if the submodule is not initialized.

To initialize after clone:

```powershell
git submodule update --init --recursive
```

To update later:

```powershell
git submodule update --remote reference/dominatus
```


## M8a font atlas audit

`reference/dominatus/src/Dominatus.Assets.Toml` and `reference/dominatus/src/Dominatus.SpriteForge` were audited for Machina font atlas design in `../docs/machina-font-atlas-architecture-m8a.md`. The submodule remains reference-only; active builds continue to use NuGet packages rather than ProjectReferences into `reference/dominatus`.
