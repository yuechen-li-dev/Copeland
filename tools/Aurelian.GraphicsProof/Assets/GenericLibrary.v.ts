export interface HasValue<T> { value: T; }
export record Box<T> { value: T; }
export record Grid<T, static Rows: u32, static Columns: u32> {
    cells: NDArray<T, Rows, Columns>;
}
export type FloatBox = Box<f32>;

export template<type T>
function Identity(value: T): T { return value; }

function Read<T extends HasValue<f32>>(value: T): f32 { return value.value; }
function Forward<T extends HasValue<f32>>(value: T): f32 { return Read<T>(value); }
function Square<static N: u32>(value: Matrix<f32, N, N>): Matrix<f32, N, N> {
    return MatMul(value, value);
}
function Make<T = f32, static Count: u32 = 2>(value: T): Box<T> {
    return { value: value };
}

template<static Seed: f32 = 2.0> Build: Box<f32> { return { value: Seed }; }
function Samples<static N: u32>(v: f32): Array<f32, N> { return [v, 2.0]; }
function Shift<static N: u32 = 2, static M: u32 = (N + 1)>(v: f32): f32 {
    return v + Convert<f32>(N + M);
}

export function Shade(v: f32): f32 {
    let box: FloatBox = { value: v };
    let grid: Grid<f32, Rows: 2, Columns: 2> = { cells: [v, 2.0, 3.0, 4.0] };
    let matrix: Matrix<f32, 2, 2> = [v, 2.0, 3.0, 4.0];
    let squared: Matrix<f32, 2, 2> = Square(matrix);
    let constants: Box<f32> = instantiate Build<Seed: 3.0>;
    let samples: Array<f32, 2> = static Samples<N: 2>(4.0);
    return Forward(Identity<Box<f32>>(box)) + Read(Make<f32>(v))
        + squared.at(0, 1) + grid.cells.at(1, 0) + constants.value + samples[0] + Shift(0.0);
}
