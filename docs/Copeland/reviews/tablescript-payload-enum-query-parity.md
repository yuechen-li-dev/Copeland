# INFRA-M10A TableScript payload-enum query parity

## Outcome

Outcome A for equality. TableScript queries now compare generated nominal enum values, including payloads, instead of requiring flattened textual cases. Both forms work:

```powershell
tscl table query Schedule.obj.ts Schedules --where "day == Day(6)" --format json
tscl table query Schedule.obj.ts Schedules --where "day == ScheduleDay.Day(6)" --format json
```

The first form receives `ScheduleDay` context from the `day` column. The second verifies the qualification against that same nominal type. Both lower to a generated nominal record construction and use the C# backend's structural record equality for case and payload values. They do not stringify TSON.

## Failure and fix

The source parser already accepted both expressions. Failure occurred in `TableQueryBinder.NormalizePredicate`: it knew query columns and only zero-payload enum cases. `Day(6)` failed as unknown name `Day`; the qualified form failed as unknown name `ScheduleDay`. TSON loading, table schema binding, MIR table constants, and generated enum representation were already correct.

The binder now walks the parsed expression around equality, obtains the contextual enum from the column symbol, validates the ordinary `EnumTypeSymbol` and `EnumCaseSymbol` metadata, validates payload arity and literal types, and emits the existing generated nominal value. There is no runtime reflection, string surrogate, payload accessor, new parser syntax, or alternate TSON representation.

Focused diagnostics are:

- `COPE-TABLE-QUERY-0030`: wrong nominal enum type.
- `COPE-TABLE-QUERY-0031`: unknown case.
- `COPE-TABLE-QUERY-0032`: wrong payload arity, including called zero-payload cases and uncalled payload cases.
- `COPE-TABLE-QUERY-0033`: wrong literal payload type.

## TSON and schema law

`TsonEnum` remains a dedicated semantic value with enum identity, case identity, case name, and ordered payload values. Canonical printing remains `ScheduleDay.Day(...)`; no JSON-like `{ case, value }` bridge was introduced. Schema output continues to report the nominal column type `ScheduleDay` and now includes payload-bearing case signatures such as `Day(int)` instead of silently omitting them.

Canonical numeric TSON spells values as `$number("...")`. The ordinary compiler path used by generated table queries did not previously bind that existing canonical intrinsic, so canonical `.tson` could reload through `TsonDocumentReader` but not pass through query compilation. The compiler now binds finite canonical `$number` values to its existing float literal representation. This is canonical TSON compatibility, not new user enum syntax.

The TSON profile currently has one numeric value family (`number`), while ordinary Copeland tables can use `int`. Tests therefore cover `ScheduleDay.Day(int)` directly in ordinary table queries and the same payload-enum identity/case/payload roundtrip with a TSON `number` payload. Host validation can retain the integral day law.

## Compatibility

Regression coverage includes contextual and qualified payload equality, wrong enum/case/arity/type diagnostics, zero-payload enums, string/bool/int/number comparisons, compound predicates, row reorder, canonical TSON roundtrip then query, CLI validate, truthful schema output, and a Mara/Day(6) schedule fixture. Existing Option construction/match and Result behavior continue through the same enum/MIR machinery; this change adds no Option or Result feature and the full compiler suite remains the authority for those spot checks.

Ordinary `match` remains unchanged. The ad-hoc query backend does not yet lower match expressions; adding that lowering was secondary and is not necessary for equality parity. No query-only pattern syntax was added.

Recommendation: `MIGRATE_TINYFARM_TO_PAYLOAD_ENUM_NEXT`. The production content migration is small but should remain a separate TinyFarm content milestone because its host model currently stores an integral day discriminator and the TSON schema expresses the payload as `number`.
