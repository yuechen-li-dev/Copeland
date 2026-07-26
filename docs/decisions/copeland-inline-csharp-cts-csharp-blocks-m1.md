# CTS-CSHARP-BLOCKS-M1: typed inline C# blocks

Copeland TS supports a deliberately narrow CLR migration escape hatch:

```typescript
using Demo;

function Normalize(value: string): string {
    csharp {
        return LegacyNormalizer.Normalize(value);
    }
}
```

`csharp { ... }` is a statement-only, block-scoped native island. The Copeland
parser locates its lexical boundary while respecting C# comments, quoted
strings, verbatim strings, and raw strings; it does not parse C# grammar.
The raw body, source line, expected function result, and typed lexical captures
are retained through Bound and MIR nodes. The C# backend emits the original
statements directly in the generated method body, surrounded by `#line`
directives when a source path is available. Normal project Roslyn compilation
therefore owns syntax, overload resolution, accessibility, and semantic errors.

The boundary follows the existing CLR projection law: scalar values, strings,
arrays, Copeland records/classes, and nominal CLR values are admitted. Closures,
async values, npm values, structural host objects, and other unprojectable
values are rejected; no value is silently boxed to `object` and no `dynamic` or
runtime invocation bridge is emitted. Captured Copeland bindings are read-only:
assignment or increment of a capture is diagnosed. C# locals remain local to
the block. A value-returning block returns directly from its enclosing function;
a void function may fall through. C# internal loops, `try/catch`, and `using var`
are ordinary C#.

Inline C# is CLR-only. JavaScript emission reports `COPE-JS-CSHARP-0001` for a
program containing one. Copeland intentionally has no `js { ... }` or
`javascript { ... }` equivalent: C# shares the project’s statically checked CLR
compilation and reference context, whereas arbitrary JavaScript would introduce
dynamic semantics that cannot be consistently contained across the CLR and
JavaScript backends. npm interop remains the declared static module boundary.

M1 deliberately defers expression blocks, top-level C# declarations, whole-file
migration wrappers, async/`await`, `Task<T>` adaptation, cross-boundary writable
bindings, and declaration merging. These restrictions keep the feature a visible
migration bridge rather than a second freely mixed language.
