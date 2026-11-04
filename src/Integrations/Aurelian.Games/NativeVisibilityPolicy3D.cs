using System.Numerics;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Runtime.Rendering;

namespace Aurelian.Games;

/// <summary>Neutral policy/mechanism bridge. It never reads visibility counters back from the GPU.</summary>
public sealed class NativeVisibilityPolicy3D
{
    private readonly bool hzbAvailable;
    private readonly float pixelError;
    private object? source;
    private VulkanSolid3DRenderer? owner;
    private Matrix4x4 previousCamera;
    private bool hasCamera;
    private bool hasLods;
    private bool applied;
    private VisibilityPolicyMode submitted;

    public NativeVisibilityPolicy3D(bool hzbAvailable, float maximumLodPixelError = .25f, int traceCapacity = 256)
    {
        if (!float.IsFinite(maximumLodPixelError) || maximumLodPixelError < 0 || maximumLodPixelError > 16)
            throw new ArgumentOutOfRangeException(nameof(maximumLodPixelError));
        this.hzbAvailable = hzbAvailable;
        pixelError = maximumLodPixelError;
        Controller = new(traceCapacity: traceCapacity);
    }

    public VisibilityPolicyController Controller { get; }

    public void BeforeFrame(VulkanSolid3DRenderer renderer, Native3DScene scene, Matrix4x4 camera, TimeSpan elapsed)
    {
        applied = renderer.EnableVisibilityBuffer && scene.Instances is NativeInstanceSet3D;
        if (!applied)
        {
            source = null;
            hasCamera = false;
            Controller.InvalidateMeasurements();
            return;
        }
        if (!ReferenceEquals(owner, renderer) || !ReferenceEquals(source, scene.Instances))
        {
            owner = renderer;
            source = scene.Instances;
            hasLods = scene.Instances.Any(instance => instance.Mesh.LodRanges.Length > 1);
            Controller.InvalidateMeasurements();
        }
        if (hasCamera && CameraDistance(camera, previousCamera) > .25f) Controller.InvalidateMeasurements();
        previousCamera = camera;
        hasCamera = true;
        submitted = Controller.Tick(elapsed, hzbAvailable, hasLods && pixelError > 0);
        renderer.EnableHierarchicalZ = (submitted & VisibilityPolicyMode.Hzb) != 0;
        renderer.LodPixelError = (submitted & VisibilityPolicyMode.Lod) != 0 ? pixelError : 0;
    }

    public void AfterFrame(Native3DFrameResult result)
    {
        if (!applied || result.CpuTimings is null) return;
        Controller.RecordSubmission(result.CpuTimings.Sequence, submitted);
        if (result.CompletedGpuFrame is { } completed)
        {
            double milliseconds = (completed.EndTicks - completed.StartTicks) * completed.NanosecondsPerTick / 1_000_000d;
            Controller.Complete(completed.Sequence, milliseconds);
        }
    }

    private static float CameraDistance(Matrix4x4 left, Matrix4x4 right)
    {
        var a = MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateReadOnlySpan(ref left, 1));
        var b = MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateReadOnlySpan(ref right, 1));
        float squared = 0;
        for (int index = 0; index < a.Length; index++) squared += (a[index] - b[index]) * (a[index] - b[index]);
        return squared;
    }
}
