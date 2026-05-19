# Machina.Renderer.Raster Contract

`Machina.Renderer.Raster` is a dependency-free CPU raster pixel backend.

## Supported operations

- `RasterSurface` pixel buffer allocation and indexed access
- `Rgba32` non-premultiplied RGBA color representation
- `Rasterizer.Clear`
- `Rasterizer.FillRect`
- deterministic source-over alpha blending
- binary PPM (`P6`) encoding via `PpmWriter.WriteP6`

## Rect rasterization rule

Rectangles are converted to integer pixel bounds with deterministic rounding:

- `left = floor(X)`
- `top = floor(Y)`
- `right = ceil(X + Width)`
- `bottom = ceil(Y + Height)`

## Clipping behavior

`FillRect` always clips to surface bounds.

M0d also supports optional rectangular clip input for `FillRect`; when provided, the draw region is intersected with:

1. rect pixel bounds
2. clip pixel bounds
3. surface bounds

Zero/negative rect size or clip size are no-op.
Non-finite rect or clip numbers are rejected deterministically.

## Alpha model

`FillRect` blends source-over destination with deterministic integer math using premultiplied intermediates, then writes non-premultiplied output channels.

## PPM output contract

`PpmWriter.WriteP6` writes:

1. `P6\n`
2. `<width> <height>\n`
3. `255\n`
4. row-major RGB payload bytes

PPM output ignores alpha and writes stored RGB channels directly.

## Out of scope

- Dominatus integration
- text shaping/wrapping/font loading
- strokes, borders, rounded rectangles, transforms
- hit testing, input, animation
- GPU backends
