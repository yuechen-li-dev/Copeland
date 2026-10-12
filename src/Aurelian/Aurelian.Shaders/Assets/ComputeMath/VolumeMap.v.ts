import { KernelParameters } from "./KernelMath";
import { Float16 } from "./Float16";

enum VolumeOperation { RoundHalf, Columns, Shuffle, Copy, Invalid }

function Decode(mode: u32): VolumeOperation {
    if (mode == 0) { return VolumeOperation.RoundHalf; }
    if (mode == 1) { return VolumeOperation.Columns; }
    if (mode == 2) { return VolumeOperation.Shuffle; }
    if (mode == 3) { return VolumeOperation.Copy; }
    return VolumeOperation.Invalid;
}

@compute
@numthreads(64, 1, 1)
function VolumeMapMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Auxiliary: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let index = thread.x + thread.y * p.stride;
    if (index >= p.rows * p.columns) { return; }
    match Decode(p.mode) {
        case VolumeOperation.RoundHalf => {
            Output[index] = Float16(Input[index]);
        }
        case VolumeOperation.Columns => {
            let voxel = index / p.columns + p.offset;
            let columnIndex = index % p.columns;
            let kernelCount = p.height * p.height * p.height;
            let channelIndex = columnIndex / kernelCount;
            let kernel = columnIndex % kernelCount;
            let x = voxel / (p.width * p.width) + kernel / (p.height * p.height);
            let y = voxel / p.width % p.width + kernel / p.height % p.height;
            let z = voxel % p.width + kernel % p.height;
            var value: f32 = 0.0;
            if (x >= p.padding && y >= p.padding && z >= p.padding
                && x < p.width + p.padding && y < p.width + p.padding && z < p.width + p.padding) {
                let sourceVoxel = ((x - p.padding) * p.width + y - p.padding) * p.width + z - p.padding;
                value = Input[sourceVoxel * p.inner + channelIndex];
            }
            Output[index] = value;
        }
        case VolumeOperation.Shuffle => {
            let voxel = index / p.inner;
            let channelIndex = index % p.inner;
            let resolution = p.width * 2;
            let x = voxel / (resolution * resolution);
            let y = voxel / resolution % resolution;
            let z = voxel % resolution;
            let sourceVoxel = (x / 2 * p.width + y / 2) * p.width + z / 2;
            let offset = x % 2 * 4 + y % 2 * 2 + z % 2;
            Output[index] = Input[sourceVoxel * p.inner * 8 + channelIndex * 8 + offset];
        }
        case VolumeOperation.Copy => {
            Output[index + p.offset] = Input[index + p.padding];
        }
        case VolumeOperation.Invalid => { }
    }
}
