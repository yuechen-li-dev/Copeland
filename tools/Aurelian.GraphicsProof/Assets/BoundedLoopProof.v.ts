@compute
@numthreads(64, 1, 1)
function BoundedLoopMain(@builtin(groupIndex) lane: u32,
    @workgroup readwrite Shared: WorkgroupArray<f32, 64>,
    @binding(0) readonly Count: StorageBuffer<u32>,
    @binding(1) readwrite Output: StorageBuffer<f32>): void {
    let count = Count[0];
    var sum: f32 = 0.0;
    for (let index of BoundedIndices<512>(count)) {
        Shared[lane] = Convert<f32>(index);
        WorkgroupBarrier();
        sum = sum + Shared[(lane + 1) % 64];
        WorkgroupBarrier();
    }
    Output[lane] = sum;
}
