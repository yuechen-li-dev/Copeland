export default (Layers([
    Shadow(25.0),
    Block("Trunk",0.0,0.0,3.0,2.0,28.0,Palette),
    Layer("Canopy", [
        Paint("lower", Polygon({ points: [[-24.0,20.0],[0.0,62.0],[26.0,18.0],[4.0,10.0]] }), { fill: "#3f6850" }),
        Paint("middle", Polygon({ points: [[-20.0,39.0],[0.0,79.0],[21.0,37.0],[3.0,29.0]] }), { fill: "#578354" }),
        Paint("upper", Polygon({ points: [[-14.0,58.0],[0.0,98.0],[16.0,55.0],[2.0,49.0]] }), { fill: "#71935d" }),
        Paint("facet", Polygon({ points: [[0.0,98.0],[16.0,55.0],[2.0,49.0]] }), { fill: "#456f53" })
    ])
]));
