// Ordinary Copeland Profile templates. Coordinates use +Y upward, with the foot at (0, 0).
// Every overlapping silhouette is a separate paint item; no geometric Add abuse.
record StrategyPalette {
    light: ProfileStyle;
    wall: ProfileStyle;
    shade: ProfileStyle;
    roof: ProfileStyle;
    ink: ProfileStyle;
    gold: ProfileStyle;
}

const Palette: StrategyPalette = {
    light: { fill: "#e3d3a5" },
    wall: { fill: "#b9b088" },
    shade: { fill: "#73826a" },
    roof: { fill: "#477d72" },
    ink: { fill: "#293c35" },
    gold: { fill: "#d9b66c" }
};

function Paint(name: string, shape: ProfileShape, style: ProfileStyle): ProfileSource {
    return Profile({ name: name, shape: shape, operations: [], yieldState: "Base", style: style });
}

function Block(name: string, x: number, y: number, w: number, d: number, h: number, p: StrategyPalette): ProfileLayer {
    return Layer(name, [
        Paint(name + " left", Polygon({ points: [[x-w,y],[x,y-d],[x,y-d+h],[x-w,y+h]] }), p.shade),
        Paint(name + " right", Polygon({ points: [[x,y-d],[x+w,y],[x+w,y+h],[x,y-d+h]] }), p.wall),
        Paint(name + " top", Polygon({ points: [[x-w,y+h],[x,y+d+h],[x+w,y+h],[x,y-d+h]] }), p.light)
    ]);
}

function Roof(name: string, x: number, y: number, w: number, d: number, h: number, p: StrategyPalette): ProfileLayer {
    return Layer(name, [
        Paint(name + " gable", Polygon({ points: [[x-w,y],[x,y-d],[x-w/2.0,y+h]] }), p.light),
        Paint(name + " lit slope", Polygon({ points: [[x-w,y],[x,y+d],[x+w/2.0,y+h+d],[x-w/2.0,y+h]] }), p.gold),
        Paint(name + " dark slope", Polygon({ points: [[x-w/2.0,y+h],[x+w/2.0,y+h+d],[x+w,y],[x,y-d]] }), p.roof),
        Paint(name + " ridge", Tube({ from: Point(x-w/2.0,y+h), to: Point(x+w/2.0,y+h+d), width: 1.4 }), p.light)
    ]);
}

function Shadow(radius: number): ProfileLayer {
    return Layer("Ground shadow", [Paint("shadow", Ellipse({ radiusX: radius, radiusY: radius/2.6, x: 6.0, y: -2.0 }), { fill: "#263e31" })]);
}

function Figure(p: StrategyPalette, heavy: boolean): ProfileLayer[] {
    let width: number = 7.0;
    if (heavy) {
        width = 10.0;
    }
    return [
        Shadow(12.0),
        Layer("Legs", [
            Paint("left boot", Tube({ from: Point(-4.0,2.0), to: Point(-3.0,13.0), width: 4.0 }), p.ink),
            Paint("right boot", Tube({ from: Point(5.0,2.0), to: Point(3.0,13.0), width: 4.0 }), p.ink)
        ]),
        Layer("Coat", [
            Paint("silhouette", Polygon({ points: [[-width,11.0],[width,11.0],[width-2.0,29.0],[-width+2.0,29.0]] }), p.roof),
            Paint("sash", Tube({ from: Point(-5.0,25.0), to: Point(5.0,13.0), width: 2.5 }), p.gold)
        ]),
        Layer("Head", [
            Paint("face", Circle({ radius: 5.0, x: 0.0, y: 34.0 }), p.light),
            Paint("hat", Ellipse({ radiusX: 9.0, radiusY: 2.5, x: 0.0, y: 37.0 }), p.gold),
            Paint("crown", Polygon({ points: [[-5.0,37.0],[5.0,37.0],[3.0,42.0],[-3.0,42.0]] }), p.gold)
        ]),
        Layer("Tool", [
            Paint("shaft", Tube({ from: Point(10.0,10.0), to: Point(13.0,36.0), width: 2.0 }), p.gold),
            Paint("head", Tube({ from: Point(8.0,35.0), to: Point(19.0,32.0), width: 3.0 }), p.light)
        ])
    ];
}

function Lodge(p: StrategyPalette, tall: boolean): ProfileLayer[] {
    let h: number = 30.0;
    if (tall) {
        h = 48.0;
    }
    return [
        Shadow(47.0),
        Block("Foundation",0.0,0.0,37.0,18.0,6.0,p),
        Block("Walls",0.0,6.0,31.0,15.0,h,p),
        Layer("Windows and door", [
            Paint("door", Polygon({ points: [[-22.0,4.0],[-12.0,-1.0],[-12.0,23.0],[-22.0,28.0]] }), p.ink),
            Paint("window one", Polygon({ points: [[9.0,8.0],[16.0,12.0],[16.0,24.0],[9.0,20.0]] }), p.roof),
            Paint("window two", Polygon({ points: [[22.0,14.0],[28.0,17.0],[28.0,29.0],[22.0,26.0]] }), p.roof)
        ]),
        Roof("Roof",0.0,h+9.0,39.0,20.0,22.0,p),
        Block("Cupola",2.0,h+31.0,9.0,5.0,17.0,p),
        Roof("Cupola roof",2.0,h+49.0,13.0,7.0,12.0,p),
        Layer("Banner", [
            Paint("pole", Tube({ from: Point(2.0,h+60.0), to: Point(2.0,h+79.0), width: 1.5 }), p.gold),
            Paint("flag", Polygon({ points: [[3.0,h+78.0],[22.0,h+73.0],[16.0,h+67.0],[3.0,h+69.0]] }), p.gold)
        ])
    ];
}
