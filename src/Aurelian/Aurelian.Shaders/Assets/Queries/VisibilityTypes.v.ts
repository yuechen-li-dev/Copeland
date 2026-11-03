export record VisibilityCandidate {
    instanceSphere: float4;
    sphere: float4;
    cone: float4;
    firstIndex: u32;
    indexCount: u32;
    firstInstance: u32;
    reserved: u32;
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

