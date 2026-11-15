using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Device;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Diagnostics;

/// <summary>Bounded validation capture. Own and dispose before the associated Vulkan instance.</summary>
public sealed unsafe class VulkanValidationCapture : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly delegate* unmanaged<Instance, DebugUtilsMessengerEXT, AllocationCallbacks*, void> destroy;
    private readonly DebugUtilsMessengerCallbackFunctionEXT callback;
    private readonly DebugUtilsMessengerEXT messenger;
    private readonly ConcurrentQueue<string> errors = new();
    private int errorCount;
    private int warningCount;
    public int ErrorCount => System.Threading.Volatile.Read(ref errorCount);
    public int WarningCount => System.Threading.Volatile.Read(ref warningCount);
    public string[] Messages => errors.ToArray();

    public VulkanValidationCapture(AurelianVulkanPlant plant)
    {
        this.plant = plant;
        if (!plant.Facts.EnabledValidationLayers.Contains("VK_LAYER_KHRONOS_validation"))
        {
            throw new InvalidOperationException("Vulkan qualification requires validation and debug-utils reporting.");
        }
        var create = (delegate* unmanaged<Instance, DebugUtilsMessengerCreateInfoEXT*, AllocationCallbacks*,
            DebugUtilsMessengerEXT*, Result>)(nint)plant.Vk.GetInstanceProcAddr(plant.Instance, "vkCreateDebugUtilsMessengerEXT");
        destroy = (delegate* unmanaged<Instance, DebugUtilsMessengerEXT, AllocationCallbacks*, void>)
            (nint)plant.Vk.GetInstanceProcAddr(plant.Instance, "vkDestroyDebugUtilsMessengerEXT");
        if (create == null || destroy == null)
        {
            throw new InvalidOperationException("Vulkan qualification requires debug-utils reporting.");
        }
        callback = OnMessage;
        DebugUtilsMessengerCreateInfoEXT info = new()
        {
            SType = StructureType.DebugUtilsMessengerCreateInfoExt,
            MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt | DebugUtilsMessageSeverityFlagsEXT.WarningBitExt,
            MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt
                | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt,
            PfnUserCallback = new(callback),
        };
        DebugUtilsMessengerEXT created;
        if (create(plant.Instance, &info, null, &created) != Result.Success)
        {
            throw new InvalidOperationException("Vulkan validation messenger creation failed.");
        }
        messenger = created;
    }

    public void RequireClean()
    {
        if (ErrorCount != 0)
        {
            throw new InvalidOperationException("Vulkan validation failed:\n" + string.Join("\n", errors));
        }
    }

    private uint OnMessage(DebugUtilsMessageSeverityFlagsEXT severity, DebugUtilsMessageTypeFlagsEXT type,
        DebugUtilsMessengerCallbackDataEXT* data, void* user)
    {
        string message = Marshal.PtrToStringUTF8((nint)data->PMessage) ?? "Unknown Vulkan validation error";
        if (severity.HasFlag(DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt))
        {
            System.Threading.Interlocked.Increment(ref errorCount);
        }
        else
        {
            System.Threading.Interlocked.Increment(ref warningCount);
        }
        if (ErrorCount + WarningCount <= 128) errors.Enqueue(message);
        return 0;
    }

    public void Dispose()
    {
        destroy(plant.Instance, messenger, null);
        GC.KeepAlive(callback);
    }
}
