import time
import bpy
import numpy as np
from mathutils import Vector
from pxr import Gf, Sdf, Usd, UsdGeom, UsdLux, UsdShade, Vt

def write_scene(fixture, path):
    stage = Usd.Stage.CreateNew(str(path))
    UsdGeom.SetStageUpAxis(stage, UsdGeom.Tokens.y)
    UsdGeom.SetStageMetersPerUnit(stage, 1.0)
    root = UsdGeom.Xform.Define(stage, "/Room")
    stage.SetDefaultPrim(root.GetPrim())
    root.GetPrim().CreateAttribute("aurelian:sceneKey", Sdf.ValueTypeNames.String).Set(fixture["SceneKey"])
    sunlight = UsdLux.DistantLight.Define(stage, "/Lights/Sun")
    sunlight.CreateIntensityAttr(1)
    sunlight.CreateAngleAttr(0)
    direction = Gf.Vec3d(*fixture["Sun"])
    matrix = Gf.Matrix4d().SetRotate(Gf.Rotation(Gf.Vec3d(0, 0, 1), direction))
    UsdGeom.Xformable(sunlight.GetPrim()).AddTransformOp().Set(matrix)
    sunlight.GetPrim().CreateAttribute("aurelian:directionToLight", Sdf.ValueTypeNames.Float3).Set(Gf.Vec3f(*fixture["Sun"]))
    for index, body in enumerate(fixture["Bodies"]):
        name = "body_" + str(index)
        mesh = UsdGeom.Mesh.Define(stage, "/Room/" + name)
        positions = body["Positions"]
        mesh.CreatePointsAttr(Vt.Vec3fArray([Gf.Vec3f(*point) for point in positions]))
        mesh.CreateFaceVertexCountsAttr([3] * (len(positions) // 3))
        mesh.CreateFaceVertexIndicesAttr(list(range(len(positions))))
        mesh.CreateSubdivisionSchemeAttr(UsdGeom.Tokens.none)
        mesh.GetPrim().CreateAttribute("aurelian:bodyId", Sdf.ValueTypeNames.String).Set(body["Id"])
        if body.get("FaceAlbedos"):
            mesh.GetPrim().CreateAttribute("aurelian:faceAlbedos", Sdf.ValueTypeNames.FloatArray).Set(Vt.FloatArray(body["FaceAlbedos"]))
        material_path = "/Materials/" + name
        material = UsdShade.Material.Define(stage, material_path)
        shader = UsdShade.Shader.Define(stage, material_path + "/Surface")
        shader.CreateIdAttr("UsdPreviewSurface")
        shader.CreateInput("diffuseColor", Sdf.ValueTypeNames.Color3f).Set(Gf.Vec3f(*body["Albedo"]))
        shader.CreateInput("emissiveColor", Sdf.ValueTypeNames.Color3f).Set(Gf.Vec3f(*body["Emission"]))
        shader.CreateOutput("surface", Sdf.ValueTypeNames.Token)
        material.CreateSurfaceOutput().ConnectToSource(shader.ConnectableAPI(), "surface")
        UsdShade.MaterialBindingAPI.Apply(mesh.GetPrim()).Bind(material)
    stage.GetRootLayer().Save()
    return stage


def configure_cycles():
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    preferences = bpy.context.preferences.addons["cycles"].preferences
    preferences.compute_device_type = "OPTIX"
    preferences.get_devices()
    devices = []
    for device in preferences.devices:
        device.use = device.type == "OPTIX"
        if device.use:
            devices.append(device.name)
    if not devices:
        raise RuntimeError("This witness requires a Cycles OptiX GPU; CPU fallback is not qualified.")
    scene.cycles.device = "GPU"
    scene.cycles.samples = 8192
    scene.cycles.max_bounces = 8
    scene.cycles.diffuse_bounces = 8
    scene.cycles.use_denoising = False
    scene.cycles.use_adaptive_sampling = False
    scene.cycles.sample_clamp_direct = 0
    scene.cycles.sample_clamp_indirect = 0
    scene.render.bake.margin = 0
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0
    return devices


def load_scene(stage, receiver_id="floor", domain=None):
    domain = domain or {"MinimumX": -2.7, "MinimumZ": -2.7, "Width": 5.4, "Depth": 5.4, "Height": 0}
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    receiver = None
    emitters = []
    for prim in stage.Traverse():
        if not prim.IsA(UsdGeom.Mesh):
            continue
        mesh = UsdGeom.Mesh(prim)
        points = [tuple(point) for point in mesh.GetPointsAttr().Get()]
        indices = list(mesh.GetFaceVertexIndicesAttr().Get())
        # Explicit Y-up game metres -> Blender Z-up metres.
        vertices = [(x, -z, y) for x, y, z in points]
        faces = [tuple(indices[index:index + 3]) for index in range(0, len(indices), 3)]
        data = bpy.data.meshes.new(prim.GetName())
        data.from_pydata(vertices, [], faces)
        data.update()
        obj = bpy.data.objects.new(prim.GetName(), data)
        bpy.context.collection.objects.link(obj)
        material, _ = UsdShade.MaterialBindingAPI(prim).ComputeBoundMaterial()
        surface = UsdShade.Shader(stage.GetPrimAtPath(str(material.GetPath()) + "/Surface"))
        albedo = tuple(surface.GetInput("diffuseColor").Get())
        emission = tuple(surface.GetInput("emissiveColor").Get())
        blender_material = bpy.data.materials.new(prim.GetName())
        blender_material.use_nodes = True
        nodes = blender_material.node_tree.nodes
        nodes.clear()
        diffuse = nodes.new("ShaderNodeBsdfDiffuse")
        diffuse.inputs["Color"].default_value = (*albedo, 1)
        emitted = nodes.new("ShaderNodeEmission")
        emitted.inputs["Color"].default_value = (*emission, 1)
        addition = nodes.new("ShaderNodeAddShader")
        output = nodes.new("ShaderNodeOutputMaterial")
        links = blender_material.node_tree.links
        links.new(diffuse.outputs[0], addition.inputs[0])
        links.new(emitted.outputs[0], addition.inputs[1])
        links.new(addition.outputs[0], output.inputs[0])
        obj.data.materials.append(blender_material)
        emitters.append((emitted, emission))
        face_attribute = prim.GetAttribute("aurelian:faceAlbedos")
        if face_attribute and face_attribute.HasAuthoredValueOpinion():
            colours = np.asarray(face_attribute.Get()).reshape(-1, 3)
            if len(colours) != len(data.polygons):
                raise ValueError("receiver-face-albedo-shape-mismatch")
            material_ids = {}
            for polygon, colour in zip(data.polygons, colours):
                key = tuple(colour)
                if key not in material_ids:
                    customized = blender_material.copy()
                    customized.node_tree.nodes[diffuse.name].inputs["Color"].default_value = (*key, 1)
                    obj.data.materials.append(customized)
                    material_ids[key] = len(obj.data.materials) - 1
                    emitters.append((customized.node_tree.nodes[emitted.name], emission))
                polygon.material_index = material_ids[key]
        if prim.GetAttribute("aurelian:bodyId").Get() == receiver_id:
            receiver = obj
            uv = data.uv_layers.new(name="BakeReceiver")
            for polygon in data.polygons:
                for loop_index in polygon.loop_indices:
                    point = points[data.loops[loop_index].vertex_index]
                    if polygon.normal.z > .9:
                        uv.data[loop_index].uv = ((point[0] - domain["MinimumX"]) / domain["Width"], (point[2] - domain["MinimumZ"]) / domain["Depth"])
                    else:
                        uv.data[loop_index].uv = (-2, -2)
    if receiver is None:
        raise RuntimeError("USD scene has no authored floor receiver.")
    sun_data = bpy.data.lights.new("UnitSun", "SUN")
    sun_data.angle = 0
    sun = bpy.data.objects.new("UnitSun", sun_data)
    bpy.context.collection.objects.link(sun)
    x, y, z = stage.GetPrimAtPath("/Lights/Sun").GetAttribute("aurelian:directionToLight").Get()
    sun.rotation_euler = Vector((x, -z, y)).to_track_quat("Z", "Y").to_euler()
    return receiver, sun_data, emitters


def bake(receiver, sun, emitters, size, basis, seed):
    sun.energy = 1 if basis == 0 else 0
    for node, colour in emitters:
        node.inputs["Color"].default_value = (*colour, 1) if basis == 1 else (0, 0, 0, 1)
    scene = bpy.context.scene
    scene.cycles.seed = seed
    bpy.ops.object.select_all(action="DESELECT")
    receiver.select_set(True)
    bpy.context.view_layer.objects.active = receiver
    image = bpy.data.images.new("Reference", width=size, height=size, float_buffer=True, alpha=True)
    image.colorspace_settings.name = "Non-Color"
    targets = []
    for material in receiver.data.materials:
        nodes = material.node_tree.nodes
        target = nodes.new("ShaderNodeTexImage")
        target.image = image
        nodes.active = target
        targets.append((nodes, target))
    started = time.perf_counter()
    passes = {"INDIRECT", "COLOR"} if basis == 0 else {"DIRECT", "INDIRECT", "COLOR"}
    bpy.ops.object.bake(type="DIFFUSE", pass_filter=passes, use_clear=True)
    pixels = np.empty(size * size * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    result = pixels.reshape(size, size, 4)[:, :, :3].copy()
    for nodes, target in targets:
        nodes.remove(target)
    bpy.data.images.remove(image)
    print(f"REFERENCE basis={basis} size={size} seconds={time.perf_counter() - started:.3f}", flush=True)
    if not np.isfinite(result).all() or result.min() < -1e-6:
        raise RuntimeError("Cycles returned invalid radiance.")
    return result


