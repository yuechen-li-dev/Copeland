using System.Runtime.InteropServices;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Pipelines.Graphics;

public static unsafe class VulkanGraphicsPipelineFactory
{
    public static VulkanGraphicsPipelineCreateResult Create(
        AurelianVulkanPlant plant,
        AurelianVulkanRenderPass renderPass,
        VulkanGraphicsPipelineDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(descriptor);

        PlantId plantId = plant.Context.Id;
        List<VulkanGraphicsPipelineDiagnostic> diagnostics = [];
        Validate(plantId, plant, renderPass, descriptor, diagnostics);

        if (plant.Device.Handle == 0)
        {
            diagnostics.Add(Diagnostic(
                VulkanGraphicsPipelineDiagnosticCodes.PipelineDisposed,
                "Cannot create a graphics pipeline from a disposed Vulkan plant/device.",
                plantId));
        }

        if (diagnostics.Any(static diagnostic => diagnostic.Severity == VulkanGraphicsPipelineDiagnosticSeverity.Error))
        {
            return new VulkanGraphicsPipelineCreateResult(VulkanGraphicsPipelineStatus.Rejected, null, diagnostics);
        }

        Vk vk = plant.Vk;
        Silk.NET.Vulkan.Device device = plant.Device;
        PipelineLayout pipelineLayout = default;
        Pipeline pipeline = default;
        List<ShaderModule> shaderModules = [];
        List<IntPtr> entryPointPointers = [];

        try
        {
            IReadOnlyList<VulkanShaderStageDescriptor> shaderStages = descriptor.ShaderStages;
            PipelineShaderStageCreateInfo* stageCreateInfos = stackalloc PipelineShaderStageCreateInfo[shaderStages.Count];

            for (int index = 0; index < shaderStages.Count; index++)
            {
                VulkanShaderStageDescriptor stage = shaderStages[index];
                ShaderModule shaderModule = CreateShaderModule(vk, device, stage, diagnostics, plantId);
                if (shaderModule.Handle == 0)
                {
                    DestroyShaderModules(vk, device, shaderModules);
                    return new VulkanGraphicsPipelineCreateResult(VulkanGraphicsPipelineStatus.Failed, null, diagnostics);
                }

                shaderModules.Add(shaderModule);
                IntPtr entryPointPointer = Marshal.StringToHGlobalAnsi(stage.EntryPoint);
                entryPointPointers.Add(entryPointPointer);
                stageCreateInfos[index] = new PipelineShaderStageCreateInfo
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = MapShaderStage(stage.Stage),
                    Module = shaderModule,
                    PName = (byte*)entryPointPointer,
                };
            }

            PipelineLayoutCreateInfo layoutCreateInfo = new()
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 0,
                PSetLayouts = null,
                PushConstantRangeCount = 0,
                PPushConstantRanges = null,
            };

            Result layoutResult = vk.CreatePipelineLayout(device, &layoutCreateInfo, (AllocationCallbacks*)null, out pipelineLayout);
            if (layoutResult != Result.Success)
            {
                diagnostics.Add(Diagnostic(
                    VulkanGraphicsPipelineDiagnosticCodes.PipelineLayoutCreationFailed,
                    $"vkCreatePipelineLayout failed with result {layoutResult}.",
                    plantId));
                DestroyShaderModules(vk, device, shaderModules);
                return new VulkanGraphicsPipelineCreateResult(VulkanGraphicsPipelineStatus.Failed, null, diagnostics);
            }

            VertexInputBindingDescription[] bindingDescriptions = CreateBindingDescriptions(descriptor.VertexBuffers);
            VertexInputAttributeDescription[] attributeDescriptions = CreateAttributeDescriptions(descriptor.VertexAttributes);

            fixed (VertexInputBindingDescription* bindingDescriptionsPointer = bindingDescriptions)
            fixed (VertexInputAttributeDescription* attributeDescriptionsPointer = attributeDescriptions)
            {
                PipelineVertexInputStateCreateInfo vertexInputState = new()
                {
                    SType = StructureType.PipelineVertexInputStateCreateInfo,
                    VertexBindingDescriptionCount = (uint)bindingDescriptions.Length,
                    PVertexBindingDescriptions = bindingDescriptions.Length == 0 ? null : bindingDescriptionsPointer,
                    VertexAttributeDescriptionCount = (uint)attributeDescriptions.Length,
                    PVertexAttributeDescriptions = attributeDescriptions.Length == 0 ? null : attributeDescriptionsPointer,
                };

                PipelineInputAssemblyStateCreateInfo inputAssemblyState = new()
                {
                    SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                    Topology = PrimitiveTopology.TriangleList,
                    PrimitiveRestartEnable = false,
                };

                PipelineViewportStateCreateInfo viewportState = new()
                {
                    SType = StructureType.PipelineViewportStateCreateInfo,
                    ViewportCount = 1,
                    ScissorCount = 1,
                };

                PipelineRasterizationStateCreateInfo rasterizationState = new()
                {
                    SType = StructureType.PipelineRasterizationStateCreateInfo,
                    DepthClampEnable = false,
                    RasterizerDiscardEnable = false,
                    PolygonMode = PolygonMode.Fill,
                    CullMode = CullModeFlags.None,
                    FrontFace = FrontFace.CounterClockwise,
                    DepthBiasEnable = false,
                    LineWidth = 1.0f,
                };

                PipelineMultisampleStateCreateInfo multisampleState = new()
                {
                    SType = StructureType.PipelineMultisampleStateCreateInfo,
                    RasterizationSamples = SampleCountFlags.Count1Bit,
                    SampleShadingEnable = false,
                };

                PipelineColorBlendAttachmentState colorBlendAttachment = new()
                {
                    BlendEnable = false,
                    ColorWriteMask = ColorComponentFlags.RBit
                        | ColorComponentFlags.GBit
                        | ColorComponentFlags.BBit
                        | ColorComponentFlags.ABit,
                };

                PipelineColorBlendStateCreateInfo colorBlendState = new()
                {
                    SType = StructureType.PipelineColorBlendStateCreateInfo,
                    LogicOpEnable = false,
                    AttachmentCount = 1,
                    PAttachments = &colorBlendAttachment,
                };

                DynamicState* dynamicStates = stackalloc DynamicState[2]
                {
                    DynamicState.Viewport,
                    DynamicState.Scissor,
                };

                PipelineDynamicStateCreateInfo dynamicState = new()
                {
                    SType = StructureType.PipelineDynamicStateCreateInfo,
                    DynamicStateCount = 2,
                    PDynamicStates = dynamicStates,
                };

                GraphicsPipelineCreateInfo pipelineCreateInfo = new()
                {
                    SType = StructureType.GraphicsPipelineCreateInfo,
                    StageCount = (uint)shaderStages.Count,
                    PStages = stageCreateInfos,
                    PVertexInputState = &vertexInputState,
                    PInputAssemblyState = &inputAssemblyState,
                    PViewportState = &viewportState,
                    PRasterizationState = &rasterizationState,
                    PMultisampleState = &multisampleState,
                    PDepthStencilState = null,
                    PColorBlendState = &colorBlendState,
                    PDynamicState = &dynamicState,
                    Layout = pipelineLayout,
                    RenderPass = renderPass.NativeRenderPass,
                    Subpass = 0,
                    BasePipelineHandle = default,
                    BasePipelineIndex = -1,
                };

                Result pipelineResult = vk.CreateGraphicsPipelines(
                    device,
                    default,
                    1,
                    &pipelineCreateInfo,
                    (AllocationCallbacks*)null,
                    out pipeline);
                if (pipelineResult != Result.Success)
                {
                    diagnostics.Add(Diagnostic(
                        VulkanGraphicsPipelineDiagnosticCodes.GraphicsPipelineCreationFailed,
                        $"vkCreateGraphicsPipelines failed with result {pipelineResult}.",
                        plantId));
                    vk.DestroyPipelineLayout(device, pipelineLayout, (AllocationCallbacks*)null);
                    DestroyShaderModules(vk, device, shaderModules);
                    return new VulkanGraphicsPipelineCreateResult(VulkanGraphicsPipelineStatus.Failed, null, diagnostics);
                }
            }

            DestroyShaderModules(vk, device, shaderModules);
            return new VulkanGraphicsPipelineCreateResult(
                VulkanGraphicsPipelineStatus.Created,
                new AurelianVulkanGraphicsPipeline(vk, device, pipeline, pipelineLayout, plantId, descriptor),
                diagnostics);
        }
        catch (Exception exception)
        {
            diagnostics.Add(Diagnostic(
                VulkanGraphicsPipelineDiagnosticCodes.GraphicsPipelineCreationFailed,
                $"Graphics pipeline creation failed: {exception.Message}",
                plantId));

            if (pipeline.Handle != 0)
            {
                vk.DestroyPipeline(device, pipeline, (AllocationCallbacks*)null);
            }

            if (pipelineLayout.Handle != 0)
            {
                vk.DestroyPipelineLayout(device, pipelineLayout, (AllocationCallbacks*)null);
            }

            DestroyShaderModules(vk, device, shaderModules);
            return new VulkanGraphicsPipelineCreateResult(VulkanGraphicsPipelineStatus.Failed, null, diagnostics);
        }
        finally
        {
            foreach (IntPtr entryPointPointer in entryPointPointers)
            {
                Marshal.FreeHGlobal(entryPointPointer);
            }
        }
    }

    private static ShaderModule CreateShaderModule(
        Vk vk,
        Silk.NET.Vulkan.Device device,
        VulkanShaderStageDescriptor stage,
        List<VulkanGraphicsPipelineDiagnostic> diagnostics,
        PlantId plantId)
    {
        uint[] spirvWords = stage.SpirvWords.ToArray();
        fixed (uint* spirvWordsPointer = spirvWords)
        {
            ShaderModuleCreateInfo createInfo = new()
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)(spirvWords.Length * sizeof(uint)),
                PCode = spirvWordsPointer,
            };

            Result result = vk.CreateShaderModule(device, &createInfo, (AllocationCallbacks*)null, out ShaderModule shaderModule);
            if (result != Result.Success)
            {
                diagnostics.Add(Diagnostic(
                    VulkanGraphicsPipelineDiagnosticCodes.ShaderModuleCreationFailed,
                    $"vkCreateShaderModule failed for {stage.Stage} shader with result {result}.",
                    plantId,
                    stage.Stage));
                return default;
            }

            return shaderModule;
        }
    }

    private static void DestroyShaderModules(Vk vk, Silk.NET.Vulkan.Device device, List<ShaderModule> shaderModules)
    {
        foreach (ShaderModule shaderModule in shaderModules)
        {
            if (shaderModule.Handle != 0 && device.Handle != 0)
            {
                vk.DestroyShaderModule(device, shaderModule, (AllocationCallbacks*)null);
            }
        }

        shaderModules.Clear();
    }

    private static void Validate(
        PlantId plantId,
        AurelianVulkanPlant plant,
        AurelianVulkanRenderPass? renderPass,
        VulkanGraphicsPipelineDescriptor descriptor,
        List<VulkanGraphicsPipelineDiagnostic> diagnostics)
    {
        if (renderPass is null)
        {
            diagnostics.Add(Diagnostic(
                VulkanGraphicsPipelineDiagnosticCodes.RenderPassMissing,
                "Graphics pipeline creation requires an explicit render pass.",
                plantId));
        }
        else
        {
            if (renderPass.IsDisposed || renderPass.NativeRenderPass.Handle == 0)
            {
                diagnostics.Add(Diagnostic(
                    VulkanGraphicsPipelineDiagnosticCodes.RenderPassDisposed,
                    "Cannot create a graphics pipeline for a disposed render pass.",
                    plantId));
            }

            if (renderPass.PlantId != plant.Context.Id)
            {
                diagnostics.Add(Diagnostic(
                    VulkanGraphicsPipelineDiagnosticCodes.PlantMismatch,
                    "Graphics pipeline render pass must belong to the target Vulkan plant.",
                    plantId));
            }
        }

        IReadOnlyList<VulkanShaderStageDescriptor>? shaderStages = descriptor.ShaderStages;
        if (shaderStages is null)
        {
            diagnostics.Add(Diagnostic(
                VulkanGraphicsPipelineDiagnosticCodes.MissingVertexShader,
                "Graphics pipeline M0 requires one vertex shader stage.",
                plantId,
                VulkanShaderStageKind.Vertex));
            diagnostics.Add(Diagnostic(
                VulkanGraphicsPipelineDiagnosticCodes.MissingFragmentShader,
                "Graphics pipeline M0 requires one fragment shader stage.",
                plantId,
                VulkanShaderStageKind.Fragment));
            return;
        }

        if (!shaderStages.Any(stage => stage?.Stage == VulkanShaderStageKind.Vertex))
        {
            diagnostics.Add(Diagnostic(
                VulkanGraphicsPipelineDiagnosticCodes.MissingVertexShader,
                "Graphics pipeline M0 requires one vertex shader stage.",
                plantId,
                VulkanShaderStageKind.Vertex));
        }

        if (!shaderStages.Any(stage => stage?.Stage == VulkanShaderStageKind.Fragment))
        {
            diagnostics.Add(Diagnostic(
                VulkanGraphicsPipelineDiagnosticCodes.MissingFragmentShader,
                "Graphics pipeline M0 requires one fragment shader stage.",
                plantId,
                VulkanShaderStageKind.Fragment));
        }

        foreach (IGrouping<VulkanShaderStageKind, VulkanShaderStageDescriptor?> group in shaderStages.GroupBy(stage => stage?.Stage ?? VulkanShaderStageKind.Vertex))
        {
            if (group.Count() > 1)
            {
                diagnostics.Add(Diagnostic(
                    VulkanGraphicsPipelineDiagnosticCodes.DuplicateShaderStage,
                    $"Graphics pipeline M0 accepts only one {group.Key} shader stage.",
                    plantId,
                    group.Key));
            }
        }

        foreach (VulkanShaderStageDescriptor? stage in shaderStages)
        {
            if (stage is null)
            {
                diagnostics.Add(Diagnostic(
                    VulkanGraphicsPipelineDiagnosticCodes.EmptySpirv,
                    "Shader stage descriptor must not be null.",
                    plantId));
                continue;
            }

            if (string.IsNullOrWhiteSpace(stage.EntryPoint))
            {
                diagnostics.Add(Diagnostic(
                    VulkanGraphicsPipelineDiagnosticCodes.InvalidEntryPoint,
                    $"{stage.Stage} shader entry point must not be empty.",
                    plantId,
                    stage.Stage));
            }

            if (stage.SpirvWords is null || stage.SpirvWords.Count == 0)
            {
                diagnostics.Add(Diagnostic(
                    VulkanGraphicsPipelineDiagnosticCodes.EmptySpirv,
                    $"{stage.Stage} shader SPIR-V word list must not be empty.",
                    plantId,
                    stage.Stage));
            }
        }

        if (descriptor.EnableDepthTest || descriptor.EnableDepthWrite)
        {
            diagnostics.Add(Diagnostic(
                VulkanGraphicsPipelineDiagnosticCodes.UnsupportedDepthState,
                "Graphics pipeline M0 rejects depth test/write until depth render pass attachments are implemented.",
                plantId));
        }

        ValidateVertexInput(plantId, descriptor, diagnostics);
    }

    private static void ValidateVertexInput(
        PlantId plantId,
        VulkanGraphicsPipelineDescriptor descriptor,
        List<VulkanGraphicsPipelineDiagnostic> diagnostics)
    {
        IReadOnlyList<VulkanVertexBufferLayoutDescriptor>? vertexBuffers = descriptor.VertexBuffers;
        IReadOnlyList<VulkanVertexAttributeDescriptor>? vertexAttributes = descriptor.VertexAttributes;

        if (vertexBuffers is null || vertexAttributes is null)
        {
            diagnostics.Add(Diagnostic(
                VulkanGraphicsPipelineDiagnosticCodes.InvalidVertexInput,
                "Vertex buffer and attribute descriptor lists must not be null; use empty lists for shader-only input.",
                plantId));
            return;
        }

        HashSet<uint> bindings = [];
        foreach (VulkanVertexBufferLayoutDescriptor vertexBuffer in vertexBuffers)
        {
            if (!bindings.Add(vertexBuffer.Binding))
            {
                diagnostics.Add(Diagnostic(
                    VulkanGraphicsPipelineDiagnosticCodes.InvalidVertexInput,
                    $"Duplicate vertex buffer binding {vertexBuffer.Binding}.",
                    plantId));
            }

            if (vertexBuffer.Stride == 0)
            {
                diagnostics.Add(Diagnostic(
                    VulkanGraphicsPipelineDiagnosticCodes.InvalidVertexInput,
                    $"Vertex buffer binding {vertexBuffer.Binding} must have a non-zero stride.",
                    plantId));
            }
        }

        HashSet<uint> locations = [];
        foreach (VulkanVertexAttributeDescriptor vertexAttribute in vertexAttributes)
        {
            if (!locations.Add(vertexAttribute.Location))
            {
                diagnostics.Add(Diagnostic(
                    VulkanGraphicsPipelineDiagnosticCodes.InvalidVertexInput,
                    $"Duplicate vertex attribute location {vertexAttribute.Location}.",
                    plantId));
            }

            if (!bindings.Contains(vertexAttribute.Binding))
            {
                diagnostics.Add(Diagnostic(
                    VulkanGraphicsPipelineDiagnosticCodes.InvalidVertexInput,
                    $"Vertex attribute location {vertexAttribute.Location} references unknown binding {vertexAttribute.Binding}.",
                    plantId));
            }
        }
    }

    private static VertexInputBindingDescription[] CreateBindingDescriptions(IReadOnlyList<VulkanVertexBufferLayoutDescriptor> vertexBuffers)
        => vertexBuffers.Select(static vertexBuffer => new VertexInputBindingDescription
        {
            Binding = vertexBuffer.Binding,
            Stride = vertexBuffer.Stride,
            InputRate = VertexInputRate.Vertex,
        }).ToArray();

    private static VertexInputAttributeDescription[] CreateAttributeDescriptions(IReadOnlyList<VulkanVertexAttributeDescriptor> vertexAttributes)
        => vertexAttributes.Select(static vertexAttribute => new VertexInputAttributeDescription
        {
            Location = vertexAttribute.Location,
            Binding = vertexAttribute.Binding,
            Format = MapVertexAttributeFormat(vertexAttribute.Format),
            Offset = vertexAttribute.Offset,
        }).ToArray();

    private static ShaderStageFlags MapShaderStage(VulkanShaderStageKind stage)
        => stage switch
        {
            VulkanShaderStageKind.Vertex => ShaderStageFlags.VertexBit,
            VulkanShaderStageKind.Fragment => ShaderStageFlags.FragmentBit,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unsupported shader stage."),
        };

    private static Format MapVertexAttributeFormat(VulkanVertexAttributeFormat format)
        => format switch
        {
            VulkanVertexAttributeFormat.Float2 => Format.R32G32Sfloat,
            VulkanVertexAttributeFormat.Float3 => Format.R32G32B32Sfloat,
            VulkanVertexAttributeFormat.Float4 => Format.R32G32B32A32Sfloat,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported vertex attribute format."),
        };

    private static VulkanGraphicsPipelineDiagnostic Diagnostic(
        string code,
        string message,
        PlantId plantId,
        VulkanShaderStageKind? stage = null)
        => new(code, VulkanGraphicsPipelineDiagnosticSeverity.Error, message, plantId, stage);
}
