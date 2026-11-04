using System.Numerics;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class ReferenceDiffuseScene3DTests
{
    [Fact]
    public void CapturePreservesRecipeWindingFaceAlbedosAndAdditiveEmission()
    {
        float[] positions = [0, 0, 0, 0, 0, 1, 1, 0, 0, 1, 0, 0, 0, 0, 1, 1, 0, 1];
        float[] albedos = [.2f, .3f, .4f, .6f, .7f, .8f];
        var contributor = new StaticDiffuseContributor("floor", positions, [1, 1, 1], [3, 2, 1])
        {
            FaceAlbedos = albedos,
        };
        var recipe = new StaticDiffuseLightingRecipe("capture", "floor", new(0, 0, 1, 1), [0, 1, 0], [contributor]);
        ReferenceTriangle3D[] triangles = ReferenceDiffuseScene3D.Describe(recipe);
        Assert.Equal(2, triangles.Length);
        Assert.True(Vector3.Cross(triangles[0].B - triangles[0].A, triangles[0].C - triangles[0].A).Y > 0);
        Assert.Equal(new(.2f, .3f, .4f), triangles[0].Material.BaseColor);
        Assert.Equal(new(.6f, .7f, .8f), triangles[1].Material.BaseColor);
        Assert.All(triangles, triangle =>
        {
            Assert.Equal(new(3, 2, 1), triangle.Material.Emission);
            Assert.True(triangle.Material.TwoSidedEmission);
            Assert.Equal(0, triangle.Material.Dielectric);
            Assert.Equal(DirectDiffuseModel3D.Lambert, triangle.Material.DiffuseModel);
        });
        positions[0] = 5;
        albedos[0] = 1;
        Assert.Equal(Vector3.Zero, triangles[0].A);
        Assert.Equal(.2f, triangles[0].Material.BaseColor.X);
    }
}
