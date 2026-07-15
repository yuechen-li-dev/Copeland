# Copeland TS interfaces and explicit closed generics (CTS-TYPE-M1b)

**Status:** implemented bounded frontend feature with closed-MIR/backend erasure. Interfaces and open generics remain frontend-only facts. [CTS-TYPE-M2a](../language/copeland-ts-generic-inference-design-cts-type-m2a.md) is the accepted documentation-only design for a future direct-argument inference slice; ordinary calls still require explicit type arguments today. The repository now has strong frontend/runtime/CLI evidence for the implemented slice, but this document does not claim that every broader closeout checklist item outside that slice has been independently re-ratified.

## Scope

CTS-TYPE-M1b adds:

- field-only contextual `interface` declarations
- named generic functions
- explicit closed type arguments only
- unconstrained parameters as empty requirement sets
- interface constraints with constraint-local `&`
- one-time generic body binding
- deterministic closed specialization before MIR

It does not add inference, generic nominal declarations, interface storage/runtime behavior, generic-to-generic calls, or generic recursion. Direct inference remains a future M2b implementation under the M2a evidence and resource boundary.

## Grammar

```text
InterfaceDeclaration ::= `interface` Identifier `{` InterfaceField+ `}`
InterfaceField       ::= Identifier `:` Type `;`

TypeParameterList ::= `<` TypeParameter (`,` TypeParameter)* `>`
TypeParameter     ::= Identifier
                    | Identifier `extends` RequirementList
RequirementList   ::= InterfaceName (`&` InterfaceName)*

GenericCall ::= Identifier `<` Type (`,` Type)* `>` `(` Arguments `)`
```

`interface` and `extends` are contextual identifiers. `&` is tokenized only after the lexer prefers `&&`, and M1b recognizes it only inside constraint parsing.

## Requirement law

Interfaces are erased requirement sets. They enter the compilation-unit type namespace, not the value namespace. They never reach MIR, C#, JavaScript, TSON schema identity, or runtime metadata.

Initial interfaces:

- require one or more semicolon-terminated readable fields
- reject empty declarations
- reject methods, optional fields, initializers, accessors, inheritance/composition, `implements`, and generic interfaces
- reject storage positions, record fields, array/result components, and alias targets

Constraint normalization is authored-order and field-order preserving:

- repeated named interfaces are diagnostics
- equivalent same-name fields merge
- conflicting same-name fields are diagnostics
- extra candidate fields are ignored
- only records and table rows satisfy nonempty requirements

## Bind-once and specialization law

Generic bodies bind once against `TypeParameterTypeSymbol` and `RequirementSet`. Requirement-proven member reads bind as `BoundRequirementFieldAccessExpression`.

Closed specialization:

1. canonicalizes each closed type argument
2. checks requirement satisfaction
3. interns a stable specialization identity
4. substitutes over the already-bound body
5. rewrites requirement reads to ordinary record or table-row field access
6. lowers only the resulting closed ordinary body to MIR

No source body is reparsed or rebound per instantiation.

## Stable identity

Semantic instantiation identity is:

```text
generic function stable identity
+ ordered canonical closed type identities
```

Canonical closed type identities are UTF-8 text over:

- `primitive:number`, `primitive:string`, `primitive:boolean`, `primitive:void`
- `record:<stable record identity>`
- `table:<stable table identity>`
- `row:<stable row identity>`
- `enum:<stable enum identity>`
- `column(...)`, `array(...)`, `result(...,...)`

Specialized function names are deterministic display names plus the first sixteen hexadecimal characters of `SHA-256(UTF-8(identityText))`. The cache key remains the full canonical identity text. The current name map detects a conflict and throws an invariant failure rather than silently merging; [CTS-TYPE-M2a](../language/copeland-ts-generic-inference-design-cts-type-m2a.md#specialization-name-hash-audit) requires M2b to stabilize this as collision-safe allocation or a deterministic frontend diagnostic.

This removes the earlier `sum__r1`-style dependence on record/table traversal ordinals for generic specialization identity.

## Resource limits

M1b currently enforces these frontend bounds:

- 8 type parameters per generic function
- 8 required interfaces per type parameter
- 32 normalized requirement fields per type parameter
- 128 total interface fields per compilation
- 16 closed-type nesting depth
- 16 closed instantiations per generic definition
- 128 closed instantiations per compilation
- 4 listed fields in a single requirement diagnostic before `(+N more)`

Exceeding a bound is a frontend diagnostic and produces no MIR/backend artifact.

## Recursion policy

M1b supports ordinary nongeneric calls inside generic bodies when type-correct.

M1b rejects:

- generic-to-generic calls from inside a generic body
- generic self-recursion
- expansive specialization by construction

Open generic calls never reach MIR.

## MIR/backend/TSON boundary

Successful M1b programs emit only existing closed MIR:

- no interface MIR definitions or types
- no requirement sets
- no type parameters
- no open generic functions or calls
- no requirement-access MIR nodes

C# and JavaScript consume the closed MIR exactly as ordinary specialized functions. They emit no source-visible interfaces, runtime carriers, brands, wrappers, validators, or metadata.

TSON remains concrete-only. A closed generic function may feed existing `tsonEncode` only when the result is already a legal concrete TSON root by pre-existing law.

## Requirement ledger

| Requirement | Status | Evidence |
| --- | --- | --- |
| Field-only interface declarations | Satisfied | parser/binder/frontend tests and language fixtures |
| Explicit closed generic calls | Satisfied | parser/binder/frontend tests, runtime parity, CLI |
| Unconstrained generics | Satisfied | frontend tests, valid fixtures, runtime parity |
| Constrained record satisfaction | Satisfied | frontend tests, fixtures, runtime parity |
| Constrained table-row satisfaction | Satisfied | frontend tests, fixtures, runtime parity |
| Bind-once body law | Satisfied | dedicated open-body bound-node test and undeclared-member rejection |
| Closed substitution before MIR | Satisfied | MIR rewrite tests and runtime parity |
| Stable specialization identity independent of declaration order | Satisfied | dedicated identity-stability test |
| Deterministic specialization naming | Satisfied | SHA-256 naming law plus repeated-compile tests |
| Resource limits | Satisfied | diagnostic inventory and focused boundary tests |
| Generic-to-generic/recursion exclusion | Satisfied | diagnostics, fixtures, and policy tests |
| Closed MIR/backend erasure | Satisfied | MIR assertions, runtime parity, CLI artifact checks |
| C#/Node parity | Satisfied | dedicated runtime parity matrix |
| Closed generic result into existing TSON | Satisfied | valid fixture and concrete-only boundary |
| Inference/defaults/generic nominal declarations | Accepted-scope exclusion | M1b does not authorize them |

Within the implemented bounded M1b contract above, there are no `Missing` rows. Broader project-closeout bookkeeping still belongs to the migration record and final validation report.
