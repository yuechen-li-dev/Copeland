export default (Layers([
    Shadow(25.0),
    Block("Stone",0.0,1.0,24.0,11.0,10.0,Palette with { light: { fill: "#9da58b" }, wall: { fill: "#667a6c" } }),
    Layer("Crystal", [
        Paint("bright face", Polygon({ points: [[-10.0,12.0],[-5.0,45.0],[3.0,34.0],[1.0,10.0]] }), { fill: "#b0e4ca" }),
        Paint("dark face", Polygon({ points: [[-5.0,45.0],[9.0,32.0],[7.0,9.0],[1.0,10.0],[3.0,34.0]] }), { fill: "#4da796" }),
        Paint("small face", Polygon({ points: [[10.0,9.0],[18.0,31.0],[24.0,22.0],[20.0,6.0]] }), { fill: "#79c5b3" })
    ])
]));
