using Aurelian.Graphics.FramePlans;

namespace Aurelian.Graphics.Vulkan.Native3D;

internal enum Native3DPass
{
    Uploads, Particles, DirectionalShadows, LocalShadows, Geometry, AmbientOcclusion, LightTiles,
    SurfaceResolve, Subsurface, Atmosphere, Refraction, Transparency, Temporal, Bloom, Output, HistoryCopy,
    Visibility, IdentityRaster, HzbSeedRaster, HzbSeedPyramid, HzbFinalCull, HzbHistoryPyramid,
}

internal readonly record struct Native3DFeatures(bool Surface, bool Modern, bool Motion, bool Shadows,
    bool Ambient, bool Tiles, bool Temporal, bool Bloom, bool Subsurface, bool Fog, bool Volume,
    bool Refraction, bool Transparency, bool Particles, bool AsyncParticles, bool GpuHistory,
    int LocalShadows, bool Alias, bool Visibility = false, bool VisibilityBuffer = false, bool HierarchicalZ = false);

/// <summary>Declarative authoring for the production renderer. The compiler publishes only resolved data.</summary>
internal static class Native3DFrameGraph
{
    public static FramePlan Compile(Native3DFeatures features, int width, int height)
    {
        var graph = new FrameGraphBuilder();
        FrameResource Texture(string name, string format = "rgba16f", bool initialized = false,
            bool allowUnused = false, bool aliasable = false, int scale = 1)
        {
            return graph.Resource(name, format, (width + scale - 1) / scale, (height + scale - 1) / scale,
                initialized ? FrameLifetime.Imported : FrameLifetime.Transient, initialized, allowUnused,
                dedicated: !aliasable);
        }
        FrameResource External(string name) => graph.Resource(name, "external", 1, 1, FrameLifetime.Imported, initialized: true);
        var inputs = External("frame-inputs");
        var lights = External("local-light-data");
        var output = Texture("display-output");
        var scene = features.Modern ? Texture("geometry-color") : output;
        var motion = Texture("motion-depth", "rgba32f", allowUnused: true);
        var normal = Texture("surface-normal");
        var emission = Texture("surface-emission");
        var profile = Texture("subsurface-profile");
        FrameResource Shadow(string name, int size, bool initialized = false) => graph.Resource(name, "r32f", size, size,
            initialized ? FrameLifetime.Imported : FrameLifetime.Transient, initialized, dedicated: true);
        var shadow = Shadow("directional-shadow", features.Shadows ? (int)Lighting3DUniforms.ShadowSize : 1, initialized: !features.Shadows);
        var cascade1 = Shadow("directional-cascade-1", (int)Lighting3DUniforms.ShadowSize);
        var cascade2 = Shadow("directional-cascade-2", (int)Lighting3DUniforms.ShadowSize);
        var spot1 = Shadow("spot-shadow-1", 512, initialized: true);
        var spot2 = Shadow("spot-shadow-2", 512, initialized: true);
        var volume = graph.Resource("volume-lighting", "external", 1, 1, FrameLifetime.Imported, initialized: true, allowUnused: true);
        var particles = graph.Resource("particle-vertices", "buffer", 1, 1, FrameLifetime.History, initialized: true);

        if (features.Particles)
        {
            graph.Pass("particle-update", (int)Native3DPass.Particles,
                features.AsyncParticles ? FrameQueue.Compute : FrameQueue.Graphics)
                .Read(inputs).Write(particles, FrameAccess.ComputeWrite);
        }
        if (features.Surface)
        {
            graph.Pass("local-light-upload", (int)Native3DPass.Uploads).Write(lights, FrameAccess.TransferWrite);
        }
        if (features.Shadows)
        {
            var pass = graph.Pass("directional-shadows", (int)Native3DPass.DirectionalShadows)
                .Read(inputs, FrameAccess.Vertex).Write(shadow);
            if (features.Surface)
            {
                pass.Write(cascade1).Write(cascade2);
            }
        }
        if (features.Surface && features.LocalShadows > 0)
        {
            var pass = graph.Pass("local-shadows", (int)Native3DPass.LocalShadows).Read(inputs, FrameAccess.Vertex).Write(spot1);
            if (features.LocalShadows > 1) pass.Write(spot2);
        }
        FrameResource? arguments = null;
        FramePassBuilder? visibilityPass = null;
        FrameResource? lodHistory = null;
        if (features.Visibility)
        {
            arguments = graph.Resource("cluster-indirect-arguments", "buffer", 1, 1, dedicated: true);
            lodHistory = graph.Resource("prepared-lod-history", "buffer", 1, 1, FrameLifetime.History, initialized: true);
            graph.Export(lodHistory);
            visibilityPass = graph.Pass("cluster-visibility", (int)Native3DPass.Visibility).Read(inputs).Write(arguments, FrameAccess.ComputeWrite);
            visibilityPass.Write(lodHistory, FrameAccess.HistoryReadWrite);
        }
        FrameResource? identity = null;
        FrameResource? hzbHistory = null;
        FrameResource? hzbFinalDepth = null;
        if (features.HierarchicalZ)
        {
            hzbHistory = graph.Resource("hzb-history", "buffer", 1, 1, FrameLifetime.History, initialized: true);
            graph.Export(hzbHistory);
            var seedMask = graph.Resource("hzb-seed-mask", "buffer", 1, 1, dedicated: true);
            visibilityPass!.Read(hzbHistory).Write(seedMask, FrameAccess.ComputeWrite);
            var seedDepth = Texture("hzb-seed-depth", "d32f");
            var seedPyramid = graph.Resource("hzb-seed-pyramid", "buffer", 1, 1, dedicated: true);
            graph.Pass("hzb-seed-depth", (int)Native3DPass.HzbSeedRaster)
                .Read(arguments!, FrameAccess.Indirect).Read(inputs, FrameAccess.Vertex).Write(seedDepth, FrameAccess.DepthWrite);
            graph.Pass("hzb-seed-pyramid", (int)Native3DPass.HzbSeedPyramid)
                .Read(seedDepth, FrameAccess.TransferRead).Write(seedPyramid, FrameAccess.ComputeWrite);
            graph.Pass("hzb-final-cull", (int)Native3DPass.HzbFinalCull)
                .Read(seedPyramid).Read(seedMask).Read(inputs).Write(arguments!, FrameAccess.ComputeWrite)
                .Write(lodHistory!, FrameAccess.HistoryReadWrite);
            // Previous-frame history only selects an early set; remaining geometry gets a current-frame test.
            hzbFinalDepth = Texture("hzb-final-depth", "d32f");
        }
        if (features.VisibilityBuffer)
        {
            identity = Texture("visibility-identity", "rg32ui");
            var raster = graph.Pass("visibility-raster", (int)Native3DPass.IdentityRaster)
                .Read(inputs, FrameAccess.Vertex).Write(identity);
            if (arguments is not null) raster.Read(arguments, FrameAccess.Indirect);
            if (hzbFinalDepth is not null) raster.Write(hzbFinalDepth, FrameAccess.DepthWrite);
        }
        if (features.HierarchicalZ)
            graph.Pass("hzb-history-pyramid", (int)Native3DPass.HzbHistoryPyramid, sideEffect: true)
                .Read(hzbFinalDepth!, FrameAccess.TransferRead).Write(hzbHistory!, FrameAccess.ComputeWrite);
        var geometry = graph.Pass(features.VisibilityBuffer ? "visibility-material-resolve" : "geometry", (int)Native3DPass.Geometry)
            .Read(inputs, FrameAccess.Vertex).Write(scene);
        if (identity is not null) geometry.Read(identity);
        else if (arguments is not null) geometry.Read(arguments, FrameAccess.Indirect);
        if (!features.Surface) geometry.Read(shadow);
        if (features.Motion) geometry.Write(motion);
        if (features.Surface) geometry.Write(normal).Write(emission).Write(profile);

        FrameResource unfogged = scene;
        if (features.Surface)
        {
            // Consumed inside the compound AO/denoise implementation.
            var rawAo = Texture("ambient-raw", "rgba32f", allowUnused: true, scale: 2);
            var ambient = Texture("ambient-filtered", "rgba32f", scale: 2);
            graph.Pass("ambient-occlusion", (int)Native3DPass.AmbientOcclusion).Read(motion).Read(normal).Write(rawAo).Write(ambient);
            var tiles = Texture("light-tiles", "rgba32f", scale: 16);
            graph.Pass("light-tiles", (int)Native3DPass.LightTiles).Read(lights).Write(tiles);
            var lit = Texture("surface-lighting");
            var diffuse = Texture("surface.diffuse", allowUnused: true, aliasable: true);
            var resolve = graph.Pass("surface-resolve", (int)Native3DPass.SurfaceResolve)
                .Read(scene).Read(motion).Read(normal).Read(emission).Read(profile).Read(lights)
                .Read(shadow).Read(cascade1).Read(cascade2).Read(spot1).Read(spot2).Read(inputs).Write(lit).Write(diffuse);
            if (features.Ambient) resolve.Read(ambient);
            if (features.Tiles) resolve.Read(tiles);
            scene = lit;
            if (features.Subsurface)
            {
                var scattered = Texture("subsurface-color");
                graph.Pass("subsurface-diffusion", (int)Native3DPass.Subsurface)
                    .Read(scene).Read(diffuse).Read(motion).Read(normal).Read(profile).Write(scattered);
                scene = scattered;
            }
            unfogged = scene;
            if (features.Fog)
            {
                var fog = Texture("surface.fog", aliasable: true);
                var atmosphere = graph.Pass("atmosphere", (int)Native3DPass.Atmosphere)
                    .Read(scene).Read(motion).Read(lights).Read(shadow).Read(cascade1).Read(cascade2).Read(spot1).Read(spot2).Write(fog);
                if (features.Volume) atmosphere.Write(volume);
                scene = fog;
            }
        }
        if (features.Refraction)
        {
            var refracted = Texture("refraction-color");
            var pass = graph.Pass("refraction", (int)Native3DPass.Refraction).Read(scene).Read(motion).Read(volume)
                .Read(lights).Read(shadow).Read(cascade1).Read(cascade2).Read(spot1).Read(spot2).Read(inputs).Write(refracted);
            if (unfogged.Id != scene.Id) pass.Read(unfogged);
            scene = refracted;
        }
        if (features.Transparency)
        {
            var transparent = Texture("transparency-color");
            var pass = graph.Pass("transparency", (int)Native3DPass.Transparency).Read(scene).Read(motion)
                .Read(volume).Read(lights).Read(inputs).Write(transparent);
            if (features.Particles) pass.Read(particles, FrameAccess.Vertex);
            scene = transparent;
        }
        if (features.Temporal)
        {
            var temporal = Texture("temporal-color");
            var history = graph.Resource("temporal-history", "rgba32f", width, height, FrameLifetime.History, initialized: true);
            graph.Pass("temporal-resolve", (int)Native3DPass.Temporal).Read(scene).Read(motion)
                .Write(history, FrameAccess.HistoryReadWrite).Write(temporal);
            graph.Export(history);
            scene = temporal;
        }
        FrameResource? glow = null;
        if (features.Bloom)
        {
            glow = Texture("bloom-color", scale: 2);
            graph.Pass("bloom", (int)Native3DPass.Bloom).Read(scene).Write(glow);
        }
        if (features.Modern)
        {
            var pass = graph.Pass("display-output", (int)Native3DPass.Output).Read(scene).Write(output);
            if (glow is not null) pass.Read(glow);
        }
        if (features.GpuHistory)
        {
            var previous = graph.Resource("previous-gpu-positions", "buffer", 1, 1, FrameLifetime.History, initialized: true);
            graph.Pass("retain-gpu-positions", (int)Native3DPass.HistoryCopy).Read(inputs, FrameAccess.TransferRead)
                .Write(previous, FrameAccess.TransferWrite);
            graph.Export(previous);
        }
        graph.Export(output);
        return FramePlanCompiler.Compile(graph.Build(), features.Alias);
    }
}
