# CTS-TYPE-M2b bounded direct-argument inference

CTS-TYPE-M2b is closed. It implements the direct generic-call inference path without extending the language's type-system surface. A call with no explicit type arguments is inferred locally from its value arguments and declared parameter types. Explicit closed calls continue to use the M1b path. [CTS-TYPE-M3](copeland-ts-foundational-type-system-closeout-cts-type-m3.md) is the final canonical authority and records the foundational closeout evidence.

## Algorithm and boundary

The binder first binds independently typable arguments exactly once and matches each actual type against its declared generic parameter pattern. Matching uses an explicit worklist, bounded to depth 16 and 128 steps per call. It only decomposes `Array`, `Result`, and `column`; records, enums, tables, and table rows are atomic. A type-parameter slot accepts at most 16 canonically equivalent evidence entries. The first disagreement is one conflict diagnostic; no union, common-type, backtracking, overload, return-context, or constraint-based inference is attempted.

Empty arrays, record literals, and bare `ok`/`err` constructors are deferred rather than speculatively bound. Once every slot has a candidate and requirements validate, the existing closed-instantiation factory substitutes parameter types and binds each deferred argument once with that expected type. If evidence is missing, the binder recommends explicit arguments. Requirements validate candidates; they never create them.

## Erasure and identity

Candidates are already canonical semantic types, so transparent aliases are provenance only. Inferred and explicit calls invoke the same closed-instantiation factory keyed by the full canonical semantic identity; no inference object reaches MIR, C#, JavaScript, or TSON. Existing requirement specialization and TSON schema planning therefore continue to see only concrete types.

Generated names retain the readable 16-hex digest suffix where possible. A display-name collision expands deterministically through 24, 32, and full SHA-256 suffixes, then uses an escaped semantic-identity fallback. The specialization cache remains keyed solely by full canonical identity, and forced-collision tests cover the sorted collision-group allocation seam.

## Exclusions

Inference is unavailable in generic bodies, including generic recursion. Partial explicit arguments, return-context inference, overload search, best-common-type synthesis, and generic-to-generic calls remain unsupported.
