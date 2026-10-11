// The opaque query belongs to the compiler, following Oct's SDSL-V model.
// Packed ABI and batch bounds are owned by AurelianTraceClosest lowering.
@compute
@numthreads(64, 1, 1)
function RayQueryMain(
    @builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Scene: acceleration_structure,
    @binding(1) readonly Spheres: StorageBuffer<f32>,
    @binding(2) readonly Rays: StorageBuffer<f32>,
    @binding(3) readwrite Hits: StorageBuffer<f32>,
    @binding(4) readonly Triangles: StorageBuffer<f32>
): void {
    RayQueryTraceClosest(Scene, Spheres, Rays, Hits, Triangles, thread.x);
    return;
}
