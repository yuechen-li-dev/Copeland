# Machina Playback Scenario Format M16a

## Purpose

This document defines the first TOML playback scenario format for internal Machina presenter interaction playback.

The format is deterministic, sample-local, and intended for checked-in review artifacts.

## File extension

M16a scenarios use:

```text
*.machina-playback.toml
```

## Scenario section

`[scenario]` contains identity plus initial presenter state.

Required fields:

- `id`
- `name`
- `viewport = { width = ..., height = ... }`
- `section`
- `tab`

Currently supported optional fields:

- `selectedCard`
- `expandedCard`
- `expandedCardBodyScroll`
- `inspectorScroll`
- `inspectorRawSourceScroll`
- `mainStackScroll`

## Output section

`[output]` controls artifact generation.

Supported fields:

- `captureFinalPng`
- `captureTraceJson`
- `captureManifest`

## Step schema

Each `[[steps]]` entry requires `type`.

Supported step kinds:

- `wait`
  - `ms`
- `click`
  - `target` and optional `card`
  - or `point = { x = ..., y = ... }`
- `wheel`
  - `target`
  - optional `card`
  - `deltaY`
- `key`
  - `key`
- `drag`
  - `target`
  - optional `card`
  - either normalized `from` and `to`
  - or point `from = { x = ..., y = ... }` and `to = { x = ..., y = ... }`

## Assertion schema

Each `[[assertions]]` entry requires:

- `type`
- `reason`

Assertion-specific fields depend on the assertion kind.

Supported assertion kinds:

- `selected-card`
- `card-expanded`
- `scroll-offset-changed`
- `scroll-offset-equals`
- `scroll-offset-greater-than`
- `shell-mode`
- `region-exists`

## Assertion reason policy

Every assertion must include a non-empty human-readable `reason`.

The reason is not optional commentary. It is part of the artifact contract because future readers need to know what behavior the assertion protects and why it matters.

Assertions without reasons are invalid and must be rejected by the parser.

## Supported targets

Current M16a semantic targets:

- `main-stack`
- `card-header`
- `expanded-body`
- `inspector-pane`
- `raw-source`
- `main-stack-scrollbar-thumb`
- `expanded-body-scrollbar-thumb`
- `inspector-scrollbar-thumb`
- `raw-source-scrollbar-thumb`

## Supported assertions

Current M16a assertions:

- `selected-card`
  - asserts the final selected card id
- `card-expanded`
  - asserts whether a specific card is expanded
- `scroll-offset-changed`
  - asserts a target scroll offset changed from the initial captured value
- `scroll-offset-equals`
  - asserts a target scroll offset equals a specific value
- `scroll-offset-greater-than`
  - asserts a target scroll offset is above a threshold
- `shell-mode`
  - asserts the resolved shell mode
- `region-exists`
  - asserts a semantic region resolves successfully

## Examples

```toml
[scenario]
id = "oblivion-expand-scroll-collapse"
name = "Expand a Markdown card, scroll body, collapse"
viewport = { width = 1280, height = 720 }
section = "oblivion"
tab = "docs"
selectedCard = "doc-aurelian-monorepo-import-audit-m13a"

[output]
captureFinalPng = true
captureTraceJson = true
captureManifest = true

[[steps]]
type = "click"
target = "card-header"
card = "doc-aurelian-monorepo-import-audit-m13a"

[[steps]]
type = "wheel"
target = "expanded-body"
card = "doc-aurelian-monorepo-import-audit-m13a"
deltaY = 360

[[steps]]
type = "key"
key = "Escape"

[[assertions]]
type = "card-expanded"
card = "doc-aurelian-monorepo-import-audit-m13a"
value = false
reason = "Escape should collapse the selected expanded Markdown card so keyboard users can leave reading mode without using the mouse."
```

## Validation errors

Examples of intentional parser failures:

- missing `[scenario]`
- missing required scenario fields
- unsupported initial-state field combinations
- unknown step type
- unknown assertion type
- assertion without `reason`
- assertion with empty `reason`
- target-dependent assertion without the required target/card context
