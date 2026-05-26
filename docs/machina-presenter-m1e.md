# Machina Presenter M1e — visual polish with existing primitives

## Scope

M1e is a visual tuning milestone for `samples/Machina.Presenter.Sample` and existing `Machina.Standard` components.

This milestone deliberately stays inside current primitives and architecture:

- no new renderer primitives
- no rounded corners or shadows
- no anti-aliasing or real font backend
- no runtime interaction model changes

## What changed

### Presenter composition

The sample now renders into an explicit surface wrapper and places the settings card with deterministic top/left offsets instead of starting directly at the root origin.

- render area: `640 x 360`
- explicit muted surface background
- explicit `VSpace` + `HSpace` offsets before the card
- clearer card width/height for this sample composition

### Text sizing choices

The sample now uses smaller text for state lines and control labels where readability benefits from reduced chunking in the current bitmap text renderer.

### Component tuning

Using existing style fields only:

- button padding scale adjusted for better proportion
- checkbox shell size tuned to 18x18 and label text uses `TextSize.Sm`
- switch track/thumb dimensions tuned, with deterministic track/thumb border styling for clearer state presentation

## Limitations retained after M1e

M1e remains intentionally flat and deterministic.

Still not included:

- rounded corners
- real typography engine
- anti-aliased vector text
- hover/pressed/focus visual states
- animated switch/checkbox transitions
- DPI-specific visual polish beyond existing coordinate mapping work

## Outcome

M1e moves the presenter sample from prototype-like debug composition toward a simple, intentional desktop panel look while preserving current package boundaries and deterministic output expectations.
