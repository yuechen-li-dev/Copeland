using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Device;
using Silk.NET.Vulkan;

/// <summary>Validation errors are qualification failures, including asynchronous queue-submit hazards.</summary>
internal sealed unsafe class RenderingR0Validation : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly delegate* unmanaged<Instance, DebugUtilsMessengerEXT, AllocationCallbacks*, void> destroy;
    private readonly DebugUtilsMessengerCallbackFunctionEXT callback;
    private readonly DebugUtilsMessengerEXT messenger;
    private readonly ConcurrentQueue<string> errors = new();

    public RenderingR0Validation(AurelianVulkanPlant plant)
    {
        this.plant = plant;
        if (!plant.Facts.EnabledValidationLayers.Contains("VK_LAYER_KHRONOS_validation"))
        {
            throw new InvalidOperationException("R0 qualification requires validation and debug-utils reporting.");
        }
        var create = (delegate* unmanaged<Instance, DebugUtilsMessengerCreateInfoEXT*, AllocationCallbacks*,
            DebugUtilsMessengerEXT*, Result>)(nint)plant.Vk.GetInstanceProcAddr(plant.Instance, "vkCreateDebugUtilsMessengerEXT");
        destroy = (delegate* unmanaged<Instance, DebugUtilsMessengerEXT, AllocationCallbacks*, void>)
            (nint)plant.Vk.GetInstanceProcAddr(plant.Instance, "vkDestroyDebugUtilsMessengerEXT");
        if (create == null || destroy == null)
        {
            throw new InvalidOperationException("R0 qualification requires debug-utils reporting.");
        }
        callback = OnMessage;
        DebugUtilsMessengerCreateInfoEXT info = new()
        {
            SType = StructureType.DebugUtilsMessengerCreateInfoExt,
            MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt,
            MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt
                | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt,
            PfnUserCallback = new(callback),
        };
        DebugUtilsMessengerEXT created;
        if (create(plant.Instance, &info, null, &created) != Result.Success)
        {
            throw new InvalidOperationException("R0 validation messenger creation failed.");
        }
        messenger = created;
    }

    public void RequireClean()
    {
        if (!errors.IsEmpty)
        {
            throw new InvalidOperationException("R0 Vulkan validation failed:\n" + string.Join("\n", errors));
        }
    }

    private uint OnMessage(DebugUtilsMessageSeverityFlagsEXT severity, DebugUtilsMessageTypeFlagsEXT type,
        DebugUtilsMessengerCallbackDataEXT* data, void* user)
    {
        string message = Marshal.PtrToStringUTF8((nint)data->PMessage) ?? "Unknown Vulkan validation error";
        errors.Enqueue(message);
        return 0;
    }

    public void Dispose()
    {
        destroy(plant.Instance, messenger, null);
        GC.KeepAlive(callback);
    }
}
