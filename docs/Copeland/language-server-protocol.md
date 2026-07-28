# Copeland language-server protocol guide

Copeland clients launch `tscl language-server` and communicate using standard LSP JSON-RPC framed over stdin/stdout. Do not send logs to stdout.

Only files listed as `tscl` in `obj/copeland/workspace/editor-ownership.generated.json` are served. Clients must route `tsc` files to `tsserver`; the Copeland server declines them without diagnostics.

Clients should send `initialize` with `initializationOptions.workspaceRoot`, optionally an explicit `ownershipFile`, and `project`. The server evaluates the declared (or ownership-declared) project to select its TS-XML profile and compiler references; `tsXmlProfile: "react-m0"` remains a fallback for projects without evaluated metadata. The server reports version `0.1.0` in `serverInfo`; use `tscl language-server --version` for launch compatibility checks.

Use full-text `textDocument/didChange` notifications in M0. Each open buffer is applied as an immutable overlay to the compiler's `CopelandProjectSnapshot`; versions must increase and older changes are ignored. The server never writes editor buffers back to disk. The ownership artifact is checked before requests and a changed artifact reloads the project snapshot.

Supported requests are hover, completion, definition, document symbols, full semantic tokens, and basic signature help. `tsconfig.tsx` receives bounded ownership-manifest completion and hover. Future clients should rely only on standard LSP methods in M0; no custom Copeland request is required.

## Evaluated project contracts

The language server evaluates the declared project through the Copeland SDK's
read-only MSBuild target. The target exposes the same explicit project inputs
used by normal Copeland compilation:

```xml
<ItemGroup>
  <CopelandCompile Include="src/App.tsx" />
  <CopelandNpmContract Include="contracts/react.json" />
  <CopelandPackageContract Include=".../copeland/contract.v1.json" />
</ItemGroup>
```

`CopelandNpmContract` is an exact, already-resolved package contract. It is
not a request to search `node_modules` or select a version. Its JSON schema is
versioned (`schemaVersion: 1`) and carries package name, resolved version,
materialization state/path, supported function exports, and optional TS-XML
component contracts. Both normal MSBuild compilation and the LSP snapshot read
the same item. The compiler reports an unavailable materialization rather than
guessing a package location.

```json
{
  "schemaVersion": 1,
  "package": "@example/dialog",
  "version": "2.4.0",
  "materialization": "node_modules/@example/dialog/index.js",
  "materialized": true,
  "exports": [{
    "name": "open",
    "parameters": ["string"],
    "result": "number"
  }],
  "components": [{
    "name": "Dialog",
    "properties": [{ "name": "title", "type": "string", "required": true }]
  }]
}
```
