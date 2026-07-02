# Machina Markdown Reading Style M15d

## Purpose

M15d introduces one explicit record for Oblivion/Machina Markdown reading surfaces so expanded document reading and inspector raw-source viewing share the same intentional readability contract.

## Why records instead of CSS

Machina styles are explicit immutable records, not CSS.

The presenter already uses typed C# style/theme data on purpose:

- deterministic defaults
- reviewable immutable shape
- no selector cascade
- no hidden cross-surface inheritance

## Style record

`OblivionMarkdownReadingStyle` now defines the shared Markdown reading-surface knobs:

- document surface
- document foreground
- muted/heading/link colors
- code surface and code foreground
- borders and scrollbar colors
- raw-source surface colors
- body/source line height and gap

## Default colors

The default style is dark-surface oriented because dark backgrounds are acceptable in this workbench, but the foregrounds are explicitly light and readable.

- document surface: dark blue-black
- document foreground: light near-white
- code surface: darker inset panel
- raw source surface: bounded code-like dark panel
- borders and scrollbar colors: explicit slate tones

Unreadable dark-on-dark defaults are not allowed.

## Typography knobs

M15d keeps the typography controls narrow and explicit:

- body line height
- body line gap
- source line height
- source line gap

This is enough to harden the reading surface without introducing a general-purpose style system.

## TOML readiness

The record shape is intentionally TOML-ready, but TOML loading is deferred in M15d.

That keeps the runtime hardening pass narrow while still giving the style a stable data shape that can be mapped later through the existing Tomlyn-based config patterns.

## Relationship to themes

The Markdown reading style is global-ish and shared, but it does not replace `StandardTheme`.

`StandardTheme` still owns the broader presenter shell and component defaults. The Markdown reading style composes with that theme for this specific document-reading surface.

## Non-goals

- CSS selectors
- cascading theme inheritance
- broad theme rewrite
- font pipeline rewrite
- Markdown editing
- execution/runtime notebook work

## Deferred work

- TOML read/write integration for the reading style
- richer typography controls if later earned by real reading pressure
- future theme composition only if additional reading surfaces actually need it
