record Vec3 {
    x: f32;
    y: f32;
    z: f32;
}
function V(x: f32, y: f32, z: f32): Vec3 {
    return { x: x, y: y, z: z };
}
// Flat-rest XPBD baseline, scalar ABI documented in ClothGpuData3D.
// Every dispatch writes disjoint vertices. Cross-color ordering is a Vulkan barrier.
function Add(a: Vec3, b: Vec3): Vec3 {
    return V(a.x + b.x, a.y + b.y, a.z + b.z);
}
function Sub(a: Vec3, b: Vec3): Vec3 {
    return V(a.x - b.x, a.y - b.y, a.z - b.z);
}
function Scale(a: Vec3, s: f32): Vec3 {
    return V(a.x * s, a.y * s, a.z * s);
}
function Dot3(a: Vec3, b: Vec3): f32 {
    return a.x * b.x + a.y * b.y + a.z * b.z;
}
enum ContactShape {
    None,
    Plane(nx: f32, ny: f32, nz: f32, offset: f32),
    Sphere(x: f32, y: f32, z: f32, radius: f32),
}
function ProjectPlane(position: Vec3, normal: Vec3, offset: f32): Vec3 {
    const gap: f32 = Dot3(normal, position) - offset;
    if (gap < 0.0) {
        return Sub(position, Scale(normal, gap));
    }
    return position;
}
function ProjectSphere(position: Vec3, center: Vec3, radius: f32): Vec3 {
    const difference: Vec3 = Sub(position, center);
    const distance: f32 = Sqrt(Dot3(difference, difference));
    if (distance >= radius) {
        return position;
    }
    var outward: Vec3 = V(0.0, 1.0, 0.0);
    if (distance > 0.0000000001) {
        outward = Scale(difference, 1.0 / distance);
    }
    return Add(center, Scale(outward, radius));
}
function ProjectContact(position: Vec3, contact: ContactShape): Vec3 {
    return match contact {
        ContactShape.None => position,
        ContactShape.Plane(payload) => ProjectPlane(position, V(payload.nx, payload.ny, payload.nz), payload.offset),
        ContactShape.Sphere(payload) => ProjectSphere(position, V(payload.x, payload.y, payload.z), payload.radius),
    };
}
function SegmentBary(p: Vec3, a: Vec3, b: Vec3, wa: Vec3, wb: Vec3): Vec3 {
    const edge: Vec3 = Sub(b, a);
    const length: f32 = Dot3(edge, edge);
    var t: f32 = 0.0;
    if (length > 0.00000000000000000001) {
        t = Clamp(Dot3(Sub(p, a), edge) / length, 0.0, 1.0);
    }
    return Add(Scale(wa, 1.0 - t), Scale(wb, t));
}
function ClosestBary(p: Vec3, a: Vec3, b: Vec3, c: Vec3): Vec3 {
    const ab: Vec3 = Sub(b, a);
    const ac: Vec3 = Sub(c, a);
    const aa: f32 = Dot3(ab, ab);
    const bb: f32 = Dot3(ab, ac);
    const cc: f32 = Dot3(ac, ac);
    if (aa * cc - bb * bb <= 0.00000001 * aa * cc || aa * cc < 0.000000000000000000000001) {
        const bc: Vec3 = Sub(c, b);
        const edgeLength: f32 = Dot3(bc, bc);
        if (aa >= cc && aa >= edgeLength) {
            return SegmentBary(p, a, b, V(1.0, 0.0, 0.0), V(0.0, 1.0, 0.0));
        }
        if (cc >= edgeLength) {
            return SegmentBary(p, a, c, V(1.0, 0.0, 0.0), V(0.0, 0.0, 1.0));
        }
        return SegmentBary(p, b, c, V(0.0, 1.0, 0.0), V(0.0, 0.0, 1.0));
    }
    const ap: Vec3 = Sub(p, a);
    const d1: f32 = Dot3(ab, ap);
    const d2: f32 = Dot3(ac, ap);
    if (d1 <= 0.0 && d2 <= 0.0)
    {
        return V(1.0, 0.0, 0.0);
    }
    const bp: Vec3 = Sub(p, b);
    const d3: f32 = Dot3(ab, bp);
    const d4: f32 = Dot3(ac, bp);
    if (d3 >= 0.0 && d4 <= d3)
    {
        return V(0.0, 1.0, 0.0);
    }
    const vc: f32 = d1 * d4 - d3 * d2;
    if (vc <= 0.0 && d1 >= 0.0 && d3 <= 0.0) {
        const t: f32 = d1 / (d1 - d3);
        return V(1.0 - t, t, 0.0);
    }
    const cp: Vec3 = Sub(p, c);
    const d5: f32 = Dot3(ab, cp);
    const d6: f32 = Dot3(ac, cp);
    if (d6 >= 0.0 && d5 <= d6)
    {
        return V(0.0, 0.0, 1.0);
    }
    const vb: f32 = d5 * d2 - d1 * d6;
    if (vb <= 0.0 && d2 >= 0.0 && d6 <= 0.0) {
        const t: f32 = d2 / (d2 - d6);
        return V(1.0 - t, 0.0, t);
    }
    const va: f32 = d3 * d6 - d5 * d4;
    if (va <= 0.0 && d4 >= d3 && d5 >= d6) {
        const t: f32 = (d4 - d3) / (d4 - d3 + d5 - d6);
        return V(0.0, 1.0 - t, t);
    }
    const inverse: f32 = 1.0 / (va + vb + vc);
    const v: f32 = vb * inverse;
    const w: f32 = vc * inverse;
    return V(1.0 - v - w, v, w);
}

@compute
@numthreads(64, 1, 1)
function Main(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Source: StorageBuffer<f32>,
    @binding(1) readonly Header: StorageBuffer<f32>,
    @binding(2) readwrite State: StorageBuffer<f32>): void {
    if (thread.x >= U32(Header[1]))
    {
        return;
    }
    const phase: u32 = U32(Header[0]);
    const o: u32 = thread.x * 16;
    const h: f32 = Header[4];
    if (phase == 5) {
        State[o + 13] = State[o];
        State[o + 14] = State[o + 1];
        State[o + 15] = State[o + 2];
        return;
    }
    if (phase == 0) {
        State[o + 3] = State[o];
        State[o + 4] = State[o + 1];
        State[o + 5] = State[o + 2];
        if (State[o + 9] == 0.0) {
            State[o] = State[o + 13] * (1.0 - Header[10]) + State[o + 10] * Header[10];
            State[o + 1] = State[o + 14] * (1.0 - Header[10]) + State[o + 11] * Header[10];
            State[o + 2] = State[o + 15] * (1.0 - Header[10]) + State[o + 12] * Header[10];
        } else {
            State[o + 6] = State[o + 6] + Header[5] * h;
            State[o + 7] = State[o + 7] + Header[6] * h;
            State[o + 8] = State[o + 8] + Header[7] * h;
            State[o] = State[o] + State[o + 6] * h;
            State[o + 1] = State[o + 1] + State[o + 7] * h;
            State[o + 2] = State[o + 2] + State[o + 8] * h;
        }
        return;
    }
    if (phase == 4) {
        const damping: f32 = Header[8] / h;
        State[o + 6] = (State[o] - State[o + 3]) * damping;
        State[o + 7] = (State[o + 1] - State[o + 4]) * damping;
        State[o + 8] = (State[o + 2] - State[o + 5]) * damping;
        return;
    }
    const sphere: Vec3 = V(Header[18], Header[19], Header[20]);
    const radius: f32 = Header[21] + Header[11];
    if (phase == 2) {
        if (State[o + 9] == 0.0)
        {
            return;
        }
        var position: Vec3 = V(State[o], State[o + 1], State[o + 2]);
        var contact: ContactShape = ContactShape.None;
        if (Header[12] > 0.5) {
            const normal: Vec3 = V(Header[13], Header[14], Header[15]);
            contact = ContactShape.Plane(normal.x, normal.y, normal.z, Header[16] + Header[11]);
        }
        position = ProjectContact(position, contact);
        contact = ContactShape.None;
        if (Header[17] > 0.5) {
            contact = ContactShape.Sphere(sphere.x, sphere.y, sphere.z, radius);
        }
        position = ProjectContact(position, contact);
        State[o] = position.x;
        State[o + 1] = position.y;
        State[o + 2] = position.z;
        return;
    }
    const index: u32 = U32(Header[2]) + thread.x;
    const s: u32 = index * 12;
    const a: u32 = U32(Source[s + 1]);
    const b: u32 = U32(Source[s + 2]);
    const c: u32 = U32(Source[s + 3]);
    const d: u32 = U32(Source[s + 4]);
    var pa: Vec3 = V(State[a * 16], State[a * 16 + 1], State[a * 16 + 2]);
    var pb: Vec3 = V(State[b * 16], State[b * 16 + 1], State[b * 16 + 2]);
    var pc: Vec3 = V(0.0, 0.0, 0.0);
    var pd: Vec3 = V(0.0, 0.0, 0.0);
    if (phase == 3 || U32(Source[s]) == 3) {
        pc = V(State[c * 16], State[c * 16 + 1], State[c * 16 + 2]);
    }
    if (phase != 3 && U32(Source[s]) == 3) {
        pd = V(State[d * 16], State[d * 16 + 1], State[d * 16 + 2]);
    }
    const wa: f32 = State[a * 16 + 9];
    const wb: f32 = State[b * 16 + 9];
    const wc: f32 = State[c * 16 + 9];
    const wd: f32 = State[d * 16 + 9];
    if (phase == 3) {
        const bary: Vec3 = ClosestBary(sphere, pa, pb, pc);
        const closest: Vec3 = Add(Add(Scale(pa, bary.x), Scale(pb, bary.y)), Scale(pc, bary.z));
        const difference: Vec3 = Sub(closest, sphere);
        const distance: f32 = Sqrt(Dot3(difference, difference));
        const penetration: f32 = radius - distance;
        const denominator: f32 = wa * bary.x * bary.x + wb * bary.y * bary.y + wc * bary.z * bary.z;
        if (penetration <= 0.0 || denominator <= 0.00000000000000000001)
        {
            return;
        }
        var normal: Vec3 = V(0.0, 1.0, 0.0);
        if (distance > 0.0000000001)
        {
            normal = Scale(difference, 1.0 / distance);
        }
        const correction: Vec3 = Scale(normal, penetration / denominator);
        pa = Add(pa, Scale(correction, wa * bary.x));
        pb = Add(pb, Scale(correction, wb * bary.y));
        pc = Add(pc, Scale(correction, wc * bary.z));
        State[c * 16] = pc.x;
        State[c * 16 + 1] = pc.y;
        State[c * 16 + 2] = pc.z;
    } else {
        const l: u32 = U32(Header[3]) * 16 + index * 3;
        var previous: Vec3 = V(State[l], State[l + 1], State[l + 2]);
        if (Header[9] > 0.5)
        {
            previous = V(0.0, 0.0, 0.0);
        }
        const alpha: f32 = Source[s + 6] / (h * h);
        var increment: Vec3 = V(0.0, 0.0, 0.0);
        if (U32(Source[s]) == 3) {
            const ka: f32 = Source[s + 7];
            const kb: f32 = Source[s + 8];
            const kc: f32 = Source[s + 9];
            const kd: f32 = Source[s + 10];
            const curvature: Vec3 = Add(Add(Scale(pa, ka), Scale(pb, kb)), Add(Scale(pc, kc), Scale(pd, kd)));
            const movable: f32 = wa * ka * ka + wb * kb * kb + wc * kc * kc + wd * kd * kd;
            if (movable <= 0.00000000000000000001)
            {
                return;
            }
            increment = Scale(Sub(Scale(curvature, -1.0), Scale(previous, alpha)), 1.0 / (movable + alpha));
            pa = Add(pa, Scale(increment, wa * ka));
            pb = Add(pb, Scale(increment, wb * kb));
            pc = Add(pc, Scale(increment, wc * kc));
            pd = Add(pd, Scale(increment, wd * kd));
            State[c * 16] = pc.x;
            State[c * 16 + 1] = pc.y;
            State[c * 16 + 2] = pc.z;
            State[d * 16] = pd.x;
            State[d * 16 + 1] = pd.y;
            State[d * 16 + 2] = pd.z;
        } else {
            const difference: Vec3 = Sub(pa, pb);
            const length: f32 = Sqrt(Dot3(difference, difference));
            if (length < 0.0000000001 || wa + wb == 0.0)
            {
                return;
            }
            const delta: f32 = (0.0 - (length - Source[s + 5]) - alpha * previous.x) / (wa + wb + alpha);
            increment = V(delta, 0.0, 0.0);
            const correction: Vec3 = Scale(difference, delta / length);
            pa = Add(pa, Scale(correction, wa));
            pb = Sub(pb, Scale(correction, wb));
        }
        const next: Vec3 = Add(previous, increment);
        State[l] = next.x;
        State[l + 1] = next.y;
        State[l + 2] = next.z;
    }
    State[a * 16] = pa.x;
    State[a * 16 + 1] = pa.y;
    State[a * 16 + 2] = pa.z;
    State[b * 16] = pb.x;
    State[b * 16 + 1] = pb.y;
    State[b * 16 + 2] = pb.z;
}
