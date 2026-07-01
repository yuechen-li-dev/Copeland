# Layout interpolation (M4a)

MachinaLayout includes pure helpers for interpolating **already resolved** layout documents:

- `lerpNumber(a, b, t)`
- `lerpRect(a, b, t)`
- `lerpResolvedLayouts(a, b, t)`

## Boundary

Interpolation happens after layout resolution. MachinaLayout remains geometry authority, but it does **not** own animation timing.

Typical flow:

1. Author collapsed rows and resolve.
2. Author expanded rows and resolve.
3. Drive `t` from your own animation source (spring/RAF/timeline).
4. Call `lerpResolvedLayouts(collapsed, expanded, t)`.
5. Render the result via `MachinaReactView`.

## Compatibility requirement

`lerpResolvedLayouts` only supports compatible resolved documents:

- same `rootId`
- same node id set
- same parent/children structure
- same child ordering for every parent
- root id exists in both node maps

If compatibility fails, it throws `MachinaLayoutError` with code `"IncompatibleLayouts"`.

## Numeric behavior

- linear interpolation only: `a + (b - a) * t`
- no clamping and no rounding
- `t < 0` and `t > 1` are allowed (overshoot supported)
- non-finite numbers throw `"NonFiniteNumber"`

## Metadata behavior

`lerpResolvedLayouts` interpolates only `rect` fields. Other node metadata is preserved from the **end** layout `b` so `t=1` matches `b` structurally and semantically.

## Current limitations

M4a does not support:

- graph morphing (enter/exit nodes)
- reordering children during interpolation
- metadata interpolation (`z`, `view`, `frame`, etc.)
- text/style/color interpolation
- animation loops, timers, or spring ownership
