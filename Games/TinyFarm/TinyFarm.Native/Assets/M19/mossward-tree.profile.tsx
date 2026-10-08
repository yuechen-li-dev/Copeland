function Paint(name: string, shape: ProfileShape, style: ProfileStyle): ProfileSource {
    return Profile({ name: name, shape: shape, operations: [], yieldState: "Base", style: style });
}

export default (Layers([
    Layer("Shadow", [
        Paint("tree shadow", Ellipse({ radiusX: 27.0, radiusY: 8.0, x: 5.0, y: -2.0 }), { fill: "#23382d" })
    ]),
    Layer("Trunk", [
        Paint("trunk", Polygon({ points: [[-5.0,0.0],[7.0,0.0],[5.0,45.0],[-3.0,45.0]] }), { fill: "#785b3f" })
    ]),
    Layer("Foliage", [
        Paint("lower foliage", Polygon({ points: [[-30.0,25.0],[1.0,73.0],[34.0,24.0],[8.0,17.0]] }), { fill: "#3f6850" }),
        Paint("upper foliage", Polygon({ points: [[-22.0,51.0],[2.0,96.0],[25.0,49.0],[6.0,42.0]] }), { fill: "#71935d" }),
        Paint("foliage facet", Polygon({ points: [[2.0,96.0],[25.0,49.0],[6.0,42.0]] }), { fill: "#456f53" })
    ])
]));
