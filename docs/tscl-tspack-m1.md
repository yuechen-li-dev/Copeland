# `tscl` project build contract (TSPACK-TSCL-M1)

`tscl` is the Copeland TS compiler. It is not a JavaScript runtime.
For this milestone, TSPack invokes:

```text
tscl build --project <project.json> --result <result.json>
```

The project JSON supplies the project root, every project-owned source with a
logical module identity, entry module/export, Node runtime, production
JavaScript profile, output directory, optional resolved npm contracts, and a
build fingerprint. Copeland never runs npm, reads a package lock, or discovers
`node_modules`; the supplied npm rows are already resolved/materialized by
TSPack.

The result JSON has `success`, stable diagnostic `code` values, authored source
`file`/`line`/`column` locations when available, `outputs` with SHA-256 hashes,
`entryOutputPath`, compiler version, and the caller build fingerprint.

On success, output is staged and then published as a complete directory. The
emitted graph remains native ESM: local imports use relative `.js` paths. The
Node M1 profile is always `production`; `package.json` with `type: module` and
an entry launcher are emitted so ordinary Node can execute the selected export.

Current boundary: Node production JavaScript only. Vite/browser, Bun, Deno,
CLR orchestration, sidecars, publication, watch/HMR, source maps, and arbitrary
npm declaration ingestion remain deferred.
