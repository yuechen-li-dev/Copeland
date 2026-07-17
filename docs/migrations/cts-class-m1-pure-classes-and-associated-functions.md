# CTS-CLASS-M1: pure classes and associated functions

**Status:** completed.

CTS-CLASS-M1 implements the accepted M0a pure-class law end to end. A source class is a controlled immutable nominal record, pure primary constructor, associated-function namespace, and privacy/invariant boundary; it is not a JavaScript prototype or conventional mutable object.

The frontend adds deliberate class/member syntax, class/value provenance, visibility checks, constructor-contextual literals, private field/function access checks, qualified lookup, instance-call diagnostics, class-only `with`, constructor Result typing, and bounded class limits. Classes reuse nominal record construction/access/update and ordinary call/callable/Result operations rather than adding an OO execution model.

MIR keeps ordinary record and function nodes, with a narrow class-origin marker on record definitions and field visibility for malformed-MIR validation and backend carrier shape. C# realizes a sealed complete get-only carrier and static generated functions. JavaScript uses private tokens/slots, WeakSet provenance, frozen null-prototype values, and generated functions without emitting JavaScript classes or prototypes.

The milestone adds valid and invalid class fixtures plus focused C# compilation/runtime and Node Diagnostic/Symbolic runtime evidence. It also blocks classes at TSON and table boundaries, nominal unions, and equality. No production defect outside the class surface was discovered during the focused implementation.

The canonical closeout is [CTS-CLASS-M1 architecture](../Copeland/architecture/copeland-ts-pure-classes-cts-class-m1.md). The [M0a design](../Copeland/language/copeland-ts-pure-classes-design-cts-class-m0a.md) remains the accepted semantic authority and its audit is historical context.
