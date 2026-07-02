using Aurelian.Core.Presentation.Screens;

ScreenLayerOrder order =
[
    ScreenLayers.Background,
    ScreenLayers.World,
    Layer.At("damage-vignette", 250),
    ScreenLayers.Hud,
    ScreenLayers.Debug,
];
