using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.Composition;
using Aurelian.GameHost;
using InputMan.Core;
using Silk.NET.Maths;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal sealed record TinyFarmContainerProof(string Outcome, string Device, bool Harvest,
    bool Deposit, bool Retrieve, bool KeyAndGearProtected, bool InputIsolation, bool ShippingAtNine,
    bool NoDuplicateAfterLoad, bool OpenWindowRestored, int CoinsEarned, string Hash);

[JsonSerializable(typeof(TinyFarmContainerProof))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class TinyFarmContainerProofJsonContext : JsonSerializerContext;

internal static class TinyFarmContainerNativeProof
{
    public static void Run(string root, TinyFarmGame game, TinyFarmNativeWindow window,
        TinyFarmNativeRenderer renderer, AurelianGameHost host)
    {
        string output = Path.Combine(root, "artifacts", "tinyfarm-shipping-chest-a3");
        Directory.CreateDirectory(output);
        Require(window.IsFocused, "Shipping proof must use the real visible launch focus.");
        window.NativeWindow.WindowBorder = Silk.NET.Windowing.WindowBorder.Hidden;
        window.NativeWindow.Size = new Vector2D<int>(1920, 1080);
        void Frame() => host.RunFrame(TimeSpan.FromSeconds(1.0 / 60));
        void Key(KeyboardKey key)
        {
            window.InjectKey(key, true);
            Frame();
            window.InjectKey(key, false);
            Frame();
        }
        void Click(string action)
        {
            LayerPoint center = renderer.MenuActionCenter(action);
            window.MenuInput.Enqueue(new LayerPointerButtonChanged(center, LayerPointerButton.Primary, true));
            Frame();
            window.MenuInput.Enqueue(new LayerPointerButtonChanged(center, LayerPointerButton.Primary, false));
            Frame();
        }
        void Hold(int x, int y)
        {
            window.InjectKey(KeyboardKey.A, x < 0);
            window.InjectKey(KeyboardKey.D, x > 0);
            window.InjectKey(KeyboardKey.W, y < 0);
            window.InjectKey(KeyboardKey.S, y > 0);
        }
        void Walk(float x, float y)
        {
            for (int step = 0; step < 600; step++)
            {
                ScenePosition current = game.State.ActorScene(TinyFarmIds.Player).WorldPosition;
                int dx = (int)(x * 1024) - current.XUnits;
                int dy = (int)(y * 1024) - current.YUnits;
                if (Math.Abs(dx) < 65 && Math.Abs(dy) < 65)
                {
                    Hold(0, 0);
                    Frame();
                    return;
                }
                Hold(Math.Abs(dx) < 40 ? 0 : Math.Sign(dx), Math.Abs(dy) < 40 ? 0 : Math.Sign(dy));
                Frame();
            }
            throw new InvalidOperationException($"Shipping proof walk stalled toward {x},{y}.");
        }
        void FaceUp()
        {
            Hold(0, -1);
            Frame();
            Hold(0, 0);
            Frame();
        }
        void Shot(string name)
        {
            TinyFarmM25NativeProof.Capture(Path.Combine(output, name + ".png"), renderer, host);
        }

        Key(KeyboardKey.Enter);
        Require(renderer.Layout.Width == 1920 && renderer.Layout.Height == 1080, "1080p framebuffer dimensions are incorrect.");
        Walk(7.5f, 6.5f);
        FaceUp();
        Key(KeyboardKey.E);
        Require(game.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.Turnip) > 0, "Harvest failed.");
        Walk(7.5f, 5.95f);
        FaceUp();
        Shot("chest-closed-world");
        Key(KeyboardKey.E);
        Require(game.Screen == TinyFarmScreen.Container, "Chest interaction failed: " + game.Status);
        Require(game.State.Actor(TinyFarmShippingContent.Chest).Agent!.ObjectPose == TinyFarmObjectPose.Open, "Chest pose stayed closed.");
        Shot("container-open-empty");
        ScenePosition position = game.State.ActorScene(TinyFarmIds.Player).WorldPosition;
        int minute = game.State.Minute;
        Key(KeyboardKey.W);
        Key(KeyboardKey.J);
        Require(game.State.ActorScene(TinyFarmIds.Player).WorldPosition == position && game.State.Minute == minute,
            "Container input moved the player or advanced the world.");
        Click("deposit:product:turnip");
        Require(game.State.ProductCount(TinyFarmShippingContent.Chest, TinyFarmIds.Turnip) == 1, "Deposit failed.");
        Click("withdraw:product:turnip");
        Require(game.State.ProductCount(TinyFarmShippingContent.Chest, TinyFarmIds.Turnip) == 0, "Retrieve failed.");
        Click("deposit:product:turnip");
        Require(game.Execute(new TransferContainerIntent(TinyFarmShippingContent.Chest, true, Item: TinyFarmIds.Axe)).Results[0].Reason
            == IntentReason.EquippedItemProtected, "Equipped gear was not protected.");
        Require(TinyFarmContainers.IsKey(game.State.Item(TinyFarmIds.Letter))
            && TinyFarmContainers.IsKey(game.State.Item(TinyFarmCraftingContent.RecipeCard)), "Key policy failed.");
        Shot("container-queued");
        Require(game.Save(), "Open chest save failed: " + game.Status);
        string saved = TinyFarmSemanticHash.Compute(game.State);
        Click("close");
        Require(game.State.Actor(TinyFarmShippingContent.Chest).Agent!.ObjectPose == TinyFarmObjectPose.Closed, "Close pose failed.");
        Require(game.Load(), "Open chest load failed: " + game.Status);
        Require(game.Screen == TinyFarmScreen.Container && TinyFarmSemanticHash.Compute(game.State) == saved,
            "Open chest window or semantic state was not restored.");
        Click("close");
        int money = game.State.Actor(TinyFarmIds.Player).Money;
        int remaining = TinyFarmContainers.CollectionMinute - game.State.Minute % 1440;
        Require(remaining is > 0 and <= 240, "Native walkthrough missed today's collection.");
        game.Execute(new WaitIntent(remaining));
        Frame();
        int earned = game.Definitions.Item(TinyFarmIds.Turnip).SellPrice;
        Require(game.State.Actor(TinyFarmIds.Player).Money == money + earned
            && game.State.ProductCount(TinyFarmShippingContent.Chest, TinyFarmIds.Turnip) == 0, "09:00 payout failed.");
        Require(game.Save(), "Receipt save failed.");
        Require(game.Load(), "Receipt load failed.");
        game.Execute(new WaitIntent(1));
        Require(game.State.Actor(TinyFarmIds.Player).Money == money + earned, "Reload caused a duplicate payout.");
        Key(KeyboardKey.E);
        Require(game.Screen == TinyFarmScreen.Container, "Chest did not reopen after collection.");
        Shot("container-receipt-1080p");
        window.NativeWindow.Size = new Vector2D<int>(2560, 1440);
        Frame();
        Frame();
        Require(renderer.Layout.Width == 2560 && renderer.Layout.Height == 1440, "1440p framebuffer dimensions are incorrect.");
        Shot("container-receipt-1440p");
        WritePoseSheet(Path.Combine(output, "chest-poses.png"));
        var proof = new TinyFarmContainerProof("Success", renderer.Device, true, true, true, true,
            true, true, true, true, earned, TinyFarmSemanticHash.Compute(game.State));
        File.WriteAllText(Path.Combine(output, "native-proof.json"),
            JsonSerializer.Serialize(proof, TinyFarmContainerProofJsonContext.Default.TinyFarmContainerProof));
        Console.WriteLine("TINYFARM_CONTAINER_A3_QUALIFIED");
    }

    private static void WritePoseSheet(string path)
    {
        byte[] pixels = TinyFarmChestArt.Create().Rgba8;
        using var bitmap = new Bitmap(512, 256, PixelFormat.Format32bppArgb);
        for (int y = 0; y < 256; y++)
        {
            for (int x = 0; x < 512; x++)
            {
                int offset = (y * 512 + x) * 4;
                bitmap.SetPixel(x, y, Color.FromArgb(pixels[offset + 3], pixels[offset], pixels[offset + 1], pixels[offset + 2]));
            }
        }
        bitmap.Save(path, ImageFormat.Png);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
