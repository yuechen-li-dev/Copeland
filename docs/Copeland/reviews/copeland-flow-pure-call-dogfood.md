# CTS-FLOW-M2 dogfood review

## Result

Outcome A: existing effect semantics compose cleanly with FLOW updates. The
maintained 8-state / 7-event burn-in now uses the natural helper form at both
sequence-update sites:

```ts
board.sequence = nextSequence(board.sequence);
```

The prior inline workaround is gone. Direct, nested, multiple-argument,
named-record-returning, closed-generic, recursive-classifier, and associated
pure calls bind through ordinary `BoundCallExpression` and lower to the existing
`MirCallExpression`.

## Generated code and runtime

Diagnostic JavaScript contains one direct call per authored update, equivalent
to `nextSequence(board["sequence"])`. It adds no state machine, closure, host
indirection, or helper-body duplication. C# emits the corresponding direct
`CopelandModule.nextSequence(...)` call. The focused Node and Roslyn-compiled C#
runtime tests execute the same helper-call transition and observe
`Transitioned`, the target state, and board value `1`.

Ordinary argument emission remains left-to-right and each generated call site
contains each authored argument once. Existing update sequencing is untouched:
after each immutable board replacement, the next authored update reads that
new board snapshot.

## Negative qualification

The adversarial matrix covers transitive `LocalMutation`, CLR/inline-C#
`HostInterop`, and indirect `UnknownCall`. Each receives `COPE-FLOW-0024` with
the classified reason. Pure calls in guards remain rejected by the distinct
`COPE-FLOW-0018` law. Imported calls without a module-local summary also remain
fail-closed.

## Cost and remaining pressure

Qualification is a dictionary lookup plus bounded expression traversal per
update after the existing cached fixed-point function classification. The
maintained burn-in tool's single warmed Flow measurement moved from 15.9091 ms
to 16.9009 ms (+0.9918 ms, about 6.2%). That one-shot result is a coarse signal,
not a benchmark; it did not justify optimization.

The next independent language pressure remains constrained template type
evidence forwarding. It should not be mixed into FLOW effects.
