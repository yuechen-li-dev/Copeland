# Copeland templates

A C++ template, a webpage template, and a project starter template are the
same Copeland abstraction: a typed parameterized construction with a consumer
appropriate to its result.

```text
template parameters -> typed construction -> typed result -> materializer
```

- A program/type template is consumed by a compiler.
- A presentation/component template is consumed by a renderer.
- A project template is consumed by the filesystem materializer.

`type` is Copeland's only classification vocabulary. Copeland does not add a
separate C++-style `concept` keyword or constraint ontology.

## Declaration and instantiation

The canonical declaration places type and static value parameters in one
angle-bracket list. Type parameters precede static parameters.

```ts
interface NamedProject { name: string; }
record StandardProject { name: string; }

template<
    type TProject extends NamedProject = StandardProject,
    static name: string,
    static target: string = "net10.0"
> Build: DotNetSolution {
    // typed construction
}
```

Type parameters use `type`, may use existing interface/record field constraints,
and may have a default. Static parameters use ordinary Copeland types and
static values. Each template declares its result type, and its `return` value
must satisfy that type.

Source specialization is explicit and is not a runtime function call:

```ts
const solution: DotNetSolution = instantiate Build<
    StandardProject,
    name: "HelloCopeland"
>;
```

Static arguments are named; omitted parameters use declared defaults. Missing,
duplicate, unknown, incorrectly typed, or constraint-violating arguments are
normal diagnostics. M0 deliberately has no partial specialization, SFINAE,
template-template parameters, higher-kinded types, or compile-time reflection.

## Project templates

The maintained bootstrap is
`samples/copeland-ts/templates/BootstrapTemplate.tsx`. Its semantic lowering is:

```text
BootstrapTemplate
-> DotNetSolution
-> DotNetProject
-> TypeScriptWorkspace / NpmPackageManifest / ProjectFile / source and test files
-> ProjectTree
-> ProjectTreeMaterializer
```

`ProjectTree` is the safe backend boundary, not the main bootstrap authoring
model. `.csproj` is authored as bounded TS-XML. Element names, attributes, and
nesting are checked against the small SDK-project model before evaluation. TS
computes attribute/child values; TS-XML has no loops, conditionals, or directives.
`package.json` and `tsconfig.tsx` are emitted from typed
`NpmPackageManifest` and `TypeScriptWorkspace` values.

### Typed source artifacts (Preview M0)

Generated Copeland, Copeland-test, and C# source is authored as a typed,
validated artifact body rather than a multiline source string:

```ts
sourceFile<CopelandTS>("src/Program.ts", { ProjectNamespace: name }, code {
    using ProjectNamespace;
    export function greeting(value: string): string { return Helper.Decorate(value); }
})

sourceFile<CSharp>("src/Helper.cs", { ProjectNamespace: name }, code {
    namespace ProjectNamespace;
    public static class Helper { }
})

testFile<CopelandTest>("tests/Greeting.tsxtest", {}, code {
    using Xunit;
    [Fact] export function works(): void { Assert.True(true); }
})
```

The closed language-type set is `CopelandTS`, `CopelandTest`, and `CSharp`.
It selects parser, validation, destination extension, and materialization
behavior. Copeland bodies use the normal module parser (including nested
TS-XML); C# bodies receive Roslyn syntax validation before materialization.

The imports object is the only visibility boundary between the outer template
and a source body. M0 accepts explicit string-valued imports only in validated
identifier roles. Values are checked as identifiers before replacement and the
result is parsed again, so imports cannot inject arbitrary tokens. There is no
ambient capture, expression/statement injection, token-pasting, or macro
expansion. Raw `sourceFile(path, text)` and `testFile(path, text)` remain
low-level untyped escape hatches.

The filesystem materializer is selected from the typed template result. A
`DotNetSolution` lowers through its artifact graph; an individual source/test
artifact also materializes directly. Unsupported results report that no
artifact materializer is available for the command.

Hierarchical `<SourceFile<Language>>` TS-XML is intentionally deferred: the
function-shaped generic form is clearer for this non-tree artifact. Rich
embedded-language LSP, virtual generated files, generalized AST substitution,
additional embedded languages, formatter support, and full Document/React
surface convergence are also deferred. Documents and components share the same
typed-construction → result → consumer model, but are not migrated by this M0.

```console
tscl template materialize BootstrapTemplate.tsx --entry BootstrapTemplate --name HelloCopeland --target net10.0 --output ./HelloCopeland
```

CLI-facing type parameters must have defaults. `--name` and optional `--target`
bind the same ordered static parameter plan used by source instantiation. The
CLI does not accept an angle-bracket expression as shell text.

The filesystem actuator still creates only a new output root, normalizes
relative paths, rejects traversal and duplicate paths, emits deterministic
ordering and LF UTF-8 text, and never merges or overwrites.

## Static language boundary

Ordinary source code has a separate expression-level form:

```ts
const table: int[] = static buildTable(256);
```

Here `static` means **evaluate this ordinary Copeland expression during
compilation**. It is not a runtime static member, storage-duration annotation,
optimization hint, C# `static`, or linker constant. A normal function remains
dual-use: it can run at runtime and can also be called by `static` when its
ordinary effect summary is `StaticSafe` and all arguments are compile-time
values. The post-static pass embeds the resulting immutable value before MIR.

Template bodies are compiler-bound static plans, not runtime functions. They
support immutable locals, typed values, `return`, `emit`, finite `static for`,
`static if`, `static match`, template instantiation, and compiler-owned
constructors. Runtime calls, environment/process/network access, clocks,
randomness, recursion, `while`, and unbounded loops are rejected.

The old `template Name(static value: Type): Result` prototype is parser-recovered
only to issue `COPE-TEMPLATE-0011`; it does not compile. Maintained samples and
tests use the canonical syntax.

Templates can inspect bounded semantic metadata without receiving AST nodes or
source text. `nameOf<T>()`, `fieldsOf<T>()`, and `enumCasesOf<T>()` accept
concrete types and template type parameters. Field metadata contains `name`,
`typeName`, `optional`, and `readonly`; an authored optional record field reports
its semantic `Option<T>` type. Enum metadata contains `name`, `payloadCount`, and
declaration-ordered `payloadTypes`. The metadata is compile-time only and does
not introduce runtime reflection.
