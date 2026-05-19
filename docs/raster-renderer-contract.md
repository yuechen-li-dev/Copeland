# Machina.Renderer.Raster M0a Contract

`Machina.Renderer.Raster` is a dependency-free CPU raster pixel backend for Machina M0a.

## Supported operations

M0a supports only:

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

Filled pixels are all `(x, y)` where:

- `x` is in `[left, right)`
- `y` is in `[top, bottom)`

Bounds are clipped to the surface rectangle:

- `x` in `[0, surface.Width)`
- `y` in `[0, surface.Height)`

Zero or negative rect sizes are no-op. Non-finite rect numbers are rejected.

## Alpha model

M0a stores non-premultiplied RGBA in `Rgba32`.

`FillRect` blends source-over destination with deterministic integer math using premultiplied intermediates, then writes non-premultiplied output channels.

## PPM output contract

`PpmWriter.WriteP6` writes:

1. `P6\n`
2. `<width> <height>\n`
3. `255\n`
4. row-major RGB payload bytes

PPM output ignores alpha and writes stored RGB channels directly.

## Explicitly out of scope in M0a

- Dominatus integration
- render-command consumption (`FillRectCommand`, `RenderSnapshot`)
- text rendering and font systems
- PNG/image loading
- strokes, borders, rounded rectangles, transforms
- clipping stacks beyond surface-bound clipping
- windows/presenters/live presentation
- GPU/Vulkan/Skia/MonoGame/Avalonia/Stride backends
- hit testing, input, animation
- Machina UI/layout tree walking

## M0b update

A Dominatus raster adapter now exists in `Machina.Renderer.Raster.Dominatus` for `BeginFrame`/`FillRect`/`EndFrame` command actuation and PPM output through completed raster frames.
