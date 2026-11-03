export record HzbReduction {
    source: u32;
    sourceWidth: u32;
    sourceHeight: u32;
    destination: u32;
    width: u32;
    height: u32;
}
@compute
@numthreads(64, 1, 1)
function HzbReduceMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readwrite Pyramid: StorageBuffer<f32>,
    @binding(1) readonly Parameters: StorageBuffer<HzbReduction>): void {
    let p = Parameters[0];
    if (thread.x >= p.width * p.height) { return; }
    let x = (thread.x % p.width) * 2;
    let y = (thread.x / p.width) * 2;
    var right = x + 1;
    var bottom = y + 1;
    if (right >= p.sourceWidth) { right = x; }
    if (bottom >= p.sourceHeight) { bottom = y; }
    let maximum = Max(Max(Pyramid[p.source + y * p.sourceWidth + x], Pyramid[p.source + y * p.sourceWidth + right]),
        Max(Pyramid[p.source + bottom * p.sourceWidth + x], Pyramid[p.source + bottom * p.sourceWidth + right]));
    Pyramid[p.destination + thread.x] = maximum;
}
