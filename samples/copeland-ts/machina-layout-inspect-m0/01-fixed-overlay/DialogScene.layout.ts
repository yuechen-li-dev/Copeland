layers AppLayers { content; modal; }

layout DialogScene<0px, 0px> {
    width: 320px;
    height: 180px;
    layers: AppLayers;
    overlay root {
        slot page { x: 0px; y: 0px; width: 320px; height: 180px; layer: content; z: 5; }
        slot dialog { x: 20px; y: 20px; width: 260px; height: 120px; layer: modal; z: -1; }
        slot tooltip { x: 40px; y: 40px; width: 160px; height: 40px; layer: modal; z: 1; }
    }
}
