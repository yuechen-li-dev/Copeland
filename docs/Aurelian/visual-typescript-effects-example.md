# Visual TypeScript effect example

`SoftShockwave.v.ts` is the canonical small effect example. Define one ordinary typed `@material` record, one vertex/pixel pair, and small pure helpers; compile under `CopelandCompilerProfile.Gpu`; export the resulting VD-MIR/backend result as `CompiledGraphicsProgram`; then construct `VulkanOrderedQuadRenderer` with `Native2DPipelineOptions.SoftShockwave` and submit typed `NativeSoftShockwaveSubmission` values.

The material ABI is explicit: `color: float4`, then `age`, `lifetime`, `radius`, `thickness`, `intensity`, and `seed` as `f32`. Values are validated before submission. Visual TypeScript is never executed as JavaScript.

See `tests/Aurelian/Aurelian.Shaders.Tests/SoftShockwaveShaderM8Tests.cs` for deterministic source/VD-MIR/HLSL/SPIR-V proof and `tools/Aurelian.EffectsM8Evidence` for native Vulkan rendering.
