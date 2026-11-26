import { KernelParameters } from "./KernelMath";

// One workgroup normalizes and rotates one key row, then writes its cache slot.
@compute
@numthreads(64, 1, 1)
function NormalizeRotateCacheKeyMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Squares: WorkgroupArray<f32, 64>,
    @binding(0) readonly Keys: StorageBuffer<f32>,
    @binding(1) readonly Constants: StorageBuffer<f32>,
    @binding(2) readwrite Cache: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let source = group.x * p.width;
    let position = group.x + p.offset;
    let destination = position * p.width;
    var sum: f32 = 0.0;
    for (let tile of BoundedIndices<8>(p.inner)) {
        let value = Keys[source + tile * 64 + lane];
        sum = sum + value * value;
    }
    Squares[lane] = sum;
    WorkgroupBarrier();
    static for (let stage of Indices<6>()) {
        let stride = 32 >> stage;
        if (lane < stride) { Squares[lane] = Squares[lane] + Squares[lane + stride]; }
        WorkgroupBarrier();
    }
    let inverse = Pow(Squares[0] / Convert<f32>(p.width) + p.epsilon, -0.5);
    let half = p.width / 2;
    for (let tile of BoundedIndices<4>(p.inner / 2)) {
        let pair = tile * 64 + lane;
        var frequency = Pow(p.theta, -2.0 * Convert<f32>(pair) / Convert<f32>(p.width));
        if (p.mode == 1) {
            frequency = frequency / Constants[p.width + pair];
        }
        let angle = Convert<f32>(position) * frequency;
        let cosine = Cos(angle);
        let sine = Sin(angle);
        let x = Keys[source + pair] * inverse * Constants[pair];
        let y = Keys[source + pair + half] * inverse * Constants[pair + half];
        Cache[destination + pair] = x * cosine - y * sine;
        Cache[destination + pair + half] = y * cosine + x * sine;
    }
}
