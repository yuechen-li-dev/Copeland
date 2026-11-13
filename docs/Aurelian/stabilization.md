# Aurelian stabilization and validation lanes

`Aurelian.Core.slnx` is the standalone engine build and test lane. Its project-reference closure contains 56 projects, all inside Copeland, with no game projects or sibling repositories. `scripts/Test-AurelianCoreBoundary.ps1` checks this before CI builds. It rejects missing projects, references outside Copeland, game project references, and unresolved property-based references.

`Aurelian.slnx` remains the full integration workspace. Optional cloth, humanoid, and field-lighting bridges can depend on Aetheris; the standalone engine does not. See the existing [Aetheris boundary](aetheris-boundary.md). No Aetheris code or substitute solver was moved into the engine during this pass.

## Local dependency setup

The following sibling repositories were cloned and inspected alongside Copeland:

| Repository | Revision |
| --- | --- |
| Dominatus | `7b52df3024656ec811b9b1ceb4208cd2464eb6ab` |
| InputMan | `f9fe1f08a3d87ae5f8287e02992f771c57b9df01` |
| Deliverance | `97523cc09f054bd566e8348c7118e83009c740b2` |
| Aetheris | `c82c2c09b8871f425f348b5a301d5873e7ba8ca5` |

For the broad workspace, clone these repositories from `https://github.com/yuechen-li-dev/<name>.git` into directories beside Copeland. A sparse Copeland checkout must also include the relevant `Games`, `samples`, and documentation fixtures. These are development/integration dependencies, not dependencies of the core solution. Sibling checkouts were not modified.

## Native shader rejection

The graphics factory now checks SPIR-V binary framing before any `vkCreateShaderModule` call: a complete five-word header, supported version encoding, nonzero ID bound, reserved schema, bounded nonzero instruction lengths, and shader memory-model/entry-point records. Invalid framing returns `AGPIP1016`; the compiled-program adapter maps it to its existing invalid-SPIR-V diagnostic.

The eight-byte magic/version fixtures can still exercise descriptor mapping, but native submission rejects them. They no longer rely on a driver rejecting an invalid shader. Tests cover real compiled triangle fixtures, truncated headers, corrupt header fields, and zero/oversized instruction lengths. The full graphics suite passes **351 tests** on the Radeon 780M.

These are structural checks, not a replacement for semantic `spirv-val` validation. Built-in shader authoring continues to validate generated SPIR-V through the existing compiler path. The framing rules follow the [Khronos SPIR-V physical layout](https://registry.khronos.org/SPIR-V/specs/unified1/SPIRV.html#_physical_layout_of_a_spir_v_module_and_instruction).

## CI scope

`aurelian-core.yml` automatically builds and tests the standalone engine on pushes to `main` and pull requests. The ordinary job excludes the explicit `WebGpuToolchain` category; a separate job installs Naga CLI 30.0.1 and runs that category. The browser test remains required coverage rather than silently skipping when tooling is absent. Native GPU witnesses remain separate from hosted headless CI, which cannot guarantee a Vulkan device.

TinyFarm's workflow is now manual (`workflow_dispatch`). Its former automatic [failure](https://github.com/yuechen-li-dev/Copeland/actions/runs/43368044961) came from an old InputMan pin missing `K`, `J`, `R`, and function-key enum entries. The manual workflow now pins the InputMan revision listed above, which compiles those bindings. Further TinyFarm test failures remain game work and do not gate engine changes. No game code or tests were deleted.

`scripts/Invoke-AurelianCoreTests.ps1` runs each test assembly separately and checks its TRX counters. Every selected assembly must execute at least one test and pass all executed tests. Missing reports and discovery failures fail the script even if the underlying test runner returns zero. Each invocation has a unique results directory, preventing stale reports or filename collisions from creating false success.

The artifact-budget and release workflows retain their existing scope. Hosted CI has not been run from these unpushed working-tree changes; local commands validate the lane before publication.

## Validation results

After the user disabled the local Windows Application Control block, all **1,379 core tests across 20 assemblies** pass, with no failures or skips. Each assembly has its own checked TRX report under `artifacts/local/core-stabilized`. Earlier local failures caused by sparse-checkout omissions were resolved by restoring the sample and historical documentation fixtures. The browser-only test is deliberately outside this total; Naga/Rust are not installed locally, and the separate CI job supplies them.

Both `Aurelian.Core.slnx` and the full `Aurelian.slnx` build successfully in Release with zero warnings/errors in the final runs. The native surface-response proof remains accepted on the Radeon 780M. `GraphicsProof --rain-night --launch-smoke` presents twelve frames, with zero particle-budget overflow, under `artifacts/local/stabilized-rain`. This qualifies the previously unlaunched rain-gallery integration at window-smoke level; it does not certify every interactive toggle or the full optional integration test matrix.
