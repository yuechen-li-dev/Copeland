using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using Aurelian.Composition;
using Aurelian.GameWorld2D;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Shaders;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal sealed class SupperPortrait : INativeLayerPresenter
{
    private readonly AurelianVulkanPlant plant;
    private readonly CompiledGraphicsProgram program;
    private readonly TinyFarmSupperGame game;
    private readonly SpriteAtlasResource resource;
    private VulkanOrderedQuadRenderer renderer = null!;
    private NativeSpriteResourceScope resources = null!;

    public SupperPortrait(AurelianVulkanPlant plant, CompiledGraphicsProgram program, TinyFarmSupperGame game, string path)
    {
        this.plant = plant;
        this.program = program;
        this.game = game;
        resource = Load(path);
    }

    public LayerId Layer { get; } = new("mara-portrait");

    public void Attach(VulkanNativeFrameTarget target)
    {
        renderer = new VulkanOrderedQuadRenderer(plant, program, target, Native2DPipelineOptions.SpriteLinear);
        resources = new NativeSpriteResourceScope(renderer, SpriteSampling.Linear);
    }

    public void Resize(VulkanNativeFrameTarget target)
    {
        Detach();
        Attach(target);
    }

    public void Present(NativeLayerFrameContext context)
    {
        if (!game.Dialogue.IsActive)
        {
            return;
        }
        Native2DTextureHandle texture = resources.Resolve(resource);
        context.Present(renderer, pass => pass.SubmitQuad(new NativeQuadSubmission(
            new Native2DRect(55, 101, 260, 260), Native2DUvRect.Full, texture, Native2DTint.White)));
    }

    public void Detach()
    {
        resources?.Dispose();
        renderer?.Dispose();
    }

    private static unsafe SpriteAtlasResource Load(string path)
    {
        using var source = new Bitmap(path);
        BitmapData data = source.LockBits(new Rectangle(0, 0, source.Width, source.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] rgba = new byte[source.Width * source.Height * 4];
        try
        {
            for (int y = 0; y < source.Height; y++)
            {
                byte* row = (byte*)data.Scan0 + y * data.Stride;
                for (int x = 0; x < source.Width; x++)
                {
                    int target = (y * source.Width + x) * 4;
                    rgba[target] = row[x * 4 + 2];
                    rgba[target + 1] = row[x * 4 + 1];
                    rgba[target + 2] = row[x * 4];
                    rgba[target + 3] = row[x * 4 + 3];
                }
            }
        }
        finally
        {
            source.UnlockBits(data);
        }
        return new SpriteAtlasResource(new SpriteAssetId("mara-portrait"), Convert.ToHexString(SHA256.HashData(rgba)),
            (uint)source.Width, (uint)source.Height, rgba, SpriteSampling.Linear);
    }
}
