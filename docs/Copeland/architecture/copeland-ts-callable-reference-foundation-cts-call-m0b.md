# CTS-CALL-M0b callable reference foundation

CTS-CALL-M0b introduced the reference foundation. It is superseded by [CTS-CALL-M1 complete callable semantics](copeland-ts-complete-callable-semantics-cts-call-m1.md), which adds lifted arrows, explicit immutable environments, and callable storage while preserving this direct-call/reference distinction.

## Source and type law

Callable types use `(name: Type, ...) => ReturnType`. Names are retained for diagnostics only; compatibility compares ordered canonical parameter types and canonical return type exactly. Aliases erase to the same canonical callable type. Callable types have a 32-parameter limit and a nesting limit of 16.

An unshadowed nongeneric function name in value position produces a callable reference. An open generic function name is rejected; `functionName<T>` produces a reference to the existing closed specialization. A lexical variable shadows a same-named function in both value and callee positions. Direct named `functionName(args)` remains a direct call; a callable-valued callee is a first-class invocation.

Callable values are permitted only as locals, parameters, and returns. Callable field, enum payload, interface, array, Result-container, table, and equality uses are rejected.

## Canonical lowering

The bound model distinguishes `BoundCallExpression`, `BoundFunctionReferenceExpression`, and `BoundInvokeExpression`. MIR preserves that distinction with `MirCallExpression`, `MirFunctionReferenceExpression`, and `MirInvokeExpression`. A reference names the existing function/specialization identity and carries an exact `MirCallableType`; it creates no wrapper body or environment.

## Backends

C# demand-emits one delegate declaration per exact canonical signature and uses method-group conversion for function references. JavaScript emits frozen null-prototype carriers whose provenance, signature, and code references live in backend-private weak collections. Invocation checks provenance and signature before calling an existing generated top-level function.

## Next boundary

M0b's remaining evidence rows are absorbed by CTS-CALL-M1; no separate M0c or M2 milestone remains.
