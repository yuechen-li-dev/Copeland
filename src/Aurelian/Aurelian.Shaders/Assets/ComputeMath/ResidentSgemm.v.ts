import { KernelParameters } from "./KernelMath";

// A full-K resident 64x64 tile with a 4x4 register tile per lane. Host admission
// proves K <= 8192, so the uniform bounded loop covers every 16-wide K tile.
// Adjacent lanes load contiguous input columns. All lanes reach both barriers.
@compute
@numthreads(16, 16, 1)
function ResidentSgemmMain(@builtin(groupId) group: uint3,
    @builtin(groupThreadId) local: uint3,
    @workgroup readwrite TileA: WorkgroupArray<f32, 1024>,
    @workgroup readwrite TileB: WorkgroupArray<f32, 1024>,
    @binding(0) readonly A: StorageBuffer<f32>,
    @binding(1) readonly B: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let tileRow = group.x * 64;
    let tileColumn = group.y * 64;
    let lane = local.y * 16 + local.x;
    var sum00: f32 = 0.0;
    var sum01: f32 = 0.0;
    var sum02: f32 = 0.0;
    var sum03: f32 = 0.0;
    var sum10: f32 = 0.0;
    var sum11: f32 = 0.0;
    var sum12: f32 = 0.0;
    var sum13: f32 = 0.0;
    var sum20: f32 = 0.0;
    var sum21: f32 = 0.0;
    var sum22: f32 = 0.0;
    var sum23: f32 = 0.0;
    var sum30: f32 = 0.0;
    var sum31: f32 = 0.0;
    var sum32: f32 = 0.0;
    var sum33: f32 = 0.0;
    for (let tile of BoundedIndices<512>((p.inner + 15) / 16)) {
        static for (let part of Indices<4>()) {
            let index = lane + part * 256;
            let aRow = tileRow + index / 16;
            let aColumn = p.offset + tile * 16 + index % 16;
            let bRow = p.offset + tile * 16 + index / 64;
            let bColumn = tileColumn + index % 64;
            var a: f32 = 0.0;
            var b: f32 = 0.0;
            if (aRow < p.rows && aColumn < p.inner) {
                a = A[aRow * p.inner + aColumn];
            }
            if (bRow < p.inner && bColumn < p.columns) {
                if (p.mode == 1) {
                    b = B[bColumn * p.inner + bRow];
                } else {
                    b = B[bRow * p.columns + bColumn];
                }
            }
            TileA[index] = a;
            TileB[index] = b;
        }
        WorkgroupBarrier();
        static for (let k of Indices<16>()) {
            sum00 = sum00 + TileA[(local.y + 0) * 16 + k] * TileB[k * 64 + local.x + 0];
            sum01 = sum01 + TileA[(local.y + 0) * 16 + k] * TileB[k * 64 + local.x + 16];
            sum02 = sum02 + TileA[(local.y + 0) * 16 + k] * TileB[k * 64 + local.x + 32];
            sum03 = sum03 + TileA[(local.y + 0) * 16 + k] * TileB[k * 64 + local.x + 48];
            sum10 = sum10 + TileA[(local.y + 16) * 16 + k] * TileB[k * 64 + local.x + 0];
            sum11 = sum11 + TileA[(local.y + 16) * 16 + k] * TileB[k * 64 + local.x + 16];
            sum12 = sum12 + TileA[(local.y + 16) * 16 + k] * TileB[k * 64 + local.x + 32];
            sum13 = sum13 + TileA[(local.y + 16) * 16 + k] * TileB[k * 64 + local.x + 48];
            sum20 = sum20 + TileA[(local.y + 32) * 16 + k] * TileB[k * 64 + local.x + 0];
            sum21 = sum21 + TileA[(local.y + 32) * 16 + k] * TileB[k * 64 + local.x + 16];
            sum22 = sum22 + TileA[(local.y + 32) * 16 + k] * TileB[k * 64 + local.x + 32];
            sum23 = sum23 + TileA[(local.y + 32) * 16 + k] * TileB[k * 64 + local.x + 48];
            sum30 = sum30 + TileA[(local.y + 48) * 16 + k] * TileB[k * 64 + local.x + 0];
            sum31 = sum31 + TileA[(local.y + 48) * 16 + k] * TileB[k * 64 + local.x + 16];
            sum32 = sum32 + TileA[(local.y + 48) * 16 + k] * TileB[k * 64 + local.x + 32];
            sum33 = sum33 + TileA[(local.y + 48) * 16 + k] * TileB[k * 64 + local.x + 48];
        }
        WorkgroupBarrier();
    }
    if (tileRow + local.y + 0 < p.rows && tileColumn + local.x + 0 < p.columns) {
        let index = (tileRow + local.y + 0) * p.columns + tileColumn + local.x + 0;
        if (p.offset == 0) {
            var prior: f32 = 0.0;
            if (p.beta != 0.0) {
                prior = Output[index] * p.beta;
            }
            Output[index] = p.alpha * sum00 + prior;
        } else {
            Output[index] = Output[index] + p.alpha * sum00;
        }
    }
    if (tileRow + local.y + 0 < p.rows && tileColumn + local.x + 16 < p.columns) {
        let index = (tileRow + local.y + 0) * p.columns + tileColumn + local.x + 16;
        if (p.offset == 0) {
            var prior: f32 = 0.0;
            if (p.beta != 0.0) {
                prior = Output[index] * p.beta;
            }
            Output[index] = p.alpha * sum01 + prior;
        } else {
            Output[index] = Output[index] + p.alpha * sum01;
        }
    }
    if (tileRow + local.y + 0 < p.rows && tileColumn + local.x + 32 < p.columns) {
        let index = (tileRow + local.y + 0) * p.columns + tileColumn + local.x + 32;
        if (p.offset == 0) {
            var prior: f32 = 0.0;
            if (p.beta != 0.0) {
                prior = Output[index] * p.beta;
            }
            Output[index] = p.alpha * sum02 + prior;
        } else {
            Output[index] = Output[index] + p.alpha * sum02;
        }
    }
    if (tileRow + local.y + 0 < p.rows && tileColumn + local.x + 48 < p.columns) {
        let index = (tileRow + local.y + 0) * p.columns + tileColumn + local.x + 48;
        if (p.offset == 0) {
            var prior: f32 = 0.0;
            if (p.beta != 0.0) {
                prior = Output[index] * p.beta;
            }
            Output[index] = p.alpha * sum03 + prior;
        } else {
            Output[index] = Output[index] + p.alpha * sum03;
        }
    }
    if (tileRow + local.y + 16 < p.rows && tileColumn + local.x + 0 < p.columns) {
        let index = (tileRow + local.y + 16) * p.columns + tileColumn + local.x + 0;
        if (p.offset == 0) {
            var prior: f32 = 0.0;
            if (p.beta != 0.0) {
                prior = Output[index] * p.beta;
            }
            Output[index] = p.alpha * sum10 + prior;
        } else {
            Output[index] = Output[index] + p.alpha * sum10;
        }
    }
    if (tileRow + local.y + 16 < p.rows && tileColumn + local.x + 16 < p.columns) {
        let index = (tileRow + local.y + 16) * p.columns + tileColumn + local.x + 16;
        if (p.offset == 0) {
            var prior: f32 = 0.0;
            if (p.beta != 0.0) {
                prior = Output[index] * p.beta;
            }
            Output[index] = p.alpha * sum11 + prior;
        } else {
            Output[index] = Output[index] + p.alpha * sum11;
        }
    }
    if (tileRow + local.y + 16 < p.rows && tileColumn + local.x + 32 < p.columns) {
        let index = (tileRow + local.y + 16) * p.columns + tileColumn + local.x + 32;
        if (p.offset == 0) {
            var prior: f32 = 0.0;
            if (p.beta != 0.0) {
                prior = Output[index] * p.beta;
            }
            Output[index] = p.alpha * sum12 + prior;
        } else {
            Output[index] = Output[index] + p.alpha * sum12;
        }
    }
    if (tileRow + local.y + 16 < p.rows && tileColumn + local.x + 48 < p.columns) {
        let index = (tileRow + local.y + 16) * p.columns + tileColumn + local.x + 48;
        if (p.offset == 0) {
            var prior: f32 = 0.0;
            if (p.beta != 0.0) {
                prior = Output[index] * p.beta;
            }
            Output[index] = p.alpha * sum13 + prior;
        } else {
            Output[index] = Output[index] + p.alpha * sum13;
        }
    }
    if (tileRow + local.y + 32 < p.rows && tileColumn + local.x + 0 < p.columns) {
        let index = (tileRow + local.y + 32) * p.columns + tileColumn + local.x + 0;
        if (p.offset == 0) {
            var prior: f32 = 0.0;
            if (p.beta != 0.0) {
                prior = Output[index] * p.beta;
            }
            Output[index] = p.alpha * sum20 + prior;
        } else {
            Output[index] = Output[index] + p.alpha * sum20;
        }
    }
    if (tileRow + local.y + 32 < p.rows && tileColumn + local.x + 16 < p.columns) {
        let index = (tileRow + local.y + 32) * p.columns + tileColumn + local.x + 16;
        if (p.offset == 0) {
            var prior: f32 = 0.0;
            if (p.beta != 0.0) {
                prior = Output[index] * p.beta;
            }
            Output[index] = p.alpha * sum21 + prior;
        } else {
            Output[index] = Output[index] + p.alpha * sum21;
        }
    }
    if (tileRow + local.y + 32 < p.rows && tileColumn + local.x + 32 < p.columns) {
        let index = (tileRow + local.y + 32) * p.columns + tileColumn + local.x + 32;
        if (p.offset == 0) {
            var prior: f32 = 0.0;
            if (p.beta != 0.0) {
                prior = Output[index] * p.beta;
            }
            Output[index] = p.alpha * sum22 + prior;
        } else {
            Output[index] = Output[index] + p.alpha * sum22;
        }
    }
    if (tileRow + local.y + 32 < p.rows && tileColumn + local.x + 48 < p.columns) {
        let index = (tileRow + local.y + 32) * p.columns + tileColumn + local.x + 48;
        if (p.offset == 0) {
            var prior: f32 = 0.0;
            if (p.beta != 0.0) {
                prior = Output[index] * p.beta;
            }
            Output[index] = p.alpha * sum23 + prior;
        } else {
            Output[index] = Output[index] + p.alpha * sum23;
        }
    }
    if (tileRow + local.y + 48 < p.rows && tileColumn + local.x + 0 < p.columns) {
        let index = (tileRow + local.y + 48) * p.columns + tileColumn + local.x + 0;
        if (p.offset == 0) {
            var prior: f32 = 0.0;
            if (p.beta != 0.0) {
                prior = Output[index] * p.beta;
            }
            Output[index] = p.alpha * sum30 + prior;
        } else {
            Output[index] = Output[index] + p.alpha * sum30;
        }
    }
    if (tileRow + local.y + 48 < p.rows && tileColumn + local.x + 16 < p.columns) {
        let index = (tileRow + local.y + 48) * p.columns + tileColumn + local.x + 16;
        if (p.offset == 0) {
            var prior: f32 = 0.0;
            if (p.beta != 0.0) {
                prior = Output[index] * p.beta;
            }
            Output[index] = p.alpha * sum31 + prior;
        } else {
            Output[index] = Output[index] + p.alpha * sum31;
        }
    }
    if (tileRow + local.y + 48 < p.rows && tileColumn + local.x + 32 < p.columns) {
        let index = (tileRow + local.y + 48) * p.columns + tileColumn + local.x + 32;
        if (p.offset == 0) {
            var prior: f32 = 0.0;
            if (p.beta != 0.0) {
                prior = Output[index] * p.beta;
            }
            Output[index] = p.alpha * sum32 + prior;
        } else {
            Output[index] = Output[index] + p.alpha * sum32;
        }
    }
    if (tileRow + local.y + 48 < p.rows && tileColumn + local.x + 48 < p.columns) {
        let index = (tileRow + local.y + 48) * p.columns + tileColumn + local.x + 48;
        if (p.offset == 0) {
            var prior: f32 = 0.0;
            if (p.beta != 0.0) {
                prior = Output[index] * p.beta;
            }
            Output[index] = p.alpha * sum33 + prior;
        } else {
            Output[index] = Output[index] + p.alpha * sum33;
        }
    }
}
