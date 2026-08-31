# Oblivion Mermaid derived artifacts — M19e

## Durable and derived truth

Durable truth remains the Mermaid source stored in the card body. PNGs, renderer facts, source hashes, cache keys, diagnostics, and provenance are derived and can be deleted without changing the workspace schema.

Workspace loading and presenter selection do not initialize Mermaid CLI. Headless `oblivion show <card>` exposes the full source plus `diagramSourceHash`, renderer identity/version/status, cache key, and a cached path when present. This works when the renderer is absent.

## Source hashing

`OblivionMermaidHashing` canonicalizes CRLF and bare CR to LF, then hashes the UTF-8 bytes without a BOM using SHA-256. No other whitespace is changed. This prevents Windows newline conversion from creating a false miss while preserving every semantically relevant source character.

For the M19 briefing source:

```text
15fd2a94bdbe107e2e87931352871e246f50a75d0b794950574aea892dbeaea6
```

## Key and location

`MermaidDerivedArtifactKey` contains `SourceHash`, `RendererId`, `RendererVersion`, `OutputFormat`, and `RenderingOptions`. Its `Value` is SHA-256 over those ordered, LF-delimited fields. It contains no timestamp, machine name, absolute path, PID, card ID, or random value. The fixed options are `theme=default;background=white;securityLevel=strict`.

The cache is:

```text
artifacts/derived/mermaid/<cache-key>.png
artifacts/derived/mermaid/<cache-key>.json
```

It is inspectable, easy to clear, outside durable workspace source, and shared by identical render contracts.

## Metadata and provenance

The versioned sidecar stores the typed key plus source kind/hash, renderer ID/version, render operation, output format, producer package/version, workspace/page/card/content owner, source reference, and `derived=true`.

The application validates sidecar presence and deserialization, exact key/provenance match, artifact presence, and PNG readability before reuse. File existence alone is insufficient. Metadata is written through a temporary file and atomically moved after the PNG has passed validation.

## Reuse and invalidation

The cold M19 briefing render invoked the qualified CLI and wrote the PNG and sidecar. An immediate second request with the same source and renderer returned `CacheHit=true` without invoking the render process. Fake-process tests assert one version call and one render call across those two requests.

Changing the source produced:

```text
sourceHash=20284e56c79a114d81fa3101f3221c5b74ea9a6d54007eb4b3f544c1c86597db
cacheKey=6484c2d45992e0dd3b434a74aebcf4af0685fa2b8483e9bd5714def6490596eb
cacheHit=false
```

Focused tests separately prove a renderer-version change and output-format change produce different keys. Missing artifacts, corrupt metadata, and malformed PNGs are diagnosed and rebuilt. No general cache framework was introduced.

## Failure and headless behavior

Every failure result retains source hash and provenance. The semantic presentation plan still contains the Mermaid source, and the Avalonia fallback shows it. `OblivionProductSurface.ShowCard` computes inspection facts without invoking a process, so product inspection remains operational with neither Mermaid CLI nor Avalonia initialized. Rendering is required only by explicit visual realization.
