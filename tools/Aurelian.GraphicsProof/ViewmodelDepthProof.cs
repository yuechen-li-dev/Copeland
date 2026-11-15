using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;

internal static class ViewmodelDepthProof
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var created = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Viewmodel depth proof"));
        if (!created.Success)
        {
            throw new InvalidOperationException(string.Join("; ", created.Diagnostics.Select(item => item.Message)));
        }
        using var plant = created.Plant!;
        var shader = new GameAssets().Shader("Solid3D.v.ts");
        var results = new List<object>();
        foreach (var size in new[] { (Width: 160, Height: 120), (Width: 200, Height: 128) })
        {
            using var target = new VulkanNativeFrameTarget(plant, (uint)size.Width, (uint)size.Height);
            using var world = new VulkanSolid3DRenderer(plant, shader, target);
            using var foreground = new VulkanSolid3DRenderer(plant, shader, target, preserveColor: true);
            using var unordered = new VulkanSolid3DRenderer(plant, shader, target, enableDepth: false, preserveColor: true);
            var clear = new NativeFrameClearColor(.02f, .02f, .02f, 1);
            float aspect = (float)size.Width / size.Height;
            Matrix4x4 Camera(float near)
            {
                var camera = Matrix4x4.CreatePerspectiveFieldOfView(1, aspect, near, 4);
                camera.M22 *= -1;
                return camera;
            }
            var wall = Quad(-.15f, 1, new(0, 0, 1, 1));
            var near = Quad(-.35f, .065f, new(0, 1, 0, 1));
            var far = Quad(-.55f, .13f, new(1, 0, 0, 1));
            Native3DVertex[] first = [.. near, .. far];
            Native3DVertex[] reverse = [.. far, .. near];
            byte[] baseline = DrawWorld();
            // A conventional world pass correctly hides both weapon quads behind the wall.
            byte[] worldOnly = world.Render([.. wall, .. first], Camera(.1f), clear, capture: true).Pixels!;
            Require(baseline.SequenceEqual(worldOnly), "The world occluder did not hide the conventional weapon.");
            byte[] layered = foreground.Render(first, Camera(.025f), clear, capture: true).Pixels!;
            DrawWorld();
            byte[] oppositeOrder = foreground.Render(reverse, Camera(.025f), clear, capture: true).Pixels!;
            Require(layered.SequenceEqual(oppositeOrder), "Foreground self-occlusion changed with submission order.");
            int changed = Changed(baseline, layered);
            Require(changed > 100 && changed < size.Width * size.Height / 2, "Foreground did not preserve world pixels around the weapon.");
            Require(baseline.AsSpan(0, size.Width * 4).SequenceEqual(layered.AsSpan(0, size.Width * 4)),
                "The foreground cleared the existing world color.");
            int center = (size.Height / 2 * size.Width + size.Width / 2) * 4;
            Require(layered[center + 1] > layered[center] * 2, "Near green weapon geometry did not occlude far red geometry.");
            DrawWorld();
            byte[] disabledDepth = unordered.Render(first, Camera(.025f), clear, capture: true).Pixels!;
            Require(Changed(layered, disabledDepth) > 100, "Disabling foreground depth did not break self-occlusion.");

            var close = Quad(-.05f, .012f, new(0, 1, 1, 1));
            DrawWorld();
            byte[] clipped = foreground.Render(close, Camera(.1f), clear, capture: true).Pixels!;
            Require(clipped.SequenceEqual(baseline), "World near plane unexpectedly retained the close weapon.");
            DrawWorld();
            byte[] closeVisible = foreground.Render(close, Camera(.025f), clear, capture: true).Pixels!;
            int nearPlanePixels = Changed(baseline, closeVisible);
            Require(nearPlanePixels > 100, "Independent weapon near plane did not retain close geometry.");
            string prefix = size.Width + "x" + size.Height;
            NativeGameGraphics.WritePng(Path.Combine(output, prefix + "-layered.png"), size.Width, size.Height, layered);
            NativeGameGraphics.WritePng(Path.Combine(output, prefix + "-near.png"), size.Width, size.Height, closeVisible);
            results.Add(new { size.Width, size.Height, ChangedPixels = changed, NearPlanePixels = nearPlanePixels,
                OrderIndependent = true, WorldColorPreserved = true });

            byte[] DrawWorld() => world.Render(wall, Camera(.1f), clear, capture: true).Pixels!;
        }
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            Results = results,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("VIEWMODEL_DEPTH_ACCEPTED " + evidence);
    }

    private static Native3DVertex[] Quad(float z, float halfSize, Vector4 color)
    {
        Native3DVertex Point(float x, float y) => new(new(x, y, z), Vector3.UnitZ, color);
        return [Point(-halfSize, -halfSize), Point(halfSize, -halfSize), Point(halfSize, halfSize),
            Point(-halfSize, -halfSize), Point(halfSize, halfSize), Point(-halfSize, halfSize)];
    }

    private static int Changed(byte[] left, byte[] right)
    {
        int count = 0;
        for (int offset = 0; offset < left.Length; offset += 4)
        {
            if (!left.AsSpan(offset, 4).SequenceEqual(right.AsSpan(offset, 4)))
            {
                count++;
            }
        }
        return count;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
