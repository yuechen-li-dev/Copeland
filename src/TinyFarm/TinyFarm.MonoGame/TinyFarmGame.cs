using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Aurelian.Composition;
using TinyFarm.Core;
using TinyFarm.Presentation;

internal sealed class TinyFarmGame : Game
{
    private readonly GraphicsDeviceManager graphics;
    private readonly TinyFarmDefinitions definitions;
    private readonly string savePath;
    private readonly TinyFarmSimulationHost simulationHost;
    private readonly TinyFarmPlayerUiController playerUiController;
    private TinyFarmSession session => simulationHost.Session;
    private SpriteBatch? spriteBatch;
    private Texture2D? pixel;
    private KeyboardState previousKeyboard;
    private MouseState previousMouse;
    private IReadOnlyList<NarrativeLine> narrative = [];
    private string status = "Welcome to TinyFarm";
    private readonly TinyFarmApplicationMessageSink applicationMessageSink = new();
    private AurelianLayerCompositor? compositor;
    private TinyFarmFrame? compositionFrame;
    private ulong compositionFrameId;
    private readonly TinyFarmCompositionMetrics compositionMetrics = new();
    private readonly int compositionProofFrames;

    public TinyFarmGame(string[] args)
    {
        graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = ReadIntOption(args, "--width", 2560),
            PreferredBackBufferHeight = ReadIntOption(args, "--height", 1440),
            SynchronizeWithVerticalRetrace = true
        };
        Window.Title = "TinyFarm M21 - Old Burrow";
        Window.AllowUserResizing = true;
        IsMouseVisible = true;
        definitions = TinyFarmDefinitionLoader.LoadM21();
        simulationHost = new TinyFarmSimulationHost(
            new TinyFarmSession(TinyFarmM21ControlStates.Create(definitions), definitions),
            definitions,
            TinyFarmSimulationMode.Playing);
        playerUiController = new TinyFarmPlayerUiController(simulationHost);
        savePath = ReadOption(args, "--save-file")
            ?? Path.Combine(Environment.CurrentDirectory, "tiny-farm.save");
        compositionProofFrames = ReadPositiveIntOption(args, "--composition-proof-frames");
    }

    protected override void LoadContent()
    {
        spriteBatch = new SpriteBatch(GraphicsDevice);
        pixel = new Texture2D(GraphicsDevice, 1, 1);
        pixel.SetData([Color.White]);
        LayerSurfaceDescriptor surface = CurrentSurface();
        compositor = new AurelianLayerCompositor(surface);
        compositor.Add(new TinyFarmMonoGameWorldLayer(
            () => DrawWorld(compositionFrame ?? throw new InvalidOperationException("World frame is unavailable.")),
            surface,
            compositionMetrics));
        var machinaLayer = new TinyFarmMachinaUiLayer(applicationMessageSink, surface);
        compositor.Add(new TinyFarmMachinaMonoGameLayer(
            machinaLayer,
            new TinyFarmMonoGamePresentationRenderer(spriteBatch, pixel),
            compositionMetrics));
        compositor.Attach();
    }

    protected override void Update(GameTime gameTime)
    {
        KeyboardState keyboard = Keyboard.GetState();
        MouseState mouse = Mouse.GetState();
        if (Pressed(keyboard, Keys.Escape))
        {
            Exit();
            return;
        }

        EnsureCompositionSurface();
        PublishUiSnapshot();
        RouteUiInput(keyboard, mouse);
        ApplyUiCommands();

        if (Pressed(keyboard, Keys.F5))
        {
            Save();
        }
        else if (Pressed(keyboard, Keys.F9))
        {
            Load();
        }
        else if (!playerUiController.InventoryOpen
            && simulationHost.Mode != TinyFarmSimulationMode.Paused
            && Pressed(keyboard, Keys.E))
        {
            ApplyControl(TinyFarmControl.Interact);
        }
        else if (!playerUiController.InventoryOpen
            && simulationHost.Mode != TinyFarmSimulationMode.Paused
            && ReadControl(keyboard) is TinyFarmControl control)
        {
            ApplyControl(control);
        }


        TinyFarmControl? heldMovement = simulationHost.Mode == TinyFarmSimulationMode.Paused
            || playerUiController.SuppressWorldMovement
            ? null
            : ReadHeldMovement(keyboard);
        if (heldMovement is null)
        {
            simulationHost.SetPlayerMovement(0, 0);
        }
        else
        {
            (int deltaX, int deltaY) = heldMovement.Value switch
            {
                TinyFarmControl.MoveLeft => (-1, 0),
                TinyFarmControl.MoveRight => (1, 0),
                TinyFarmControl.MoveUp => (0, -1),
                _ => (0, 1)
            };
            simulationHost.SetPlayerMovement(deltaX, deltaY);
        }
        TinyFarmHostAdvanceResult hostAdvance = simulationHost.AdvanceHostTime(gameTime.ElapsedGameTime);
        if (hostAdvance.Narrative.Count > 0)
        {
            narrative = hostAdvance.Narrative;
        }
        IntentResult? movement = hostAdvance.Results.LastOrDefault(result =>
            result.Envelope.Source == IntentSourceKind.Human
            && result.Envelope.Intent is SpatialMoveIntent);
        if (movement is not null)
        {
            status = movement.Status == IntentResultStatus.Accepted
                ? "Move"
                : $"{movement.Status}: {movement.Reason}";
        }
        previousKeyboard = keyboard;
        previousMouse = mouse;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(34, 52, 43));
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(session.State, definitions, narrative);
        compositionFrame = frame;
        PublishUiSnapshot(frame);
        simulationHost.ObserveRenderFrame();
        spriteBatch!.Begin(samplerState: SamplerState.PointClamp);
        compositor!.RunFrame(compositionFrameId++, gameTime.ElapsedGameTime);
        spriteBatch.End();
        if (compositionProofFrames > 0 && compositionMetrics.Frames >= compositionProofFrames)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
                compositionMetrics.Snapshot(),
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Exit();
        }
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        compositor?.Dispose();
        compositor = null;
        base.UnloadContent();
    }

    private LayerSurfaceDescriptor CurrentSurface()
    {
        return new LayerSurfaceDescriptor(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    }

    private void EnsureCompositionSurface()
    {
        LayerSurfaceDescriptor current = CurrentSurface();
        if (compositor!.Surface.Width != current.Width || compositor.Surface.Height != current.Height)
        {
            compositor.Resize(current);
        }
    }

    private void PublishUiSnapshot(TinyFarmFrame? projectedFrame = null)
    {
        TinyFarmFrame frame = projectedFrame ?? TinyFarmFrameProjector.Project(session.State, definitions, narrative);
        var snapshot = new TinyFarmPresentationSnapshot(
            TinyFarmPlayerUiProjector.Project(session.State, definitions),
            frame.Day,
            frame.Time,
            frame.CurrentLocationName,
            simulationHost.Mode,
            playerUiController.InventoryOpen,
            status,
            frame.InteractionHints,
            frame.Narrative.ToArray());
        compositor!.SendToLayer(new LayerMessage<TinyFarmPresentationSnapshot>(
            TinyFarmMachinaUiLayer.ApplicationId,
            TinyFarmMachinaUiLayer.Id,
            snapshot));
    }

    private void RouteUiInput(KeyboardState keyboard, MouseState mouse)
    {
        foreach ((Keys nativeKey, LayerKey layerKey) in UiKeyBindings())
        {
            if (Pressed(keyboard, nativeKey))
            {
                compositor!.RouteInput(new LayerKeyChanged(layerKey, true));
            }
            else if (Released(keyboard, nativeKey))
            {
                compositor!.RouteInput(new LayerKeyChanged(layerKey, false));
            }
        }

        if (mouse.Position != previousMouse.Position)
        {
            compositor!.RouteInput(new LayerPointerMoved(
                new LayerPoint(mouse.X, mouse.Y),
                new LayerPoint(previousMouse.X, previousMouse.Y)));
        }

        if (mouse.ScrollWheelValue != previousMouse.ScrollWheelValue)
        {
            compositor!.RouteInput(new LayerPointerWheel(
                new LayerPoint(mouse.X, mouse.Y),
                0,
                (mouse.ScrollWheelValue - previousMouse.ScrollWheelValue) / 120d));
        }

        if (mouse.LeftButton != previousMouse.LeftButton)
        {
            compositor!.RouteInput(new LayerPointerButtonChanged(
                new LayerPoint(mouse.X, mouse.Y),
                LayerPointerButton.Primary,
                mouse.LeftButton == ButtonState.Pressed));
        }
    }

    private void ApplyUiCommands()
    {
        while (applicationMessageSink.TryDequeue(out TinyFarmUiCommandDto? command))
        {
            switch (command!.Kind)
            {
                case TinyFarmUiCommandKind.SelectHotbarSlot:
                    var slot = new HotbarSlotId(command.HotbarSlot!.Value);
                    playerUiController.ClickSlot(slot);
                    status = $"Selected hotbar slot {slot.Value}";
                    break;
                case TinyFarmUiCommandKind.ToggleInventory:
                    playerUiController.HandleKey(TinyFarmUiKey.Inventory);
                    status = playerUiController.InventoryOpen ? "Inventory opened" : "Inventory closed";
                    break;
                case TinyFarmUiCommandKind.TogglePausePlay:
                    playerUiController.HandleKey(TinyFarmUiKey.PausePlay);
                    status = simulationHost.Mode == TinyFarmSimulationMode.Paused
                        ? "Simulation paused"
                        : "Simulation playing";
                    break;
                case TinyFarmUiCommandKind.ToggleFastForward:
                    playerUiController.HandleKey(TinyFarmUiKey.FastForward);
                    status = simulationHost.Mode == TinyFarmSimulationMode.FastForward
                        ? "Simulation fast forward x10"
                        : "Simulation playing";
                    break;
                case TinyFarmUiCommandKind.Wait:
                    playerUiController.HandleKey(TinyFarmUiKey.Wait);
                    break;
                case TinyFarmUiCommandKind.UseSelected:
                    if (!playerUiController.InventoryOpen && simulationHost.Mode != TinyFarmSimulationMode.Paused)
                    {
                        ApplyControl(TinyFarmControl.UseSelected);
                    }
                    break;
                case TinyFarmUiCommandKind.Interact:
                    if (!playerUiController.InventoryOpen
                        && simulationHost.Mode != TinyFarmSimulationMode.Paused
                        && narrative.Count > 0)
                    {
                        narrative = [];
                        status = "Conversation closed";
                    }
                    else if (!playerUiController.InventoryOpen && simulationHost.Mode != TinyFarmSimulationMode.Paused)
                    {
                        ApplyControl(TinyFarmControl.Interact);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command), command.Kind, "Unknown UI command.");
            }
        }
    }

    private static IReadOnlyList<(Keys NativeKey, LayerKey LayerKey)> UiKeyBindings()
    {
        return
        [
            (Keys.D1, LayerKey.Number1),
            (Keys.D2, LayerKey.Number2),
            (Keys.D3, LayerKey.Number3),
            (Keys.D4, LayerKey.Number4),
            (Keys.D5, LayerKey.Number5),
            (Keys.D6, LayerKey.Number6),
            (Keys.D7, LayerKey.Number7),
            (Keys.D8, LayerKey.Number8),
            (Keys.I, LayerKey.I),
            (Keys.Space, LayerKey.Space),
            (Keys.F, LayerKey.F),
            (Keys.N, LayerKey.N),
            (Keys.Q, LayerKey.Q),
            (Keys.Enter, LayerKey.Enter),
            (Keys.Left, LayerKey.ArrowLeft),
            (Keys.Right, LayerKey.ArrowRight),
            (Keys.Up, LayerKey.ArrowUp),
            (Keys.Down, LayerKey.ArrowDown)
        ];
    }

    private void DrawWorld(TinyFarmFrame frame)
    {
        if (frame.ActiveScene is not null)
        {
            DrawScene(frame);
            return;
        }

        TinyFarmPoint player = frame.Actors.Single(actor => actor.IsPlayer).Position;
        int cameraX = Math.Clamp(player.X - 400, 0, 120);
        int cameraY = Math.Clamp(player.Y - 250, 0, 80);

        foreach (TinyFarmLocationView location in frame.Locations)
        {
            foreach (LocationId exit in location.Exits.Where(exit => string.CompareOrdinal(location.Id.Value, exit.Value) < 0))
            {
                TinyFarmLocationView target = frame.Locations.Single(candidate => candidate.Id == exit);
                DrawLine(ToVector(location.Position, cameraX, cameraY), ToVector(target.Position, cameraX, cameraY), new Color(112, 91, 62), 12);
            }
        }

        foreach (TinyFarmLocationView location in frame.Locations)
        {
            Vector2 position = ToVector(location.Position, cameraX, cameraY);
            Color color = location.Id == TinyFarmIds.Farmhouse
                ? new Color(105, 155, 78)
                : location.Id == TinyFarmIds.GeneralStore
                    ? new Color(180, 119, 67)
                    : location.Id == TinyFarmIds.Riverside
                        ? new Color(60, 137, 176)
                        : new Color(196, 174, 118);
            Fill(new Rectangle((int)position.X - 72, (int)position.Y - 48, 144, 96), color);
            Border(new Rectangle((int)position.X - 72, (int)position.Y - 48, 144, 96), location.IsCurrent ? Color.Gold : new Color(50, 43, 35), location.IsCurrent ? 5 : 2);
            BitmapText.Draw(spriteBatch!, pixel!, location.Name.ToUpperInvariant(), position + new Vector2(-62, -40), Color.White, 2);
        }

        foreach (TinyFarmPlotView plot in frame.Plots)
        {
            Vector2 position = ToVector(plot.Position, cameraX, cameraY);
            Color soil = plot.WateredToday ? new Color(80, 83, 109) : new Color(112, 73, 46);
            Fill(new Rectangle((int)position.X - 22, (int)position.Y - 15, 44, 30), soil);
            if (plot.Crop is not null)
            {
                int height = 6 + (plot.GrowthStage * 6);
                Color crop = plot.Harvestable ? Color.Gold : new Color(92, 190, 80);
                Fill(new Rectangle((int)position.X - 5, (int)position.Y - height, 10, height), crop);
            }
        }

        foreach (TinyFarmItemView item in frame.GroundItems)
        {
            Vector2 position = ToVector(item.Position, cameraX, cameraY);
            Fill(new Rectangle((int)position.X - 5, (int)position.Y - 5, 10, 10), new Color(127, 220, 130));
        }

        foreach (TinyFarmActorView actor in frame.Actors)
        {
            Vector2 position = ToVector(actor.Position, cameraX, cameraY);
            Color color = actor.IsPlayer ? new Color(245, 218, 95) : ActorColor(actor.Id);
            Fill(new Rectangle((int)position.X - 10, (int)position.Y - 16, 20, 30), color);
            Border(new Rectangle((int)position.X - 10, (int)position.Y - 16, 20, 30), new Color(30, 30, 30), 2);
            BitmapText.Draw(spriteBatch!, pixel!, actor.Name.ToUpperInvariant(), position + new Vector2(-18, 18), Color.White, 1);
        }
    }

    private void DrawScene(TinyFarmFrame frame)
    {
        int viewportWidth = GraphicsDevice.Viewport.Width;
        int viewportHeight = GraphicsDevice.Viewport.Height;
        int worldHeight = viewportHeight;
        int tileSize = Math.Max(
            12,
            Math.Min((viewportWidth - 48) / frame.SceneWidth, (worldHeight - 48) / frame.SceneHeight));
        int scenePixelWidth = frame.SceneWidth * tileSize;
        int scenePixelHeight = frame.SceneHeight * tileSize;
        int offsetX = (viewportWidth - scenePixelWidth) / 2;
        int offsetY = (worldHeight - scenePixelHeight) / 2;

        Fill(new Rectangle(offsetX, offsetY, scenePixelWidth, scenePixelHeight), new Color(75, 111, 67));
        for (int x = 0; x <= frame.SceneWidth; x++)
        {
            Fill(new Rectangle(offsetX + (x * tileSize), offsetY, 1, scenePixelHeight), new Color(64, 91, 59));
        }
        for (int y = 0; y <= frame.SceneHeight; y++)
        {
            Fill(new Rectangle(offsetX, offsetY + (y * tileSize), scenePixelWidth, 1), new Color(64, 91, 59));
        }

        foreach (TinyFarmSceneObjectView item in frame.SceneObjects ?? [])
        {
            var rectangle = new Rectangle(
                offsetX + (item.Position.X * tileSize),
                offsetY + (item.Position.Y * tileSize),
                item.Width * tileSize,
                item.Height * tileSize);
            if (item.Kind == SceneObjectKind.Tree)
            {
                DrawTree(rectangle, item.Depleted);
            }
            else
            {
                Fill(rectangle, SceneObjectColor(item.Kind));
            }
            Border(rectangle, item.BlocksMovement ? new Color(45, 39, 31) : new Color(224, 192, 96), Math.Max(1, tileSize / 24));
            if (item.Kind is SceneObjectKind.Portal
                or SceneObjectKind.Landmark
                or SceneObjectKind.Shop
                or SceneObjectKind.Bed
                or SceneObjectKind.Forage
                or SceneObjectKind.CookingStation
                or SceneObjectKind.Tree
                or SceneObjectKind.Enemy)
            {
                int textScale = tileSize >= 72 ? 2 : 1;
                BitmapText.Draw(
                    spriteBatch!,
                    pixel!,
                    item.Label.ToUpperInvariant(),
                    new Vector2(rectangle.X + 4, rectangle.Y + 4),
                    Color.White,
                    textScale);
            }
        }

        foreach (TinyFarmPlotView plot in frame.Plots)
        {
            var rectangle = new Rectangle(
                offsetX + (plot.Position.X * tileSize) + (tileSize / 8),
                offsetY + (plot.Position.Y * tileSize) + (tileSize / 8),
                tileSize * 3 / 4,
                tileSize * 3 / 4);
            Fill(rectangle, plot.WateredToday ? new Color(75, 80, 110) : new Color(112, 73, 46));
            if (plot.Crop is not null)
            {
                int growthHeight = Math.Max(5, tileSize * (plot.GrowthStage + 1) / Math.Max(2, plot.GrowthDays + 1));
                Fill(
                    new Rectangle(rectangle.Center.X - (tileSize / 12), rectangle.Bottom - growthHeight, tileSize / 6, growthHeight),
                    plot.Harvestable ? Color.Gold : new Color(92, 190, 80));
            }
        }

        foreach (TinyFarmItemView item in frame.GroundItems)
        {
            float sceneX = (float)item.Position.X / frame.SceneUnitsPerTile;
            float sceneY = (float)item.Position.Y / frame.SceneUnitsPerTile;
            int centerX = offsetX + (int)MathF.Round(sceneX * tileSize);
            int centerY = offsetY + (int)MathF.Round(sceneY * tileSize);
            int size = Math.Max(8, tileSize / 5);
            var rectangle = new Rectangle(centerX - (size / 2), centerY - (size / 2), size, size);
            Fill(rectangle, new Color(127, 220, 130));
            Border(rectangle, Color.Gold, Math.Max(1, tileSize / 30));
            BitmapText.Draw(
                spriteBatch!,
                pixel!,
                item.Name.ToUpperInvariant(),
                new Vector2(rectangle.Right + 3, rectangle.Y),
                Color.White,
                1);
        }

        foreach (TinyFarmActorView actor in frame.Actors)
        {
            float sceneX = (float)actor.Position.X / frame.SceneUnitsPerTile;
            float sceneY = (float)actor.Position.Y / frame.SceneUnitsPerTile;
            int centerX = offsetX + (int)MathF.Round(sceneX * tileSize);
            int centerY = offsetY + (int)MathF.Round(sceneY * tileSize);
            if (frame.SceneUnitsPerTile == 1)
            {
                centerX += tileSize / 2;
                centerY += tileSize / 2;
            }
            int actorWidth = Math.Max(10, tileSize / 3);
            int actorHeight = Math.Max(16, tileSize / 2);
            var rectangle = new Rectangle(centerX - (actorWidth / 2), centerY - (actorHeight / 2), actorWidth, actorHeight);
            Fill(rectangle, actor.IsPlayer ? new Color(245, 218, 95) : ActorColor(actor.Id));
            Border(rectangle, new Color(30, 30, 30), Math.Max(1, tileSize / 24));
            if (actor.IsInteractionTarget)
            {
                Border(rectangle, Color.Gold, Math.Max(2, tileSize / 16));
            }
            BitmapText.Draw(
                spriteBatch!,
                pixel!,
                actor.Name.ToUpperInvariant(),
                new Vector2(centerX - (actorWidth / 2), rectangle.Bottom + 2),
                Color.White,
                tileSize >= 72 ? 2 : 1);
            if (actor.Energy is int energy)
            {
                string activity = actor.IsResting ? "RESTING" : actor.Regime?.ToString().ToUpperInvariant() ?? "ACTIVE";
                BitmapText.Draw(
                    spriteBatch!,
                    pixel!,
                    $"ENERGY {energy / 100d:0.00}  {activity}",
                    new Vector2(centerX - (actorWidth / 2), rectangle.Bottom + (tileSize >= 72 ? 24 : 12)),
                    actor.IsResting ? new Color(135, 220, 255) : new Color(204, 221, 190),
                    1);
            }
        }
    }

    private TinyFarmControl? ReadControl(KeyboardState keyboard)
    {
        (Keys Key, TinyFarmControl Control)[] bindings =
        [
            (Keys.L, TinyFarmControl.Look),
            (Keys.E, TinyFarmControl.Talk),
            (Keys.T, TinyFarmControl.Take),
            (Keys.G, TinyFarmControl.Give),
            (Keys.B, TinyFarmControl.Buy),
            (Keys.V, TinyFarmControl.Sell),
            (Keys.P, TinyFarmControl.Plant),
            (Keys.R, TinyFarmControl.Water),
            (Keys.H, TinyFarmControl.Harvest)
        ];
        foreach ((Keys key, TinyFarmControl control) in bindings)
        {
            if (Pressed(keyboard, key))
            {
                return control;
            }
        }
        return null;
    }

    private static TinyFarmControl? ReadHeldMovement(KeyboardState keyboard)
    {
        if (keyboard.IsKeyDown(Keys.Left) || keyboard.IsKeyDown(Keys.A))
        {
            return TinyFarmControl.MoveLeft;
        }
        if (keyboard.IsKeyDown(Keys.Right) || keyboard.IsKeyDown(Keys.D))
        {
            return TinyFarmControl.MoveRight;
        }
        if (keyboard.IsKeyDown(Keys.Up) || keyboard.IsKeyDown(Keys.W))
        {
            return TinyFarmControl.MoveUp;
        }
        if (keyboard.IsKeyDown(Keys.Down) || keyboard.IsKeyDown(Keys.S))
        {
            return TinyFarmControl.MoveDown;
        }
        return null;
    }

    private bool Pressed(KeyboardState keyboard, Keys key) => keyboard.IsKeyDown(key) && previousKeyboard.IsKeyUp(key);

    private bool Released(KeyboardState keyboard, Keys key) => keyboard.IsKeyUp(key) && previousKeyboard.IsKeyDown(key);

    private bool Pressed(MouseState mouse)
    {
        return mouse.LeftButton == ButtonState.Pressed
            && previousMouse.LeftButton == ButtonState.Released;
    }

    private void ApplyControl(TinyFarmControl control)
    {
        GameIntent? intent = TinyFarmHumanController.Map(control, session.State, definitions);
        if (intent is null)
        {
            status = "Nothing to do here";
            narrative = [];
            return;
        }

        TinyFarmStepResult step = simulationHost.ExecuteIntent(intent);
        narrative = step.Narrative;
        IntentResult human = step.Results.Single(result => result.Envelope.Source == IntentSourceKind.Human);
        status = human.Status == IntentResultStatus.Accepted
            ? intent.GetType().Name.Replace("Intent", string.Empty, StringComparison.Ordinal)
            : $"{human.Status}: {human.Reason}";
    }

    private void Save()
    {
        string fullPath = Path.GetFullPath(savePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, session.CaptureWeekSave());
        status = $"Saved {Path.GetFileName(fullPath)}";
    }

    private void Load()
    {
        try
        {
            simulationHost.ReplaceSession(
                TinyFarmChunkedSaveCodec.Read(File.ReadAllBytes(Path.GetFullPath(savePath)), definitions));
            narrative = [];
            status = "Loaded authoritative state";
        }
        catch (IOException exception)
        {
            status = exception.Message;
        }
    }

    private void Fill(Rectangle rectangle, Color color) => spriteBatch!.Draw(pixel!, rectangle, color);

    private void Border(Rectangle rectangle, Color color, int width)
    {
        Fill(new Rectangle(rectangle.Left, rectangle.Top, rectangle.Width, width), color);
        Fill(new Rectangle(rectangle.Left, rectangle.Bottom - width, rectangle.Width, width), color);
        Fill(new Rectangle(rectangle.Left, rectangle.Top, width, rectangle.Height), color);
        Fill(new Rectangle(rectangle.Right - width, rectangle.Top, width, rectangle.Height), color);
    }

    private void DrawLine(Vector2 from, Vector2 to, Color color, int width)
    {
        Vector2 delta = to - from;
        spriteBatch!.Draw(pixel!, from, null, color, MathF.Atan2(delta.Y, delta.X), Vector2.Zero, new Vector2(delta.Length(), width), SpriteEffects.None, 0);
    }

    private static Vector2 ToVector(TinyFarmPoint point, int cameraX, int cameraY) => new(point.X - cameraX, point.Y - cameraY);

    private static Color ActorColor(ActorId id) => id == TinyFarmIds.Mara
        ? new Color(201, 100, 126)
        : id == TinyFarmIds.Elias
            ? new Color(105, 154, 209)
            : new Color(188, 129, 205);

    private static Color SceneObjectColor(SceneObjectKind kind)
    {
        return kind switch
        {
            SceneObjectKind.Portal => new Color(158, 109, 197),
            SceneObjectKind.Plot => new Color(112, 73, 46),
            SceneObjectKind.Shop => new Color(178, 115, 65),
            SceneObjectKind.Landmark => new Color(190, 164, 105),
            SceneObjectKind.Decoration => new Color(52, 126, 174),
            SceneObjectKind.Bed => new Color(74, 111, 153),
            SceneObjectKind.Forage => new Color(179, 153, 112),
            SceneObjectKind.CookingStation => new Color(166, 92, 72),
            SceneObjectKind.Tree => new Color(45, 122, 62),
            SceneObjectKind.Enemy => new Color(88, 196, 102),
            _ => new Color(91, 103, 70)
        };
    }

    private void DrawTree(Rectangle rectangle, bool depleted)
    {
        int trunkWidth = Math.Max(4, rectangle.Width / 4);
        int trunkHeight = depleted ? Math.Max(6, rectangle.Height / 3) : Math.Max(8, rectangle.Height / 2);
        var trunk = new Rectangle(
            rectangle.Center.X - (trunkWidth / 2),
            rectangle.Bottom - trunkHeight,
            trunkWidth,
            trunkHeight);
        Fill(trunk, new Color(102, 67, 39));
        if (!depleted)
        {
            var canopy = new Rectangle(
                rectangle.X + (rectangle.Width / 8),
                rectangle.Y,
                rectangle.Width * 3 / 4,
                rectangle.Height * 2 / 3);
            Fill(canopy, new Color(45, 122, 62));
        }
    }

    private static string? ReadOption(string[] args, string option)
    {
        int index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int ReadIntOption(string[] args, string option, int fallback)
    {
        string? value = ReadOption(args, option);
        return int.TryParse(value, out int parsed) && parsed >= 640 ? parsed : fallback;
    }

    private static int ReadPositiveIntOption(string[] args, string option)
    {
        string? value = ReadOption(args, option);
        return int.TryParse(value, out int parsed) && parsed > 0 ? parsed : 0;
    }
}
