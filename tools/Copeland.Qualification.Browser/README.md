# Browser qualification infrastructure

The TableScript application, editing and variable-row samples share this tool.
It owns dependency/bootstrap orchestration, profile bundling, an ephemeral loopback
asset server, Playwright lifetime and page/console/HTTP error collection. Each
sample owns its scenario actions, expected results and probes; no assertions were
replaced by a generic page-load check.

```powershell
./tools/Copeland.Qualification.Browser/run.ps1 `
    -SampleRoot samples/copeland-ts/tablescript-rows-m15
```

The shorter sample `run.ps1` entry points delegate here. Requires .NET 10, Node 26,
npm, the installed `tspack` command and Edge. Pass `-BrowserChannel chromium` for
an installed Playwright Chromium. Locked dependencies are installed with `npm ci`.
New compiler/browser reports and screenshots remain under each sample's ignored
`artifacts/`; bundles live under its ignored `dist/`. Historical docs are not
rewritten. The server closes after browser completion or failed browser launch.

To inspect an already-built example in the foreground:

```powershell
npm run serve --prefix samples/copeland-ts/tablescript-rows-m15
```

Open <http://127.0.0.1:4191/diagnostic/index.html>. Stop with Ctrl+C. Other emission
profiles use the same URL shape. The standalone server also accepts `--root` and
`--port`; it binds only to loopback and enforces the real asset root.

Shared TypeScript and server checks:

```powershell
npm ci --prefix tools/Copeland.Qualification.Browser
npm run check --prefix tools/Copeland.Qualification.Browser
npm test --prefix tools/Copeland.Qualification.Browser
```

The canonical frame/lifecycle runtime remains TSPack-owned. Its current locked
materialization input is `samples/copeland-ts/react-friction-m0`; if missing,
this tool invokes that manifest's existing TSPack build. Its `dist` file is a
disposable runtime projection, not a second implementation. Removing that input
requires a separate TSPack integration change. The older React/Vue/islands runners
retain framework comparison, lifecycle HTTP routes and baseline/asset mounting
that differ from this table runner; they are not interchangeable copies.

Repeated application-side React behavior instead lives in
[Copeland.Browser.React](../../libraries/Copeland.Browser.React/README.md).
