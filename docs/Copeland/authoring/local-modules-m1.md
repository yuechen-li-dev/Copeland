# Copeland local modules (M1)

Copeland projects may split application code across explicit `.ts` and `.tsx`
`@(CopelandCompile)` items. Relative imports resolve only within that declared
source set; Copeland never scans unrelated TypeScript files on disk.

```xml
<ItemGroup>
  <CopelandCompile Include="Copeland\RecipeBook.ts" />
  <CopelandCompile Include="Copeland\Planning.ts" />
  <CopelandCompile Include="Copeland\Main.ts" />
</ItemGroup>
```

```ts
// RecipeBook.ts
export function BuildDailySummary(name: string): string { /* ... */ }

// Main.ts
import { BuildDailySummary as Build } from "./RecipeBook";
export function Run(name: string): string { return Build(name); }
```

## Resolution law

- `./Name` searches the declared project sources in this order: `Name.ts`, then
  `Name.tsx`.
- Explicit `./Name.ts` and `./Name.tsx` are accepted. Other extensions are
  rejected. Directory `index.ts` resolution is not implemented.
- `../` is normalized against the importing module's project-relative logical
  path. A target is valid only if it is already in `@(CopelandCompile)`.
- A bare specifier such as `@fixture/tools` remains an npm import and follows
  the manifest-owned npm contract law. `using System.Text.Json` remains CLR
  binding. These domains do not fall back into one another.

M1 supports named imports and named exported declarations: functions, records,
types/aliases, enums, interfaces/requirements, classes, and flows. An alias is
local spelling only; it does not change the exported declaration's identity.
Non-exported declarations cannot be imported.

The project graph is deterministic and rejects cycles, reporting the complete
path. Module sources with relative imports compile together so a change to any
owned source invalidates the graph artifact and stale per-file artifacts are
removed. The generated CLR entry class is `Main` when `Main.ts` is present,
otherwise `CopelandProject`; local calls lower as ordinary direct generated
calls inside that class.

Source-level privacy is enforced at imports: only an `export` may be named by
another Copeland source file, and cross-module function references require an
explicit named import. The CLR graph projection emits exported functions as
`public` and non-exported functions as `internal`; direct Copeland calls remain
assembly-local.

## Semantic binding law

Modules are first-class compiler scopes, identified by their normalized logical
project-relative paths. Project binding parses every source, collects headers
and exports, resolves relative imports, constructs each module's imported
symbol table, then binds that module's declarations and bodies before lowering
its module-owned bound tree to MIR. The old concatenated/flattened CLR binding
was a transitional M1 implementation and is no longer the semantic model.

A module scope contains its local declarations, its exported subset, resolved
imports, aliases, and private visibility. Imported symbols are the exact
semantic symbols from their defining scope; an alias changes only the local
lookup spelling. Consequently unrelated modules may use the same declaration
names. Backend collision spellings are derived from module identity only when
needed and never define semantic identity.

## Type and runtime identity

Records and enums are nominal declarations owned by their defining module.
Two modules may both declare `Result` or `Status`; imported aliases such as
`AlphaResult` and `BetaStatus` select a local spelling only. MIR carries the
defining record identity and module-qualified enum spelling into constructors,
field access, calls, and match lowering. CLR carrier and enum names are made
readably module-qualified only on a collision, so the generated assembly has
distinct nominal types rather than alias-derived lookalikes.

JavaScript retains the same distinction. Local function edges emit native named
ESM imports. Record construction and enum-case construction use private,
module-qualified ESM factory bindings; an importing module receives those
bindings from the defining module, so opaque record and enum runtime tokens
remain shared for construction, validation, and match. No importer replays an
aggregate implementation or reconstructs nominal values from an authored
alias.

## Flow boundary

Flow M1 exports remain visible in graph metadata, but local flow imports are
currently rejected with `COPE-MODULE-0008`. Flow has provisional
backend-facing session APIs and no source-level flow value model yet. This
deliberately avoids cloning or aliasing flow sessions until that model is
designed.

## Deliberate M1 limits

There are no default imports/exports, namespace imports, re-exports,
side-effect imports, dynamic imports, CommonJS, `.d.ts`, JavaScript imports,
`tsconfig` paths/baseUrl, package export maps, JSON/assets, or directory-index
resolution.

## JavaScript output

The JavaScript project emitter writes one public ESM file per logical module:
`Recipes/RecipeBook.ts` becomes `Recipes/RecipeBook.js`. Relative import paths
are rewritten to those `.js` paths and retain named aliases; npm imports retain
their bare package specifiers. Each output module contains its own lowered
declarations and imports cross-module functions as ordinary named ESM imports.
The project MIR graph is a backend container over already-bound module MIR,
not a flattened semantic namespace, runtime registry, or bundled
implementation module. Where two modules export the same callable spelling,
the emitter retains the authored named ESM export while using a
module-derived private implementation spelling.

The earlier [authoring ergonomics review](../reviews/cts-codex-food-m1-authoring-ergonomics.md)
predates this feature and should be read as historical evidence, not current
module behavior.
