# Copeland TS transparent type aliases (CTS-TYPE-M0b)

**Status:** implemented and closed for non-generic compilation-unit aliases. This record implements the first code milestone selected by [CTS-TYPE-M0a](../language/copeland-ts-type-system-design-cts-type-m0a.md); it does not reopen M0a's accepted type, record, interface, generic, static-evaluation, or backend doctrine.

> **Later design authority:** [CTS-TYPE-M1a](../language/copeland-ts-interface-requirements-design-cts-type-m1a.md) selects erased field-only interfaces as generic-constraint requirement sets. It does not implement or widen this M0b alias boundary.

## Source and scope

The grammar is:

```text
TypeAliasDeclaration ::= `type` Identifier `=` Type `;`
```

`Type` is exactly the existing Copeland type grammar: approved primitives, nominal names, `Table.Row`, `column T`, postfix arrays, parenthesized types, Results, and other aliases. The semicolon is required. Type parameters and every unapproved TypeScript type-level form are rejected rather than partially interpreted.

`type` is a contextual declaration keyword. The lexer continues to produce `IdentifierToken`; the parser recognizes text `type` only at the start of a compilation-unit member. This is the smallest compatible change and preserves local variables, parameters, fields, and functions named `type`. A compilation-unit statement beginning with `type` is consequently reserved for an alias declaration, matching ordinary TypeScript declaration expectations.

Aliases have compilation-unit scope. This record deliberately does not call that module scope: Copeland has no module system. There are no block aliases, shadowing, merging, imports, or exports.

## Namespaces and semantic representation

Aliases, records, payload enums, and record tables occupy one case-sensitive compilation-unit type-name namespace. A later declaration that collides with an alias, or a later alias that collides with a nominal type, receives `COPE-ALIAS-0003`; declarations never merge.

Type and value lookup remain conceptually distinct. `TypeAliasSymbol` is compile-time-only and is held in the binder's alias table. It is never installed in the runtime `Scope` as a `VariableSymbol`. Existing nominal declarations retain their value symbols because their construction, case, table-singleton, and member laws require them. A function or local value may therefore have the same spelling as an alias and wins ordinary value lookup. An alias with no value declaration reports `COPE-ALIAS-0006` when used as a value or constructor.

## Resolution and transparency

Binding uses these phases:

```text
parse aliases
-> predeclare compiler and authored type names
-> predeclare nominal and alias symbols
-> build the alias dependency graph
-> detect cycles and resolve aliases
-> bind nominal bodies, executable signatures, and executable bodies
-> lower canonical types to MIR
```

Forward references to aliases and later nominal declarations are supported. Alias targets are canonical existing `TypeSymbol` values; `TypeAliasSymbol` identity is never part of `TypeFacts.AreEquivalent`. Consequently aliases preserve assignment, argument, return, field and payload compatibility, expected-type propagation, contextual record construction, arrays, Results, primitive equality eligibility, table/row/column positions, and TSON eligibility. An alias never makes an illegal canonical type legal. `COPE-TYPE-0020` now enforces the existing M0a `void` law uniformly: direct `void` is legal only as a function return or Result success type, whether authored directly or through an alias.

Direct authored alias names are retained as nonsemantic diagnostic provenance. Ordinary mismatch diagnostics can say, for example, `expected 'UserId' (alias of 'number')`; canonical equivalence and lowering still see only `number`.

## Cycles, order, and recovery

Alias dependency collection uses an explicit `Stack<TypeSyntax>`. Cycle discovery uses explicit visit states and a heap-backed frame list; resolution uses declaration indices, dependency counts, reverse edges, and a declaration-ordered ready set. No traversal recurses in proportion to alias-chain length. A focused test resolves 5,000 forward aliases.

Dependencies are sorted by declaration index. Cycle search starts in declaration order, rotates a discovered path to its earliest declaration, reports one diagnostic for that path, and suppresses repeated reports that overlap the already reported cycle. Paths show at most 16 alias names plus a bounded ellipsis and closing primary name. Aliases depending on an invalid or cyclic alias recover as the compiler error type and do not create downstream mismatch cascades. The error type is never a successful authored target or MIR type.

Diagnostic precedence is parser-owned malformed/generic rejection, declaration-order collision analysis, cycle detection, target resolution, then executable binding. All alias diagnostics use the responsible nonempty token span:

| Diagnostic | Law and primary span |
| --- | --- |
| `COPE-ALIAS-0001` | malformed declaration or unsupported type-level tail; offending/missing-site token |
| `COPE-ALIAS-0002` | generic alias syntax; opening `<` |
| `COPE-ALIAS-0003` | duplicate/colliding type name; later declaration name |
| `COPE-ALIAS-0004` | unknown ordinary alias target; unknown identifier |
| `COPE-ALIAS-0005` | direct/indirect expansion cycle; earliest alias name in the bounded cycle path |
| `COPE-ALIAS-0006` | erased alias used as a runtime value or constructor; alias use |
| `COPE-TYPE-0020` | canonical `void` used in an illegal value position; declaration/use name |

The existing proof-era rule that an undeclared name may be the error component of `T ! E` remains intact; this record does not silently convert it into a general named-type declaration mechanism.

## MIR, backends, and TSON

Aliases disappear before `MirLowerer`. There is no alias bound declaration, `MirAliasType`, `.cope` declaration, runtime metadata, C# declaration, JavaScript declaration, brand, wrapper, projector, or demand-emitted helper. A reflection-based structural test proves that the MIR assembly contains no type whose name contains `Alias`.

Alias-authored and direct canonical programs produce byte-identical `.cope`, generated C#, and generated JavaScript in the focused erasure proofs. The CLI repeats all three emissions byte-for-byte and preserves stale outputs while creating no fresh output after alias failure.

TSON receives the canonical nominal type. `type SettingsAlias = Settings` therefore retains `Settings`'s stable identity and existing encoding plan; the alias spelling appears neither in canonical TSON identity nor emitted artifacts. No TSON syntax, value kind, schema kind, carrier, or runtime projector was added.

## Exclusions and closure

M0b does not implement interfaces, `extends`, `implements`, type parameters, generic aliases/functions/records, constraints, unions, intersections, conditional or mapped types, `infer`, `keyof`, indexed access, type queries, template-literal types, anonymous runtime object types, classes, modules, static evaluation, CLR metadata/interoperation, reflection, JSON, new TSON variants, or CTS-JS-EMIT changes.

CTS-TYPE-M0b is honestly complete for transparent non-generic compilation-unit aliases. Later milestones must use this canonicalization boundary rather than adding alias identity to MIR or a backend.
