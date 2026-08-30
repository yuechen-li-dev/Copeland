# Oblivion artifact resolution — M19b

## Outcome and identity

M19b establishes an application-owned artifact resolution layer without changing format-1 persistence. An authored `OblivionArtifactId` is local to its owning card. The unambiguous runtime identity is the typed `OblivionArtifactAddress`:

```text
WorkspaceId + PageId + CardId + ArtifactId
```

The JSON form is an object with `workspaceId`, `pageId`, `cardId`, and `artifactId`; string formatting is presentation only. Page participates because it is part of materialized ownership. Workspace participates so addresses remain meaningful when multiple workspaces are inspected. Duplicate local IDs on different cards are valid and resolve to different addresses. Multiple matching declarations for the same card/local ID are rejected with `OBLIVION-ARTIFACT-ID-AMBIGUOUS`; missing owners or declarations use `OBLIVION-ARTIFACT-NOT-FOUND`. Generated cards use the same rule.

## Durable declaration versus resolved state

`OblivionCardArtifact` remains durable product truth: typed local ID, label, semantic kind, declared reference, generated flag, and optional declaration source reference. Its compatibility string constructor and the JSON/TOML readers preserve existing persistence behavior.

`OblivionResolvedArtifact` is derived in `Oblivion.App`. It contains the address, declaration fields, resolved absolute path when safe, existence, file/directory state, normalized extension, byte length for files, deterministic media type, composed provenance, and resolution diagnostics. No absolute path, filesystem metadata, or media inference is written back to persistence.

## Path policy and diagnostics

Artifact references are resolved from the loaded workspace root. Relative nested paths are accepted. Absolute references and paths that escape the root are rejected rather than normalized into an allowed target. Missing paths remain inspectable with `Exists=false` and a warning. A directory is distinguished from a file and has no byte length. An absent reference is semantic-only and receives an informational diagnostic.

Current codes are:

- `OBLIVION-ARTIFACT-PATH-UNSAFE`
- `OBLIVION-ARTIFACT-NOT-FOUND`
- `OBLIVION-ARTIFACT-NOT-A-FILE`
- `OBLIVION-ARTIFACT-REFERENCE-MISSING`
- `OBLIVION-ARTIFACT-ID-AMBIGUOUS`
- `OBLIVION-ARTIFACT-OWNER-NOT-FOUND`

Media type uses a small extension table. Markdown, text/code/TOML/log, JSON, common images, PDF, common audio, and common video have deterministic values. Unknown extensions use `application/octet-stream`; no extension remains unknown. Semantic artifact `Kind` is preserved separately.

## Provenance

Resolution preserves the artifact declaration source when an artifact TOML asset exists, plus the owner card's source kind, producer action, parent artifact, and parent card. An artifact marked generated reports generated source kind even when its owner is authored. Generated-card provenance composes without a parallel provenance model.

## Product surface

`artifacts [card-id]` lists resolved artifacts in workspace order. `artifact show <card-id> <artifact-id>` returns one resolved object. Card inspection embeds the same resolved projection. Human text includes the full scoped address and state; JSON includes the typed address object and stable fields rather than implementation objects.

## Non-goals

M19b adds no hashing, content-addressable storage, blob store, payload loading, content sniffing, generation runtime, rich viewer, editor, execution, networking, or generic provider framework.
