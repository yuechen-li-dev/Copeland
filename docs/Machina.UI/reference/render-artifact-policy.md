# Render Artifact Policy (M0e)

M0e introduces deterministic raster render artifacts for inspection and regression testing.

## Artifact format

- Binary PPM (`P6`) is the first deterministic render artifact format.
- Raster artifacts are generated from the real UI->layout->bridge->raster command path.
- PPM payload is row-major RGB bytes.
- Alpha is not serialized in PPM (stored RGBA alpha is ignored by the PPM format).

## Golden checksum tests

- Golden tests render small, stable samples and compute SHA256 over full PPM bytes.
- The hash assertions intentionally lock down deterministic output.
- Golden samples should remain tiny and explicit (fixed dimensions, fixed colors, fixed text).
- Hash updates must only happen for intentional rendering behavior changes.

## Optional artifact writing

- Artifacts are opt-in.
- Set `MACHINA_WRITE_RENDER_ARTIFACTS=1` to write `.ppm` files during golden tests.
- Files are written to `artifacts/render/m0e/` with deterministic names.
- Tests must pass both with and without artifact writing enabled.

## Text rendering note

- M1a golden text uses `ReadableBitmapTextRasterizer` (pipeline default).
- This is deterministic readable bitmap text, still not real typography.
- Golden changes from text behavior should be treated as renderer-contract changes.

## M1b border/stroke artifact coverage

Golden artifacts now cover rectangular stroke output in addition to fill/text behavior where applicable.

The `standard-card` golden includes border-enabled output in M1b and protects against regressions in stroke command rendering.

Rect-only and text-only goldens remain unchanged unless their command output is genuinely affected by stroke metadata.
