# Oblivion Mermaid renderer qualification — M19e

## Outcome

M19e qualifies one production renderer: the official `@mermaid-js/mermaid-cli` package, pinned exactly at `11.16.0`. The repo-owned install lives at `tools/mermaid`; `package.json` and `package-lock.json` are durable inputs while `node_modules` and Puppeteer's local browser cache remain installed tooling, not source.

The primary output is PNG. This deliberately reuses the M19d Avalonia PNG decode and `Stretch.Uniform` fit path rather than introducing an SVG host dependency.

## Tool contract

| Field | Qualified value |
| --- | --- |
| Renderer ID | `mermaid-cli` |
| Package | `@mermaid-js/mermaid-cli` |
| Pinned version | `11.16.0` |
| Durable install inputs | `tools/mermaid/package.json`, `tools/mermaid/package-lock.json` |
| Repo-local CLI entry | `tools/mermaid/node_modules/@mermaid-js/mermaid-cli/src/cli.js` |
| Runtime | Node.js plus the Puppeteer-compatible local Chrome downloaded by package setup |
| Input | canonical UTF-8 Mermaid `.mmd` file, no BOM |
| Output | PNG |
| Timeout | 30 seconds per render; 10 seconds for version qualification |

One setup command is required from the repository root:

```powershell
npm ci --prefix tools/mermaid --no-audit --no-fund
```

The application never installs or downloads software at runtime. Once setup has completed, rendering has no network dependency.

## Discovery and qualification

Lookup order is deliberately short:

1. `OBLIVION_MERMAID_CLI`, interpreted as an explicit path;
2. the repo-owned pinned CLI entry under `tools/mermaid/node_modules`;
3. otherwise `OBLIVION-MERMAID-RENDERER-UNAVAILABLE`.

There is no arbitrary `PATH` search and no `npx` fallback. A JavaScript CLI entry is launched through `OBLIVION_NODE_EXE` when explicitly set, otherwise the known Windows Program Files Node location. The Presenter creates and retains one renderer instance, so successful version qualification is performed once per live host.

Before any cache lookup or render, the adapter invokes `--version` and requires exact output `11.16.0`. The configured path is not trusted as identity. Any other output, timeout, start error, or nonzero exit produces `OBLIVION-MERMAID-RENDERER-VERSION-MISMATCH`; an unqualified renderer never consumes an old cache entry.

## Invocation and safety

The process uses `ProcessStartInfo.ArgumentList`, `UseShellExecute = false`, a known working directory, captured stdout/stderr, bounded capture, explicit exit-code checks, and process-tree termination on timeout. Mermaid source crosses the process boundary only through a temporary `.mmd` file. The temporary directory is removed in `finally`.

Fixed rendering options are default theme, white background, PNG output, and Mermaid `securityLevel=strict`. The adapter supplies a generated config file; source is never interpolated into arguments or a shell command. No remote API, browser fallback stack, local-file inclusion option, or network renderer exists.

## Failure contract

The typed diagnostics are:

- `OBLIVION-MERMAID-RENDERER-UNAVAILABLE`
- `OBLIVION-MERMAID-RENDERER-VERSION-MISMATCH`
- `OBLIVION-MERMAID-RENDER-TIMEOUT`
- `OBLIVION-MERMAID-RENDER-FAILED`
- `OBLIVION-MERMAID-OUTPUT-MISSING`
- `OBLIVION-MERMAID-OUTPUT-INVALID`
- `OBLIVION-MERMAID-CACHE-INVALID`

Diagnostics retain the source reference and include available workspace, page, card, and content IDs. Captured process text is bounded to 8,192 characters. Failure returns the canonical source hash and provenance and the Avalonia host shows the retained Mermaid source below the diagnostic.

## Real qualification

The nontrivial M19c architecture diagram rendered through the real adapter with:

```text
renderer=mermaid-cli
rendererVersion=11.16.0
sourceHash=15fd2a94bdbe107e2e87931352871e246f50a75d0b794950574aea892dbeaea6
cacheKey=28b7d3065a23334a972439fc76a0835cd54b6b952d7c37acde488e2464756109
output=artifacts/derived/mermaid/28b7d3065a23334a972439fc76a0835cd54b6b952d7c37acde488e2464756109.png
```

The first call was a cache miss, the second was a cache hit, both succeeded, and success diagnostics were empty. The host test initializes the real Avalonia platform, decodes a realized diagram PNG, and proves inline `Image`, `Stretch.Uniform`, a 520-pixel maximum height, and bounded vertical scrolling. Existing collapsed-summary and expanded-plan tests retain the two reading states.
