using System.Numerics;
using Aetheris.Continuum.Backends.Sdf;
using Aetheris.Kernel.Core.Math;
using Aurelian.Lighting.Aetheris;
using Aurelian.Shaders.Compute;
using Aurelian.Shaders.Graphics;
using Aurelian.Rendering.Contracts.Lighting;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Lighting.Aetheris.Tests;

public sealed class FieldAdapterTests
{
    [Fact]
    public void Static_surface_composition_preserves_geometry_identity_but_invalidates_materials()
    {
        var geometry = new SdfBoxNode(2000, 2000, 2000);
        var first = new AetherisLightingScene([new("wall", geometry, new(.8f, .02f, .01f), Vector3.Zero)]);
        var changed = new AetherisLightingScene([new("wall", geometry, new(.8f, .02f, .01f), new(4, 2, 1))]);
        Assert.Equal(first.Field.Program.StructuralHash, changed.Field.Program.StructuralHash);
        Assert.NotEqual(first.MaterialIdentity, changed.MaterialIdentity);
        Assert.Throws<ArgumentException>(() => new AetherisLightingScene(
            [new("wall", geometry, new(2, 0, 0), Vector3.Zero)]));
        Assert.Throws<ArgumentException>(() => new AetherisLightingScene(
            [new("wall", geometry, Vector3.One, Vector3.Zero), new("wall", geometry, Vector3.One, Vector3.Zero)]));
    }

    [Fact]
    public void Authored_surface_materials_and_shared_bake_lower_through_the_real_graphics_binder()
    {
        var scene = new AetherisLightingScene([
            new("wall", new SdfBoxNode(2000, 2000, 2000), new(.8f, .02f, .01f), Vector3.Zero),
            new("lamp", new SdfTransformNode(new SdfSphereNode(200), Transform3D.CreateTranslation(new(2000, 0, 0))),
                Vector3.One, new(4, 2, 1)),
        ]);
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../../src/Aurelian/Aurelian.Shaders/Assets"));
        var sources = scene.Sources("FieldBake3D.v.ts", name =>
        {
            string path = Path.Combine(directory, name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        });
        var module = GpuGraphicsBinder.Compile(new(sources));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        string hlsl = VdMirGraphicsHlslEmitter.Emit(module);
        Assert.Contains("SurfaceRadiance", hlsl);
        Assert.Contains("HemisphereDirection", hlsl);
        Assert.Contains("TraceField", hlsl);
        Assert.Contains(sources, source => source.Path.EndsWith("LightingBody1.v.ts", StringComparison.Ordinal)
            && source.Source.Contains("AGPL-3.0-only", StringComparison.Ordinal));
    }

    [Fact]
    public void Recipe_admits_only_its_declared_domain_and_canonicalizes_view_independent_inputs()
    {
        var scene = new AetherisLightingScene([new("floor", new SdfBoxNode(2000, 2000, 200), Vector3.One, Vector3.Zero)]);
        var receiver = new LightingReceiverPlane(new(-1), new(2));
        var first = new AetherisLightingBakeRecipe(scene, LightingArtifactKind.DiffuseTransfer, receiver, Vector3.UnitY, new(1, 2, 3));
        var otherEye = new AetherisLightingBakeRecipe(scene, LightingArtifactKind.DiffuseTransfer, receiver, Vector3.UnitY, new(3, 4, 5));
        Assert.Equal(first.UniformValues(), otherEye.UniformValues());
        Assert.Throws<NotSupportedException>(() => new AetherisLightingBakeRecipe(scene, LightingArtifactKind.BrdfIntegral, receiver, Vector3.UnitY));
        Assert.Throws<ArgumentException>(() => new AetherisLightingBakeRecipe(scene, LightingArtifactKind.ShadowVisibility,
            receiver with { Size = new(40) }, Vector3.UnitY));
        Assert.Throws<ArgumentException>(() => new AetherisLightingBakeRecipe(scene, LightingArtifactKind.ReflectionRadiance,
            receiver, Vector3.UnitY, new(0, -1, 0)));
    }

    [Fact]
    public void Coordinates_and_distances_convert_mm_Z_up_to_metres_Y_up()
    {
        var source = new SdfTransformNode(new SdfSphereNode(500), Transform3D.CreateTranslation(new(1000, -3000, 2000)));
        var field = AetherisLightField.Create(source);
        Assert.Equal(-.5, field.EvaluateMetres(new(1, 2, 3)), 6);
        Assert.Equal(.5, field.EvaluateMetres(new(1, 3, 3)), 6);
        Assert.Contains("export function Field", field.WorldSource);
        Assert.Contains(field.Program.StructuralHash, field.WorldSource);
        Assert.Contains("AGPL-3.0-only", field.WorldSource);
    }

    [Fact]
    public void Unsupported_fields_are_rejected_before_shader_compilation()
    {
        Assert.Throws<ArgumentException>(() => AetherisLightField.Create(new SdfTransformNode(
            new SdfSphereNode(500), Transform3D.CreateScale(2))));
        Assert.Throws<ArgumentException>(() => AetherisLightField.Create(new SdfSphereNode(40_000)));
        var field = AetherisLightField.Create(new SdfSphereNode(500));
        Assert.Throws<ArgumentOutOfRangeException>(() => field.EvaluateMetres(new(float.NaN, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => field.EvaluateMetres(new(65, 0, 0)));
    }

    [Fact]
    public void Tree_limits_reject_large_and_deep_fields_before_recursive_bounds_evaluation()
    {
        SdfNode deep = new SdfSphereNode(500);
        for (int index = 0; index < 100; index++)
        {
            deep = new SdfTransformNode(deep, Transform3D.Identity);
        }
        Assert.Contains("depth", Assert.Throws<ArgumentException>(() => AetherisLightField.Create(deep)).Message);
        SdfNode wide = new SdfSphereNode(500);
        for (int index = 0; index < 7; index++)
        {
            wide = new SdfUnionNode(wide, wide);
        }
        Assert.Contains("128 nodes", Assert.Throws<ArgumentException>(() => AetherisLightField.Create(wide)).Message);
    }

    [Fact]
    public void Package_field_owner_has_no_compiler_or_continuum_reference()
    {
        var assembly = typeof(SdfNode).Assembly;
        Assert.Equal("Aetheris.Fields", assembly.GetName().Name);
        string[] dependencies = assembly.GetReferencedAssemblies().Select(item => item.Name!).ToArray();
        Assert.DoesNotContain(dependencies, name => name.StartsWith("Copeland", StringComparison.Ordinal));
        Assert.DoesNotContain("Aetheris.Continuum", dependencies);
        Assert.DoesNotContain("Aetheris.Kernel.Firmament", dependencies);
        Assert.Contains("GNU AFFERO GENERAL PUBLIC LICENSE", AetherisLightField.LicenseText("AGPL-3.0"));
        Assert.Contains("GNU GENERAL PUBLIC LICENSE", AetherisLightField.LicenseText("GPL-3.0"));
    }

    [Fact]
    public void Real_Aetheris_CSG_source_lowers_through_compute_without_shader_rewriting()
    {
        var field = AetherisLightField.Create(new SdfSubtractNode(new SdfBoxNode(2000, 3000, 4000), new SdfSphereNode(900)));
        const string shader = """
            import { Field } from "./AetherisField";
            @compute
            @numthreads(1, 1, 1)
            function Main(@builtin(dispatchThreadId) thread: uint3,
                @binding(0) readonly Input: StorageBuffer<f32>,
                @binding(1) readwrite Output: StorageBuffer<f32>): void {
                Output[thread.x] = Field(Input[thread.x], -20.0, 0.0);
            }
            """;
        var sources = GpuSourceLoader.Load("sample.v.ts", name => name switch
        {
            "sample.v.ts" => shader,
            "AetherisField.v.ts" => field.Program.FieldSource,
            _ => null,
        });
        var module = GpuComputeBinder.Compile(new(sources));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        string hlsl = VdMirComputeHlslEmitter.Emit(module);
        Assert.Contains("abs(", hlsl);
        Assert.Contains("max(", hlsl);
        Assert.Contains("min(", hlsl);
        Assert.Contains("sqrt(", hlsl);
    }
}
