export default (Layers([
    Layer("Objective", [
        Paint("diamond", Polygon({ points: [[0.0,0.0],[-9.0,15.0],[0.0,32.0],[9.0,15.0]] }), Palette.gold),
        Paint("inset", Polygon({ points: [[0.0,7.0],[-4.0,15.0],[0.0,24.0],[4.0,15.0]] }), Palette.ink)
    ])
]));
