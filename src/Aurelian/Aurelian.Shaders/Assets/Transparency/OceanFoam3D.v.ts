@compute
@numthreads(64, 1, 1)
function OceanFoamMain(@builtin(groupId) group: uint3, @builtin(groupIndex) x: u32,
    @binding(0) readonly Displacement: StorageBuffer<float4>,
    @binding(1) readwrite Surface: StorageBuffer<float4>,
    @binding(2) readonly Parameters: StorageBuffer<float4>): void {
    let row = group.x * 64;
    let p = Displacement[group.y * 4096 + row + x];
    var foam = 0.0;
    if (group.y == 0) {
        let east = Displacement[row + ((x + 1) & 63)];
        let west = Displacement[row + ((x + 63) & 63)];
        let north = Displacement[((group.x + 1) & 63) * 64 + x];
        let south = Displacement[((group.x + 63) & 63) * 64 + x];
        let scale = Parameters[1].y;
        let dx = (east - west) * scale;
        let dz = (north - south) * scale;
        let jacobian = (1.0 + dx.x) * (1.0 + dz.z) - dx.z * dz.x;
        let compression = Clamp((0.64 - jacobian) / 0.54, 0.0, 1.0);
        foam = compression * compression * (3.0 - 2.0 * compression);
    }
    Surface[group.y * 4096 + row + x] = float4(p.x, p.y, p.z, foam);
}
