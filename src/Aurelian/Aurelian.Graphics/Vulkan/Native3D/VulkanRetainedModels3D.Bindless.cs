using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.RenderPasses;
using Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;
using Aurelian.Graphics.Vulkan.Pipelines.Graphics;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

internal sealed unsafe partial class VulkanRetainedModels3D
{
    private VulkanBindlessMaterials3D? bindlessTable;
    private AurelianVulkanGraphicsPipeline? bindlessPipeline;
    private readonly List<BindlessRun> bindlessRuns = [];
    private readonly AurelianVulkanRenderPass geometryPass;
    public bool BindlessActive { get; private set; }
    private sealed record BindlessRun(DrawGroup Draw, ImmutableArray<SceneInstance3D> Source);

    public bool CanUseBindless(IReadOnlyList<SceneInstance3D> source, bool enabled)
        => enabled && source is NativeInstanceSet3D && programs.Bindless is not null && plant.Facts.BindlessMaterialsEnabled;

    private void PrepareBindless(NativeInstanceSet3D source)
    {
        var table = new VulkanBindlessMaterials3D(plant, allocator, commands, fences, frames, materials, programs.Bindless!, source);
        try
        {
            bindlessPipeline ??= CreatePipeline(geometryPass, programs.Bindless!.Shaders, table.Layout, shadow: false);
            // Merge consecutive groups of one mesh only, preserving global opaque tie order.
            int index = 0;
            while (index < groups.Count)
            {
                DrawGroup first = groups[index];
                uint count = 0;
                var members = ImmutableArray.CreateBuilder<SceneInstance3D>();
                while (index < groups.Count && ReferenceEquals(groups[index].Mesh, first.Mesh))
                {
                    count += groups[index].Count;
                    members.AddRange(source.Groups[index]);
                    index++;
                }
                bindlessRuns.Add(new(first with { Count = count }, members.ToImmutable()));
            }
            bindlessTable = table;
        }
        catch
        {
            table.Dispose();
            bindlessRuns.Clear();
            throw;
        }
    }

    public void UpdateBindlessFrame(Matrix4x4 clip, Vector3 eye, float[] lighting, Matrix4x4 previousClip)
    {
        if (!BindlessActive) return;
        bindlessTable!.UpdateFrame(clip, eye, lighting, previousClip);
        Statistics = Statistics with
        {
            DrawGroups = bindlessRuns.Count,
            BindlessMaterials = true,
            ResidentTextures = bindlessTable.TextureCount,
        };
    }

    private void DrawBindless(VulkanCommandBufferLease command, VulkanRenderPassScope scope, AurelianVulkanFramebuffer framebuffer)
    {
        DescriptorSet set = bindlessTable!.Set;
        plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Graphics,
            bindlessPipeline!.NativePipelineLayout, 0, 1, &set, 0, null);
        for (int index = 0; index < bindlessRuns.Count; index++)
        {
            DrawGroupIndexed(command, scope, framebuffer, bindlessPipeline, bindlessRuns[index].Draw,
                HasVisibility ? visibility[index] : null);
        }
        Statistics = Statistics with { MaterialDescriptorBinds = 1 };
    }
}
