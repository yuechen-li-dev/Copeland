using System.Numerics;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;
using Xunit;

namespace Aurelian.World.Tests;

public sealed class PrimitiveModels3DTests
{
    private static readonly ModelMaterial Material = new("stone") { Metallic = 0 };

    [Fact]
    public void PrimitiveTrianglesFaceOutwardAndTangentFramesAreOrthonormal()
    {
        ModelSlot[] models =
        [
            PrimitiveModels3D.Plane("plane", new(4, 6), Material),
            PrimitiveModels3D.Box("box", new(2, 3, 4), Material),
            PrimitiveModels3D.BeveledBox("bevel", new(2, 3, 4), .1f, Material),
            PrimitiveModels3D.Cylinder("cylinder", .7f, 3, Material),
            PrimitiveModels3D.Cylinder("cone", .7f, 3, Material, topRadius: 0),
            PrimitiveModels3D.Sphere("sphere", 2, Material),
            PrimitiveModels3D.Revolve("post", [new(.3f, 0), new(.1f, .2f), new(.08f, 2), new(.2f, 2.1f)], Material),
        ];
        foreach (ModelSlot slot in models)
        {
            ModelPrimitive primitive = slot.Current.Occurrences.Single().Primitive;
            Assert.True(primitive.HasUv && primitive.HasTangents);
            foreach (ModelVertex vertex in primitive.Vertices)
            {
                Vector3 tangent = new(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z);
                Assert.InRange(vertex.Normal.Length(), .9999f, 1.0001f);
                Assert.InRange(tangent.Length(), .9999f, 1.0001f);
                Assert.InRange(Math.Abs(Vector3.Dot(vertex.Normal, tangent)), 0, .0001f);
                Assert.True(vertex.Tangent.W is -1 or 1);
            }
            for (int first = 0; first < primitive.Indices.Length; first += 3)
            {
                ModelVertex a = primitive.Vertices[primitive.Indices[first]];
                ModelVertex b = primitive.Vertices[primitive.Indices[first + 1]];
                ModelVertex c = primitive.Vertices[primitive.Indices[first + 2]];
                Vector3 face = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
                Assert.True(face.LengthSquared() > 0, slot.Id);
                Assert.True(Vector3.Dot(face, a.Normal + b.Normal + c.Normal) > 0, slot.Id);
            }
        }
    }

    [Fact]
    public void ClosedPrimitivesHaveTwoIncidentFacesAtEveryWeldedEdge()
    {
        ModelSlot[] models =
        [
            PrimitiveModels3D.Box("box", Vector3.One, Material),
            PrimitiveModels3D.BeveledBox("bevel", Vector3.One, .1f, Material),
            PrimitiveModels3D.Cylinder("cylinder", .5f, 2, Material),
            PrimitiveModels3D.Cylinder("cone", .5f, 2, Material, topRadius: 0),
            PrimitiveModels3D.Sphere("sphere", 1, Material),
            PrimitiveModels3D.Revolve("post", [new(.3f, 0), new(.1f, .2f), new(.08f, 2)], Material),
        ];
        foreach (ModelSlot model in models)
        {
            ModelPrimitive primitive = model.Current.Occurrences.Single().Primitive;
            var positions = new Dictionary<(int, int, int), int>();
            int[] welded = primitive.Vertices.Select(vertex =>
            {
                var key = ((int)MathF.Round(vertex.Position.X * 100000),
                    (int)MathF.Round(vertex.Position.Y * 100000), (int)MathF.Round(vertex.Position.Z * 100000));
                if (!positions.TryGetValue(key, out int index))
                {
                    index = positions.Count;
                    positions.Add(key, index);
                }
                return index;
            }).ToArray();
            var edges = new Dictionary<(int, int), int>();
            for (int first = 0; first < primitive.Indices.Length; first += 3)
            {
                int a = welded[primitive.Indices[first]];
                int b = welded[primitive.Indices[first + 1]];
                int c = welded[primitive.Indices[first + 2]];
                Add(a, b);
                Add(b, c);
                Add(c, a);
            }
            Assert.All(edges.Values, count => Assert.Equal(2, count));

            void Add(int a, int b)
            {
                var key = (Math.Min(a, b), Math.Max(a, b));
                edges[key] = edges.GetValueOrDefault(key) + 1;
            }
        }
    }

    [Fact]
    public void MetreUvsAndHandednessFollowTheSurfaceDirections()
    {
        ModelPrimitive plane = PrimitiveModels3D.Plane("plane", new(4, 6), Material).Current.Occurrences.Single().Primitive;
        ModelVertex origin = plane.Vertices[0];
        Vector3 tangent = new(origin.Tangent.X, origin.Tangent.Y, origin.Tangent.Z);
        Vector3 bitangent = Vector3.Cross(origin.Normal, tangent) * origin.Tangent.W;
        Assert.Equal(new Vector2(4, 6), plane.Vertices[2].Uv);
        Assert.Equal(Vector3.UnitX, tangent);
        Assert.Equal(Vector3.UnitZ, bitangent);
        ModelPrimitive tiled = PrimitiveModels3D.Plane("tiled", new(4, 6), Material,
            uvTileSizeMetres: new(2, 3)).Current.Occurrences.Single().Primitive;
        Assert.Equal(new Vector2(2, 2), tiled.Vertices[2].Uv);

        ModelPrimitive cylinder = PrimitiveModels3D.Cylinder("cylinder", .5f, 2, Material, 16).Current.Occurrences.Single().Primitive;
        ModelVertex side = cylinder.Vertices[0];
        Vector3 sideTangent = new(side.Tangent.X, side.Tangent.Y, side.Tangent.Z);
        Assert.Equal(Vector3.UnitY, Vector3.Cross(side.Normal, sideTangent) * side.Tangent.W);
        Assert.Equal(cylinder.Vertices[0].Position, cylinder.Vertices[16].Position);
        Assert.Equal(MathF.PI, cylinder.Vertices[16].Uv.X, 5);
    }

    [Fact]
    public void BevelRoundsOnlyTheEdgesAndKeepsRequestedBounds()
    {
        ModelPrimitive box = PrimitiveModels3D.BeveledBox("box", new(2, 4, 6), .15f, Material).Current.Occurrences.Single().Primitive;
        Assert.Equal(new Vector3(-1, -2, -3), Bounds(minimum: true));
        Assert.Equal(new Vector3(1, 2, 3), Bounds(minimum: false));
        Assert.Contains(box.Vertices, vertex => Math.Abs(vertex.Normal.X) > .1f && Math.Abs(vertex.Normal.Y) > .1f);
        Assert.DoesNotContain(box.Vertices, vertex => vertex.Position == new Vector3(1, 2, 3));

        Vector3 Bounds(bool minimum)
        {
            Vector3 result = box.Vertices[0].Position;
            foreach (ModelVertex vertex in box.Vertices)
            {
                result = minimum ? Vector3.Min(result, vertex.Position) : Vector3.Max(result, vertex.Position);
            }
            return result;
        }
    }

    [Fact]
    public void ContentIdentityIncludesGeometryMaterialAndTextureBytes()
    {
        ModelSlot initial = PrimitiveModels3D.Box("box", Vector3.One, Material);
        Assert.Equal(initial.Current.ContentIdentity, PrimitiveModels3D.Box("box", Vector3.One, Material).Current.ContentIdentity);
        Assert.NotEqual(initial.Current.ContentIdentity, PrimitiveModels3D.Box("box", new(2), Material).Current.ContentIdentity);
        Assert.NotEqual(initial.Current.ContentIdentity, PrimitiveModels3D.Box("box", Vector3.One, Material with { Roughness = .3f }).Current.ContentIdentity);
        var texture = new ModelTextureBinding(new("same-label", 1, 1, [128, 128, 255, 255]), new());
        var normal = Material with { NormalTexture = texture };
        string before = PrimitiveModels3D.Box("box", Vector3.One, normal).Current.ContentIdentity;
        var changed = normal with { NormalTexture = texture with { Texture = texture.Texture with { Rgba = [180, 128, 255, 255] } } };
        Assert.NotEqual(before, PrimitiveModels3D.Box("box", Vector3.One, changed).Current.ContentIdentity);

        ScenePlan plan = SceneCompiler.Compile(Scene.World("test", [Scene.Model("object", initial)]));
        Assert.Single(plan.Models);
        Assert.Empty(plan.Boxes);
        Assert.Empty(plan.Meshes);
    }

    [Fact]
    public void InvalidAndExcessiveParametersHaveDiagnostics()
    {
        Assert.Throws<ArgumentException>(() => PrimitiveModels3D.Plane("bad", new(float.NaN), Material));
        Assert.Throws<ArgumentException>(() => PrimitiveModels3D.BeveledBox("bad", Vector3.One, .5f, Material));
        Assert.Throws<ArgumentException>(() => PrimitiveModels3D.Sphere("bad", 1, Material, 100000));
        Assert.Throws<ArgumentException>(() => PrimitiveModels3D.Revolve("bad", [new(1, 1), new(1, 0)], Material));
        Assert.Throws<ArgumentException>(() => PrimitiveModels3D.Revolve("bad", [new(0, 0), new(0, 1)], Material));
        Assert.Throws<ArgumentException>(() => PrimitiveModels3D.Plane("bad", Vector2.One, Material, Vector2.Zero));
    }
}
