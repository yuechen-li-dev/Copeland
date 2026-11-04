using System.Collections;
using System.Numerics;
using System.Collections.Immutable;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Immutable resident transforms. A predecessor publishes one frame of explicit motion without retaining a snapshot chain.</summary>
public sealed class NativeInstanceSet3D : IReadOnlyList<SceneInstance3D>
{
    private readonly ImmutableArray<SceneInstance3D> instances;
    internal (string Identity, NativeMesh3D Mesh, ModelMaterial Material)[] Keys { get; }
    internal ModelMaterial[] Materials { get; }
    internal bool HasSubsurface { get; }
    internal bool HasAnalytic { get; }
    internal ImmutableArray<ImmutableArray<SceneInstance3D>> Groups { get; }
    /// <summary>Shared packed order for raster instance IDs and TLAS custom indices.
    /// Enumeration of this set retains the original authoring order.</summary>
    public ImmutableArray<SceneInstance3D> PackedInstances { get; }
    internal object SnapshotIdentity { get; } = new();
    internal object? MotionPredecessor { get; }
    /// <summary>Predecessor-relative transform or ray-filter changes, computed once at publication.</summary>
    public bool HasRaySceneChanges { get; }
    public bool ContinuesFrom(NativeInstanceSet3D previous) => Continues(previous);
    public bool HasMotion => MotionPredecessor is not null;
    internal bool Continues(NativeInstanceSet3D? previous) => previous is not null
        && ReferenceEquals(MotionPredecessor, previous.SnapshotIdentity);
    public int Count => instances.Length;
    public SceneInstance3D this[int index] => instances[index];

    public NativeInstanceSet3D(IEnumerable<SceneInstance3D> source, NativeInstanceSet3D? previous = null)
    {
        instances = source.ToImmutableArray();
        if (instances.Length is < 1 or > 65_536 || instances.Any(instance => string.IsNullOrWhiteSpace(instance.Identity))
            || instances.Select(instance => instance.Identity).Distinct(StringComparer.Ordinal).Count() != instances.Length)
        {
            throw new InvalidDataException("AUR-VIS-001: Resident sets require 1..65536 uniquely identified instances.");
        }
        foreach (SceneInstance3D instance in instances)
        {
            VulkanRetainedModels3D.ValidateTransform(instance.Transform, instance.Mesh.PrimitiveKind != AnalyticPrimitiveKind3D.Triangles);
            instance.Material.Validate();
            if (instance.Mesh.PrimitiveKind != AnalyticPrimitiveKind3D.Triangles && instance.Material.AlphaMask)
                throw new NotSupportedException("AUR-HYBRID-002: Analytic MASK surfaces require multi-hit coverage; this profile admits opaque analytic surfaces only.");
            if (instance.PreviousTransform.HasValue || instance.Material.AlphaBlend || instance.Material.Transmission > 0)
            {
                throw new NotSupportedException("AUR-VIS-002: Resident sets admit opaque/MASK instances. Derive motion from a predecessor snapshot; forward-only materials use the existing batch path.");
            }
        }
        if (previous is not null)
        {
            var keys = instances.Select(instance => (instance.Identity, instance.Mesh, instance.Material));
            if (!keys.SequenceEqual(previous.Keys))
                throw new InvalidDataException("AUR-MOTION-001: A continuing resident snapshot must preserve ordered instance identity, mesh and material.");
            MotionPredecessor = previous.SnapshotIdentity;
            bool changed = false;
            instances = instances.Select((instance, index) =>
            {
                var prior = previous[index];
                changed |= instance.Transform != prior.Transform || instance.RayLayer != prior.RayLayer || instance.RayMask != prior.RayMask;
                return instance with { PreviousTransform = prior.Transform };
            }).ToImmutableArray();
            HasRaySceneChanges = changed;
        }
        Keys = instances.Select(instance => (instance.Identity, instance.Mesh, instance.Material)).ToArray();
        Materials = instances.Select(instance => instance.Material).Distinct().ToArray();
        HasSubsurface = Materials.Any(material => material.SubsurfaceStrength > 0);
        HasAnalytic = instances.Any(instance => instance.Mesh.PrimitiveKind != AnalyticPrimitiveKind3D.Triangles);
        Groups = instances.GroupBy(instance => (instance.Mesh, instance.Material))
            .Select(group => group.ToImmutableArray()).ToImmutableArray();
        PackedInstances = Groups.SelectMany(group => group).ToImmutableArray();
        if (Groups.Length > 1024)
        {
            throw new InvalidDataException("AUR-VIS-001: Resident sets support at most 1024 mesh/material groups.");
        }
    }

    public IEnumerator<SceneInstance3D> GetEnumerator() => ((IEnumerable<SceneInstance3D>)instances).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
