# Copeland TS feature inventory

This is an implementation inventory, not a roadmap. “Owner” is the one
authority that gives the feature its meaning. Paths are repository-relative;
fixture directories are the tested syntax authority. Status follows the
[feature-status page](reference/feature-status.md).

| Feature | User syntax/API | Parser / representation / canonical owner | Consumers and runtime | Inspection, docs, compatibility, gaps | Status |
| --- | --- | --- | --- | --- | --- |
| Lexing and parsing | `.ts`, selected `.tsx` | `Syntax/Lexer.cs`, `Parser.cs`, `SyntaxNodes.cs`; syntax tree then binder | compiler, LSP | `Language/*` fixtures; VS Code grammar; TS subset only | Bounded |
| Functions and closures | `function`, calls, callable references, explicit `capture` | parser; bound callables and Cope MIR; binder owns resolution/capture | C#/JS | callable fixtures; authoring guide; no arbitrary JS closures | Stable |
| Modules/imports/using | `import`, local module graph, CLR `using` directives | project context and binder own resolution | C#/JS, CLI/LSP | `authoring/local-modules-m1.md`; manifest/TSPack descriptor required for project mode | Bounded |
| Packages/npm | manifest contracts and typed imports | TSPack resolves; `CopelandProjectContext` consumes resolved contracts | JS materialization, C# sidecar boundary | npm architecture docs; no package guessing | Bounded |
| Declarations/types | typed `const`/`let`, aliases, interfaces, bounded generics | binder/types; aliases/interfaces erase before MIR | C#/JS | type/generic fixtures; `var` unsupported | Bounded |
| Records / `with` | `record`, contextual braces, `value with { ... }` | binder; record bound nodes/MIR | C#/JS | record fixtures and authoring guide; no structural objects/equality | Stable |
| Classes | pure `class`, constructor, associated function | binder lowers to canonical record/function facts | C#/JS | class fixtures; no inheritance/prototypes/instance methods | Bounded |
| Enums / tagged data | enum cases, payload cases, nominal `A | B` sugar | binder canonical enum model | C#/JS | tagged-data fixtures; 2-8 direct-record union sugar only | Bounded |
| Pattern matching | `match`, accepted switch-alias forms | binder exhaustiveness/type rules; `BoundMatchExpression` | C#/JS | match fixtures; no general structural patterns | Bounded |
| Structured control | `if`, `while`, `for`, `break`, `continue`, `return` | parser/binder/MIR | C#/JS | control-flow fixtures; boolean conditions only | Stable |
| Fallibility | `Result`, `?`, `!`, `try` / `except`, `ok`/`err` | binder and Result MIR | C#/JS | fallibility fixtures; runtime boundary differs by backend | Bounded |
| Arrays / TSON | array values, typed TSON/assets | `Tson/*`; canonical reader/printer/schema | C#/JS | TSON corpus; no general JSON language API | Bounded |
| Record tables | `record table`, table `with`, rows/columns | binder/table MIR + TSON canonical model | C#/JS | table fixtures and `tables.md`; immutable, no query/mutation API | Bounded |
| Templates/static | templates and static project assets | `Templates/*`, manifest compiler | generated C#/artifacts | template docs; not dynamic browser templates | Bounded |
| Async/generator | typed async/await, generators where fixture-backed | binder/lowering/suspension automata | backend-specific | architecture records; bounded control surface | Bounded |
| `flow` | explicit flow authoring | binder/MIR and backend emitters | C#/JS | `docs/cts-flow-m1.md`; no push/pop/goto expansion | Bounded |
| TSX / XML frontend | typed XML-shaped elements in React M0 profile | parser + TSX profile + typed npm component contracts | JS/React materialization | TSX fixtures; no universal JSX ecosystem | Bounded |
| Components | component functions and calls | binder creates definitions/instances and lexical capsule facts | layouts, attachments, frames | `component::*` tables/LSP hover; no renderer ontology | Bounded |
| Props/captures/private presentation | typed arguments, `capture`, private layouts/streams | component binding and `BoundComponentInstance` | C#/JS/browser projection | capsule tests/tables; private facts must not become DOM identity | Bounded |
| Component state/events | state declaration, zero-payload browser subset | bound state model and component frame | in-process C# runtime; generated browser frames | state tests; browser payload/state breadth limited | Bounded |
| Presentation branches | state-selected child presentation | bound presentation branches | C# semantic runtime / browser frame projection | component-state tests/browser proof; complex projection deferred | Bounded |
| Effects | component effect descriptors/phases | `ComponentStateRuntime` frame model | semantic C# runtime | trace/diagnostics; browser effect execution deferred | Foundation only |
| Renderer selection | typed React / Custom Element / `ForeignComponent` bridge | binder + adapter contract registry + `HostAttachmentMir` | artifact emitter / adapter | renderer tables/hover; no Vue/Svelte/Lit/Blazor | Bounded |
| Attachments | host attachment plan, lifecycle | `HostAttachmentMir` | `attachments.json`, C# registry, browser runtime | `renderer::Attachments`; v1 wire schema | Bounded |
| Layout declarations | layouts, boxes, row/column/grid/overlay, layers | `Machina/LayoutDataCompiler.cs` normalizes | CSS/backend/host selector | `layout::*`, `layout inspect`; not CSS/Flexbox semantics | Bounded |
| Layout types / streams / bindings | typed layouts, streams, bindings | binder and Machina layout model | CSS/browser projection | layout/stream tests and tables | Bounded |
| Derivations / origin / z | relative rows, origin, z/layers | normalized layout derivation model | CSS, inspection | `layout::Derivations`, provenance | Bounded |
| CSV layout authoring | CSV-shaped rows/derivations cells | Machina source/layout compiler | same normalized layout model | table/inspection; no separate CSV semantics | Bounded |
| Overflow / content fitting | declarative regions and fit metadata | layout/text binding model | browser text-fit realization | `text::Regions`; browser behavior is bounded | Bounded |
| Documents / text | XML-shaped blocks, Markdown-style inline syntax | `Machina/TextDocuments.cs`, `DocumentMir` | projections/browser text realization | `text::*` tables; renderer fidelity bounded | Bounded |
| JavaScript backend | `--emit javascript`, symbolic/production profiles | JS backend consumes Cope MIR | Node/browser | JS corpus/runtime tests; no JS semantic reinterpretation | Stable |
| C# backend | `--emit csharp` and build integration | C# backend consumes Cope MIR | .NET | C# corpus/runtime tests | Stable |
| CLR / ASP.NET | imports, C# blocks, bounded bridge | binder CLR contracts and ASP.NET backend | .NET | CLR/ASP.NET tests; no arbitrary mixed runtime state | Bounded |
| CLI | compile, build, tables, layout inspect | CLI consumes project compilation | files/JSON | `docs/Copeland/table-tools.md`; formatting is not semantic owner | Stable |
| Projected relations | `table list/schema/rows` | `LayoutProjectedTableProvider` over bound facts | CLI/LSP consumers | `layout::*`, `text::*`, `component::*`, `renderer::*`, sources | Stable |
| LSP / VS Code | diagnostics, hover, workspace integration | shared snapshots in language server | VS Code extension | protocol/VS Code docs; no separate binder | Bounded |
| TSPack | manifest resolution/build/materialization | sibling TSPack owns resolution/materialization | browser/npm output | descriptor/fingerprint; does not bind Copeland semantics | Bounded |
| Browser runtime | `@copeland/browser-v1` host APIs/artifacts | TSPack generated runtime owns DOM/lifecycle | Custom Element + app React bootstrap | browser proof; source is Go generated string | Experimental |

## Common representations

`BoundProgram` and the bound node families are the primary compiler semantic
representation. Cope MIR is the backend boundary for general language facts.
Some domain facts are deliberately normalized alongside bound nodes: Machina
layout data, `DocumentMir`, `HostAttachmentMir`, and component presentation/
state facts. These are not competing semantic universes: each records a
specific normalized domain phase and has a named owner in the
[ownership map](architecture/semantic-ownership.md).

## Recognized but unsupported TypeScript surface

The parser/diagnostics intentionally recognize several familiar spellings so
they can be rejected clearly: `var`, `eval`, `null`, `===`, `!==`, ternary
`?:`, optional chaining, tuple types, optional fields, `try`/`catch`, inline
structural unions, readonly arrays, default parameters, general objects, and
dynamic types. Recognition is not implementation. See
[diagnostics](reference/diagnostics.md) and the invalid language fixtures.
