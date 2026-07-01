# A27 — Vulkan Memory Allocator Strategy Audit

## 1. Files changed

- Created `docs/architecture/graphics-memory-allocation.md`.
- Created this audit report: `docs/audits/0027-a27-vulkan-memory-allocator-strategy.md`.
- Updated `README.md` to record A27 and revise the next implementation direction.
- Updated `docs/architecture/mvp-roadmap.md` to add A27 and make allocator contracts the next step before buffers.
- Updated `docs/architecture/dependency-policy.md` to document that VMA is backend plumbing, not architecture.
- No source files were changed.
- No project files were changed.
- No packages were added.
- `CodeReferences/*` and `vendor/Dominatus/*` were not modified.

## 2. Task scope

A27 is a docs/design milestone before implementing buffers or textures. Its purpose is to audit Vulkan memory allocation strategy for `Aurelian.Graphics` after A24 instance/device M0, A25 timeline fences/resource pool M0, and A26 command buffer pool M0.

The strategic decision is that Aurelian should not blindly bake VMA into the architecture. VMA/VMASharp may be the correct low-level allocation backend, but Aurelian owns allocation contracts, budget facts, telemetry, fence retirement, grow/shrink policy, and future Dominatus policy hooks.

Out of scope for A27:

- VMASharp package addition;
- Vortice addition;
- buffer implementation;
- texture implementation;
- allocator code;
- source/project changes;
- CodeReferences or vendor modifications;
- Vulkan implementation changes.

## 3. Reference material read

Read and inspected:

- `docs/audits/0021-a21-aurelian-graphics-vulkan-intent-port-plan.md`;
- `docs/audits/0025-a25-timeline-fences-resource-pool-m0.md`;
- `docs/audits/0026-a26-vulkan-command-buffer-pool-m0.md`;
- `docs/architecture/dependency-policy.md`;
- `docs/claude/aurelian-vulkan-intent-port-audit.md`;
- `CodeReferences/Stride/Stride.Graphics/Vulkan/GraphicsResource.Vulkan.cs`;
- `CodeReferences/Stride/Stride.Graphics/Vulkan/GraphicsResourceBase.Vulkan.cs`;
- `CodeReferences/Stride/Stride.Graphics/Vulkan/Buffer.Vulkan.cs`;
- `CodeReferences/Stride/Stride.Graphics/Vulkan/Texture.Vulkan.cs`;
- `CodeReferences/Stride/Stride.Graphics/Vulkan/GraphicsDevice.Vulkan.cs`;
- `CodeReferences/Prometheus/reactor_vulkan_sgemm.c`.

Local package/API inspection found `Silk.NET.Vulkan` in the repo, but no VMASharp/VulkanMemoryAllocator package in project files or a local NuGet package cache. Exact VMASharp API and NativeAOT verification is therefore deferred.

## 4. Stride allocation lessons

Stride is useful as intent/reference material, not as architecture to copy.

Useful intent:

- resources centralize common Vulkan state in a graphics resource base;
- buffers and textures bind device memory after querying memory requirements;
- upload paths use host-visible transfer-source staging memory;
- fence values exist for frame/copy/command-list ordering;
- deferred `Collect(...)` destruction acknowledges GPU lifetime lag.

Pitfalls to avoid:

- `AllocateMemory` performs memory type selection and `vkAllocateMemory` per resource;
- physical device memory properties are queried inside allocation paths instead of being cached once;
- upload buffer growth discards the previous upload buffer through deferred collection and allocates a new one, but does not implement bounded reuse/shrink policy;
- stale TODO comments describe intended D3D12-era behavior and should not become Aurelian requirements;
- resource lifetime can be coupled to command-list object references instead of plain fence facts;
- texture/layout state patterns require separate audit later and should not be mixed into allocator M0.

Aurelian should keep the useful intent: explicit resource state, fence-aware retirement, upload staging, and backend-isolated native handles. Aurelian should not copy the per-resource allocation pattern.

## 5. Prometheus arena/policy lessons

Prometheus provides the more useful policy shape for Aurelian than Stride's upload-buffer behavior.

Useful lessons:

- typed arenas record role, required bytes, capacity bytes, live/valid state, generation, memory class, owner, and in-flight state;
- arena facts include reuse, grow, shrink, budget rejection, and ownership rejection counters;
- required/capacity/high-water-style facts make policy observable rather than implicit;
- grow/shrink has low-usage epoch counts and cooldowns instead of immediate oscillation;
- generation-tagged facts and selector caches prevent stale decisions from masquerading as current state;
- ownership and validity checks are explicit, making cross-owner reuse failures diagnosable;
- fence/in-flight state is tracked as policy input rather than hidden in resource objects;
- telemetry is rich enough for a controller to explain why it reused, grew, shrank, rejected, or fell back.

For Aurelian, these lessons map to per-plant allocator telemetry, upload ring/staging pressure telemetry, fence-retired byte accounting, and Dominatus-observed policy facts.

## 6. VMA/VMASharp evaluation

VMA is attractive because it avoids one `vkAllocateMemory` per resource, handles memory type selection, suballocates, supports mapped allocations, is widely used, and naturally fits one allocator per Vulkan device/plant.

The risks are architectural rather than ideological:

- the specific VMASharp package/API was not verified locally during A27;
- NativeAOT behavior remains unknown;
- poor wrapping can hide telemetry and budget facts;
- VMA policy defaults may conflict with future Dominatus utility policy if Aurelian exposes VMA as architecture;
- accidental process-global allocator state would violate the plant model;
- VMA types in buffer/texture APIs would make later backend changes expensive.

Recommendation: keep VMA/VMASharp as an intended backend candidate, but only behind an Aurelian allocator boundary. Do not expose VMA types above `Aurelian.Graphics.Vulkan.Resources`.

## 7. Raw Vulkan fallback position

A raw Vulkan allocator is acceptable for smoke/prototype work only if isolated. It is not the desired long-term allocator strategy.

The fallback can remove a blocker if VMASharp API/package/NativeAOT verification stalls. It must still cache physical device memory properties, centralize memory type selection, centralize allocation/bind/free behavior, report telemetry, carry `PlantId`, and retire allocations through explicit fence lifecycle rules.

The fallback must not spread `vkAllocateMemory` calls through buffers and textures. If raw Vulkan is implemented first, it should be named and documented as M0 backend plumbing that can be replaced by VMA.

## 8. Aurelian allocator architecture recommendation

Recommended future shape:

```text
Aurelian.Graphics/Vulkan/Resources/
  GpuResourceState
  VulkanAllocationRequest
  VulkanAllocationResult
  IVulkanMemoryAllocator
  VulkanMemoryAllocation
  VulkanMemoryAllocatorTelemetry
  RawVulkanMemoryAllocator
  VmaVulkanMemoryAllocator // later if package chosen
```

Rules:

- buffers/textures depend on `IVulkanMemoryAllocator`;
- allocator implementation is selected per plant/device;
- every allocation carries `PlantId`;
- telemetry is plain data;
- resource retirement is expressed in fence values, not command-list references;
- raw Vulkan and VMA backends satisfy the same contracts;
- no higher layer receives VMA allocation handles or raw Vulkan memory policy details.

## 9. Dominatus policy seam

A27 does not implement Dominatus policy. It preserves the seam for later.

Future Dominatus utility policy should observe:

- per-plant allocation counts and bytes;
- upload ring pressure;
- staging pressure;
- descriptor pressure;
- memory budget/usage when available;
- high-water marks;
- fence-retired bytes;
- grow/shrink cooldown state;
- rejection/fallback reasons;
- backend defrag/compaction opportunities if safely exposed.

Future decisions can include grow arena, shrink arena, defer upload, reuse staging, compact/defrag, or switch strategy under pressure. These decisions should be made over Aurelian facts, not opaque VMA state.

## 10. Do-not-carry-over checklist

- No per-resource memory properties query.
- No per-resource `vkAllocateMemory` spread across buffers/textures.
- No upload buffer unbounded growth.
- No resource lifetime tied to command-list object references.
- No global allocator.
- No cross-plant allocation sharing.
- No hidden VMA types in higher layers.
- No Dominatus policy coupled to backend-specific allocation handles.
- No allocator telemetry hidden behind opaque backend internals.

## 11. Validation/command log

Inspection commands run:

```bash
git status --short
find docs/audits -maxdepth 1 -type f | sort
find docs/architecture -maxdepth 2 -type f | sort
find src/Aurelian.Graphics -type f | sort
sed -n '1,620p' docs/audits/0021-a21-aurelian-graphics-vulkan-intent-port-plan.md
sed -n '1,520p' docs/audits/0025-a25-timeline-fences-resource-pool-m0.md || true
sed -n '1,520p' docs/audits/0026-a26-vulkan-command-buffer-pool-m0.md || true
sed -n '1,520p' docs/architecture/dependency-policy.md
rg -n "VMA|VMASharp|AllocateMemory|vkAllocateMemory|memory allocation|upload ring|arena|buffer|texture|resource|allocator|budget|grow|shrink|Prometheus|Dominatus-owned upload budget" docs/claude/aurelian-vulkan-intent-port-audit.md
sed -n '1,900p' docs/claude/aurelian-vulkan-intent-port-audit.md
rg -n "AllocateMemory|vkAllocateMemory|vkGetPhysicalDeviceMemoryProperties|MemoryPropertyFlags|DeviceMemory|BindBufferMemory|BindImageMemory|AllocateUploadBuffer|upload|staging|Collect\(|FrameFence|CopyFence|OnDestroyed|Buffer\.Vulkan|Texture\.Vulkan|GraphicsResource\.Vulkan" CodeReferences/Stride/Stride.Graphics -g '*.cs' > /tmp/a27-stride-memory-search.txt || true
wc -l /tmp/a27-stride-memory-search.txt
head -n 1600 /tmp/a27-stride-memory-search.txt
sed -n '1,260p' CodeReferences/Stride/Stride.Graphics/Vulkan/GraphicsResource.Vulkan.cs
sed -n '1,260p' CodeReferences/Stride/Stride.Graphics/Vulkan/GraphicsResourceBase.Vulkan.cs
sed -n '1,340p' CodeReferences/Stride/Stride.Graphics/Vulkan/Buffer.Vulkan.cs
sed -n '1,420p' CodeReferences/Stride/Stride.Graphics/Vulkan/Texture.Vulkan.cs
sed -n '1,620p' CodeReferences/Stride/Stride.Graphics/Vulkan/GraphicsDevice.Vulkan.cs
rg -n "arena|allocation|allocator|capacity|required|budget|grow|shrink|high_water|low_usage|cooldown|generation|telemetry|policy|retire|reuse|fence|in_flight|owner|valid|invalid|fallback" CodeReferences/Prometheus/reactor_vulkan_sgemm.c > /tmp/a27-prometheus-allocator-search.txt || true
wc -l /tmp/a27-prometheus-allocator-search.txt
head -n 1600 /tmp/a27-prometheus-allocator-search.txt
sed -n '1,900p' CodeReferences/Prometheus/reactor_vulkan_sgemm.c
dotnet nuget list source
rg -n "VMASharp|VulkanMemoryAllocator|VMA|Vortice.Vulkan|Silk.NET.Vulkan" Directory.Packages.props src tests docs -g '*.props' -g '*.csproj' -g '*.md' || true
find ~/.nuget/packages -maxdepth 2 -type d | grep -Ei 'vma|vulkanmemory|vortice.vulkan|silk.net.vulkan' | sort || true
```

Validation commands run after edits:

```bash
test -f docs/architecture/graphics-memory-allocation.md
test -f docs/audits/0027-a27-vulkan-memory-allocator-strategy.md
git status --short
```

No build was required because A27 is docs-only and changed no source/project files.

## 12. Next recommendation

```text
A28 — Vulkan allocator contracts + raw allocator M0
```

Rationale: VMASharp package/API and NativeAOT behavior were not verified locally during A27. A28 should add Aurelian-owned allocator contracts and a narrow raw Vulkan M0 backend first, preserving the VMA seam. If VMASharp becomes easy to verify within A28 scope, it may be added as a backend without exposing VMA types above the allocator boundary.
