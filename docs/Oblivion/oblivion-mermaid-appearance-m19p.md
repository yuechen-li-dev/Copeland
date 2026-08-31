# Theme-qualified Mermaid derived artifacts — M19p

## Resolved appearance

Mermaid appearance is a render-realization input. `OblivionConfig.Appearance` remains application policy, and Standalone resolves it through `OblivionStandaloneAppearanceResolver` before constructing the window. The renderer accepts only `OblivionResolvedAppearance.Light` or `.Dark`; `System` is not part of the renderer contract.

Explicit light and dark config values do not consult the platform. `system` uses Avalonia's resolved platform theme at startup. Headless inspection does not invent a platform signal: it reports the requested config value, reports a resolved value only for explicit light/dark, and inspects both qualified cache variants without initializing Avalonia or invoking Mermaid.

## Qualified renderer configurations

The pinned `@mermaid-js/mermaid-cli@11.16.0` help surface supports `--theme`, `--backgroundColor`, and `--configFile`. M19p uses that smallest stable path:

| Appearance | Mermaid theme | PNG background | Fixed configuration identity |
| --- | --- | --- | --- |
| Light | `default` | `#ffffff` | `m19p-light-v1` |
| Dark | `dark` | `#0f172a` | `m19p-dark-v1` |

Both configurations use the same generated `securityLevel = strict` config file. There are no user-authored theme variables, CSS, per-Card themes, or raw theme strings outside the Mermaid backend. Direct argument lists, the 30-second bound, bounded output, process-tree termination, temporary cleanup, offline repo-local discovery, and exact version qualification are unchanged.

## Cache identity and provenance

The cache key remains SHA-256 over source hash, renderer ID, renderer version, output format, and fixed render options. The fixed render-options identity now includes resolved appearance through its theme, background, strict-security setting, and configuration identity. The same VehicleFlow semantic source therefore produced:

- source hash `a6fbe5ded43ebd92b8278413cbd01c14fa1d89d6c31b15f477b9a50ca5a2f56b`;
- light key `491004140a2cb52f385776e79b7a003afa0ae7bd009032d67b3f93b5d22c483c`;
- dark key `30bb7657baf04af22d020fb6e6e56c18a9882b84701176ea88c70434e9042e39`.

Sidecar format 2 records `ResolvedAppearance` as `light` or `dark` plus `RenderOptionsIdentity`, source hash, renderer/version, producer, workspace/Page/Card/content ownership, source reference, and `Derived = true`. The two artifacts answer the provenance question directly: the semantic source is identical and the qualified render appearance differs.

## Cache behavior and backward policy

Fake-runner qualification proves light miss/hit and dark miss/hit with exactly two renderer invocations across four requests. Repeated appearance requests preserve their key and artifact path; changing only appearance preserves source hash while changing key, path, and provenance.

Appearance-unqualified M19e/M19o entries are not trusted. Their old rendering-options identity and sidecar format 1 cannot match a format-2 appearance-qualified request, so the new renderer ignores and rerenders them. Old derived files may remain safely; M19p adds no automatic cache garbage collection.

## Host integration

Standalone resolves appearance once at startup and passes the same typed value to both the Card palette and `AvaloniaOblivionContentHost`. The host puts that value on `OblivionDiagramRenderRequest`, and the Mermaid backend selects the matching derived artifact. No Card or Diagram semantic record is mutated or duplicated. Restart-based light/dark changes are sufficient; runtime hot switching is not implemented.

`card show` retains semantic source, projection, fingerprint, and renderer status. It now also reports cached render appearances plus requested/resolved appearance when deterministically available. Product headless snapshots expose both light/dark cache identities without rendering.

## Boundaries and non-goals

Diagram IR, `DiagramNode`, `DiagramEdge`, the VehicleFlow source, and Card order are unchanged. M19p adds no diagram kind, native SVG, graph layout, theme framework, custom theme JSON, Settings UI, WebView, browser renderer, or runtime reflection.

