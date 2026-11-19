# Neural texture qualification

The Vulkan proof compares a two-layer VTS decoder with hardware BC7 sampling. Both controls pack albedo and roughness into one BC7 image. The second image stores normals as BC7 or BC5. The proof captures level-zero texels and a bilinearly filtered oblique material view using the shared lighting functions.

The input sets are [Brick Wall 001](https://polyhaven.com/a/brick_wall_001), [Oak Wood Planks](https://polyhaven.com/a/oak_wood_planks), [Rock Boulder Cracked](https://polyhaven.com/a/rock_boulder_cracked), and [Wood Planks Dirt](https://polyhaven.com/a/wood_planks_dirt). Put each material's 1K PNG diffuse, OpenGL normal, and roughness maps in its own input directory as `albedo.png`, `normal.png`, and `roughness.png`. The source maps use the [Poly Haven CC0 license](https://polyhaven.com/license).

With Python, NumPy, SciPy, Pillow and [DirectXTex texconv](https://github.com/microsoft/DirectXTex/releases/tag/may2026), run these commands from the repository root.

```powershell
$experimentRoot = "artifacts/local/rendering-r8-neural-textures"
python tools/aurelian-rendering-reference/fit_neural_textures.py "${experimentRoot}/tools/texconv.exe" "${experimentRoot}/input" "${experimentRoot}/fitted"
dotnet run --project tools/Aurelian.GraphicsProof -c Release -- --rendering-neural-textures --codec-fixtures "${experimentRoot}/fitted" --output "${experimentRoot}/qualified"
```

The proof emits source-channel PSNR, normal-angle error, shaded-image PSNR, CPU decoder agreement, and GPU timestamps for dense and sparse samples. `PayloadBytes` counts compressed blocks and FP16 weights. `ResidentBytes` uses Vulkan allocation requirements. The sample output is common to both routes, and decoder intermediates use workgroup memory. `Selected` identifies the smallest allocation that meets the recorded quality gate for that material.

The refined layout retains full-resolution BC5 normals and BC4 luminance. It fits the auxiliary chroma and roughness plane to the bilinear sampling kernel before encoding and learns the decoder from the resulting BCn features. This experiment follows the use of hardware-filtered latent textures in the [HPG 2025 paper](https://arxiv.org/abs/2506.06040).
