import { KernelParameters } from "./KernelMath";
import { MergeProjectionTiles } from "./NormalizeSingleProjection";

function ProjectionValue(readonly values: StorageBuffer<f32>, index: u32, tiles: u32): f32 {
    var value = values[index];
    if (MergeProjectionTiles) {
        value = value + values[1536 + index];
        if (tiles == 4) {
            value = value + values[3072 + index] + values[4608 + index];
        }
    }
    return value;
}

// The residual width divides into twelve values per lane. Keep them in
// registers across the reduction so the epilogue can reuse the original load.
@compute
@numthreads(128, 1, 1)
function DecoderNormalize1536Main(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Squares: WorkgroupArray<f32, 128>,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Gamma: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let base = group.x * 1536;
    var input: Array<f32, 12> = [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0];
    var even: f32 = 0.0;
    var odd: f32 = 0.0;
    static for (let pair of Indices<6>()) {
        const left = pair * 2;
        const right = left + 1;
        let x = ProjectionValue(Input, base + left * 128 + lane, p.inner);
        let y = ProjectionValue(Input, base + right * 128 + lane, p.inner);
        input[left] = x;
        input[right] = y;
        even = even + x * x;
        odd = odd + y * y;
    }
    Squares[lane] = even + odd;
    WorkgroupBarrier();
    static for (let stage of Indices<7>()) {
        let stride = 64 >> stage;
        if (lane < stride) { Squares[lane] = Squares[lane] + Squares[lane + stride]; }
        WorkgroupBarrier();
    }
    let inverse = Pow(Squares[0] / 1536.0 + p.epsilon, -0.5);
    static for (let tile of Indices<12>()) {
        let channel = tile * 128 + lane;
        Output[base + channel] = input[tile] * inverse * Gamma[channel];
    }
}
