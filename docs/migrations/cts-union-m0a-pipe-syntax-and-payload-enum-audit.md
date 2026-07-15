# CTS-UNION-M0a pipe syntax and payload-enum audit

## Scope and starting state

CTS-UNION-M0a is documentation and audit only. It establishes no `|` token, parser node, union symbol, injection, MIR node, backend behavior, test, fixture, corpus, project, package, or tooling change. The authoritative proposed design is [CTS-UNION-M0a](../Copeland/language/copeland-ts-union-syntax-design-cts-union-m0a.md).

| Item | Observed state |
| --- | --- |
| Revision | `7b980290c51399f5c34da9c77d86e25bc68ab8c9` |
| Branch | `main` tracking `origin/main` |
| Upstream divergence | `0 ahead / 0 behind` |
| Worktree | clean before CTS-UNION-M0a edits |

## Corrected inventory

- `||` is currently one `PipePipeToken`, logical-or precedence 2, in `Lexer.cs`, `SyntaxKind.cs`, and `SyntaxFacts.cs`. A single `|` produces `COPE-LEX-0003`; no lone-pipe token or bitwise-or exists.
- `ParseTypeAliasDeclaration` accepts exactly one existing `TypeSyntax`, then consumes extra content as unsupported alias syntax. `ParseTypeSyntax` supports predefined, identifier, array, parenthesized, Result, qualified row, and column types only.
- Alias predeclaration/resolution is ordered and transparent. Its cycles are bounded and canonical aliases disappear before MIR. A union must not be documented as an alias implementation detail.
- Existing payload enums have ordered symbols, construction, exhaustive matching, bound and MIR values/matches, shared validation, C# abstract-record/sealed-case realization, and JavaScript token/provenance/frozen-carrier validation. TSON uses existing enum schema/value/identity/canonical-plan forms.
- Expected-type binding already covers the positions a future explicit injection needs, but `IsAssignable` is exact equivalence today. Injection is therefore a new frontend conversion rule, not current behavior.
- Current generic inference keeps nominal types atomic and does not synthesize unions. Equality accepts primitives only. Record/enum containment checks and JavaScript recursive enum-payload rejection are existing constraints.
- Historical references that say “payload enums replace unions” are historical proposals, not an implemented pipe grammar. This milestone links them to the current M0a design where needed.

## Decision record

The recommended first implementation accepts only `type Name = Record | Record ...;`, including optional leading pipes, and turns it into an ordinary nominal payload enum with `value` payload fields. `type Name = SingleType;` remains a transparent alias. Aliases, enum alternatives, nested unions, primitives, inline pipes, transitive injection, structural narrowing, and new MIR/backend/TSON families are excluded.

The canonical identity, exhaustive match, TSON identities, and C#/JavaScript paths are existing enum law. A union-specific source-provenance symbol may survive through binding for diagnostics and injection, but must erase to the existing enum representation before MIR. The first concrete implementation should decide whether the existing enum route makes M0b/M1 one safely atomic end-to-end slice.

## Validation contract

The M0a diff is limited to Markdown. Validate links/headings/fences/tables/terminology, UTF-8 without BOM, and trailing whitespace; run both topology scripts in Windows PowerShell 5.1 and PowerShell 7 plus `git diff --check`. Do not run full builds/tests unless a non-document change appears. Do not commit, push, publish, or change versions.
