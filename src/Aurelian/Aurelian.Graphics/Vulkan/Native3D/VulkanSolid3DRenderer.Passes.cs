using System.Numerics;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Draw;
using Aurelian.Graphics.Vulkan.Commanding.RenderPasses;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Rendering.Contracts.Models;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

public sealed unsafe partial class VulkanSolid3DRenderer
{
    private void RecordDirectionalShadows(VulkanCommandBufferLease command, FrameState state)
    {
        var models = state.Models;
        var gpuGeometry = state.GpuGeometry;
        var settings = state.Settings;
        var cascades = state.Cascades;
        var shadowCamera = state.ShadowCamera;
        var encoder = new VulkanRenderPassCommandEncoder();
        gpuTimings?.Mark(command, 0);
        if (shadowPass is not null)
        {
            shadowPass.Upload(Lighting3DUniforms.Rows(shadowCamera));
            var shadowBegin = encoder.Begin(plant, command, new(shadowPass.Pass, shadowPass.Framebuffer, new(1, 1, 1, 1)));
            Require(shadowBegin.Success, "Shadow pass begin failed.");
            if (settings.Shadows)
            {
                if (state.VertexCount > 0)
                {
                    shadowPass.Draw(command, shadowBegin.Scope!.Value, vertices, (uint)state.VertexCount);
                }
                if (gpuGeometry is not null)
                {
                    shadowPass.Draw(command, shadowBegin.Scope!.Value, gpuGeometry.Buffer, gpuGeometry.VertexCount);
                }
                modelRenderer?.DrawShadows(command, shadowBegin.Scope!.Value, shadowPass, models);
                retainedModels?.DrawShadows(command, shadowBegin.Scope!.Value, shadowPass);
            }
            Require(encoder.End(plant, command, shadowBegin.Scope!.Value).Success, "Shadow pass end failed.");
            Vulkan3DPass.SampleAfterRendering(plant, command, shadowMap);
        }
        if (surfaceLighting is not null)
        {
            for (int index = 0; index < 2; index++)
            {
                var pass = surfaceLighting.CascadePass(index);
                pass.Upload(Lighting3DUniforms.Rows(cascades!.Cameras[index + 1]));
                var cascadeBegin = encoder.Begin(plant, command, new(pass.Pass, pass.Framebuffer, new(1, 1, 1, 1)));
                Require(cascadeBegin.Success, "Directional cascade begin failed.");
                if (settings.Shadows)
                {
                    if (state.VertexCount > 0) pass.Draw(command, cascadeBegin.Scope!.Value, vertices, (uint)state.VertexCount);
                    if (gpuGeometry is not null) pass.Draw(command, cascadeBegin.Scope!.Value, gpuGeometry.Buffer, gpuGeometry.VertexCount);
                    modelRenderer?.DrawShadows(command, cascadeBegin.Scope!.Value, pass, models);
                    retainedModels?.DrawShadows(command, cascadeBegin.Scope!.Value, pass);
                }
                Require(encoder.End(plant, command, cascadeBegin.Scope!.Value).Success, "Directional cascade end failed.");
                surfaceLighting.FinishCascade(command, index);
            }
        }
        gpuTimings?.Mark(command, 1);
    }

    private void RecordLocalShadows(VulkanCommandBufferLease command, FrameState state)
    {
        var models = state.Models;
        var gpuGeometry = state.GpuGeometry;
        var encoder = new VulkanRenderPassCommandEncoder();
        if (surfaceLighting is not null)
        {
            gpuTimings?.Mark(command, 16);
            for (int index = 0; index < surfaceLighting.ShadowCount; index++)
            {
                var pass = surfaceLighting.ShadowPass(index);
                pass.Upload(Lighting3DUniforms.Rows(surfaceLighting.ShadowCamera(index)));
                var spotBegin = encoder.Begin(plant, command, new(pass.Pass, pass.Framebuffer, new(1, 1, 1, 1)));
                Require(spotBegin.Success, "Local shadow pass begin failed.");
                if (state.VertexCount > 0) pass.Draw(command, spotBegin.Scope!.Value, vertices, (uint)state.VertexCount);
                if (gpuGeometry is not null) pass.Draw(command, spotBegin.Scope!.Value, gpuGeometry.Buffer, gpuGeometry.VertexCount);
                if (models.Count > 0) modelRenderer!.DrawShadows(command, spotBegin.Scope!.Value, pass, models);
                retainedModels?.DrawShadows(command, spotBegin.Scope!.Value, pass);
                Require(encoder.End(plant, command, spotBegin.Scope!.Value).Success, "Local shadow pass end failed.");
                surfaceLighting.FinishShadow(command, index);
            }
            gpuTimings?.Mark(command, 17);
        }
    }

    private void RecordGeometry(VulkanCommandBufferLease command, FrameState state)
    {
        var models = state.Models;
        var gpuGeometry = state.GpuGeometry;
        var clearColor = state.Clear;
        bool useTemporal = state.UseTemporal;
        var encoder = new VulkanRenderPassCommandEncoder();
        gpuTimings?.Mark(command, 2);
        var begin = encoder.Begin(plant, command, new VulkanRenderPassBeginRequest(
            renderPass, framebuffer, hdr is null ? target.PrepareClearColor(clearColor)
                : new VulkanColorClearValue(NativeSrgbTransfer.Decode(clearColor.Red), NativeSrgbTransfer.Decode(clearColor.Green),
                    NativeSrgbTransfer.Decode(clearColor.Blue), clearColor.Alpha))
        {
            AdditionalClearColors = motionOutput ? [new VulkanColorClearValue(0, 0, 1, 1),
            .. surfaceOnly ? new[] { new VulkanColorClearValue(0, 1, 0, 0), new VulkanColorClearValue(0, 0, 0, 1),
                new VulkanColorClearValue(0, 0, 0, 0), new VulkanColorClearValue(0, 0, 0, 0),
                VulkanColorClearValue.UInt(0, 0) } : []] : []
        });
        if (!begin.Success)
        {
            throw new InvalidOperationException("3D render pass begin failed. "
                + string.Join(" ", begin.Diagnostics.Select(item => item.Message)));
        }
        DescriptorSet set = descriptorSet;
        plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Graphics,
            pipeline.NativePipelineLayout, 0, 1, &set, 0, null);
        if (state.VertexCount > 0)
        {
            var draw = new VulkanDrawCommandEncoder().DrawVertices(plant, command, begin.Scope!.Value,
                new VulkanDrawVerticesRequest(pipeline, vertices, (uint)state.VertexCount, 0,
                    VulkanViewportScissor.FromFramebuffer(framebuffer))
                { PreviousVertexBuffer = previousVertices });
            Require(draw.Success, string.Join("; ", draw.Diagnostics.Select(item => item.Message)));
        }
        if (models.Count > 0) modelRenderer!.Draw(command, begin.Scope!.Value, framebuffer, models);
        if (retainedModels?.VisibilityBufferActive == true)
            retainedModels.DrawMaterialResolve(command, begin.Scope!.Value, framebuffer, outputVertices!);
        else
            retainedModels?.Draw(command, begin.Scope!.Value, framebuffer);
        if (gpuGeometry is not null)
        {
            DescriptorSet cameraSet = descriptorSet;
            plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Graphics,
                pipeline.NativePipelineLayout, 0, 1, &cameraSet, 0, null);
            AurelianVulkanBuffer? priorGpu = null;
            if (motionOutput) priorGpu = useTemporal && !resetHistory ? previousGpu : gpuGeometry.Buffer;
            var draw = new VulkanDrawCommandEncoder().DrawVertices(plant, command, begin.Scope!.Value,
                new VulkanDrawVerticesRequest(pipeline, gpuGeometry.Buffer, gpuGeometry.VertexCount, 0,
                    VulkanViewportScissor.FromFramebuffer(framebuffer))
                { PreviousVertexBuffer = priorGpu });
            Require(draw.Success, string.Join("; ", draw.Diagnostics.Select(item => item.Message)));
        }
        Require(encoder.End(plant, command, begin.Scope!.Value).Success, "3D render pass end failed.");
        gpuTimings?.Mark(command, 3);
    }

    private void RecordRefraction(VulkanCommandBufferLease command, FrameState state)
    {
        var models = state.Models;
        var settings = state.Settings;
        var worldToClip = state.WorldToClip;
        var eye = state.Eye;
        var cascades = state.Cascades;
        var lighting = state.Lighting;
        bool useTemporal = state.UseTemporal;
        bool hasRefraction = state.HasRefraction;
        var waterFrame = state.WaterFrame;
        bool useWaterReflection = state.UseWaterReflection;
        var logicalCamera = state.LogicalCamera;
        if (surfaceOnly) gpuTimings?.Mark(command, 24);
        if (hasRefraction && settings.SurfaceDebugView == SurfaceDebugView3D.Shaded)
        {
            state.Resolved = refraction!.Record(command, outputVertices!, models, worldToClip, eye, lighting,
                surfaceLighting!.TransparentUniforms(cascades!, settings, RenderWidth, RenderHeight),
                surfaceLighting.TransparentInputs(shadowMap, motion!), surfaceLighting.UnfoggedOutput!, state.Resolved!,
                waterFrame, resetHistory ? waterFrame.Time : previousWaterTime, previousClip, logicalCamera,
                resetHistory, useTemporal, settings.TemporalHistoryWeight,
                useWaterReflection ? waterReflection!.Color : null, waterReflection?.Clip ?? Matrix4x4.Identity, state.Ocean);
        }
        if (surfaceOnly) gpuTimings?.Mark(command, 25);
    }

    private void RecordTransparency(VulkanCommandBufferLease command, FrameState state)
    {
        var settings = state.Settings;
        bool hasTransparency = state.HasTransparency;
        if (surfaceOnly) gpuTimings?.Mark(command, 22);
        if (hasTransparency && settings.SurfaceDebugView == SurfaceDebugView3D.Shaded)
        {

            state.Resolved = transparency!.Record(command, outputVertices!, state.Resolved!);

        }
        if (surfaceOnly) gpuTimings?.Mark(command, 23);
    }

    private void RecordTemporal(VulkanCommandBufferLease command, FrameState state)
    {
        var settings = state.Settings;
        bool useTemporal = state.UseTemporal;
        gpuTimings?.Mark(command, 4);
        if (useTemporal || ScaledOutput)
            state.Resolved = temporal!.Resolve(command, outputVertices!, state.Resolved!, motion!, settings.TemporalHistoryWeight,
                surfaceLighting?.ReflectionOutput, accumulate: useTemporal);
        gpuTimings?.Mark(command, 5);
    }

    private void RecordBloom(VulkanCommandBufferLease command, FrameState state)
    {
        var settings = state.Settings;
        gpuTimings?.Mark(command, 6);
        state.Glow = state.Resolved;
        if (settings.BloomIntensity > 0)
            state.Glow = bloom!.Record(command, outputVertices!, state.Resolved!, settings);
        gpuTimings?.Mark(command, 7);
    }

    private void RecordEyeExposure(VulkanCommandBufferLease command, FrameState state)
    {
        uint query = (uint)EyeTimingIndex * 2;
        gpuTimings?.Mark(command, query);
        eyeMeter!.Record(command, state.Resolved!, state.Settings.EyeExposure!, EyeExposureElapsedSeconds);
        gpuTimings?.Mark(command, query + 1);
    }

    private void RecordOcularGlare(VulkanCommandBufferLease command, FrameState state)
    {
        uint query = (uint)GlareTimingIndex * 2;
        gpuTimings?.Mark(command, query);
        state.Resolved = ocularGlare!.Record(command, outputVertices!, state.Resolved!, state.LogicalCamera, state.Settings.OcularGlare!);
        gpuTimings?.Mark(command, query + 1);
    }

    private void RecordOutput(VulkanCommandBufferLease command, FrameState state)
    {
        var settings = state.Settings;
        var encoder = new VulkanRenderPassCommandEncoder();
        gpuTimings?.Mark(command, 8);
        var displayPass = settings.EyeExposure is null ? outputPass : eyeOutputPass;
        if (displayPass is not null)
        {
            Vulkan3DPass.SampleAfterRendering(plant, command, state.Resolved!);
            if (outputHasBloom || ScaledOutput || ocularGlare is not null)
                displayPass.SetOutputTextures(state.Resolved!, outputHasBloom ? state.Glow ?? state.Resolved! : null);
            bool encodeSrgb = target.TextureFormat is not (VulkanTextureFormat.Rgba8Srgb or VulkanTextureFormat.Bgra8Srgb);
            float toneControl = settings.ToneMapping
                ? 1 + settings.ToneMapColorRetention + (settings.ToneMapGamutCompression ? 2 : 0)
                : 0;
            float bloomIntensity = settings.OcularGlare is null ? settings.BloomIntensity : 0;
            if (settings.EyeExposure is { } eye)
            {
                displayPass.Upload([settings.Exposure, toneControl, encodeSrgb ? 1 : 0, bloomIntensity,
                    eye.LowLight?.Strength ?? 0, eye.LowLight?.PhotopicLuminance ?? 1, 0, 0]);
            }
            else
            {
                displayPass.Upload([settings.Exposure, toneControl, encodeSrgb ? 1 : 0, bloomIntensity]);
            }
            long recordStarted = MeasureOutputRecording ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            var request = new VulkanRenderPassBeginRequest(displayPass.Pass, displayPass.Framebuffer, new(0, 0, 0, 1));
            var contents = ReplayOutputCommands ? SubpassContents.SecondaryCommandBuffers : SubpassContents.Inline;
            var outputBegin = encoder.Begin(plant, command, request, contents);
            Require(outputBegin.Success, "Tone mapping pass begin failed.");
            if (ReplayOutputCommands)
            {
                if (displayPass.DrawPreparedOutput(command, outputVertices!))
                {
                    ReplayedOutputDraws++;
                }
            }
            else
            {
                displayPass.Draw(command, outputBegin.Scope!.Value, outputVertices!, 3);
            }
            Require(encoder.End(plant, command, outputBegin.Scope!.Value).Success, "Tone mapping pass end failed.");
            if (recordStarted != 0)
            {
                OutputRecordingMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(recordStarted).TotalMilliseconds;
            }
        }
        gpuTimings?.Mark(command, 9);
    }

}
