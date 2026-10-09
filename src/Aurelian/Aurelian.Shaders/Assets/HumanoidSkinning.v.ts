// DQS ABI: immutable expanded vertices (24 floats), palette (8 floats per joint).
// Pose header: output count, two correction amounts and world matrix. Millimetres to Y-up metres.
@compute
@numthreads(64, 1, 1)
function SkinMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Rest: StorageBuffer<f32>,
    @binding(1) readonly Palette: StorageBuffer<f32>,
    @binding(2) readonly Pose: StorageBuffer<f32>,
    @binding(3) readwrite Output: StorageBuffer<f32>): void {
    if (thread.x >= U32(Pose[0])) {
        return;
    }
    const v: u32 = thread.x * 24;
    const o: u32 = thread.x * 10;
    const reference: u32 = U32(Rest[v + 6]) * 8;
    var rx: f32 = 0.0;
    var dx: f32 = 0.0;
    var ry: f32 = 0.0;
    var dy: f32 = 0.0;
    var rz: f32 = 0.0;
    var dz: f32 = 0.0;
    var rw: f32 = 0.0;
    var dw: f32 = 0.0;
    const joint0: u32 = U32(Rest[v + 6]) * 8;
    var weight0: f32 = Rest[v + 7];
    // Align influence 0 to the reference quaternion's hemisphere.
    if (Palette[reference + 0] * Palette[joint0 + 0] +
        Palette[reference + 1] * Palette[joint0 + 1] +
        Palette[reference + 2] * Palette[joint0 + 2] +
        Palette[reference + 3] * Palette[joint0 + 3] < 0.0) {
        weight0 = 0.0 - weight0;
    }
    rx = rx + Palette[joint0 + 0] * weight0;
    dx = dx + Palette[joint0 + 4] * weight0;
    ry = ry + Palette[joint0 + 1] * weight0;
    dy = dy + Palette[joint0 + 5] * weight0;
    rz = rz + Palette[joint0 + 2] * weight0;
    dz = dz + Palette[joint0 + 6] * weight0;
    rw = rw + Palette[joint0 + 3] * weight0;
    dw = dw + Palette[joint0 + 7] * weight0;
    const joint1: u32 = U32(Rest[v + 8]) * 8;
    var weight1: f32 = Rest[v + 9];
    // Align influence 1 to the reference quaternion's hemisphere.
    if (Palette[reference + 0] * Palette[joint1 + 0] +
        Palette[reference + 1] * Palette[joint1 + 1] +
        Palette[reference + 2] * Palette[joint1 + 2] +
        Palette[reference + 3] * Palette[joint1 + 3] < 0.0) {
        weight1 = 0.0 - weight1;
    }
    rx = rx + Palette[joint1 + 0] * weight1;
    dx = dx + Palette[joint1 + 4] * weight1;
    ry = ry + Palette[joint1 + 1] * weight1;
    dy = dy + Palette[joint1 + 5] * weight1;
    rz = rz + Palette[joint1 + 2] * weight1;
    dz = dz + Palette[joint1 + 6] * weight1;
    rw = rw + Palette[joint1 + 3] * weight1;
    dw = dw + Palette[joint1 + 7] * weight1;
    const joint2: u32 = U32(Rest[v + 10]) * 8;
    var weight2: f32 = Rest[v + 11];
    // Align influence 2 to the reference quaternion's hemisphere.
    if (Palette[reference + 0] * Palette[joint2 + 0] +
        Palette[reference + 1] * Palette[joint2 + 1] +
        Palette[reference + 2] * Palette[joint2 + 2] +
        Palette[reference + 3] * Palette[joint2 + 3] < 0.0) {
        weight2 = 0.0 - weight2;
    }
    rx = rx + Palette[joint2 + 0] * weight2;
    dx = dx + Palette[joint2 + 4] * weight2;
    ry = ry + Palette[joint2 + 1] * weight2;
    dy = dy + Palette[joint2 + 5] * weight2;
    rz = rz + Palette[joint2 + 2] * weight2;
    dz = dz + Palette[joint2 + 6] * weight2;
    rw = rw + Palette[joint2 + 3] * weight2;
    dw = dw + Palette[joint2 + 7] * weight2;
    const joint3: u32 = U32(Rest[v + 12]) * 8;
    var weight3: f32 = Rest[v + 13];
    // Align influence 3 to the reference quaternion's hemisphere.
    if (Palette[reference + 0] * Palette[joint3 + 0] +
        Palette[reference + 1] * Palette[joint3 + 1] +
        Palette[reference + 2] * Palette[joint3 + 2] +
        Palette[reference + 3] * Palette[joint3 + 3] < 0.0) {
        weight3 = 0.0 - weight3;
    }
    rx = rx + Palette[joint3 + 0] * weight3;
    dx = dx + Palette[joint3 + 4] * weight3;
    ry = ry + Palette[joint3 + 1] * weight3;
    dy = dy + Palette[joint3 + 5] * weight3;
    rz = rz + Palette[joint3 + 2] * weight3;
    dz = dz + Palette[joint3 + 6] * weight3;
    rw = rw + Palette[joint3 + 3] * weight3;
    dw = dw + Palette[joint3 + 7] * weight3;
    const joint4: u32 = U32(Rest[v + 14]) * 8;
    var weight4: f32 = Rest[v + 15];
    // Align influence 4 to the reference quaternion's hemisphere.
    if (Palette[reference + 0] * Palette[joint4 + 0] +
        Palette[reference + 1] * Palette[joint4 + 1] +
        Palette[reference + 2] * Palette[joint4 + 2] +
        Palette[reference + 3] * Palette[joint4 + 3] < 0.0) {
        weight4 = 0.0 - weight4;
    }
    rx = rx + Palette[joint4 + 0] * weight4;
    dx = dx + Palette[joint4 + 4] * weight4;
    ry = ry + Palette[joint4 + 1] * weight4;
    dy = dy + Palette[joint4 + 5] * weight4;
    rz = rz + Palette[joint4 + 2] * weight4;
    dz = dz + Palette[joint4 + 6] * weight4;
    rw = rw + Palette[joint4 + 3] * weight4;
    dw = dw + Palette[joint4 + 7] * weight4;
    const joint5: u32 = U32(Rest[v + 16]) * 8;
    var weight5: f32 = Rest[v + 17];
    // Align influence 5 to the reference quaternion's hemisphere.
    if (Palette[reference + 0] * Palette[joint5 + 0] +
        Palette[reference + 1] * Palette[joint5 + 1] +
        Palette[reference + 2] * Palette[joint5 + 2] +
        Palette[reference + 3] * Palette[joint5 + 3] < 0.0) {
        weight5 = 0.0 - weight5;
    }
    rx = rx + Palette[joint5 + 0] * weight5;
    dx = dx + Palette[joint5 + 4] * weight5;
    ry = ry + Palette[joint5 + 1] * weight5;
    dy = dy + Palette[joint5 + 5] * weight5;
    rz = rz + Palette[joint5 + 2] * weight5;
    dz = dz + Palette[joint5 + 6] * weight5;
    rw = rw + Palette[joint5 + 3] * weight5;
    dw = dw + Palette[joint5 + 7] * weight5;
    const inverseLength: f32 = 1.0 / Sqrt(rx * rx + ry * ry + rz * rz + rw * rw);
    rx = rx * inverseLength;
    dx = dx * inverseLength;
    ry = ry * inverseLength;
    dy = dy * inverseLength;
    rz = rz * inverseLength;
    dz = dz * inverseLength;
    rw = rw * inverseLength;
    dw = dw * inverseLength;
    const parallel: f32 = rx * dx + ry * dy + rz * dz + rw * dw;
    dx = dx - rx * parallel;
    dy = dy - ry * parallel;
    dz = dz - rz * parallel;
    dw = dw - rw * parallel;
    const px: f32 = Rest[v + 0] + Rest[v + 18] * Pose[1] + Rest[v + 21] * Pose[2];
    const py: f32 = Rest[v + 1] + Rest[v + 19] * Pose[1] + Rest[v + 22] * Pose[2];
    const pz: f32 = Rest[v + 2] + Rest[v + 20] * Pose[1] + Rest[v + 23] * Pose[2];
    const tx: f32 = 2.0 * (rw * dx - dw * rx + ry * dz - rz * dy);
    const ty: f32 = 2.0 * (rw * dy - dw * ry + rz * dx - rx * dz);
    const tz: f32 = 2.0 * (rw * dz - dw * rz + rx * dy - ry * dx);
    const pcx: f32 = ry * pz - rz * py;
    const pcy: f32 = rz * px - rx * pz;
    const pcz: f32 = rx * py - ry * px;
    const prx: f32 = px + 2.0 * rw * pcx + 2.0 * (ry * pcz - rz * pcy) + tx;
    const pry: f32 = py + 2.0 * rw * pcy + 2.0 * (rz * pcx - rx * pcz) + ty;
    const prz: f32 = pz + 2.0 * rw * pcz + 2.0 * (rx * pcy - ry * pcx) + tz;
    const ncx: f32 = ry * Rest[v + 5] - rz * Rest[v + 4];
    const ncy: f32 = rz * Rest[v + 3] - rx * Rest[v + 5];
    const ncz: f32 = rx * Rest[v + 4] - ry * Rest[v + 3];
    const nrx: f32 = Rest[v + 3] + 2.0 * rw * ncx + 2.0 * (ry * ncz - rz * ncy);
    const nry: f32 = Rest[v + 4] + 2.0 * rw * ncy + 2.0 * (rz * ncx - rx * ncz);
    const nrz: f32 = Rest[v + 5] + 2.0 * rw * ncz + 2.0 * (rx * ncy - ry * ncx);
    const x: f32 = prx * 0.001;
    const y: f32 = prz * 0.001;
    const z: f32 = 0.0 - pry * 0.001;
    const nx: f32 = nrx;
    const ny: f32 = nrz;
    const nz: f32 = 0.0 - nry;
    Output[o + 0] = x * Pose[3] + y * Pose[7] + z * Pose[11] + Pose[15];
    Output[o + 3] = nx * Pose[3] + ny * Pose[7] + nz * Pose[11];
    Output[o + 1] = x * Pose[4] + y * Pose[8] + z * Pose[12] + Pose[16];
    Output[o + 4] = nx * Pose[4] + ny * Pose[8] + nz * Pose[12];
    Output[o + 2] = x * Pose[5] + y * Pose[9] + z * Pose[13] + Pose[17];
    Output[o + 5] = nx * Pose[5] + ny * Pose[9] + nz * Pose[13];
    Output[o + 6] = 0.67;
    Output[o + 7] = 0.69;
    Output[o + 8] = 0.72;
    Output[o + 9] = 1.00;
    return;
}
