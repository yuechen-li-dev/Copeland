import { HzbView, HzbOccluded } from "./HzbTypes";
import { VisibilityCandidate, VisibilityCamera, InsideFrustum } from "./VisibilityTypes";

// Stable workgroup prefix ranks; no global atomics, draw-order races or CPU compaction.
@compute
@numthreads(64, 1, 1)
function HzbCullMain(@builtin(dispatchThreadId) thread: uint3, @builtin(groupIndex) lane: u32,
    @builtin(groupId) group: uint3,
    @workgroup readwrite Prefix: WorkgroupArray<u32, 64>,
    @binding(0) readonly Candidates: StorageBuffer<VisibilityCandidate>,
    @binding(1) readwrite Ranks: StorageBuffer<u32>,
    @binding(2) readwrite Blocks: StorageBuffer<u32>,
    @binding(3) readonly View: StorageBuffer<VisibilityCamera>,
    @binding(4) readwrite Seed: StorageBuffer<u32>,
    @binding(5) readonly Pyramid: StorageBuffer<f32>,
    @binding(6) readonly Hzb: StorageBuffer<HzbView>): void {
    let camera = View[0];
    var keep: u32 = 0;
    if (thread.x < camera.candidateCount) {
        let candidate = Candidates[thread.x];
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
        let hzb = Hzb[0];
        if (camera.reserved == 1) {
            visible = visible && hzb.valid != 0 && !HzbOccluded(candidate.sphere, hzb, Pyramid);
            Seed[thread.x] = 0;
            if (visible) { Seed[thread.x] = 1; }
        } else {
            if (visible && Seed[thread.x] == 0) {
                visible = !HzbOccluded(candidate.sphere, hzb, Pyramid);
            }
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
    Ranks[thread.x] = Prefix[lane] * keep;
    if (lane == 63) {
        Blocks[group.x] = Prefix[63];
    }
}
