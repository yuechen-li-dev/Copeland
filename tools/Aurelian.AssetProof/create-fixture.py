"""Author the small static GLB witness in Blender. No Blender dependency in the runtime."""
import bpy
import os
import sys

destination = os.path.abspath(sys.argv[sys.argv.index("--") + 1])
os.makedirs(destination, exist_ok=True)
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)


def image(name, color_a, color_b):
    result = bpy.data.images.new(name, width=64, height=64, alpha=True)
    result.pixels = [component for y in range(64) for x in range(64)
                     for component in (color_a if (x // 8 + y // 8) % 2 else color_b)]
    result.filepath_raw = os.path.join(destination, name + ".png")
    result.file_format = "PNG"
    result.save()
    result.pack()
    return result


checker = image("checker", (0.04, 0.42, 0.72, 1), (0.85, 0.64, 0.17, 1))
replacement_checker = image("replacement-checker", (0.54, 0.06, 0.32, 1), (0.16, 0.8, 0.67, 1))


def material(name, texture, metallic, roughness):
    result = bpy.data.materials.new(name)
    result.use_nodes = True
    nodes = result.node_tree.nodes
    principled = nodes.get("Principled BSDF")
    principled.inputs["Metallic"].default_value = metallic
    principled.inputs["Roughness"].default_value = roughness
    if texture:
        node = nodes.new("ShaderNodeTexImage")
        node.image = texture
        result.node_tree.links.new(node.outputs["Color"], principled.inputs["Base Color"])
    else:
        principled.inputs["Base Color"].default_value = (0.12, 0.18, 0.22, 1)
    return result


panel = material("panel", checker, 0.2, 0.6)
frame = material("frame", None, 0.7, 0.3)
bpy.ops.mesh.primitive_cube_add(size=1.5, location=(0, 0, 0.75))
crate = bpy.context.object
crate.name = "static-crate"
crate.data.materials.append(panel)
crate.data.materials.append(frame)
for polygon in crate.data.polygons:
    polygon.material_index = 0 if abs(polygon.normal.z) < 0.5 else 1


def export(filename):
    bpy.ops.export_scene.gltf(filepath=os.path.join(destination, filename), export_format="GLB",
                              export_tangents=True, export_animations=False, export_cameras=False,
                              export_lights=False, export_yup=True)


bpy.ops.wm.save_as_mainfile(filepath=os.path.join(destination, "crate.blend"))
export("crate.glb")
crate.scale.x = 1.15
texture_node = next(node for node in panel.node_tree.nodes if node.type == "TEX_IMAGE")
texture_node.image = replacement_checker
export("crate-replacement.glb")
frame.name = "renamed-frame"
export("crate-slot-loss.glb")
frame.name = "frame"
texture_node.image = checker
crate.scale.x = 1
normal_image = image("normal", (0.5, 0.5, 1, 1), (0.8, 0.5, 0.8, 1))
normal_image.colorspace_settings.name = "Non-Color"
principled = panel.node_tree.nodes.get("Principled BSDF")
normal_texture = panel.node_tree.nodes.new("ShaderNodeTexImage")
normal_texture.image = normal_image
normal_node = panel.node_tree.nodes.new("ShaderNodeNormalMap")
normal_node.inputs["Strength"].default_value = 0.7
panel.node_tree.links.new(normal_texture.outputs["Color"], normal_node.inputs["Color"])
panel.node_tree.links.new(normal_node.outputs["Normal"], principled.inputs["Normal"])
principled.inputs["Emission Color"].default_value = (0.05, 0.1, 0.2, 1)
principled.inputs["Emission Strength"].default_value = 1
export("crate-channels.glb")
