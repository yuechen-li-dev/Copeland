# CTS-TSON-ARRAY-M1 runtime array encoding

CTS-TSON-ARRAY-M1 extends the existing runtime canonical encoder with structural `MirTsonArrayPlan` nodes and shared validation. It keeps one nominal record or payload-enum root, supports only Boolean, Number, String, Record, Enum, and nested array elements, and retains the existing two-case `TsonEncodeError` model.

Generated C# and JavaScript use direct indexed traversal over ordinary mutable `T[]` and JavaScript arrays. Each entry captures the receiver and length once, checks the shared 100,000-element ceiling before traversal, and reads indices once in ascending order. Empty arrays retain plan schema evidence. Canonical schema text remains ordinary `T[]`; canonical values retain M0b multiline array formatting.

The runtime has no parser, filesystem, compiler-host TSON dependency, JSON, reflection, `dynamic`, property enumeration, root arrays, TSON Results/tables/optionals/interfaces/aliases, package change, commit, push, or publication.
