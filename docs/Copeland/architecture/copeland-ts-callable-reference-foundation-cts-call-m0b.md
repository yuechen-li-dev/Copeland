# CTS-CALL-M0b callable reference foundation

CTS-CALL-M0b introduces first-class references to compiler-owned named functions. It does not introduce callable bodies, arrow syntax, capture, environments, containers, equality, or host interop.

## Source and type law

Callable types use `(name: Type, ...) => ReturnType`. Names are retained for diagnostics only; compatibility compares ordered canonical parameter types and canonical return type exactly. Aliases erase to the same canonical callable type. Callable types have a 32-parameter limit and a nesting limit of 16.

An unshadowed nongeneric function name in value position produces a callable reference. An open generic function name is rejected; `functionName<T>` produces a reference to the existing closed specialization. A lexical variable shadows a same-named function in both value and callee positions. Direct named `functionName(args)` remains a direct call; a callable-valued callee is a first-class invocation.

Callable values are permitted only as locals, parameters, and returns. Callable field, enum payload, interface, array, Result-container, table, and equality uses are rejected.

## Canonical lowering

The bound model distinguishes `BoundCallExpression`, `BoundFunctionReferenceExpression`, and `BoundInvokeExpression`. MIR preserves that distinction with `MirCallExpression`, `MirFunctionReferenceExpression`, and `MirInvokeExpression`. A reference names the existing function/specialization identity and carries an exact `MirCallableType`; it creates no wrapper body or environment.

## Backends

C# demand-emits one delegate declaration per exact canonical signature and uses method-group conversion for function references. JavaScript emits frozen null-prototype carriers whose provenance, signature, and code references live in backend-private weak collections. Invocation checks provenance and signature before calling an existing generated top-level function.

## Next boundary

`CTS-CALL-M0c` may add noncapturing arrow expressions, lifted callable definitions, implicit-capture diagnostics, and conditional/match/container flow. `CTS-CALL-M1` owns explicit `capture { ... }` and immutable environments.
