export record VisibilityLod {
    error: f32;
    firstIndex: u32;
    indexCount: u32;
    reserved: u32;
}
export record VisibilityCandidate {
    instanceSphere: float4;
    sphere: float4;
    cone: float4;
    firstIndex: u32;
    indexCount: u32;
    firstInstance: u32;
    lodCount: u32;
    level1: VisibilityLod;
    level2: VisibilityLod;
    level3: VisibilityLod;
}
export record VisibilityCamera {
    left: float4;
    right: float4;
    bottom: float4;
    top: float4;
    near: float4;
    far: float4;
    eye: float4;
    candidateCount: u32;
    blockCount: u32;
    mode: u32;
    reserved: u32;
    clipX: float4;
    clipY: float4;
    clipW: float4;
    lodSettings: float4;
}

export function PreparedLod(candidate: VisibilityCandidate, level: u32): VisibilityLod {
    if (level == 1) { return candidate.level1; }
    if (level == 2) { return candidate.level2; }
    if (level == 3) { return candidate.level3; }
    return { error: 0.0, firstIndex: candidate.firstIndex, indexCount: candidate.indexCount, reserved: 0 };
}

function EvaluatePlane(plane: float4, sphere: float4): f32 {
    return plane.x * sphere.x + plane.y * sphere.y + plane.z * sphere.z + plane.w;
}

// Bounds projection sensitivity over the object's sphere; the offline geometric error remains an estimate.
export function SelectPreparedLod(candidate: VisibilityCandidate, camera: VisibilityCamera, previous: u32): u32 {
    if (candidate.lodCount == 0) { return 0; }
    let sphere = candidate.instanceSphere;
    if (camera.lodSettings.z <= 0.0 || EvaluatePlane(camera.near, sphere) <= sphere.w) { return 0; }
    let wLength = Length(float3(camera.clipW.x, camera.clipW.y, camera.clipW.z));
    let xLength = Length(float3(camera.clipX.x, camera.clipX.y, camera.clipX.z));
    let yLength = Length(float3(camera.clipY.x, camera.clipY.y, camera.clipY.z));
    let minimumW = EvaluatePlane(camera.clipW, sphere) - sphere.w * wLength;
    let maximumX = Abs(EvaluatePlane(camera.clipX, sphere)) + sphere.w * xLength;
    let maximumY = Abs(EvaluatePlane(camera.clipY, sphere)) + sphere.w * yLength;
    var selected: u32 = 0;
    static for (let index of Indices<3>()) {
        let level = index + 1;
        if (level <= candidate.lodCount) {
            let error = PreparedLod(candidate, level).error;
            let denominator = minimumW * (minimumW - error * wLength);
            if (minimumW > error * wLength && denominator > 0.0) {
                let xError = error * (minimumW * xLength + maximumX * wLength) / denominator * camera.lodSettings.x;
                let yError = error * (minimumW * yLength + maximumY * wLength) / denominator * camera.lodSettings.y;
                let pixels = Length(float2(xError, yError));
                var limit = camera.lodSettings.z;
                if (level > previous) { limit = limit * 0.8; }
                if (pixels <= limit) { selected = level; }
            }
        }
    }
    return selected;
}
export record IndexedCommand {
    indexCount: u32;
    instanceCount: u32;
    firstIndex: u32;
    vertexOffset: u32;
    firstInstance: u32;
}
function InsidePlane(plane: float4, sphere: float4): bool {
    return plane.x * sphere.x + plane.y * sphere.y + plane.z * sphere.z + plane.w + sphere.w >= -0.0001;
}
export function InsideFrustum(camera: VisibilityCamera, sphere: float4): bool {
    return InsidePlane(camera.left, sphere) && InsidePlane(camera.right, sphere)
        && InsidePlane(camera.bottom, sphere) && InsidePlane(camera.top, sphere)
        && InsidePlane(camera.near, sphere) && InsidePlane(camera.far, sphere);
}

