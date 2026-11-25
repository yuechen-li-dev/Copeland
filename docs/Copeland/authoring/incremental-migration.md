# Incremental TypeScript migration for Node

Keep the original library on its existing TypeScript compiler and test suite.
Use Copeland for one public operation at a time. A Node facade can route calls
to either emitted implementation while the rest of the library stays on the
legacy build.

## Create a workspace

Run from the TypeScript project. The commands use Node and the project's
installed TypeScript compiler API, tested with TypeScript 5.9 and 6. Native
TypeScript projects can install the API from `@typescript/typescript6`
alongside their existing compiler. See
[TypeScript's side-by-side tool guidance](https://devblogs.microsoft.com/typescript/announcing-typescript-7-0/#running-side-by-side-with-typescript-60).

```powershell
tscl migrate report -p tsconfig.json
tscl migrate init -p tsconfig.json --out copeland
tscl build --standalone copeland --target node --result copeland/obj/build-result.json
node copeland/dist/main.js
```

Omit `--out` to use `copeland/` when its scaffold is outside the legacy source
globs. If the globs include it, the default moves to a sibling named
`<library>-copeland`. An explicit overlapping destination produces an error
before any files are written. Choose a destination whose future sources also
stay outside those globs.

The scaffold contains `Migration.csproj` for Copeland workspace ownership,
`tsconfig.tsx`, `manifest.tsx`, a small `src/Main.ts` Node entry, and ignored
output folders. It records the original config in `copeland-migration.json`
with the Legacy profile. From the scaffold root, `tscl migrate report` follows
that pointer. The original `tsconfig.json`, package scripts, source files, and
emitted JavaScript remain under the original build. A repeated `init` preserves
edits in an existing scaffold.

## Use migration hints

`tscl migrate report --format json` reads the saved TypeScript project and its
project references. It reports warnings with a file path relative to the
config, a UTF-16 source span, one-based line and column ranges, and a stable
code. Resolved local import suggestions include the exact expected text and a
replacement scoped to the Copeland copy. Apply those edits after both files are
in the Copeland source graph. The legacy `.js` import can be required by Node.

In VS Code, run **Copeland: Show Migration Hints** from a saved TypeScript
project to see the same warnings in the editor. Run it again to refresh.
**Copeland: Clear Migration Hints** removes them. Editing a file clears its
saved-source ranges. The report is advisory and performs no source edits or
JavaScript emission.

## Switch one export

Copy or rewrite the operation into the scaffold's `src/`, keep its import
closure Copeland-owned, and test its output against the original emitted
library. Copeland's generated modules live under `dist/src/`. For a Node
facade that passes records or callbacks, add `--node-interop` to the build and
import the generated `*.node.js` module.

For example, a facade beside the two builds can route the two-string
`diffLines` contract while retaining support for legacy options and callbacks.
`ported-lines.mjs` is the adapter over the generated Copeland module.

```js
import { diffLines as legacyDiffLines } from "./libesm/index.js";
import { diffLines as copelandDiffLines } from "./ported-lines.mjs";

export * from "./libesm/index.js";

export function diffLines(...args) {
    if (args.length === 2 && typeof args[0] === "string" && typeof args[1] === "string") {
        return copelandDiffLines(...args);
    }
    return legacyDiffLines(...args);
}
```

Run the existing TypeScript checks through the original config. Use
`--noEmit` for the comparison check. Build the Copeland workspace and run the
Node parity tests separately. Once a port handles the entire contract of an
export, its facade can directly re-export that implementation.

## Use typed builders and Node boundaries

Use `MutableList<T>()` when the number of items is discovered during the
operation. It starts empty, supports `append(value)`, `length`, checked
indexing and indexed assignment, and takes a snapshot for `for...of`.
`freeze()` returns a separate `T[]` snapshot. Use `MutableArray<T>` when the
size is known up front.

```ts
export record Match {
    index: int;
    text: string;
}

export function collect(lines: string[], accept: (line: string) => boolean): Match[] {
    const matches = MutableList<Match>();
    for (let index = 0; index < lines.length; index++) {
        const line: string = lines[index];
        if (accept(line)) {
            matches.append({ index: index, text: line });
        }
    }
    return matches.freeze();
}
```

```powershell
tscl build --standalone copeland --target node --node-interop --result copeland/obj/build-result.json
```

```js
import { collect } from './copeland/dist/src/Matches.node.js';

const matches = collect(['keep this', 'skip this'], line => line.startsWith('keep'));
// [{ index: 0, text: 'keep this' }]
```

The generated facade exposes synchronous functions with primitive values,
dense arrays, and acyclic records with public fields. Incoming records need
their declared fields as own properties. Outgoing records use the authored
field names, with arrays and nested records copied across the boundary.
Direct callback parameters receive the same plain values. Copeland calls the
callback synchronously where the source invokes it and validates its result
when it returns. Exceptions propagate to the Node caller. Other signature
shapes stay available in the regular compiled module ABI.

## M2 verification

The JsDiff `v9.0.0` report inspected 21 legacy files and offered 47 resolved
import edits. A generated workspace built the M1 Copeland sources for Node.
The facade routed the default `diffLines` calls to Copeland and preserved
legacy results for options, callback calls, and patch operations. The original
tsgo and tsrs checks passed, and hashes matched for all 87 original source,
configuration, and emitted files checked. An isolated VS Code extension host
showed the warning on the expected import range in a TypeScript buffer and
cleared the warning when the buffer changed.

A TypeScript 7.1 beta fixture used the TypeScript 6 compatibility API for its
report. Its broad include matched an in-tree scaffold, so the default selected
a sibling directory. The original native checker passed both before and after
scaffolding.
