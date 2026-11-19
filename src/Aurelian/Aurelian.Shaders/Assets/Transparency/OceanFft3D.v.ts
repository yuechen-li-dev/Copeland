function ReverseSix(index: u32): u32 {
    return ((index & 1) << 5) | ((index & 2) << 3) | ((index & 4) << 1)
        | ((index & 8) >> 1) | ((index & 16) >> 3) | ((index & 32) >> 5);
}

function SignedMode(index: u32): f32 {
    let magnitude = Convert<f32>(index);
    if (index >= 32) {
        return magnitude - 64.0;
    }
    return magnitude;
}

function Rotate(value: float2, cosine: f32, sine: f32): float2 {
    return float2(value.x * cosine - value.y * sine, value.x * sine + value.y * cosine);
}

// Each group transforms one complete row or column at the current or previous
// presentation time. Pack two real spatial fields into the first complex FFT.
@compute
@numthreads(64, 1, 1)
function OceanFftMain(@builtin(groupId) group: uint3, @builtin(groupIndex) lane: u32,
    @workgroup readwrite RealA: WorkgroupArray<f32, 64>,
    @workgroup readwrite ImagA: WorkgroupArray<f32, 64>,
    @workgroup readwrite RealB: WorkgroupArray<f32, 64>,
    @workgroup readwrite ImagB: WorkgroupArray<f32, 64>,
    @binding(0) readonly Spectrum: StorageBuffer<float2>,
    @binding(1) readonly Source: StorageBuffer<float4>,
    @binding(2) readwrite Destination: StorageBuffer<float4>,
    @binding(3) readonly Parameters: StorageBuffer<float4>): void {
    let parameters = Parameters[0];
    let vertical = parameters.w > 0.5;
    let reversed = ReverseSix(lane);
    let plane = group.y * 4096;
    var fields: float4 = float4(0.0, 0.0, 0.0, 0.0);
    if (vertical) {
        fields = Source[plane + reversed * 64 + group.x];
    } else {
        var time = parameters.x;
        if (group.y == 1) {
            time = parameters.y;
        }
        let kx = SignedMode(reversed) * parameters.z;
        let kz = SignedMode(group.x) * parameters.z;
        let k = Sqrt(kx * kx + kz * kz);
        let phase = -Sqrt(9.81 * k) * time;
        let cosine = Cos(phase);
        let sine = Sin(phase);
        let h0 = Spectrum[group.x * 64 + reversed];
        let opposite = Spectrum[((64 - group.x) & 63) * 64 + ((64 - reversed) & 63)];
        let h = Rotate(h0, cosine, sine) + Rotate(float2(opposite.x, -opposite.y), cosine, -sine);
        let choppy = Parameters[1].x / Max(k, 0.00001);
        let dx = float2(-h.y, h.x) * (kx * choppy);
        let dz = float2(-h.y, h.x) * (kz * choppy);
        fields = float4(dx.x - h.y, dx.y + h.x, dz.x, dz.y);
    }
    RealA[lane] = fields.x;
    ImagA[lane] = fields.y;
    RealB[lane] = fields.z;
    ImagB[lane] = fields.w;
    WorkgroupBarrier();
    static for (let stage of Indices<6>()) {
        let half = 1 << stage;
        let span = half * 2;
        let offset = lane & (half - 1);
        let even = (lane / span) * span + offset;
        let odd = even + half;
        let angle = 6.283185307179586 * Convert<f32>(offset) / Convert<f32>(span);
        let cosine = Cos(angle);
        let sine = Sin(angle);
        var sign = 1.0;
        if ((lane & half) != 0) {
            sign = -1.0;
        }
        let a = float2(RealA[even], ImagA[even]) + Rotate(float2(RealA[odd], ImagA[odd]), cosine, sine) * sign;
        let b = float2(RealB[even], ImagB[even]) + Rotate(float2(RealB[odd], ImagB[odd]), cosine, sine) * sign;
        WorkgroupBarrier();
        RealA[lane] = a.x;
        ImagA[lane] = a.y;
        RealB[lane] = b.x;
        ImagB[lane] = b.y;
        WorkgroupBarrier();
    }
    var address = plane + group.x * 64 + lane;
    if (vertical) {
        address = plane + lane * 64 + group.x;
    }
    Destination[address] = float4(RealA[lane], ImagA[lane], RealB[lane], ImagB[lane]);
}
