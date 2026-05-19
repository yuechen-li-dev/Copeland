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

- M0e golden text uses `DebugBitmapTextRasterizer`.
- This is deterministic debug text, not real typography.
- Golden changes from text behavior should be treated as renderer-contract changes.
