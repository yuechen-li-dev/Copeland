# Oblivion local host capabilities — M19b

## Contract

The existing `OpenSourceEffectRequest`, `CopySourcePathEffectRequest`, and `OpenArtifactEffectRequest` remain product action requests. `OblivionProductSurface` resolves their targets and adapts them to two small host operations:

- `OblivionOpenPathCapabilityRequest` carries request/workspace/page/card/action/effect correlation, target kind, declared reference, safe resolved path, and optional artifact address.
- `OblivionCopyTextCapabilityRequest` carries the same correlation plus text and semantic kind.

The host returns `OblivionHostCapabilityResult`; App converts it into the existing typed completed or rejected effect result. Presenter meaning does not leak into the contract, and the host never receives an unresolved path.

## Action semantics

`open-source` resolves the referenced Markdown body when present, otherwise the card declaration source. It rejects missing, absolute, escaping, and nonexistent targets before calling the host.

`copy-source-path` copies the resolved absolute source path. This is deliberately distinct from the authored relative reference exposed by inspection. The semantic kind is `resolved-source-path`, so future hosts do not infer which form was requested.

`open-artifact` accepts the local artifact ID in addition to the card. A single-artifact card may omit it. A multi-artifact card must supply it; App rejects omission rather than opening the first declaration. Resolution, safety, existence, and file state are checked before the host call.

## Platform and headless behavior

`OblivionSystemHostCapabilities` is the standalone local adapter. It opens a path with the platform shell and, on Windows, writes text through the native `clip.exe` clipboard utility. Unit tests inject in-memory delegates and perform no OS automation. An App surface with no local adapter returns `OBLIVION-HOST-CAPABILITY-UNAVAILABLE`; platform failures return explicit `OBLIVION-HOST-OPEN-FAILED` or `OBLIVION-HOST-COPY-FAILED` diagnostics instead of hanging.

All result diagnostics are correlated to workspace, page, card, action, effect, artifact when supplied, and source path when available.

## Security boundary

Resolution is App-owned and uses the loaded workspace root. The host receives only a validated existing file. Absolute source/artifact declarations, traversal, missing files, and directories passed to file-open actions are rejected before platform invocation.

## Non-goals

There is no universal execute operation, service locator, process execution product feature, OS input automation, network endpoint, or clipboard assertion against the real desktop.
