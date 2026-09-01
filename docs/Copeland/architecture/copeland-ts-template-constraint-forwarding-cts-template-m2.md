# CTS-TEMPLATE-M2 template constraint forwarding

## Previous failure

Template declarations already retained a `TypeParameterSymbol` with a
`RequirementSet`, but `Binder.Satisfies` extracted candidate fields only from
concrete classes, records, table rows, and structural objects. Passing an open
`TypeParameterTypeSymbol` therefore produced `COPE-REQUIREMENT-0005` even when
the active outer parameter had already proven the same requirement.

The evaluator had a second half of the same seam: a bound nested invocation
carried the outer open type parameter into the inner evaluation context instead
of substituting the outer concrete argument. Reflection could therefore observe
`T` rather than the concrete source type after binding was repaired.

## Current constraint model and evidence

`TypeParameterSymbol.Requirements` remains the sole evidence representation.
`RequirementSet.Interfaces` retains declared interface provenance and
`RequirementSet.Fields` is the finite structural capability set used by normal
satisfaction. Template constraints may originate from an interface, immutable
record, or structural `type` alias. No proof object or runtime witness exists.

Template requirement binding normalizes fields by name in declaration order.
Equivalent duplicates collapse; incompatible duplicate field types use the
existing `COPE-REQUIREMENT-0003`; aliases are already resolved to canonical
structural types. Entailment compares field types with
`TypeFacts.AreEquivalent`, the same relation used by ordinary interface
satisfaction. It does not compare source text or interface names as proof.

## Forwarding and substitution law

When a template argument is an active `TypeParameterTypeSymbol`,
`Binder.Satisfies` reads that parameter's existing normalized requirements as
the candidate field set. The destination is accepted exactly when every
required field is present with an equivalent type. Thus exact evidence and
stronger structural evidence pass; weaker or absent evidence fails.

Concrete outer instantiation still validates the actual class, record, table
row, or structural object normally. Type defaults are revalidated during the
template-plan phase, after class fields and table rows are fully bound, rather
than prematurely during template predeclaration.

At evaluation, nested invocation type arguments are substituted through the
active concrete type arguments before inner evaluation. Arrays, mutable arrays,
and Results recurse through the same bounded substitution. This preserves
multi-level forwarding and gives `fieldsOf<T>()` and `nameOf<T>()` the same
concrete metadata they receive under direct instantiation.

## Memoization and runtime erasure

The evaluator constructs its existing specialization key from the substituted
concrete types and static values. Constraint evidence and proof-object identity
are absent from the key. Equivalent forwarded calls therefore reuse the same
completed invocation; evidence ordering cannot create a second specialization.

The boundary remains:

```text
template binder/evaluator -> artifacts or static value
runtime MIR               -> no template evidence
JavaScript/C# backends     -> unchanged
```

No parser, runtime MIR, JavaScript backend, C# backend, reflection category, or
runtime interface changed.

## Diagnostics

Invalid forwarding produces one `COPE-REQUIREMENT-0011` at the nested
instantiation's `<` token. It names the source parameter, destination template,
missing or incompatible fields, destination requirement, and the source's known
constraints. Concrete satisfaction retains the existing `0005`-`0007` family.

## Non-goals

This milestone adds no syntax, `where` clause, interface inheritance, explicit
witness, theorem prover, generic inference change, runtime structural dispatch,
or open runtime carrier. Table-row forwarding uses the existing `Table.Row`
surface; existing reflection category limits are unchanged.
