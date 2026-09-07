using System.Diagnostics;
using Aurelian.GameWorld2D;
using Aurelian.Spatial2D;
using Aurelian.Strategy;
using InputMan.Core;
using SkiaSharp;

namespace Aurelian.StrategyDemo;

public static class StrategyProof
{
    public static int Run()
    {
        CheckSelectionAndFog();
        ProofArtifacts.Write("fresh-system-proof.json", FreshSystemProof.Run());
        ProofArtifacts.Write("fresh-presentation-proof.json", FreshPresentationProof.Run());
        var session = new StrategySession();
        var view = new StrategyView();
        view.Center(StrategySession.Home);
        using var renderer = new StrategyRenderer(Path.Combine(AppContext.BaseDirectory, "Assets"));
        Save("strategy-sample-main.png");
        using (var input = new StrategyInput(session, view))
        {
            SKPoint worker = view.Screen(session.Units[2].Position);
            Click(input, view, worker.X, worker.Y, .1);
            Require(session.Selection.Contains(3), "Physical -> InputMan -> typed selection failed.");
            Save("strategy-sample-selection.png");
            input.RecordButton(Controls.Key(KeyboardKey.LeftControl), true);
            input.RecordButton(Controls.Key(KeyboardKey.Number1), true);
            input.Update(TimeSpan.Zero, TimeSpan.FromSeconds(.2));
            input.RecordButton(Controls.Key(KeyboardKey.LeftControl), false);
            input.RecordButton(Controls.Key(KeyboardKey.Number1), false);
            input.Update(TimeSpan.Zero, TimeSpan.FromSeconds(.3));
            session.Dispatch(new SelectIntent([]));
            input.RecordButton(Controls.Key(KeyboardKey.Number1), true);
            input.Update(TimeSpan.Zero, TimeSpan.FromSeconds(.4));
            Require(session.Selection.Contains(3), "Control-group recall failed.");
            input.Focus(false);
            int count = session.Tape.Count;
            input.Update(TimeSpan.Zero, TimeSpan.FromSeconds(.5));
            input.Focus(true);
            input.Update(TimeSpan.Zero, TimeSpan.FromSeconds(.6));
            Require(session.Tape.Count == count, "Focus regain synthesized a stale command.");
            string before = session.Hash();
            WorldPoint2 cameraBefore = view.Camera.Position;
            Click(input, view, 1150, 695, .8);
            Require(session.Hash() == before && view.Camera.Position != cameraBefore, "Minimap changed game truth or failed to move camera.");
            Save("strategy-sample-minimap.png");
        }
        view.Center(StrategySession.Home);
        view.Placing = true;
        view.Pointer = new(15, 14);
        string prePreview = session.Hash();
        Save("strategy-sample-build.png");
        Require(session.Hash() == prePreview, "Preview mutated semantic state.");
        int stock = session.Stock;
        Require(!session.Dispatch(new BuildIntent(StrategySession.Home)).Accepted && session.Stock == stock, "Rejected build charged resources.");
        view.Placing = false;

        var first = Scenario([TimeSpan.FromMilliseconds(100)]);
        var second = Scenario([TimeSpan.FromMilliseconds(7), TimeSpan.FromMilliseconds(31), TimeSpan.FromMilliseconds(62)]);
        Require(first.Hash() == second.Hash(), "Cadence partition determinism failed.");
        var replay = new StrategySession();
        foreach (RecordedIntent entry in first.Tape)
        {
            replay.Dispatch(entry.Intent);
        }
        Require(replay.Hash() == first.Hash(), "Semantic tape replay failed.");
        Require(first.Gathered >= 20 && first.ObjectiveComplete, $"Gather/build integration did not complete: {first.Gathered}, buildings {string.Join(',', first.Buildings.Select(b => b.Progress))}");
        Require(first.Units.Count == 5 && first.Production.Count == 0, "Reserved production failed.");
        using (var final = renderer.Render(first, view))
        {
            ProofArtifacts.Save(final, "wilderland-style-proof.png");
        }
        ProofArtifacts.Write("deterministic-proof.json", new
        {
            passed = true, cadence = "10 Hz semantic / 2 Hz visibility", ticks = first.Tick,
            partitions = new[] { "100ms", "7+31+62ms" }, hash = first.Hash(), replayHash = replay.Hash(),
            gathered = first.Gathered, objectiveComplete = first.ObjectiveComplete, produced = first.Units.Count - 4,
            physicalInput = true, focusLoss = true, previewIsolation = true, rejectedBuildAtomic = true,
            persistence = "in-memory semantic tape replay; Deliverance save envelope deferred",
        });
        RenderPack(renderer);
        renderer.Style = FreshPresentationProof.Style();
        renderer.HudOverride = FreshPresentationProof.FarmingSnapshot();
        using (var farming = renderer.Render(first, view))
        {
            ProofArtifacts.Save(farming, "forest-farming-restyle.png");
        }
        renderer.Style = Aurelian.Machina.StrategyHudStyle.Woodland;
        renderer.HudOverride = null;
        Measure(renderer, first, view);
        ProofArtifacts.Write("vector-asset-hashes.json", renderer.Assets.Hashes);
        Console.WriteLine("M18_PROOF_PASSED " + first.Hash());
        return 0;

        void Save(string file)
        {
            using SKBitmap bitmap = renderer.Render(session, view);
            ProofArtifacts.Save(bitmap, file);
        }
    }

    public static StrategySession Scenario(TimeSpan[] partitions)
    {
        var session = new StrategySession();
        Require(session.Dispatch(new OrderIntent([3], new GatherOrder(100))).Accepted, "Gather order rejected.");
        Require(session.Dispatch(new BuildIntent(new(15, 14))).Accepted, "Build order rejected.");
        Require(session.Dispatch(new ProduceIntent(UnitKind.Worker)).Accepted, "Production rejected.");
        var host = new StrategyHost(session);
        TimeSpan total = TimeSpan.Zero;
        int index = 0;
        while (total < TimeSpan.FromSeconds(60))
        {
            TimeSpan delta = partitions[index++ % partitions.Length];
            if (total + delta > TimeSpan.FromSeconds(60))
            {
                delta = TimeSpan.FromSeconds(60) - total;
            }
            host.Advance(delta);
            total += delta;
        }
        string beforePause = session.Hash();
        host.Advance(TimeSpan.FromSeconds(1), paused: true);
        Require(session.Hash() == beforePause, "Pause advanced game truth.");
        return session;
    }

    private static void CheckSelectionAndFog()
    {
        var selection = new EntitySelection<int>();
        selection.Apply([3, 1, 1, 2], SelectionChange.Replace, id => id != 2);
        Require(selection.Snapshot().SequenceEqual([1, 3]), "Selection unstable or duplicate.");
        selection.StoreGroup(1);
        selection.Prune(id => id == 3);
        selection.RecallGroup(1, SelectionChange.Replace, _ => true);
        Require(selection.Snapshot().SequenceEqual([3]), "Group retained a dead identity.");
        var fog = new VisibilityGrid(8, 8);
        fog.Recompute([new(0, 0, 2)]);
        bool[] saved = fog.CaptureExploration();
        int explored = fog.ExploredCount;
        fog.Recompute([]);
        Require(fog[0, 0] == CellVisibility.Explored && fog.ExploredCount == explored, "Visibility erased exploration.");
        fog.RestoreExploration(saved);
        saved[0] = false;
        Require(fog[0, 0] == CellVisibility.Explored, "Exploration restore aliased caller storage.");
        var projection = new IsometricProjection(48, 24);
        foreach (WorldPoint2 point in new[] { new WorldPoint2(-3, 5), new WorldPoint2(.25, 17.5) })
        {
            Require(projection.Unproject(projection.Project(point)) == point, "Isometric inverse failed.");
        }
    }

    private static void Click(StrategyInput input, StrategyView view, float x, float y, double seconds)
    {
        view.ScreenPointer = (x, y);
        input.RecordButton(Controls.Mouse(MouseButton.Primary), true);
        input.Update(TimeSpan.Zero, TimeSpan.FromSeconds(seconds));
        input.RecordButton(Controls.Mouse(MouseButton.Primary), false);
        input.Update(TimeSpan.Zero, TimeSpan.FromSeconds(seconds + .01));
    }

    private static void RenderPack(StrategyRenderer renderer)
    {
        using var bitmap = new SKBitmap(1280, 600);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColor.Parse("#203b34"));
        using var paint = new SKPaint { Color = SKColor.Parse("#dec994"), IsAntialias = true };
        using var font = new SKFont(SKTypeface.FromFamilyName("Georgia"), 20);
        string[] names = renderer.Assets.Hashes.Keys.Order(StringComparer.Ordinal).ToArray();
        for (int index = 0; index < names.Length; index++)
        {
            float x = 120 + index % 5 * 250;
            float y = 230 + index / 5 * 290;
            renderer.Assets.Draw(canvas, names[index], x, y, 1.55f);
            canvas.DrawText(names[index], x - 45, y + 45, font, paint);
        }
        ProofArtifacts.Save(bitmap, "vector-asset-pack.png");
    }

    private static void Measure(StrategyRenderer renderer, StrategySession session, StrategyView view)
    {
        var selectionWorld = new Aurelian.Spatial2D.SpatialWorld2D(Enumerable.Range(1, 256).Select(id =>
            new SpatialCollider2D(new(id.ToString()), new Circle2(new(id % 16, id / 16), .4))));
        using (renderer.Render(session, view)) { }
        object query = Bench(1000, () => selectionWorld.Overlap(new Aabb2(new(8, 8), new(3, 3))));
        object update = Bench(300, () => session.Dispatch(new AdvanceIntent()));
        object fog = Bench(500, () => session.Fog.Recompute(session.Units.Select(u => new RevealSource((int)u.Position.X, (int)u.Position.Y, 5)).ToArray()));
        object rendering = Bench(30, () =>
        {
            using var frame = renderer.Render(session, view);
        });
        using var tacticalBitmap = new SKBitmap(1280, 800);
        using var tacticalCanvas = new SKCanvas(tacticalBitmap);
        object minimap = Bench(100, () => renderer.DrawMinimap(tacticalCanvas, session, view));
        object vectors = Bench(100, () => renderer.Assets.Draw(tacticalCanvas, "hq", 640, 400));
        ProofArtifacts.Write("performance.json", new { unitCount = session.Units.Count, stressColliders = 256, selectionQuery = query,
            simulationUpdate = update, fogUpdate = fog, semanticMinimapProjectionAndRender = minimap, cachedVectorAssetRender = vectors,
            vectorAndHudAndMinimapRender = rendering,
            limitation = "CPU proof; render includes output bitmap allocation, cached HUD, vectors and semantic minimap. GPU and large army performance unqualified." });
    }

    private static object Bench(int count, Action action)
    {
        action();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (int index = 0; index < count; index++)
        {
            action();
        }
        watch.Stop();
        return new { iterations = count, millisecondsPerOperation = watch.Elapsed.TotalMilliseconds / count,
            managedBytesPerOperation = (GC.GetAllocatedBytesForCurrentThread() - before) / count };
    }

    public static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
