# Start authoring Copeland TS

Copeland TS looks familiar to TypeScript authors, but its compiler defines a
closed, statically checked language. Do not assume that a TypeScript or
JavaScript feature works because the syntax parses or because a milestone
report describes it.

## Choose a project route

- For a new .NET console, library, or mixed .NET project, begin with the
  [maintained SDK templates](../../../src/Copeland/Copeland.TS.Templates/README.md).
  The generated project opts source files in with `CopelandCompile`; use normal
  `dotnet build`, `dotnet test`, `dotnet run`, and `dotnet publish` commands.
  The detailed [SDK and test setup](copeland-typescript-guide.md#quick-start-in-an-sdk-project)
  explains the project boundary.
- For a browser workspace, begin with the `copeland-react` or
  `copeland-workspace` template and follow its TSPack/`tsconfig.tsx` setup.
  The current [sample catalog](../../../samples/copeland-ts/README.md) distinguishes
  maintained examples from historical qualification fixtures. Browser and
  framework support is bounded; check its status before choosing this route.

## Find the current rule

1. Check [feature status](../reference/feature-status.md) to see whether a
   capability is stable, bounded, foundation-only, experimental, or unsupported.
2. Read the [authoring guide](copeland-typescript-guide.md) for the language
   rule and its repair examples. Use the [language overview](../language/overview.md)
   for the compiler-owned semantic model and presentation boundaries.
3. Check the relevant accepted and rejected examples in
   `tests/Copeland/Copeland.TS.Tests/Language`. A `.cl-valid.ts` or
   `.cl-invalid.ts` fixture is the syntax acceptance evidence; TSX fixtures
   use `.cl-valid.tsx` and `.cl-invalid.tsx`.
4. When extending the compiler, use the
   [feature inventory](../copeland-feature-inventory.md) to locate the semantic
   owner, consumers, tests, and known gaps. Use
   [compiler topology](../architecture/compiler-topology.md) for source and
   build ownership.

If prose, a milestone report, and the current fixture corpus disagree, treat
the mismatch as documentation or implementation evidence to investigate. A
historical design is not proof that a capability shipped. The
[documentation index](../README.md) links the maintained references and keeps
historical evidence discoverable.

## Copeland rules that prevent common porting errors

- `int` and `float` are distinct; `number` aliases `float`. Convert a stored
  integer explicitly with `Float.From` before floating-point arithmetic.
- Optional record fields use `Option<T>`: `name?: string` is present on every
  record as `Some(string)` or `None`. Use `?.` and `??` only with Option values;
  `null` and ordinary `undefined` remain unsupported.
- Records are closed immutable values. Construct them in a known record
  context and use `with` to replace existing fields.
- Interfaces express generic requirements; they are not value or storage
  types. Write `function Read<T extends HasName>(value: T)`.
- String concatenation does not coerce. Use interpolation or `String.From`.
- Imports, npm contracts, and CLR `using` directives belong to different
  declared boundaries; Copeland does not guess across them.
- React and Custom Element paths are bounded integrations, not general
  TypeScript or framework compatibility. See the current status page for the
  qualified subset and omissions.

The guide's sections may name the milestone that introduced a rule. Those
labels identify the evidence lineage, not the current overall product stage;
use the status page for present capability boundaries.
