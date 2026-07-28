# CTS-TEMPLATE-STATIC-M0 review

## Result

The initial language-native template path is implemented as a bounded compiler
subsystem:

```text
template declaration -> typed ProjectTree evaluation -> static selection/traversal
-> stable preview manifest -> TSPack staged materialization
```

Templates use the explicit `template` declaration form and cannot be emitted as
runtime functions. `static if`, `static match`, and `static for` are distinct
syntax nodes. The evaluator accepts only immutable local `const` values,
literals, arrays, records, approved artifact constructors, and other templates.
It has no runtime callback path, so CLR/JavaScript calls, IO, processes,
environment values, time, randomness, mutable globals, and user iterators are
unavailable by construction. Direct and indirect calls are tracked as a
template-instantiation stack and cycles report `COPE-TEMPLATE-0004`.

The artifact model consists of `ProjectTree`, `DirectoryArtifact`,
`FileArtifact`, `TextFileArtifact`, and `SourceFileArtifact`. File paths are
relative, slash-normalized, reject absolute and `.`/`..` paths, and duplicate
paths fail deterministically. Text bytes are UTF-8 without BOM and LF
normalized. Files are lexically ordered and SHA-256 hashed.

`tscl template preview <source> --entry <name> --format tree|json` loads the
source directory as a normal Copeland project snapshot, so relative template
imports use the same resolution and aliases as runtime source. It prints the
artifact graph without writing it. JSON schema version 1 contains `template`
and ordered `files` with `path`, `kind`, `sha256`, `encoding`, and `newlines`.
`tscl template materialize ... --output <new-directory>` writes a canonical
manifest and delegates to `tspack materialize-tree`. TSPack verifies paths,
content hashes, and duplicate paths; writes a sibling stage directory; and
renames it to the explicit new output root only after all writes complete.

## Console dogfood

`samples/copeland-ts/template-static-m0/ConsoleApp.template.ts` imports the
exported `BaseProject` fragment from `BaseProject.template.ts`, composes it
with `ProgramSource`, uses `static if`, `static for`, and `static match`, and
produces a normal .NET console project. Preview produced stable
hashes for `Copeland.Template.Console.csproj` and `Program.cs`. Materialization
through the TSPack seam followed by `dotnet restore`, `dotnet build`, and
`dotnet run` printed:

```text
Hello from Copeland template
```

## Diagnostics

The M0 codes are stable and intentionally specific: `COPE-TEMPLATE-0002`
(constraint/non-static invocation shape), `COPE-TEMPLATE-0004` (recursive
template expansion), `COPE-TEMPLATE-0005` (template result mismatch),
`COPE-STATIC-0001` (non-static expression), `COPE-STATIC-0002` (unbounded
operation), `COPE-STATIC-0003` (unsupported static construct),
`COPE-STATIC-0005` (forbidden side effect/runtime call),
`COPE-STATIC-0006` (unsupported static iterable), and
`COPE-ARTIFACT-0001`/`0002` (invalid/duplicate path).

## Validation

- Focused template parser/evaluator tests passed (4 tests).
- The CLI preview and the TSPack staged materialization proof passed.
- The generated console project restored, built, and ran successfully.
- Copeland CLI and language-server projects build successfully.

## Compiler integration

Templates are declared as `TemplateSymbol` values by the ordinary binder and
retained as `BoundTemplateDeclaration` entries in `BoundProgram`. The binder
then creates a source-located structural plan (`BoundTemplateBlock`, static
literals/arrays/locals, `BoundArtifactConstructor`, `BoundTemplateInvocation`,
`BoundStaticIf`, `BoundStaticMatch`, `BoundStaticFor`, and `BoundTemplateReturn`).
The evaluator consumes only that plan: it neither examines expression syntax,
resolves names, classifies calls, nor infers artifact constructor semantics.
Artifact constructors carry compiler-owned intrinsic identities and template
calls carry resolved `TemplateSymbol` identities. Project evaluation collects
the plans from ordinary module snapshots, allowing exported imports and aliases
to execute across modules without a name-based global lookup. `ProjectTree`
and the artifact node names are compiler-known structural types. Template
constraints reuse the normal requirement-field algebra; templates additionally
accept declared record constraints, while runtime generic functions retain the
existing interface-only law. The template CLI invokes `CompileTemplates`, then
evaluates the resulting `BoundCompilation`; normal runtime `--emit` requests
produce `COPE-TEMPLATE-0006` rather than silently omitting templates.

Runtime MIR remains intentionally template-free: the validated phase boundary
is `BoundProgram.Templates` to the bounded evaluator, never runtime backend
instructions.

## Deferred to M1

- Structural metadata intrinsics (`fieldsOf`, `enumCasesOf`, `nameOf`), richer
  enum/discriminated exhaustiveness, template argument literal-type projection,
  and a React template/browser proof.
- Empty-directory merge policy and project upgrade behavior. M0 materialization
  accepts a new output root only, which guarantees no unrelated overwrite.
