// Ported algorithm contracts from Oct Prometheus; see Licenses/Prometheus-NOTICE.txt.
// The host packs exactly sixteen 32-bit words. Each operation validates its shape.
export record KernelParameters {
    rows: u32;
    columns: u32;
    inner: u32;
    offset: u32;
    mode: u32;
    width: u32;
    height: u32;
    channels: u32;
    kernelWidth: u32;
    kernelHeight: u32;
    padding: u32;
    stride: u32;
    alpha: f32;
    beta: f32;
    epsilon: f32;
    theta: f32;
}

export function Silu(value: f32): f32 {
    if (value >= 0.0) {
        return value / (1.0 + Exp(-value));
    }
    let exponential = Exp(value);
    return value * exponential / (1.0 + exponential);
}

export function Gelu(value: f32): f32 {
    let scaled = 0.7978845608 * (value + 0.044715 * value * value * value);
    // tanh(x) = 2 sigmoid(2x) - 1; stable saturation through exp.
    return value / (1.0 + Exp(-2.0 * scaled));
}
