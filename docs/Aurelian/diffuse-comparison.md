# Direct diffuse comparison

Aurelian can switch the direct diffuse lobe between Lambert, Disney Burley, the published Frostbite adjustment, EON, and Aurelian PathDiffuse. **PathDiffuse is the current shaded default**, following the [fixed-lobe calibration trial](semantic-look-calibration.md). The comparison lab deliberately starts from Lambert as its reference control. This historical lobe experiment changes one part of the BRDF while preserving materials, normals, GGX specular, lights, camera, exposure and output processing.

## Run and compare

From the repository root:

```powershell
.\diffuse-lab.cmd
```

Choose Start. Press **1–5** to switch diffuse models (**3** Frostbite, **5** PathDiffuse), **C** to change camera, and **L** to cycle direct sun, sun with fixed ambient fill, and local lights. Escape opens the common game-shell menus. The three sphere columns have roughness .25, .65 and 1; the rows use chalk, clay and blue colors. The separate gold sphere is a pure-metal control. A procedural normal/roughness-mapped stone platform tests the textured path.

The lab implements `IGame`; it does not own a separate window/input loop. Its authored exposure and camera stay fixed when switching models. The same numeric roughness is supplied to each diffuse formula, which is a controlled parameter comparison, not a calibration to equal measured physical roughness.

Native application qualification:

```powershell
.\diffuse-lab.cmd --playtest-script tools/Aurelian.GraphicsProof/Playtests/diffuse-lab.json --output artifacts/local/diffuse-lab --save-root artifacts/local/diffuse-lab/saves
```

Numerical qualification, six five-way contact sheets and six Frostbite/PathDiffuse pairs:

```powershell
dotnet run --project tools/Aurelian.GraphicsProof -c Release -- --diffuse-proof --output artifacts/local/diffuse-comparison
```

The proof writes fail-closed `evidence.json`, individual native captures, `lighting-*-shot-*-comparison.png`, `lighting-*-shot-*-path-vs-frostbite.png`, a white-furnace CSV, and the instrumented forward shader used to isolate the diffuse lobe. `matched-red-control.png` compares diffuse-only native swatches at equal linear luminance, with tone mapping disabled. Keep the same output directory together when sharing a result.

## What the papers actually support

| Model | Meaning in this experiment | Primary source |
| --- | --- | --- |
| Lambert | Constant diffuse BRDF, albedo divided by pi | Baseline |
| Burley | Roughness-dependent grazing response and retroreflection; empirical rather than a universally exact scattering solution | [Burley, Disney, 2012, section 5.3](https://media.disneyanimation.com/uploads/production/publication_asset/48/asset/s2012_pbs_disney_brdf_notes_v3.pdf) |
| Frostbite | The altered grazing response and energy scale in listing 1; this is the diffuse lobe from the published 2014 work | [Lagarde and de Rousiers, course notes v3, section 3.1](https://seblagarde.wordpress.com/2015/07/14/siggraph-2014-moving-frostbite-to-physically-based-rendering/) |
| EON | Energy-preserving rough diffuse based on Oren–Nayar; the production polynomial fit approximates the directional integral | [Portsmouth, Kutz and Hill, JCGT 2025, revised 2026](https://jcgt.org/published/0014/01/06/paper.pdf) |
| PathDiffuse | Engine experiment: separable first bounce with analytic colored multiple-bounce closure | Derived below; not a published or measured-material model |

The Disney notes discuss fitting and artistic usefulness, including limitations of the diffuse approximation. Frostbite modifies the original lobe to control its energy gain. Neither paper establishes a universal image-quality ranking between engines.

Epic's [2013 UE4 shading paper](https://cdn2.unrealengine.com/Resources/files/2013SiggraphPresentationsNotes-26915738.pdf) reports a small visual difference in its Burley evaluation and identifies cost and indirect-light integration as considerations. That is historical evidence about that implementation, not a statement that every current Unreal material uses the same model.

EON's mathematical energy behavior makes it an interesting candidate for rough matte surfaces. Here its **unit-albedo lobe is multiplied by authored base color**, an option described by the authors. This preserves the existing interpretation of color for this comparison. It does not implement the colored single-scattering-albedo variant, its added multiple-scattering saturation, or its importance sampler. The adapted shader fit retains the upstream [MIT license](../../src/Aurelian/Aurelian.Shaders/Assets/Licenses/EON-LICENSE.txt).

## Aurelian PathDiffuse experiment

The visual target is the rich red clay seen with the Frostbite adjustment, without relying solely on lower brightness. PathDiffuse borrows that model's roughness-dependent first-bounce attenuation and combines a deliberately simple angular lobe with the colored multiple-scattering closure discussed in EON section 3.1. This is an engine-specific synthesis of established ideas, not a claim of research novelty or a new physical solution for rough microgeometry.

For roughness `r`, direction cosine `mu`, and a single channel of base color `rho`, define:

```text
c = lerp(1, 1 / 1.51, r)
a = r / 2
g(mu) = 1 - a * (1 - mu)^5
m = 1 - a / 21
S(l, v) = c * g(n.l) * g(n.v)
E(mu) = c * m * g(mu)
A = c * m^2
M(l, v) = (1 - E(n.l)) * (1 - E(n.v)) / (1 - A)
rho_ms = rho^2 * A / (1 - rho * (1 - A))
BRDF = (rho * S + rho_ms * M) / pi
```

At `r = 0`, use the Lambert limit (`M = 0`). The shader guards the vanishing denominator. The cosine-weighted hemispherical mean of `(1 - mu)^5` is exactly `1/21`; the directional and average integrals therefore need neither a LUT nor an angular polynomial fit. The lobe uses two fifth-power multiplication chains, shared scalar arithmetic and a rational expression per color channel. It adds no textures, samples, passes or uniform storage. Local GPU timings still determine whether that cost is acceptable on a particular device.

Both terms are reciprocal and nonnegative. A white diffuse surface has directional reflectance `E(v) + (1 - E(v)) = 1`. For `rho` in `[0, 1]`, `rho_ms <= rho`, so directional reflected energy cannot exceed `rho`. Black remains black, equal RGB channels remain neutral, and zero roughness returns Lambert. These properties apply to the **isolated diffuse lobe**, not a claim of exact energy preservation for the unchanged complete GGX/Fresnel material. The scalar `DiffuseFactor` helper represents its white response; colored consumers must use `DiffuseResponse`.

Here **base color means single-bounce reflectance**, so the same material numbers do not guarantee the same total diffuse albedo as Lambert or the white EON option. Repeated colored paths preferentially absorb already weak channels. This gives an actual change in channel ratios, rather than a global saturation operation, and the balance varies with view/light angle. The coefficients `1/1.51` and `1/2` are fixed experimental choices, not fitted physical parameters. The separable lobe discards azimuth-dependent retroreflection; use the reference models when that feature matters.

The proof compares the shader's rational closure with an independently summed double-precision bounce series. It checks colored furnaces at 175 combinations, reciprocal directions, the smooth limit, and a rough red swatch matched to Frostbite's linear luminance by scaling white illumination. The control bypasses specular and tone mapping and reports both luminance error and normalized linear RGB chroma. That chroma metric is an engineering check, not a perceptual judgment. Full-scene pairs keep illumination and exposure identical and include the original GGX specular.

The retained RTX 3070 run in `artifacts/local/path-diffuse` passed 960 isolated colored-lobe GPU cases and 540 ordinary forward/surface cases. Maximum linear RGB errors were 0.003303 and 0.001983, including final 8-bit quantization. The 175 numerical path configurations had maximum integration error `1.73e-7` and reciprocity error `4.44e-16`; the sampled white-furnace range was 0.999939–1.000004. Pure-metal images were byte-identical across all five models.

The matched rough-red control measured linear RGB chroma 0.93994 for Frostbite and 0.94821 for PathDiffuse, with 0.5025% relative luminance mismatch after quantization. This is a small measurable enrichment at matched brightness, not proof of a universal aesthetic improvement. The complete scene still includes unchanged specular highlights and indirect terms, so its visible difference depends on lighting and material.

At 960×640, using 50 samples per model after ten warmup cycles and balanced rotating order, the surface-lighting medians were 0.226304 ms Lambert, 0.227328 ms Burley, 0.228352 ms Frostbite, 0.228352 ms EON and 0.227328 ms PathDiffuse. PathDiffuse's 10th–90th percentile range (0.222208–0.238592 ms) overlaps Frostbite's (0.222208–0.232448 ms). These observations support comparable local pass cost; they do not establish a speed win, isolated BRDF instruction cost, or performance under many overlapping lights. Both use the same dynamically branched shader and uncontrolled GPU clocks.

## Interpretation and limits

The initial native comparisons show small Burley changes in many front-facing regions and larger changes around grazing contours and rough surfaces. Frostbite's energy scale also changes overall diffuse brightness. EON redistributes the rough diffuse response, so its silhouette and terminator can read differently even when albedo and illumination are unchanged. Preference should be judged in motion as well as in still images.

The white-furnace CSV integrates the **isolated unit-albedo diffuse BRDF**, not the complete diffuse-plus-specular material. Lambert returns one. Raw Burley can exceed one; the Frostbite adjustment is not exactly energy preserving. The exact EON reference stays close to one within numerical quadrature error. This is a physical invariant check, not a perceptual score or a fit to measured materials.

The retained local run sampled roughness 0/.25/.65/1 and view cosines 1/.7/.25/.05, with 128×256 cosine-weighted quadrature samples per configuration:

| Isolated diffuse model | Smallest sampled reflectance | Largest sampled reflectance |
| --- | ---: | ---: |
| Lambert | 1.000000 | 1.000000 |
| Burley | 0.598569 | 1.463417 |
| Frostbite adjustment | 0.215489 | 0.969150 |
| EON exact reference | 0.999143 | 1.000014 |

These are initial four-model sampled results, not global mathematical bounds. The original two native oracles passed 256 isolated-lobe cases and 432 ordinary forward/surface cases on an RTX 3070. Maximum measured linear errors were 0.00191 and 0.00198 respectively, including final 8-bit output quantization. The expanded proof records current five-model results in `evidence.json`; its isolated probe now tests gray, red and blue. Pure-metal captures must remain identical across model switches. The native `IGame` playtest exercises all five physical-key bindings, camera/light changes and target recreation after resize.

The GPU proof compares the production diffuse function against double-precision equations, using the exact EON integral as an independent check on the shader fit. It also checks ordinary forward and surface model rendering, with unchanged pure-metal captures. Local timing samples rotate model order after warmup and measure the surface-lighting GPU pass. They exclude CPU work and readback, use a single dynamically branched shader, and do not control GPU clocks; they are observations rather than portable performance guarantees.

The shared switch affects direct sun and local lights, including the solid/skinned, textured, transparent and refractive shader consumers. The lab qualifies opaque forward/surface rendering. Existing shader tests compile the other consumers. **Ambient hemispheres, environment prefilters and GI remain unchanged.** A published Lambertian static-lighting asset rejects non-Lambert selection with `AUR-DIFFUSE-001`, because its direct-response override would invalidate the comparison. Consistent rough-diffuse IBL and recompiled indirect transport require a separate change.

Consequently this experiment tests whether diffuse response changes Aurelian's materials in a useful way. It cannot identify the cause of an entire engine's appearance. Lighting design, indirect illumination, material calibration, geometric detail, specular response, tone mapping and temporal reconstruction remain separate variables. No new default is chosen from a screenshot alone.

## Use in an existing game

```csharp
Graphics = Graphics3DSettings.Default with
{
    DirectDiffuseModel = DirectDiffuseModel3D.Eon,
};
```

`Lighting3D.v.ts` owns the formulas. `Lighting3DUniforms.Scene` publishes the selector in the existing `ground.w` slot across forward and surface shaders, preserving the uniform layouts. The normal renderer settings comparison invalidates temporal history when the model changes. The existing GGX and Fresnel partition remain identical across models; selecting the Frostbite lobe does not reproduce Frostbite's complete material system.
