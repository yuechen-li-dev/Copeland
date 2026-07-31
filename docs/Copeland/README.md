# Copeland documentation

## Current authoring truth

[Copeland TypeScript authoring guide](authoring/copeland-typescript-guide.md)
is the **canonical current user-facing language guide**. Start there to write
Copeland TS. It supersedes older language-profile summaries for authoring
guidance.

[Language overview](language/overview.md),
[feature status](reference/feature-status.md), and the
[feature inventory](copeland-feature-inventory.md) are the canonical current
map of what exists and where it is owned. Start with the overview when you are
new to Copeland TS; use the inventory when extending the compiler.

The [semantic ownership map](architecture/semantic-ownership.md) and
[M0 consolidation review](reviews/cts-architecture-consolidation-m0.md) define
the current compiler/browser ownership boundaries. Generated artifacts and
projected-table conventions are documented in [generated artifacts](reference/generated-artifacts.md)
and [projected tables](tooling/projected-tables.md).

## Focused current references

- [Numeric conversion and canonical formatting](authoring/numeric-conversion-m1.md)
- [Local modules](authoring/local-modules-m1.md)
- [MSBuild integration](../decisions/copeland-msbuild-cts-msbuild-m1.md)
- [Same-project C# projection](../decisions/copeland-mixed-cts-mixed-m1.md)
- [CLR interop](../decisions/copeland-clr-interop-cts-clr-m1.md)
- [npm boundary](architecture/copeland-npm-import-boundary-cts-npm-m1.md)
- [Async](architecture/copeland-ts-async-and-suspension-automata-cts-async-m1.md)
- [Batch](language/copeland-ts-batch-cts-batch-m1.md)
- [Generators](../cts-generator-m1.md)
- [Flows](../cts-flow-m1.md)
- [Inline C#](../decisions/copeland-inline-csharp-cts-csharp-blocks-m1.md)
- [Standalone hosted web application M0](reviews/cts-standalone-web-m0.md)

## Historical and design records

`architecture/`, `language/`, `history/`, `docs/decisions/`, and
`docs/migrations/` preserve design decisions and milestone evidence. They may
describe an earlier implementation boundary. They are not a second current
authoring specification; the canonical guide above owns that role.
