# M19 shader provenance

`SemanticFog.v.ts` is independently authored Visual TypeScript. No source code was copied or mechanically translated.

| Project | Exact source | License | M19 use | Attribution consequence |
| --- | --- | --- | --- | --- |
| Godot Engine | commit `34d06658a85845111a50db9e485ec4a0701d4298`, `servers/rendering/renderer_rd/shaders/environment/volumetric_fog.glsl` | MIT | Conceptual comparison for bounded fog composition and temporal inputs | No copied-code attribution required |
| Bevy Engine | commit `7a21c21ecbce9ba28c970ffdf73063321b6bb636`, `crates/bevy_pbr/src/render/fog.wgsl` | MIT OR Apache-2.0 | Conceptual comparison for fog interpolation/density vocabulary | No copied-code attribution required |

Primary links:

- [Godot volumetric fog source](https://github.com/godotengine/godot/blob/34d06658a85845111a50db9e485ec4a0701d4298/servers/rendering/renderer_rd/shaders/environment/volumetric_fog.glsl)
- [Godot MIT license](https://github.com/godotengine/godot/blob/34d06658a85845111a50db9e485ec4a0701d4298/LICENSE.txt)
- [Bevy fog source](https://github.com/bevyengine/bevy/blob/7a21c21ecbce9ba28c970ffdf73063321b6bb636/crates/bevy_pbr/src/render/fog.wgsl)
- [Bevy MIT license](https://github.com/bevyengine/bevy/blob/7a21c21ecbce9ba28c970ffdf73063321b6bb636/LICENSE-MIT)
- [Bevy Apache-2.0 license](https://github.com/bevyengine/bevy/blob/7a21c21ecbce9ba28c970ffdf73063321b6bb636/LICENSE-APACHE)

The shipped authority remains:

`SemanticFog.v.ts -> VD-MIR -> generated HLSL -> DXC -> validated SPIR-V -> Vulkan`.

