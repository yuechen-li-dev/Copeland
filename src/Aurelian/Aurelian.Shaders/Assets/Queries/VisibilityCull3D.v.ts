import { VisibilityCandidate, VisibilityCamera, InsideFrustum, SelectPreparedLod } from "./VisibilityTypes";

// Stable workgroup prefix ranks; no global atomics, draw-order races or CPU compaction.
@compute
@numthreads(64, 1, 1)
function VisibilityCullMain(@builtin(dispatchThreadId) thread: uint3, @builtin(groupIndex) lane: u32,
    @builtin(groupId) group: uint3,
    @workgroup readwrite Prefix: WorkgroupArray<u32, 64>,
    @binding(0) readonly Candidates: StorageBuffer<VisibilityCandidate>,
    @binding(1) readwrite Ranks: StorageBuffer<u32>,
    @binding(2) readwrite Blocks: StorageBuffer<u32>,
    @binding(3) readonly View: StorageBuffer<VisibilityCamera>,
    @binding(7) readwrite LodState: StorageBuffer<u32>): void {
    let camera = View[0];
    var keep: u32 = 0;
    var selected: u32 = 0;
    if (thread.x < camera.candidateCount) {
        let candidate = Candidates[thread.x];
        selected = SelectPreparedLod(candidate, camera, LodState[thread.x]);
        LodState[thread.x] = selected;
        var visible = true;
        if (camera.mode >= 1) {
            visible = InsideFrustum(camera, candidate.instanceSphere);
        }
        if (visible && camera.mode >= 2) {
            visible = InsideFrustum(camera, candidate.sphere);
        }
        if (visible && camera.mode >= 3 && candidate.cone.w >= -1.0) {
            let view = float3(camera.eye.x - candidate.sphere.x, camera.eye.y - candidate.sphere.y,
                camera.eye.z - candidate.sphere.z);
            let facing = candidate.cone.x * view.x + candidate.cone.y * view.y + candidate.cone.z * view.z;
            visible = facing > candidate.cone.w * Length(view) - candidate.sphere.w - 0.0001;
        }
        if (visible) {
            keep = 1;
        }
    }
    Prefix[lane] = keep;
    WorkgroupBarrier();
    static for (let stage of Indices<6>()) {
        let stride = 1 << stage;
        var previous: u32 = 0;
        if (lane >= stride) {
            previous = Prefix[lane - stride];
        }
        WorkgroupBarrier();
        Prefix[lane] = Prefix[lane] + previous;
        WorkgroupBarrier();
    }
    Ranks[thread.x] = (Prefix[lane] + selected * 256) * keep;
    if (lane == 63) {
        Blocks[group.x] = Prefix[63];
    }
}
