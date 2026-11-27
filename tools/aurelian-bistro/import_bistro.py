"""Offline conversion of the ORCA Bistro interior. Run with Blender in background mode."""

import argparse
import json
from pathlib import Path
import subprocess
import sys


def convert_textures(manifest_path):
    from PIL import Image, ImageOps
    import numpy as np

    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    alpha = {}
    occlusion = {}
    for source_name, output_name in manifest["textures"].items():
        source, output = Path(source_name), Path(output_name)
        with Image.open(source) as texture:
            image = texture.convert("RGBA")
            image.thumbnail((manifest["edge"], manifest["edge"]), Image.Resampling.LANCZOS)
            if source.stem.lower().endswith("_normal"):
                red, green, blue, _ = image.split()
                green = ImageOps.invert(green)
                if blue.getextrema() == (0, 0):
                    x = np.asarray(red, dtype=np.float32) / 127.5 - 1
                    y = np.asarray(green, dtype=np.float32) / 127.5 - 1
                    z = np.sqrt(np.maximum(0, 1 - x*x - y*y))
                    blue = Image.fromarray(np.rint((z + 1) * 127.5).astype(np.uint8))
                image = Image.merge("RGB", (red, green, blue))
            else:
                if source.stem.lower().endswith("_specular"):
                    # The v5.2 interiors leave the packed red channel empty.
                    # Exporting it as AO would remove all environment lighting.
                    occlusion[source_name] = image.getchannel("R").getextrema()[1] > 0
                opaque = image.getchannel("A").getextrema() == (255, 255)
                alpha[source_name] = not opaque
                if opaque:
                    image = image.convert("RGB")
            image.save(output)
    manifest_path.write_text(json.dumps({"alpha": alpha, "occlusion": occlusion}), encoding="utf-8")


def export_bistro(args):
    import bpy
    from mathutils import Matrix, Vector

    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    texture_dir = output / "conversion-textures"
    texture_dir.mkdir(exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(args.source.resolve()), use_image_search=False)
    materials = list(bpy.data.materials)
    sources = {}
    for material in materials:
        for node in material.node_tree.nodes:
            if node.type == "TEX_IMAGE" and node.image:
                path = Path(bpy.path.abspath(node.image.filepath)).resolve()
                if not path.is_file():
                    raise ValueError(f"Missing Bistro texture {path}")
                sources[str(path)] = str(texture_dir / f"{path.stem}.png")
    mapping = output / "texture-conversion.json"
    mapping.write_text(json.dumps({"edge": args.texture_edge, "textures": sources}), encoding="utf-8")
    subprocess.run([str(args.texture_python.resolve()), str(Path(__file__).resolve()), "--textures", str(mapping)], check=True)
    channels_present = json.loads(mapping.read_text(encoding="utf-8"))

    modes = {}
    occlusion_group = bpy.data.node_groups.new("glTF Material Output", "ShaderNodeTree")
    occlusion_group.interface.new_socket(name="Occlusion", in_out="INPUT", socket_type="NodeSocketFloat")
    for material in materials:
        nodes = material.node_tree.nodes
        links = material.node_tree.links
        principled = next(node for node in nodes if node.type == "BSDF_PRINCIPLED")
        # ORCA's FBX "specular" slot is packed ORM, not a dielectric F0 override.
        principled.inputs["Specular IOR Level"].default_value = 0.5
        principled.inputs["IOR"].default_value = 1.5
        for node in list(nodes):
            if node.type != "TEX_IMAGE" or node.image is None:
                continue
            path = str(Path(bpy.path.abspath(node.image.filepath)).resolve())
            node.image = bpy.data.images.load(sources[path], check_existing=True)
            suffix = Path(path).stem.lower().rsplit("_", 1)[-1]
            if suffix in ("normal", "specular"):
                node.image.colorspace_settings.name = "Non-Color"
            if suffix == "specular":
                for link in list(node.outputs["Color"].links):
                    links.remove(link)
                channels = nodes.new("ShaderNodeSeparateColor")
                links.new(node.outputs["Color"], channels.inputs[0])
                links.new(channels.outputs["Green"], principled.inputs["Roughness"])
                links.new(channels.outputs["Blue"], principled.inputs["Metallic"])
                if channels_present["occlusion"].get(path):
                    occlusion = nodes.new("ShaderNodeGroup")
                    occlusion.node_tree = occlusion_group
                    links.new(channels.outputs["Red"], occlusion.inputs["Occlusion"])
            if suffix == "basecolor" and channels_present["alpha"].get(path):
                links.new(node.outputs["Alpha"], principled.inputs["Alpha"])
                modes[material.name] = "MASK"
        if "transparent" in material.name.lower() or material.name.lower() in ("glass", "water"):
            modes[material.name] = "BLEND"
        elif material.name not in modes:
            for link in list(principled.inputs["Alpha"].links):
                links.remove(link)
            principled.inputs["Alpha"].default_value = 1
        material.use_backface_culling = False

    alignment = Matrix.Identity(4)
    if args.source.name == "BistroInterior_Wine.fbx":
        # Align the two authored interiors so they share fixtures and camera stops.
        alignment = Matrix.Translation((-0.04448516, 0.01438635, 0.02258821)) @ Matrix.Scale(0.6844624, 4)

    chunks, chunk, triangles = [], [], 0
    objects = sorted((obj for obj in bpy.context.scene.objects if obj.type == "MESH"),
                     key=lambda obj: (int(obj.matrix_world.translation.y / 3), obj.name))
    for obj in objects:
        world = alignment @ obj.matrix_world
        obj.parent = None
        if world.to_3x3().determinant() < 0:
            mirror = Matrix.Diagonal((-1, 1, 1, 1))
            obj.data = obj.data.copy()
            obj.data.transform(mirror)
            obj.data.flip_normals()
            world = world @ mirror
        obj.matrix_world = world
        if args.glass_ratio < 1 and obj.data.materials and all(
            material and material.name.startswith("TransparentGlass") for material in obj.data.materials
        ):
            bpy.context.view_layer.objects.active = obj
            decimate = obj.modifiers.new("Bistro glass export LOD", "DECIMATE")
            decimate.ratio = args.glass_ratio
            decimate.use_collapse_triangulate = True
            bpy.ops.object.modifier_apply(modifier=decimate.name)
        uv_layers = obj.data.uv_layers
        if uv_layers:
            while len(uv_layers) > 1:
                uv_layers.remove(uv_layers[-1])
            uv_layers[0].name = "BistroUV"
        if any(len(polygon.vertices) > 4 for polygon in obj.data.polygons):
            # Blender triangulates at export, but tangent generation runs
            # earlier and rejects n-gons such as the wine-edition coasters.
            bpy.context.view_layer.objects.active = obj
            triangulate = obj.modifiers.new("Bistro tangent triangulation", "TRIANGULATE")
            triangulate.min_vertices = 5
            triangulate.keep_custom_normals = True
            bpy.ops.object.modifier_apply(modifier=triangulate.name)
        obj.data.calc_loop_triangles()
        count = len(obj.data.loop_triangles)
        if count >= 333333:
            raise ValueError(f"Bistro object exceeds the static importer limit: {obj.name}")
        if chunk and triangles + count > 245000:
            chunks.append(chunk)
            chunk, triangles = [], 0
        if count:
            chunk.append(obj)
            triangles += count
    if chunk:
        chunks.append(chunk)

    manifest = []
    for index, chunk in enumerate(chunks):
        # Keep the full set of triangles while reducing the number of resident
        # draws and BLAS builds for the hundreds of small static props.
        cells = {}
        for obj in chunk:
            center = sum((Vector(corner) for corner in obj.bound_box), Vector()) / 8
            center = obj.matrix_world @ center
            key = (int(center.x // 4), int(center.y // 4))
            cells.setdefault(key, []).append(obj)
        merged = []
        for cell in cells.values():
            active = cell[0]
            if len(cell) > 1:
                bpy.ops.object.select_all(action="DESELECT")
                for obj in cell:
                    obj.select_set(True)
                bpy.context.view_layer.objects.active = active
                if bpy.ops.object.join() != {"FINISHED"}:
                    raise RuntimeError("Bistro static cell consolidation failed")
            merged.append(active)
        bpy.ops.object.select_all(action="DESELECT")
        for obj in merged:
            obj.select_set(True)
        model_id = f"bistro.interior.{index:02d}"
        target = output / f"{model_id}.glb"
        bpy.ops.export_scene.gltf(filepath=str(target), export_format="GLB", use_selection=True,
                                  export_tangents=True, export_lights=False, export_cameras=False)
        finish_glb(target, modes)
        if target.stat().st_size > 128 * 1024 * 1024:
            raise ValueError(f"{target.name} exceeds the static importer byte limit. Reduce --texture-edge.")
        manifest.append(f'[[models]]\nid = "{model_id}"\npath = "{target.name}"\n')
    (output / "assets.toml").write_text("\n".join(manifest), encoding="utf-8")
    print(f"Prepared {len(chunks)} GLB chunks from {len(objects)} authored Bistro meshes.")


def repair_tangents(document, binary):
    import numpy as np

    def vectors(index, width):
        accessor = document["accessors"][index]
        view = document["bufferViews"][accessor["bufferView"]]
        offset = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
        return np.ndarray((accessor["count"], width), dtype="<f4", buffer=binary, offset=offset,
                          strides=(view.get("byteStride", width * 4), 4))

    repaired = 0
    visited = set()
    for mesh in document["meshes"]:
        for primitive in mesh["primitives"]:
            attributes = primitive["attributes"]
            index = attributes.get("TANGENT")
            if index is None or index in visited:
                continue
            visited.add(index)
            tangents = vectors(index, 4)
            normals = vectors(attributes["NORMAL"], 3)
            length_squared = np.einsum("ij,ij->i", tangents[:, :3], tangents[:, :3])
            alignment = np.einsum("ij,ij->i", normals, tangents[:, :3])
            invalid = (~np.isfinite(length_squared)) | (np.abs(length_squared - 1) > 0.0005)
            invalid |= np.abs(tangents[:, 3]) != 1
            invalid |= np.abs(alignment) > 0.0005
            if not invalid.any():
                continue
            normal = normals[invalid]
            direction = np.nan_to_num(tangents[invalid, :3])
            direction -= normal * np.sum(direction * normal, axis=1, keepdims=True)
            length = np.linalg.norm(direction, axis=1, keepdims=True)
            degenerate = length[:, 0] < 0.0001
            # UV islands collapsed to a point have no tangent. Use a stable
            # orthogonal basis for the handful of vertices the exporter leaves at zero.
            basis = np.eye(3, dtype=np.float32)[np.argmin(np.abs(normal[degenerate]), axis=1)]
            direction[degenerate] = np.cross(basis, normal[degenerate])
            tangents[invalid, :3] = direction / np.linalg.norm(direction, axis=1, keepdims=True)
            tangents[invalid, 3] = np.where(tangents[invalid, 3] < 0, -1, 1)
            repaired += int(invalid.sum())
    return repaired


def remove_degenerate_triangles(document, binary):
    import numpy as np

    def accessor_view(index, dtype, width):
        accessor = document["accessors"][index]
        view = document["bufferViews"][accessor["bufferView"]]
        offset = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
        stride = view.get("byteStride", np.dtype(dtype).itemsize * width)
        return accessor, np.ndarray((accessor["count"], width), dtype=dtype, buffer=binary, offset=offset,
                                    strides=(stride, np.dtype(dtype).itemsize))

    removed = 0
    used_indices = set()
    for mesh in document["meshes"]:
        for primitive in mesh["primitives"]:
            index = primitive["indices"]
            if index in used_indices:
                continue
            used_indices.add(index)
            dtype = {5121: "u1", 5123: "<u2", 5125: "<u4"}[document["accessors"][index]["componentType"]]
            accessor, indices = accessor_view(index, dtype, 1)
            _, positions = accessor_view(primitive["attributes"]["POSITION"], "<f4", 3)
            triangles = indices.reshape(-1, 3)
            a, b, c = (positions[triangles[:, column]] for column in range(3))
            cross = np.cross(b - a, c - a)
            valid = np.einsum("ij,ij->i", cross, cross) >= 1.1e-16
            kept = triangles[valid].reshape(-1)
            if len(kept) == 0:
                raise ValueError(f"No nondegenerate geometry in {mesh['name']}")
            removed += (accessor["count"] - len(kept)) // 3
            indices[:len(kept), 0] = kept
            accessor["count"] = len(kept)
            if "min" in accessor:
                accessor["min"] = [int(kept.min())]
            if "max" in accessor:
                accessor["max"] = [int(kept.max())]
    return removed


def finish_glb(path, modes=None):
    import struct

    data = path.read_bytes()
    json_size, kind = struct.unpack_from("<II", data, 12)
    if kind != 0x4E4F534A:
        raise ValueError(f"Missing GLB JSON chunk in {path.name}")
    document = json.loads(data[20:20 + json_size])
    for material in document.get("materials", []):
        for group in (material, material.get("pbrMetallicRoughness", {})):
            for name, texture in group.items():
                if name.endswith("Texture") and texture.get("texCoord", 0) != 0:
                    raise ValueError(f"{material['name']} uses a texture coordinate set beyond UV0")
    for mesh in document["meshes"]:
        for primitive in mesh["primitives"]:
            # The original FBX carries an unused secondary UV channel.
            primitive["attributes"].pop("TEXCOORD_1", None)
    if modes is not None:
        for material in document.get("materials", []):
            mode = modes.get(material.get("name"), "OPAQUE")
            material["alphaMode"] = mode
            if mode == "MASK":
                material["alphaCutoff"] = 0.5
    rest = bytearray(data[20 + json_size:])
    if len(rest) < 8 or struct.unpack_from("<I", rest, 4)[0] != 0x004E4942:
        raise ValueError(f"Missing GLB geometry chunk in {path.name}")
    repaired = repair_tangents(document, memoryview(rest)[8:])
    removed = remove_degenerate_triangles(document, memoryview(rest)[8:])
    payload = json.dumps(document, separators=(",", ":")).encode("utf-8")
    payload += b" " * (-len(payload) % 4)
    path.write_bytes(data[:8] + struct.pack("<III", 20 + len(payload) + len(rest), len(payload), kind) + payload + rest)
    print(f"{path.name}: repaired {repaired} tangents and removed {removed} degenerate triangles.")


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "--textures":
        convert_textures(Path(sys.argv[2]))
    else:
        parser = argparse.ArgumentParser()
        parser.add_argument("source", type=Path)
        parser.add_argument("output", type=Path)
        parser.add_argument("--texture-python", required=True, type=Path)
        parser.add_argument("--texture-edge", type=int, default=1024)
        parser.add_argument("--glass-ratio", type=float, default=1.0)
        args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
        if not 0 < args.glass_ratio <= 1:
            parser.error("--glass-ratio must be greater than zero and at most one")
        export_bistro(args)
