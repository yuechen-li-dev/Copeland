# R8 layered materials, decals and texture experiments

## OpenPBR layers and reference coverage

The R3a integrator already accepts clearcoat, sheen and anisotropy on constant
materials. It evaluates the shared VTS BRDF, samples the supported lobes and
uses their corresponding PDFs. The furnace proofs compare with independent
CPU controls and the neutral Vulkan rig checks the real-time raster paths.
The original R3a report describes its initial diffuse-only qualification.
Full reference texture, coverage and normal-map reconstruction is still
outside these material witnesses.

| Layer | Saved RTX 3070 evidence |
| --- | --- |
| Clearcoat | 88 furnace cases at 1,024 samples, peak energy 1.0000, worst GPU error .0094. The additive control peaks at 1.762. The neutral rig compares bindless and visibility resolve. |
| Sheen | 125 cases at 3,072 samples, peak 1.0006, worst GPU error .0087. The additive control peaks at 1.736. The neutral rig separates interior precision from silhouette edges. |
| Anisotropy | The review rerun covered 240 furnace cases, peak 1.0037 and independent error .0100. The neutral rig measures highlight rotation with coat and sheen. The proof also checks authored glTF factors. |

Run `Aurelian.GraphicsProof` with `--rendering-r8-coat`,
`--rendering-r8-sheen` or `--rendering-r8-anisotropy`. The saved historical
coat and sheen comparisons reside in `artifacts/local/rendering-r8-coat/prototype-2`
and `artifacts/local/rendering-r8-sheen/prototype-2`. The review rerun
is under `artifacts/local/rendering-review/r8-anisotropy`.

## Decals

The decal proof uses projected retained receivers on the ordinary and
visibility-buffer material paths. It checks clean receivers, wet and dry
material changes, sharp reflection hits and reflected diffuse light. Run
`--rendering-r8-decals` to inspect the current captures. See the
[composition constraints](rendering-composition.md) for receiver admission.

The review rerun reports HDR RMSE .000778 on the marked primary receiver,
.000021 through sharp reflection and .000026 in the diffuse-bounce witness.
The dry-material control errors are .394554, .375165 and .004744 respectively.
At 1280×768, the dry resolve had a .557 ms GPU median, the two-projector
resolve .606 ms, and the repeated dry control .557 ms. The raw evidence is in
`artifacts/local/rendering-review/r8-decals`.

## Neural texture sampling

The Vulkan qualification compares cooperative-matrix VTS sampling against
both BC7 and BC7+BC5 at mip zero. The strongest BC control is the quality
gate. Matching dense aligned, sparse oblique and shaded workloads use actual
Vulkan texture samplers and the same shading output. Input memory uses Vulkan
allocation requirements, including alignment. The common output is excluded
from the comparison.

| 1024×1024 material | Selected encoding | Allocated input, control → candidate | Shaded PSNR, strongest control → candidate | Dense GPU ms, BC7+BC5 → candidate | Sparse GPU ms, BC7+BC5 → candidate |
| --- | --- | ---: | ---: | ---: | ---: |
| Brick wall | `splitfit2` | 2,097,152 → 1,836,032 B | 46.77 → 46.44 dB | .0581 → .5509 | .0059 → .0253 |
| Dirt wood planks | `splitfit2` | 2,097,152 → 1,836,032 B | 46.54 → 45.57 dB | .0565 → .5507 | .0060 → .0251 |
| Oak wood planks | BC | 2,097,152 B | 47.62 dB control | — | — |
| Cracked boulder | BC | 2,097,152 B | 45.67 dB control | — | — |

Two of four materials pass the combined memory and quality gate. The
selected neural inputs save 12.45% allocation for those two materials.
Dense decode costs roughly 9–10× as much GPU time as the BC7+BC5 controls.
This is a material sampling experiment. BC remains the shipping baseline.
Artists would need mip and streaming comparisons before deployment.

Run `--rendering-neural-textures --codec-fixtures artifacts/local/rendering-r8-neural-textures/fp16-fitted`
through `Aurelian.GraphicsProof` to test the fitted weights used above.
The saved combined run and fitted inputs are under
`artifacts/local/rendering-r8-neural-textures`. The evidence lists
each candidate's failure reasons, timings and all three channel errors.
The [fixture and fitting recipe](../../tools/aurelian-rendering-reference/neural-texture-qualification.md)
lists the source maps, decoder tooling and offline inputs.

The review reran the saved FP16-fitted weights and reproduced both winners
and their shaded PSNR. The earlier fit directories achieved zero or one
winner with the same cooperative SPIR-V. The review ledgers are under
`artifacts/local/rendering-review/neural-fp16`, `neural-textures`,
`neural-refined` and `neural-corpus`.
