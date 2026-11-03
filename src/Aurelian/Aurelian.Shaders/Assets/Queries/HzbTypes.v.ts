export record HzbView {
    clipX: float4;
    clipY: float4;
    clipZ: float4;
    clipW: float4;
    width: u32;
    height: u32;
    levels: u32;
    valid: u32;
}
function Row(row: float4, p: float3): f32 {
    return row.x * p.x + row.y * p.y + row.z * p.z + row.w;
}
// A world-space sphere's enclosing AABB is projected conservatively when all corners are in front.
export function HzbOccluded(sphere: float4, view: HzbView, readonly pyramid: StorageBuffer<f32>): bool {
    var minimumX: f32 = 1.0e30;
    var minimumY: f32 = 1.0e30;
    var maximumX: f32 = -1.0e30;
    var maximumY: f32 = -1.0e30;
    var nearest: f32 = 1.0;
    static for (let corner of Indices<8>()) {
        var x = -sphere.w;
        var y = -sphere.w;
        var z = -sphere.w;
        if ((corner & 1) != 0) { x = sphere.w; }
        if ((corner & 2) != 0) { y = sphere.w; }
        if ((corner & 4) != 0) { z = sphere.w; }
        let p = float3(sphere.x + x, sphere.y + y, sphere.z + z);
        let w = Row(view.clipW, p);
        let depth = Row(view.clipZ, p);
        if (w <= 0.0 || depth <= 0.0) { return false; }
        let sx = (Row(view.clipX, p) / w * 0.5 + 0.5) * Convert<f32>(view.width);
        let sy = (Row(view.clipY, p) / w * 0.5 + 0.5) * Convert<f32>(view.height);
        minimumX = Min(minimumX, sx);
        minimumY = Min(minimumY, sy);
        maximumX = Max(maximumX, sx);
        maximumY = Max(maximumY, sy);
        nearest = Min(nearest, depth / w);
    }
    if (maximumX < 0.0 || maximumY < 0.0 || minimumX >= Convert<f32>(view.width) || minimumY >= Convert<f32>(view.height)) {
        return false;
    }
    // Cover raster quantization and projection rounding; background depth keeps holes conservative.
    var left = U32(Max(0.0, Floor(minimumX - 2.0)));
    var top = U32(Max(0.0, Floor(minimumY - 2.0)));
    var right = U32(Min(Convert<f32>(view.width - 1), Floor(maximumX + 2.0)));
    var bottom = U32(Min(Convert<f32>(view.height - 1), Floor(maximumY + 2.0)));
    var width = view.width;
    var height = view.height;
    var offset: u32 = 0;
    var finished = false;
    var occluded = false;
    static for (let level of Indices<16>()) {
        if (!finished && level < view.levels) {
            if (right - left <= 1 && bottom - top <= 1) {
                let farthest = Max(Max(pyramid[offset + top * width + left], pyramid[offset + top * width + right]),
                    Max(pyramid[offset + bottom * width + left], pyramid[offset + bottom * width + right]));
                occluded = nearest > farthest + 0.00001;
                finished = true;
            } else {
                offset = offset + width * height;
                width = (width + 1) / 2;
                height = (height + 1) / 2;
                left = left / 2;
                right = right / 2;
                top = top / 2;
                bottom = bottom / 2;
            }
        }
    }
    return occluded;
}
