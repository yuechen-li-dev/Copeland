using System.Diagnostics;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;
using Silk.NET.Vulkan;

sealed record WeightDelivery(string Matrix, int FirstRow, long Requested, QueuedDispatchProfile Profile);

sealed record WeightDeliveryTrace(int DecodeStep, ClockSample Start, ClockSample End,
    IReadOnlyList<WeightDelivery> Deliveries);

// The CPU records Stopwatch/QPC ticks. Pair this domain with Vulkan's device clock
// at each token boundary so host timestamps and GPU query results share a timeline.
sealed record ClockSample(ulong DeviceTicks, ulong HostTicks, ulong MaxDeviationNs)
{
    public static unsafe ClockSample Capture(AurelianVulkanPlant plant)
    {
        if (!OperatingSystem.IsWindows() || !Stopwatch.IsHighResolution
            || !plant.Facts.EnabledDeviceExtensions.Contains("VK_EXT_calibrated_timestamps"))
        {
            throw new PlatformNotSupportedException("Delivery tracing requires Vulkan calibrated timestamps and Windows QPC.");
        }
        var domains = (delegate* unmanaged<PhysicalDevice, uint*, TimeDomainKHR*, Result>)(nint)
            plant.Vk.GetInstanceProcAddr(plant.Instance, "vkGetPhysicalDeviceCalibrateableTimeDomainsEXT");
        var sample = (delegate* unmanaged<Device, uint, CalibratedTimestampInfoKHR*, ulong*, ulong*, Result>)(nint)
            plant.Vk.GetDeviceProcAddr(plant.Device, "vkGetCalibratedTimestampsEXT");
        uint count = 0;
        if (domains == null || sample == null || domains(plant.PhysicalDevice, &count, null) != Result.Success || count == 0)
        {
            throw new InvalidOperationException("Could not enumerate Vulkan timestamp domains.");
        }
        var supported = new TimeDomainKHR[count];
        fixed (TimeDomainKHR* ptr = supported)
        {
            if (domains(plant.PhysicalDevice, &count, ptr) != Result.Success
                || !supported.Contains(TimeDomainKHR.DeviceKhr) || !supported.Contains(TimeDomainKHR.QueryPerformanceCounterKhr))
            {
                throw new PlatformNotSupportedException("The device must expose both device and QPC time domains.");
            }
        }
        var info = stackalloc CalibratedTimestampInfoKHR[2];
        info[0] = new() { SType = StructureType.CalibratedTimestampInfoExt, TimeDomain = TimeDomainKHR.DeviceKhr };
        info[1] = new() { SType = StructureType.CalibratedTimestampInfoExt, TimeDomain = TimeDomainKHR.QueryPerformanceCounterKhr };
        ulong* ticks = stackalloc ulong[2];
        ClockSample? best = null;
        for (int i = 0; i < 4; i++)
        {
            ulong deviation;
            if (sample(plant.Device, 2, info, ticks, &deviation) != Result.Success)
            {
                throw new InvalidOperationException("Could not calibrate the Vulkan device clock.");
            }
            if (best is null || deviation < best.MaxDeviationNs)
            {
                best = new(ticks[0], ticks[1], deviation);
            }
        }
        return best!;
    }
}
