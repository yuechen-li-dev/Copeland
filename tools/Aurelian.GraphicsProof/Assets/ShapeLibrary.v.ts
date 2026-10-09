export type Weights = Matrix<f32, 2, 2>;
export record Sample {
    value: f32;
    weights: Weights;
}

export function Shade(value: f32): f32 {
    let original: Sample = { weights: [value, 2.0, 3.0, 4.0], value: value };
    let sample: Sample = original with { value: value * 2.0 };
    let product: Weights = MatMul(sample.weights, sample.weights);
    let a: Vector<f32, 3> = [value, 2.0, 3.0];
    let b: Tensor<f32, 3> = [4.0, 5.0, 6.0];
    let added: Tensor<f32, 3> = a + b;
    var grid: NDArray<f32, 2, 2> = [0.0, 1.0, 2.0, 3.0];
    grid.set(1, 0, value);
    var weights: Array<f32, 2> = [0.0, 1.0];
    weights[0] = value;
    return sample.value + product.at(0, 0) + product.at(0, 1) + Dot(a, b) + added[2] + grid.at(1, 0) + weights[0];
}

export function DynamicValue(index: u32): f32 {
    var grid: NDArray<f32, 2, 2> = [0.0, 1.0, 2.0, 3.0];
    grid.set(index, 1, 7.0);
    return grid.at(index, 1);
}
