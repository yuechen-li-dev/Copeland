# Maintaining documentation

The reading order is [docs home](../README.md) → [Start here](../getting-started.md)
→ subsystem authoring guide → task reference or runnable example. Keep that route
short. Authors should not need migration reports or parser source for normal work.

## Place and classify a page

| Page | Home and obligation |
| --- | --- |
| Onboarding | Subsystem index/authoring guide; explain concepts before implementation details, include a concrete example and commands |
| Current reference | Owning subsystem's language/reference/tooling section; state exact behavior, diagnostics and supported limits |
| Current status | Owning subsystem index/status page; distinguish implemented, bounded, planned and unqualified behavior |
| Architecture | Owning architecture section; label current versus target state and identify the authority boundary |
| Executable example | Owning sample/game README; prerequisites, command, expected observable result and source map |
| Historical evidence | History/reviews/audits/migrations; preserve the original observation and link forward to the current owner |
| Generated output | Local ignored artifacts; follow [retention](artifact-retention.md) |

Before adding a page, find the existing owner and update it. Avoid another
milestone-named current reference for the same concept. If a referenced page must
move, update inbound links or keep a short redirect at its old path. A filename
containing a milestone does not alone justify deletion or semantic renaming.

## Make a guide usable

Explain what the reader is building, what declarations mean, and where state/data
ownership lives. Give complete minimal examples, prerequisites, commands and
expected results. Link to deeper reference at the point it becomes relevant.
A source/test link substantiates the rule; it does not replace explaining it.

Validate examples against the current compiler/API and their real execution path
when changing code examples. A package version, native renderer or model proof
needs its own qualification; do not promote plans into capability claims. Keep
release status distinct from current source behavior. State an unsupported case
and its closest supported alternative directly.

Every current page should be reachable from its subsystem index and should offer
a clear next step or return link. Use descriptive relative Markdown links rather
than backticked filenames as navigation. Check changed pages' local file targets
and heading anchors, plus `git diff --check`. Historical paths in quoted logs or
recovery records are historical data, not links to silently rewrite.
