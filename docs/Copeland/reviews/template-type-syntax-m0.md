# TEMPLATE-TYPE-SYNTAX-M0 closeout

## Decision

Copeland has one typed template abstraction:

> A C++ template, a webpage template, and a project starter template are the
> same typed parameterized construction with different consumers.

The Preview release ladder remains paused. This milestone does not publish or
resume a release.

## Language and semantic shape

Canonical grammar:

```text
TemplateDeclaration
  := template < TemplateParameterList? > Identifier : Type Block

TemplateParameter
  := type Identifier (extends Identifier (& Identifier)*)? (= Type)?
   | static Identifier : Type (= StaticExpression)?

TemplateInstantiation
  := instantiate Identifier < TypeArgument* (, NamedStaticArgument)* >

NamedStaticArgument
  := Identifier : StaticExpression
```

The AST retains `TemplateDeclarationSyntax`, explicit type/static parameter
nodes, defaults, result syntax, and `TemplateInstantiationExpressionSyntax`.
The binder declares a non-callable `TemplateSymbol`, normalizes constraints
through `RequirementSet`, binds a syntax-free `BoundTemplateDeclaration` plan,
and produces `BoundTemplateInvocation` only from `instantiate`. Type argument
identity is retained through evaluation so `nameOf<T>()` observes the actual
specialization. Templates are no longer implicitly evaluated merely because a
module reaches the bound compiler stage.

Defaults are positional for type parameters and named for static arguments.
Constraints reuse Copeland's existing interface/record field requirement
algebra and issue direct missing/incompatible-field diagnostics. Invalid
defaults and result mismatches are diagnostics. CLI entry type parameters must
all default; source instantiation may provide explicit types.

## Typed project domain and materialization

The bounded compiler-known domain includes `DotNetSolution`, `DotNetProject`,
`TypeScriptWorkspace`, `NpmPackageManifest`, `NpmDependency`,
`CopelandSourceSet`, `CopelandProjectTypeSet`, `ProjectFile`, `SourceFile`, and
`TestFile`. Typed TS-XML `PackageReference` nodes are the bounded
`NuGetPackageReference` equivalent. The bootstrap also declares `CopelandBootstrapProject` and
`StandardCopelandProject`; the constrained `TProject` specialization affects
the generated `.copeland/project-type.txt` and README metadata.

`DotNetSolutionValue.TryLower` is the single semantic-to-artifact boundary. It
creates the directory/root nodes and delegates normalization, duplicate/path
safety, ordering, hashing, and materialization to the existing `ProjectTree`
and `ProjectTreeMaterializer`.

The `.csproj` uses actual TS-XML in `BootstrapTemplate.tsx`. The bounded schema
accepts `Project`, `PropertyGroup`, `ItemGroup`, the selected SDK property
elements, `PackageReference`, and `CopelandNpmContract`. Unknown elements,
attributes, invalid nesting, duplicate attributes, missing `Sdk`, and missing
package identity/version are rejected before materialization. `.slnx` remains a
bounded typed `slnxFile` constructor rather than a general XML schema system.

`npmPackageManifest` and `typeScriptWorkspace` create typed semantic values;
`jsonFile` and `workspaceFile` serialize them canonically. Source-code files
remain deterministic text, intentionally avoiding a general source AST emitter.

## CLI, compatibility, and tooling

```console
tscl template materialize BootstrapTemplate.tsx --entry BootstrapTemplate --name HelloCopeland --target net10.0 --output ./HelloCopeland
```

The old function-shaped prototype receives `COPE-TEMPLATE-0011` and is not a
supported compilation form. Maintained fixtures were migrated directly.

The language server reports canonical template/result hover, type/static
parameter hover and definitions, constraint interface definitions, project/type
completion, named static argument completion, the `instantiate` keyword, and
ordinary binder constraint diagnostics. Deeper specialization navigation and
signature help remain intentionally unsupported in M0.

## Intentionally unsupported

- partial or explicit specialization declarations;
- SFINAE or candidate disappearance;
- template-template parameters and higher-kinded types;
- dependent-name rules and general compile-time reflection;
- arbitrary XML schemas or XML control directives;
- general source-code AST generation;
- a shell-encoded source instantiation expression;
- materializers beyond existing compiler/renderer consumers and the bounded
  project filesystem path.
