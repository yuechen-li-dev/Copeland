// A light scouting unit uses the existing figure template and a local palette.
const RangerPalette: StrategyPalette = Palette with {
    roof: { fill: "#567345" },
    gold: { fill: "#a9b37b" },
    light: { fill: "#e0c7a0" },
    ink: { fill: "#263c30" }
};

export default (Layers(Figure(RangerPalette, false)));
