# Oblivion artifact usability — M19b

## Outcome

**Outcome A — artifact layer is usable and actionable.**

The repository-owned `machina-sample` exposes seven artifact declarations. Their machine-facing addresses are unambiguous even though three local IDs repeat across cards. Two payloads exist, two safe references are currently missing, and three code placeholders intentionally have no filesystem reference. The observed kinds are `code`, `json`, and `png`.

## Real workflow

The code-first trial used the real workspace manifest artifact:

```text
inspect workspace
→ list seven resolved artifacts
→ show cards/oblivion-artifact-placeholder-card/workspace-manifest
→ verify application/json, existing file, absolute safe path, and byte length
→ inspect declaration provenance at artifacts/workspace-manifest.artifact.toml
→ invoke open/copy through typed fake-host integration tests and copy through the real Windows host
→ edit/reload/validate through the existing source workflow
→ re-inspect stable JSON
```

The artifact can now answer what it is, who owns it, where its declaration came from, where its payload resolves, whether it exists, its low-cost payload metadata, its unambiguous address, and which local action is applicable. No implementation-source reading is required.

## LLM and human impact

The LLM surface gains explicit addresses, path/existence state, provenance, typed action discovery, and deterministic recovery diagnostics. The only routine friction is that local open changes external application state and copy changes clipboard state; headless callers should inject or omit those capabilities deliberately.

The human card/page/inspector model remains intact. M19b changes source targeting from the card TOML to the referenced body when a body exists and enables the three now-implemented actions. It does not redesign cards or add a second artifact model for agents.

## Rendering pressure

| Current kind | Semantic inspection | External open | Native inline pressure | Disposition |
|---|---|---|---|---|
| `json` | complete | supported | none observed | `OPEN_EXTERNALLY` |
| `png` | complete even when missing | supported when present | useful later, not blocking | `INLINE_LATER` |
| `code` placeholder | declaration-only | rejected until referenced | none until real payloads exist | `NO_CURRENT_PRESSURE` |

No current artifact requires a chart, video player, image editor, data grid, tree grid, or rich document viewer. The correct next presentation work should be driven by real existing image/document artifacts rather than placeholder kinds.

## Remaining friction and next milestone

- The sample PNG references are intentionally missing, so external open correctly rejects them.
- Code artifacts without references are semantic placeholders rather than actionable payloads.
- The visual inspector does not yet display resolved byte size/existence badges because resolution is App-owned and no UI need justified widening the lower-level UI contract.
- The standalone system clipboard adapter is Windows-specific; unsupported hosts get a typed unavailable result.

Recommended next milestone: **M19c — Existing Image and Document Artifact Presentation Pressure**. Use repository-owned existing PNG/Markdown/JSON/PDF artifacts, add only low-risk read-only preview affordances that actual use proves valuable, and preserve external-open fallback. Do not add generation, execution, editing, or a generic widget framework.
