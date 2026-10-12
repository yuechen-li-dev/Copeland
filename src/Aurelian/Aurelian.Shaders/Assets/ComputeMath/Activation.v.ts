import { KernelParameters, Silu, Gelu, GeluErf } from "./KernelMath";
import { BFloat16 } from "./BFloat16";

enum Activation { Silu, Gelu, SiluGate, Residual, Bias, GeluErf, GeluGate, Invalid }

function Decode(mode: u32): Activation {
    if (mode == 0) {
        return Activation.Silu;
    }
    if (mode == 1) {
        return Activation.Gelu;
    }
    if (mode == 2) {
        return Activation.SiluGate;
    }
    if (mode == 3) {
        return Activation.Residual;
    }
    if (mode == 4) {
        return Activation.Bias;
    }
    if (mode == 5) {
        return Activation.GeluErf;
    }
    if (mode == 6) {
        return Activation.GeluGate;
    }
    return Activation.Invalid;
}

@compute
@numthreads(64, 1, 1)
function ActivationMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Auxiliary: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let index = thread.x + thread.y * p.stride;
    if (index >= p.rows * p.columns) {
        return;
    }
    let value = Input[index];
    match Decode(p.mode) {
        case Activation.Silu => { Output[index] = Silu(value); }
        case Activation.Gelu => { Output[index] = Gelu(value); }
        case Activation.SiluGate => { Output[index] = Silu(value) * Auxiliary[index]; }
        case Activation.Residual => { Output[index] = value + Auxiliary[index]; }
        case Activation.Bias => { Output[index] = value + Auxiliary[index % p.columns]; }
        case Activation.GeluErf => { Output[index] = GeluErf(value); }
        case Activation.GeluGate => { Output[index] = BFloat16(BFloat16(Gelu(value)) * Auxiliary[index]); }
        case Activation.Invalid => { Output[index] = value; }
    }
}
