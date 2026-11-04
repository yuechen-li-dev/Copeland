record ClothHeader {
    phase: f32;
    count: f32;
    start: f32;
    vertexCount: f32;
    step: f32;
    gravity: float3;
    damping: f32;
    reset: f32;
    fraction: f32;
    halfThickness: f32;
    planeEnabled: f32;
    planeNormal: float3;
    planeOffset: f32;
    sphereEnabled: f32;
    sphere: float3;
    sphereRadius: f32;
    pairCount: f32;
    contactTable: f32;
    anchors: f32;
    distances: f32;
    contactLambda: f32;
    contactRadius: f32;
    safety: f32;
    contactCompliance: f32;
    offsetEnabled: f32;
    stitchComplianceScale: f32;
}
record ClothParticle {
    position: float3;
    previous: float3;
    velocity: float3;
    inverseMass: f32;
    target: float3;
    pinStart: float3;
}
record ClothAnchor {
    position: float3;
    bound: f32;
}
record ClothStencil {
    kind: f32;
    a: f32;
    b: f32;
    c: f32;
    d: f32;
    restLength: f32;
    compliance: f32;
    weights: f32;
    transverse: f32;
    reserved: Array<f32, 2>;
    arity: f32;
}
enum ClothPhase {
    Predict,
    Constraints,
    VertexContact,
    TriangleContact,
    Velocity,
    CapturePins,
    OffsetDistances,
    OffsetReduce,
    OffsetBounds,
    OffsetContacts,
    Invalid,
}
// Decode the scalar transport once. Runtime decisions use the typed phase.
function DecodePhase(value: f32): ClothPhase {
    if (value == 0.0) {
        return ClothPhase.Predict;
    }
    if (value == 1.0) {
        return ClothPhase.Constraints;
    }
    if (value == 2.0) {
        return ClothPhase.VertexContact;
    }
    if (value == 3.0) {
        return ClothPhase.TriangleContact;
    }
    if (value == 4.0) {
        return ClothPhase.Velocity;
    }
    if (value == 5.0) {
        return ClothPhase.CapturePins;
    }
    if (value == 6.0) {
        return ClothPhase.OffsetDistances;
    }
    if (value == 7.0) {
        return ClothPhase.OffsetReduce;
    }
    if (value == 8.0) {
        return ClothPhase.OffsetBounds;
    }
    if (value == 9.0) {
        return ClothPhase.OffsetContacts;
    }
    return ClothPhase.Invalid;
}
// Flat-rest XPBD baseline, scalar ABI documented in ClothGpuData3D.
// Every dispatch writes disjoint vertices. Cross-color ordering is a Vulkan barrier.
enum ContactShape {
    None,
    Plane(nx: f32, ny: f32, nz: f32, offset: f32),
    Sphere(x: f32, y: f32, z: f32, radius: f32),
}
function ProjectPlane(position: float3, normal: float3, offset: f32): float3 {
    let gap: f32 = Dot(normal, position) - offset;
    if (gap < 0.0) {
        return (position - (normal * gap));
    }
    return position;
}
function ProjectSphere(position: float3, center: float3, radius: f32): float3 {
    let difference: float3 = (position - center);
    let distance: f32 = Length(difference);
    if (distance >= radius) {
        return position;
    }
    var outward: float3 = float3(0.0, 1.0, 0.0);
    if (distance > 0.0000000001) {
        outward = (difference * (1.0 / distance));
    }
    return (center + (outward * radius));
}
function ProjectContact(position: float3, contact: ContactShape): float3 {
    return match contact {
        case ContactShape.None => position,
        case ContactShape.Plane(payload) => ProjectPlane(position, float3(payload.nx, payload.ny, payload.nz), payload.offset),
        case ContactShape.Sphere(payload) => ProjectSphere(position, float3(payload.x, payload.y, payload.z), payload.radius),
    };
}
function SegmentBary(p: float3, a: float3, b: float3, wa: float3, wb: float3): float3 {
    let edge: float3 = (b - a);
    let length: f32 = Dot(edge, edge);
    var t: f32 = 0.0;
    if (length > 0.00000000000000000001) {
        t = Clamp(Dot((p - a), edge) / length, 0.0, 1.0);
    }
    return ((wa * (1.0 - t)) + (wb * t));
}
function ClosestBary(p: float3, a: float3, b: float3, c: float3): float3 {
    let ab: float3 = (b - a);
    let ac: float3 = (c - a);
    let aa: f32 = Dot(ab, ab);
    let bb: f32 = Dot(ab, ac);
    let cc: f32 = Dot(ac, ac);
    if (aa * cc - bb * bb <= 0.00000001 * aa * cc || aa * cc < 0.000000000000000000000001) {
        let bc: float3 = (c - b);
        let edgeLength: f32 = Dot(bc, bc);
        if (aa >= cc && aa >= edgeLength) {
            return SegmentBary(p, a, b, float3(1.0, 0.0, 0.0), float3(0.0, 1.0, 0.0));
        }
        if (cc >= edgeLength) {
            return SegmentBary(p, a, c, float3(1.0, 0.0, 0.0), float3(0.0, 0.0, 1.0));
        }
        return SegmentBary(p, b, c, float3(0.0, 1.0, 0.0), float3(0.0, 0.0, 1.0));
    }
    let ap: float3 = (p - a);
    let d1: f32 = Dot(ab, ap);
    let d2: f32 = Dot(ac, ap);
    if (d1 <= 0.0 && d2 <= 0.0)
    {
        return float3(1.0, 0.0, 0.0);
    }
    let bp: float3 = (p - b);
    let d3: f32 = Dot(ab, bp);
    let d4: f32 = Dot(ac, bp);
    if (d3 >= 0.0 && d4 <= d3)
    {
        return float3(0.0, 1.0, 0.0);
    }
    let vc: f32 = d1 * d4 - d3 * d2;
    if (vc <= 0.0 && d1 >= 0.0 && d3 <= 0.0) {
        let t: f32 = d1 / (d1 - d3);
        return float3(1.0 - t, t, 0.0);
    }
    let cp: float3 = (p - c);
    let d5: f32 = Dot(ab, cp);
    let d6: f32 = Dot(ac, cp);
    if (d6 >= 0.0 && d5 <= d6)
    {
        return float3(0.0, 0.0, 1.0);
    }
    let vb: f32 = d5 * d2 - d1 * d6;
    if (vb <= 0.0 && d2 >= 0.0 && d6 <= 0.0) {
        let t: f32 = d2 / (d2 - d6);
        return float3(1.0 - t, 0.0, t);
    }
    let va: f32 = d3 * d6 - d5 * d4;
    if (va <= 0.0 && d4 >= d3 && d5 >= d6) {
        let t: f32 = (d4 - d3) / (d4 - d3 + d5 - d6);
        return float3(0.0, 1.0 - t, t);
    }
    let inverse: f32 = 1.0 / (va + vb + vc);
    let v: f32 = vb * inverse;
    let w: f32 = vc * inverse;
    return float3(1.0 - v - w, v, w);
}

function LineT(p: float3, a: float3, b: float3): f32 {
    let edge: float3 = (b - a);
    let length: f32 = Dot(edge, edge);
    if (length <= 0.00000000000000000001) {
        return 0.0;
    }
    return Dot((p - a), edge) / length;
}
function ClosestSegments(a: float3, b: float3, c: float3, d: float3): float2 {
    let first: float3 = (b - a);
    let second: float3 = (d - c);
    let offset: float3 = (a - c);
    let aa: f32 = Dot(first, first);
    let bb: f32 = Dot(first, second);
    let cc: f32 = Dot(second, second);
    let dd: f32 = Dot(first, offset);
    let ee: f32 = Dot(second, offset);
    if (aa <= 0.00000000000000000001) {
        return float2(0.0, Clamp(LineT(a, c, d), 0.0, 1.0));
    }
    if (cc <= 0.00000000000000000001) {
        return float2(Clamp(LineT(c, a, b), 0.0, 1.0), 0.0);
    }
    let determinant: f32 = aa * cc - bb * bb;
    var s: f32 = 0.0;
    if (determinant > 0.00000001 * aa * cc) {
        s = Clamp((bb * ee - cc * dd) / determinant, 0.0, 1.0);
    }
    var t: f32 = (bb * s + ee) / cc;
    if (t < 0.0) {
        t = 0.0;
        s = Clamp(-dd / aa, 0.0, 1.0);
    } else if (t > 1.0) {
        t = 1.0;
        s = Clamp((bb - dd) / aa, 0.0, 1.0);
    }
    return float2(s, t);
}
enum OffsetFeature {
    Face,
    Edge,
    Vertex,
    EdgePair,
}
function Feature(value: f32): OffsetFeature {
    if (value == 0.0) {
        return OffsetFeature.Face;
    }
    if (value == 1.0) {
        return OffsetFeature.Edge;
    }
    if (value == 2.0) {
        return OffsetFeature.Vertex;
    }
    return OffsetFeature.EdgePair;
}
record OffsetQuery {
    distance: f32;
    normal: float3;
    weights: float4;
    active: bool;
    t1: f32;
    t2: f32;
}
function OffsetClosest(kind: OffsetFeature, a: float3, b: float3, c: float3, d: float3): OffsetQuery {
    var difference: float3 = (a - b);
    var weights: float4 = float4(1.0, -1.0, 0.0, 0.0);
    var active: bool = true;
    var t1: f32 = 0.0;
    var t2: f32 = 0.0;
    if (kind == OffsetFeature.Face) {
        let bary: float3 = ClosestBary(a, b, c, d);
        difference = (a - (((b * bary.x) + (c * bary.y)) + (d * bary.z)));
        weights = float4(1.0, -bary.x, -bary.y, -bary.z);
        active = bary.x > 0.0 && bary.y > 0.0 && bary.z > 0.0;
    } else if (kind == OffsetFeature.Edge) {
        t1 = Clamp(LineT(a, b, c), 0.0, 1.0);
        difference = (a - ((b * (1.0 - t1)) + (c * t1)));
        weights = float4(1.0, t1 - 1.0, -t1, 0.0);
        active = t1 > 0.0 && t1 < 1.0;
    } else if (kind == OffsetFeature.EdgePair) {
        let t: float2 = ClosestSegments(a, b, c, d);
        t1 = t.x;
        t2 = t.y;
        difference = (((a * (1.0 - t1)) + (b * t1)) - ((c * (1.0 - t2)) + (d * t2)));
        weights = float4(1.0 - t1, t1, t2 - 1.0, -t2);
        let cross: float3 = Cross((b - a), (d - c));
        active = Dot(cross, cross) > 0.00000001 * Dot((b - a), (b - a)) * Dot((d - c), (d - c));
    }
    let distance: f32 = Length(difference);
    var normal: float3 = float3(0.0, 0.0, 0.0);
    if (distance > 0.0) {
        normal = (difference * (1.0 / distance));
    }
    return { distance: distance, normal: normal, weights: weights, active: active, t1: t1, t2: t2 };
}
function OffsetPotential(distance: f32, radius: f32): float2 {
    if (distance >= radius) {
        return float2(0.0, 0.0);
    }
    if (distance >= radius * 0.5) {
        return float2(radius - distance, -1.0);
    }
    let coefficient: f32 = radius * radius * 0.25;
    let energy: f32 = coefficient * (0.5 - Log(distance / (radius * 0.5)));
    let residual: f32 = Sqrt(2.0 * energy);
    return float2(residual, -coefficient / (distance * residual));
}
function Limit(position: float3, anchor: float3, bound: f32): float3 {
    let delta: float3 = (position - anchor);
    let length: f32 = Length(delta);
    if (length > bound) {
        return (anchor + (delta * (bound / length)));
    }
    return position;
}

function LimitToAnchor(readonly state: StorageBuffer<f32>, position: float3, offset: u32): float3 {
    let anchor = Load<ClothAnchor>(state, offset);
    return Limit(position, anchor.position, anchor.bound);
}

function Reduce(readonly Source: StorageBuffer<f32>,
    header: ClothHeader,
    readwrite State: StorageBuffer<f32>,
    item: u32): void {
    let contactTable = U32(header.contactTable);
    let distances = U32(header.distances);
    let node: u32 = U32(Source[U32(header.start) + item]);
    let nodes: u32 = U32(Source[contactTable + 4]) + node * 2;
    State[distances + U32(header.pairCount) + node] = Min(State[distances + U32(Source[nodes])], State[distances + U32(Source[nodes + 1])]);
    return;

}

function Bounds(readonly Source: StorageBuffer<f32>,
    header: ClothHeader,
    readwrite State: StorageBuffer<f32>,
    item: u32): void {
    let contactTable = U32(header.contactTable);
    let anchors = U32(header.anchors);
    let distances = U32(header.distances);
    let vertex: u32 = item;
    let position: float3 = Load<float3>(State, vertex * WordCount<ClothParticle>());
    let root: f32 = Source[U32(Source[contactTable + 5]) + vertex];
    var clearance: f32 = 1000000.0;
    if (root >= 0.0) {
        clearance = State[distances + U32(root)];
    }
    if (header.planeEnabled > 0.5) {
        clearance = Min(clearance, Dot(header.planeNormal, position) - header.planeOffset - header.halfThickness);
    }
    if (header.sphereEnabled > 0.5) {
        let vertexInfo: u32 = U32(Source[contactTable + 1]) + vertex * 4;
        let faceList: u32 = U32(Source[vertexInfo + 2]);
        let faceCount: u32 = U32(Source[vertexInfo + 3]);
        let sphere: float3 = header.sphere;
        static for (let local of Indices<32>()) {
            if (local < faceCount) {
                let face: u32 = U32(Source[contactTable + 2]) + U32(Source[faceList + local]) * 3;
                let a: u32 = U32(Source[face]) * WordCount<ClothParticle>();
                let b: u32 = U32(Source[face + 1]) * WordCount<ClothParticle>();
                let c: u32 = U32(Source[face + 2]) * WordCount<ClothParticle>();
                let pa: float3 = Load<float3>(State, a);
                let pb: float3 = Load<float3>(State, b);
                let pc: float3 = Load<float3>(State, c);
                let bary: float3 = ClosestBary(sphere, pa, pb, pc);
                let closest: float3 = (((pa * bary.x) + (pb * bary.y)) + (pc * bary.z));
                let delta: float3 = (sphere - closest);
                clearance = Min(clearance, Length(delta) - header.sphereRadius - header.halfThickness);
            }
        }
    }
    Store<ClothAnchor>(State, anchors + vertex * WordCount<ClothAnchor>(), {
        position: position,
        bound: header.safety * Max(0.0, clearance - 0.000001),
    });
    return;

}

function OffsetPair(readonly Source: StorageBuffer<f32>,
    header: ClothHeader,
    readwrite State: StorageBuffer<f32>,
    item: u32,
    phase: ClothPhase): void {
    let contactTable = U32(header.contactTable);
    let anchors = U32(header.anchors);
    let distances = U32(header.distances);
    var pair: u32 = item;
    if (phase == ClothPhase.OffsetContacts) {
        pair = U32(Source[U32(header.start) + item]);
    }
    let entry: u32 = U32(Source[contactTable + 3]) + pair * 8;
    let kind: OffsetFeature = Feature(Source[entry]);
    let a: u32 = U32(Source[entry + 2]);
    let b: u32 = U32(Source[entry + 3]);
    let c: u32 = U32(Source[entry + 4]);
    let d: u32 = U32(Source[entry + 5]);
    let pa: float3 = Load<float3>(State, a * WordCount<ClothParticle>());
    let pb: float3 = Load<float3>(State, b * WordCount<ClothParticle>());
    let pc: float3 = Load<float3>(State, c * WordCount<ClothParticle>());
    let pd: float3 = Load<float3>(State, d * WordCount<ClothParticle>());
    let query: OffsetQuery = OffsetClosest(kind, pa, pb, pc, pd);
    if (phase == ClothPhase.OffsetDistances) {
        State[distances + pair] = query.distance;
        return;
    }
    var active: bool = query.active;
    if (kind == OffsetFeature.Edge) {
        let edge: u32 = U32(Source[contactTable]) + U32(Source[entry + 6]) * 5;
        static for (let local of Indices<2>()) {
            if (local < U32(Source[edge + 2])) {
                let opposite: u32 = U32(Source[edge + 3 + local]) * WordCount<ClothParticle>();
                let point: float3 = Load<float3>(State, opposite);
                let foot: float3 = (pb + ((pc - pb) * LineT(point, pb, pc)));
                active = active && Dot((pa - foot), (foot - point)) >= 0.0;
            }
        }
    }
    // Vertex normal blocks use the complete one-ring, including endpoint
    // blocks in the edge-only manifold. No closest-triangle-only shortcut.
    static for (let side of Indices<2>()) {
        var vertex: u32 = b;
        var point: float3 = pa;
        var check: bool = (kind == OffsetFeature.Vertex);
        if (side == 1) {
            vertex = a;
            point = pb;
        }
        if (kind == OffsetFeature.EdgePair) {
            check = query.t1 <= 0.0 || query.t1 >= 1.0;
            vertex = a;
            if (query.t1 >= 1.0) {
                vertex = b;
            }
            point = ((pc * (1.0 - query.t2)) + (pd * query.t2));
            if (side == 1) {
                check = query.t2 <= 0.0 || query.t2 >= 1.0;
                vertex = c;
                if (query.t2 >= 1.0) {
                    vertex = d;
                }
                point = ((pa * (1.0 - query.t1)) + (pb * query.t1));
            }
        }
        if (check) {
            let info: u32 = U32(Source[contactTable + 1]) + vertex * 4;
            let list: u32 = U32(Source[info]);
            let count: u32 = U32(Source[info + 1]);
            let origin: float3 = Load<float3>(State, vertex * WordCount<ClothParticle>());
            static for (let local of Indices<32>()) {
                if (local < count) {
                    let neighbor: u32 = U32(Source[list + local]) * WordCount<ClothParticle>();
                    let next: float3 = Load<float3>(State, neighbor);
                    active = active && Dot((point - origin), (origin - next)) >= 0.0;
                }
            }
        }
    }
    let lambda: u32 = U32(header.contactLambda) + pair;
    if (!active || query.distance >= header.contactRadius) {
        State[lambda] = 0.0;
        return;
    }
    let potential: float2 = OffsetPotential(query.distance, header.contactRadius);
    let movable: f32 = Load<ClothParticle>(State, a * WordCount<ClothParticle>()).inverseMass * query.weights.x * query.weights.x
    + Load<ClothParticle>(State, b * WordCount<ClothParticle>()).inverseMass * query.weights.y * query.weights.y
    + Load<ClothParticle>(State, c * WordCount<ClothParticle>()).inverseMass * query.weights.z * query.weights.z
    + Load<ClothParticle>(State, d * WordCount<ClothParticle>()).inverseMass * query.weights.w * query.weights.w;
    if (movable <= 0.00000000000000000001) {
        return;
    }
    var previous: f32 = State[lambda];
    if (header.reset > 0.5) {
        previous = 0.0;
    }
    let alpha: f32 = header.contactCompliance / (header.step * header.step);
    let increment: f32 = (-potential.x - alpha * previous) / (movable * potential.y * potential.y + alpha);
    State[lambda] = previous + increment;
    static for (let local of Indices<4>()) {
        if (local < U32(Source[entry + 1])) {
            var vertex: u32 = a;
            var weight: f32 = query.weights.x;
            if (local == 1) {
                vertex = b;
                weight = query.weights.y;
            }
            if (local == 2) {
                vertex = c;
                weight = query.weights.z;
            }
            if (local == 3) {
                vertex = d;
                weight = query.weights.w;
            }
            let position: float3 = Load<float3>(State, vertex * WordCount<ClothParticle>());
            let proposed: float3 = (position + (query.normal * (Load<ClothParticle>(State, vertex * WordCount<ClothParticle>()).inverseMass * weight * potential.y * increment)));
            let corrected: float3 = LimitToAnchor(State, proposed, anchors + vertex * WordCount<ClothAnchor>());
            Store<float3>(State, vertex * WordCount<ClothParticle>(), corrected);
        }
    }
    return;

}

function CapturePins(header: ClothHeader,
    readwrite State: StorageBuffer<f32>,
    item: u32): void {
    let particle = Load<ClothParticle>(State, item * WordCount<ClothParticle>());
    Store<ClothParticle>(State, item * WordCount<ClothParticle>(), particle with { pinStart: particle.position });
}

function Predict(header: ClothHeader,
    readwrite State: StorageBuffer<f32>,
    item: u32): void {
    let anchors = U32(header.anchors);
    var particle = Load<ClothParticle>(State, item * WordCount<ClothParticle>());
    particle = particle with { previous: particle.position };
    if (particle.inverseMass == 0.0) {
        particle = particle with {
            position: particle.pinStart * (1.0 - header.fraction) + particle.target * header.fraction,
        };
    } else {
        let velocity = particle.velocity + header.gravity * header.step;
        particle = particle with { velocity: velocity, position: particle.position + velocity * header.step };
    }
    if (header.offsetEnabled > 0.5) {
        let anchor = Load<ClothAnchor>(State, U32(header.anchors) + item * WordCount<ClothAnchor>());
        particle = particle with { position: Limit(particle.position, anchor.position, anchor.bound) };
    }
    Store<ClothParticle>(State, item * WordCount<ClothParticle>(), particle);
}

function Velocity(header: ClothHeader,
    readwrite State: StorageBuffer<f32>,
    item: u32): void {
    let particle = Load<ClothParticle>(State, item * WordCount<ClothParticle>());
    Store<ClothParticle>(State, item * WordCount<ClothParticle>(), particle with {
        velocity: (particle.position - particle.previous) * (header.damping / header.step),
    });
}

function VertexContact(header: ClothHeader,
    readwrite State: StorageBuffer<f32>,
    item: u32): void {
    let anchors = U32(header.anchors);
    let o = item * WordCount<ClothParticle>();
    let sphere = header.sphere;
    let radius = header.sphereRadius + header.halfThickness;
    if (Load<ClothParticle>(State, o).inverseMass == 0.0)
    {
        return;
    }
    var position: float3 = Load<float3>(State, o);
    var contact: ContactShape = ContactShape.None;
    if (header.planeEnabled > 0.5) {
        let normal: float3 = header.planeNormal;
        contact = ContactShape.Plane(normal.x, normal.y, normal.z, header.planeOffset + header.halfThickness);
    }
    position = ProjectContact(position, contact);
    if (header.offsetEnabled > 0.5) {
        position = LimitToAnchor(State, position, anchors + item * WordCount<ClothAnchor>());
    }
    contact = ContactShape.None;
    if (header.sphereEnabled > 0.5) {
        contact = ContactShape.Sphere(sphere.x, sphere.y, sphere.z, radius);
    }
    position = ProjectContact(position, contact);
    if (header.offsetEnabled > 0.5) {
        position = LimitToAnchor(State, position, anchors + item * WordCount<ClothAnchor>());
    }
    Store<float3>(State, o, position);
    return;

}

// Authoritative Q1 material constraints and quad-edge rest-angle hinges.
// The compiler supplies grain-aligned Gauss derivatives and inverse area stiffness.
enum SurfaceKind {
    Warp,
    Weft,
    Shear,
    Bend,
}
function SurfaceKindAt(value: u32): SurfaceKind {
    if (value == 6) { return SurfaceKind.Warp; }
    if (value == 7) { return SurfaceKind.Weft; }
    if (value == 8) { return SurfaceKind.Shear; }
    return SurfaceKind.Bend;
}
function SurfaceAngle(a: float3, b: float3, c: float3, d: float3): f32 {
    let edge = Normalize(b - a);
    let first = Normalize(Cross(b - a, c - a));
    let second = Normalize(Cross(d - a, b - a));
    return Atan2(Dot(edge, Cross(first, second)), Dot(first, second));
}
function WrapSurfaceAngle(value: f32): f32 {
    if (value > 3.141592653589793) { return value - 6.283185307179586; }
    if (value < -3.141592653589793) { return value + 6.283185307179586; }
    return value;
}
export function SurfaceConstraint(readonly Source: StorageBuffer<f32>,
    header: ClothHeader, readwrite State: StorageBuffer<f32>, index: u32,
    stencil: ClothStencil): void {
    let entries = U32(stencil.weights);
    let transverse = U32(stencil.transverse);
    let count = U32(stencil.arity);
    let zero = float3(0.0, 0.0, 0.0);
    var positions: Array<float3, 6> = [zero, zero, zero, zero, zero, zero];
    var gradients: Array<float3, 6> = [zero, zero, zero, zero, zero, zero];
    var masses: Array<f32, 6> = [0.0, 0.0, 0.0, 0.0, 0.0, 0.0];
    static for (let local of Indices<6>()) {
        if (local < count) {
            let vertex = U32(Source[entries + local * 2]);
            let particle = Load<ClothParticle>(State, vertex * WordCount<ClothParticle>());
            positions[local] = particle.position;
            masses[local] = particle.inverseMass;
        }
    }
    let kind = SurfaceKindAt(U32(stencil.kind));
    var residual: f32 = 0.0;
    if (kind == SurfaceKind.Bend) {
        let a = positions[0];
        let b = positions[1];
        let c = (positions[2] + positions[3]) * 0.5;
        let d = (positions[4] + positions[5]) * 0.5;
        let edge = b - a;
        let first = Cross(edge, c - a);
        let second = Cross(d - a, edge);
        let length = Length(edge);
        if (length < 0.00000001 || Dot(first, first) < 0.0000000000000001
            || Dot(second, second) < 0.0000000000000001) {
            return;
        }
        let gc = first * (-length / Dot(first, first));
        let gd = second * (-length / Dot(second, second));
        let tc = Dot(c - a, edge) / Dot(edge, edge);
        let td = Dot(d - a, edge) / Dot(edge, edge);
        gradients[0] = gc * (tc - 1.0) + gd * (td - 1.0);
        gradients[1] = gc * (-tc) + gd * (-td);
        gradients[2] = gc * 0.5;
        gradients[3] = gc * 0.5;
        gradients[4] = gd * 0.5;
        gradients[5] = gd * 0.5;
        residual = WrapSurfaceAngle(SurfaceAngle(a, b, c, d) - stencil.restLength);
    } else {
        var u: float3 = zero;
        var v: float3 = zero;
        static for (let local of Indices<4>()) {
            u = u + positions[local] * Source[entries + local * 2 + 1];
            v = v + positions[local] * Source[transverse + local];
        }
        match kind {
            case SurfaceKind.Warp => { residual = (Dot(u, u) - 1.0) * 0.5; }
            case SurfaceKind.Weft => { residual = (Dot(v, v) - 1.0) * 0.5; }
            case SurfaceKind.Shear => { residual = Dot(u, v); }
            case SurfaceKind.Bend => { return; }
        }
        static for (let local of Indices<4>()) {
            let wu = Source[entries + local * 2 + 1];
            let wv = Source[transverse + local];
            match kind {
                case SurfaceKind.Warp => { gradients[local] = u * wu; }
                case SurfaceKind.Weft => { gradients[local] = v * wv; }
                case SurfaceKind.Shear => { gradients[local] = v * wu + u * wv; }
                case SurfaceKind.Bend => { return; }
            }
        }
    }
    var movable: f32 = 0.0;
    static for (let local of Indices<6>()) {
        movable = movable + masses[local] * Dot(gradients[local], gradients[local]);
    }
    if (movable <= 0.00000000000000000001) { return; }
    let multiplier = U32(header.vertexCount) * WordCount<ClothParticle>() + index * 3;
    var previous = Load<float3>(State, multiplier);
    if (header.reset > 0.5) { previous = zero; }
    let alpha = stencil.compliance / (header.step * header.step);
    let delta = (-residual - alpha * previous.x) / (movable + alpha);
    static for (let local of Indices<6>()) {
        if (local < count) {
            let vertex = U32(Source[entries + local * 2]);
            let corrected = positions[local] + gradients[local] * (masses[local] * delta);
            Store<float3>(State, vertex * WordCount<ClothParticle>(), corrected);
        }
    }
    Store<float3>(State, multiplier, previous + float3(delta, 0.0, 0.0));
}

function Constraint(readonly Source: StorageBuffer<f32>,
    header: ClothHeader,
    readwrite State: StorageBuffer<f32>,
    item: u32,
    phase: ClothPhase): void {
    let anchors = U32(header.anchors);
    let h = header.step;
    let sphere = header.sphere;
    let radius = header.sphereRadius + header.halfThickness;
    let index: u32 = U32(header.start) + item;
    let stencil = Load<ClothStencil>(Source, index * WordCount<ClothStencil>());
    if (phase == ClothPhase.Constraints && U32(stencil.kind) >= 6) {
        SurfaceConstraint(Source, header, State, index, stencil);
        return;
    }
    let a: u32 = U32(stencil.a);
    let b: u32 = U32(stencil.b);
    let c: u32 = U32(stencil.c);
    let d: u32 = U32(stencil.d);
    var pa: float3 = Load<float3>(State, a * WordCount<ClothParticle>());
    var pb: float3 = Load<float3>(State, b * WordCount<ClothParticle>());
    var pc: float3 = float3(0.0, 0.0, 0.0);
    var pd: float3 = float3(0.0, 0.0, 0.0);
    if (phase == ClothPhase.TriangleContact || U32(stencil.kind) == 3) {
        pc = Load<float3>(State, c * WordCount<ClothParticle>());
    }
    if (phase != ClothPhase.TriangleContact && U32(stencil.kind) == 3) {
        pd = Load<float3>(State, d * WordCount<ClothParticle>());
    }
    let wa: f32 = Load<ClothParticle>(State, a * WordCount<ClothParticle>()).inverseMass;
    let wb: f32 = Load<ClothParticle>(State, b * WordCount<ClothParticle>()).inverseMass;
    let wc: f32 = Load<ClothParticle>(State, c * WordCount<ClothParticle>()).inverseMass;
    let wd: f32 = Load<ClothParticle>(State, d * WordCount<ClothParticle>()).inverseMass;
    if (phase == ClothPhase.TriangleContact) {
        let bary: float3 = ClosestBary(sphere, pa, pb, pc);
        let closest: float3 = (((pa * bary.x) + (pb * bary.y)) + (pc * bary.z));
        let difference: float3 = (closest - sphere);
        let distance: f32 = Length(difference);
        let penetration: f32 = radius - distance;
        let denominator: f32 = wa * bary.x * bary.x + wb * bary.y * bary.y + wc * bary.z * bary.z;
        if (penetration <= 0.0 || denominator <= 0.00000000000000000001)
        {
            return;
        }
        var normal: float3 = float3(0.0, 1.0, 0.0);
        if (distance > 0.0000000001)
        {
            normal = (difference * (1.0 / distance));
        }
        let correction: float3 = (normal * (penetration / denominator));
        pa = (pa + (correction * (wa * bary.x)));
        pb = (pb + (correction * (wb * bary.y)));
        pc = (pc + (correction * (wc * bary.z)));
        if (header.offsetEnabled > 0.5) {
            pa = LimitToAnchor(State, pa, anchors + a * WordCount<ClothAnchor>());
            pb = LimitToAnchor(State, pb, anchors + b * WordCount<ClothAnchor>());
            pc = LimitToAnchor(State, pc, anchors + c * WordCount<ClothAnchor>());
        }
        Store<float3>(State, c * WordCount<ClothParticle>(), pc);
    } else {
        let l: u32 = U32(header.vertexCount) * WordCount<ClothParticle>() + index * 3;
        var previous: float3 = Load<float3>(State, l);
        if (header.reset > 0.5)
        {
            previous = float3(0.0, 0.0, 0.0);
        }
        var compliance: f32 = stencil.compliance;
        if (U32(stencil.kind) == 5) {
            compliance = compliance * header.stitchComplianceScale;
        }
        let alpha: f32 = compliance / (h * h);
        var increment: float3 = float3(0.0, 0.0, 0.0);
        if (U32(stencil.kind) == 3 || U32(stencil.kind) == 4 || U32(stencil.kind) == 5) {
            let entries: u32 = U32(stencil.weights);
            let count: u32 = U32(stencil.arity);
            var curvature: float3 = float3(0.0, 0.0, 0.0);
            var movable: f32 = 0.0;
            static for (let local of Indices<32>()) {
                if (local < count) {
                    let vertex: u32 = U32(Source[entries + local * 2]);
                    let weight: f32 = Source[entries + local * 2 + 1];
                    let position: float3 = Load<float3>(State, vertex * WordCount<ClothParticle>());
                    curvature = (curvature + (position * weight));
                    movable = movable + Load<ClothParticle>(State, vertex * WordCount<ClothParticle>()).inverseMass * weight * weight;
                }
            }
            if (movable <= 0.00000000000000000001) {
                return;
            }
            increment = (-curvature - previous * alpha) * (1.0 / (movable + alpha));
            static for (let local of Indices<32>()) {
                if (local < count) {
                    let vertex: u32 = U32(Source[entries + local * 2]);
                    let weight: f32 = Source[entries + local * 2 + 1];
                    let position: float3 = Load<float3>(State, vertex * WordCount<ClothParticle>());
                    var corrected: float3 = (position + (increment * (Load<ClothParticle>(State, vertex * WordCount<ClothParticle>()).inverseMass * weight)));
                    if (header.offsetEnabled > 0.5) {
                        corrected = LimitToAnchor(State, corrected, anchors + vertex * WordCount<ClothAnchor>());
                    }
                    Store<float3>(State, vertex * WordCount<ClothParticle>(), corrected);
                }
            }
            Store<float3>(State, l, previous + increment);
            return;
        } else {
            let difference: float3 = (pa - pb);
            let length: f32 = Length(difference);
            if (length < 0.0000000001 || wa + wb == 0.0)
            {
                return;
            }
            let delta: f32 = (0.0 - (length - stencil.restLength) - alpha * previous.x) / (wa + wb + alpha);
            increment = float3(delta, 0.0, 0.0);
            let correction: float3 = (difference * (delta / length));
            pa = (pa + (correction * wa));
            pb = (pb - (correction * wb));
            if (header.offsetEnabled > 0.5) {
                pa = LimitToAnchor(State, pa, anchors + a * WordCount<ClothAnchor>());
                pb = LimitToAnchor(State, pb, anchors + b * WordCount<ClothAnchor>());
            }
        }
        let next: float3 = (previous + increment);
        Store<float3>(State, l, next);
    }
    Store<float3>(State, a * WordCount<ClothParticle>(), pa);
    Store<float3>(State, b * WordCount<ClothParticle>(), pb);
}

@compute
@numthreads(64, 1, 1)
function Main(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Source: StorageBuffer<f32>,
    @binding(1) readonly Header: StorageBuffer<ClothHeader>,
    @binding(2) readwrite State: StorageBuffer<f32>): void {
    let header = Header[0];
    if (thread.x >= U32(header.count)) {
        return;
    }
    let phase = DecodePhase(header.phase);
    match phase {
        case ClothPhase.Predict => {
            Predict(header, State, thread.x);
        },
        case ClothPhase.Constraints => {
            Constraint(Source, header, State, thread.x, phase);
        },
        case ClothPhase.VertexContact => {
            VertexContact(header, State, thread.x);
        },
        case ClothPhase.TriangleContact => {
            Constraint(Source, header, State, thread.x, phase);
        },
        case ClothPhase.Velocity => {
            Velocity(header, State, thread.x);
        },
        case ClothPhase.CapturePins => {
            CapturePins(header, State, thread.x);
        },
        case ClothPhase.OffsetDistances => {
            OffsetPair(Source, header, State, thread.x, phase);
        },
        case ClothPhase.OffsetReduce => {
            Reduce(Source, header, State, thread.x);
        },
        case ClothPhase.OffsetBounds => {
            Bounds(Source, header, State, thread.x);
        },
        case ClothPhase.OffsetContacts => {
            OffsetPair(Source, header, State, thread.x, phase);
        },
        case ClothPhase.Invalid => {
            return;
        },
    }
}
