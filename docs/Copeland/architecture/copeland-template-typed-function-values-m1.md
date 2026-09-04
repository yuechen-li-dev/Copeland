# Copeland typed function values in templates

## Before M1

The bound template plan could construct and type-check literals, structural
objects, arrays, reflection metadata, artifact values, and nested template
specializations. The ordinary static evaluator separately supported pure
Copeland functions, records, payload enums, immutable arrays, bounded loops,
memoized calls, and deterministic limits. A template call site nevertheless
accepted only artifact constructors or another template, so a template could
not reuse an ordinary static-safe function returning a non-artifact semantic
value.

## M1 generalization

`BoundTemplateOrdinaryExpression` retains a normal, fully bound Copeland
expression in the template plan. At specialization time the existing
`StaticEvaluator` evaluates it with the template's immutable parameters and
locals. Its existing effect summaries, call cache, recursion guard, step/depth,
loop, allocation, array-length, and embedded-value limits remain authoritative.

The public template result can now be a `TemplateTypedValue` as well as a
filesystem artifact or Diagram. It carries its normal `TypeSymbol`, value, and
deterministic hash. Arrays are contextually bound from their declared result or
local type, so named record arrays and payload-enum arrays retain their real
types. Mixed element types use the normal template diagnostic path.

```text
ordinary expression binding
  -> ordinary FunctionSymbol and record/enum types
  -> existing effect classification
  -> existing StaticEvaluator
  -> typed immutable template value
```

There is no source-text result, AST result, reparse callback, runtime witness,
or domain-specific evaluator. Template specialization still does not permit
runtime/host effects, recursive expansion, unbounded template statements, or a
mutable value to cross the static boundary.

## Staging and backends

Template declarations remain compile-time structural input and are rejected by
ordinary runtime MIR compilation with `COPE-TEMPLATE-0006`. A typed template
value must be consumed by a compile-time host such as Profile or an artifact
materializer. C# and JavaScript therefore never receive `ProfileOperation` or
another compiler-only semantic value.
