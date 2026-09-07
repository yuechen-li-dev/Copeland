// Fresh building assembled entirely from the existing ordinary Profile toolkit.
const WatchtowerPalette: StrategyPalette = Palette with {
    wall: { fill: "#bcb894" },
    shade: { fill: "#78816c" },
    roof: { fill: "#516f67" }
};

export default (Layers([
    Shadow(33.0),
    Block("Stone footing", 0.0, 0.0, 26.0, 13.0, 7.0, WatchtowerPalette),
    Block("Tower shaft", 0.0, 7.0, 17.0, 8.5, 55.0, WatchtowerPalette),
    Layer("Tower openings", [
        Paint("entrance", Polygon({
            points: [[-13.0, 5.0], [-5.0, 1.0], [-5.0, 21.0], [-13.0, 25.0]]
        }), WatchtowerPalette.ink),
        Paint("lower arrow slit", Polygon({
            points: [[7.0, 21.0], [10.0, 22.5], [10.0, 33.5], [7.0, 32.0]]
        }), WatchtowerPalette.ink),
        Paint("upper arrow slit", Polygon({
            points: [[-12.0, 39.0], [-9.0, 37.5], [-9.0, 48.5], [-12.0, 50.0]]
        }), WatchtowerPalette.ink)
    ]),
    Block("Observation platform", 0.0, 61.0, 25.0, 12.5, 6.0, WatchtowerPalette),
    Block("Lookout shelter", 0.0, 68.0, 18.0, 9.0, 20.0, WatchtowerPalette),
    Layer("Lookout windows", [
        Paint("left opening", Polygon({
            points: [[-15.0, 74.5], [-4.0, 69.0], [-4.0, 81.0], [-15.0, 86.5]]
        }), WatchtowerPalette.ink),
        Paint("right opening", Polygon({
            points: [[4.0, 69.0], [15.0, 74.5], [15.0, 86.5], [4.0, 81.0]]
        }), WatchtowerPalette.ink)
    ]),
    Roof("Lookout roof", 0.0, 89.0, 27.0, 13.5, 16.0, WatchtowerPalette),
    Layer("Signal pennant", [
        Paint("mast", Tube({
            from: Point(0.0, 111.0),
            to: Point(0.0, 128.0),
            width: 1.5
        }), WatchtowerPalette.gold),
        Paint("pennant", Polygon({
            points: [[1.0, 127.0], [15.0, 123.0], [1.0, 118.0]]
        }), WatchtowerPalette.gold)
    ])
]));
