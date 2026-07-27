# `tscl` project build contract (TSPACK-TSCL-M1)

`tscl` is the Copeland TS compiler. It is not a JavaScript runtime.
For this milestone, TSPack invokes:

```text
tscl build --project <project.json> --result <result.json>
```

The project JSON supplies the project root, every project-owned source with a
logical module identity, entry module/export, explicit `javascriptRuntime`
(`node` or `browser`), production JavaScript profile, output directory,
optional resolved npm contracts, and a build fingerprint. Copeland never runs
npm, reads a package lock, or discovers `node_modules`; the supplied npm rows
are already resolved/materialized by TSPack.

The result JSON has `success`, stable diagnostic `code` values, authored source
`file`/`line`/`column` locations when available, `outputs` with SHA-256 hashes,
`entryOutputPath`, compiler version, and the caller build fingerprint.

On success, output is staged and then published as a complete directory. The
emitted graph remains native ESM: local imports use relative `.js` paths. The
Both targets always use the `production` JavaScript profile. Node output keeps
its established `package.json` with `type: module` and logging entry launcher.
Browser output emits a browser ESM entry launcher (`await Main()`), does not
write Node launcher metadata, and reports `target: "browser"` in the result
manifest. Local module imports remain relative `.js` paths and package imports
remain declared bare specifiers for the browser host/import map to realize.

For the browser target, `tscl` exposes only the supplied
`@copeland/browser-v1` typed host contract (`setText`, `onClick`, and generic
`dispatch`). TSPack owns its implementation, package materialization, and the
HTML/import-map host. This maintains target separation: browser materialization
does not reinterpret or change the Node target.

Current browser boundary: direct static ESM packages with root export selection
in `browser`, `import`, then `default` order. CommonJS entries are rejected by
the host materializer; package subpath import maps, CommonJS transformation,
and arbitrary assets remain TSPack follow-up work. Vite, Bun, Deno, CLR
orchestration, sidecars, publication, watch/HMR, and source maps remain
deferred.
