"""Bake explicit Mixamo references against a gameplay rest skeleton using installed Blender.

Raw and derived reference assets remain local. Runtime consumes typed baked data,
never FBX, bone-name discovery, or the reference character's mesh/skin weights.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Vector


def arguments():
    parser = argparse.ArgumentParser()
    parser.add_argument("--body", required=True)
    parser.add_argument("--idle", required=True)
    parser.add_argument("--walk", required=True)
    parser.add_argument("--run", required=True)
    parser.add_argument("--out", required=True)
    return parser.parse_args(sys.argv[sys.argv.index("--") + 1:])


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest().upper()


def body_matrix(value):
    return Matrix([[value[f"m{column + 1}{row + 1}"] for column in range(4)] for row in range(4)])


def vector(value):
    return {"x": value.x, "y": value.y, "z": value.z}


def quaternion(value):
    return {"x": value.x, "y": value.y, "z": value.z, "w": value.w}


def roles():
    result = {"Pelvis": "Hips", "SpineLower": "Spine", "SpineMid": "Spine1",
              "Chest": "Spine2", "Neck": "Neck", "Head": "Head"}
    for side in ("Left", "Right"):
        for semantic, native in {"Clavicle": "Shoulder", "Shoulder": "Arm", "Elbow": "ForeArm",
                                 "Wrist": "Hand", "Hip": "UpLeg", "Knee": "Leg",
                                 "Ankle": "Foot", "ToeBase": "ToeBase"}.items():
            result[side + semantic] = "mixamorig:" + side + native
    for key in ("Pelvis", "SpineLower", "SpineMid", "Chest", "Neck", "Head"):
        result[key] = "mixamorig:" + result[key]
    return result


def bake(path, kind, body):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.wm.fbx_import(filepath=str(Path(path).resolve()))
    rigs = [obj for obj in bpy.data.objects if obj.type == "ARMATURE"]
    if len(rigs) != 1 or not rigs[0].animation_data or not rigs[0].animation_data.action:
        raise RuntimeError(f"{path}: expected one animated armature")
    rig = rigs[0]
    mapping = roles()
    joints = body["skeleton"]["joints"]
    by_kind = {joint["kind"]: joint for joint in joints}
    binds = {joint["kind"]: body_matrix(joint["globalBind"]) for joint in joints}
    for semantic, native in mapping.items():
        if semantic not in by_kind or native not in rig.data.bones:
            raise RuntimeError(f"{path}: missing semantic correspondence {semantic} -> {native}")
        parent = by_kind[semantic]["parentIndex"]
        if parent is not None and joints[parent]["kind"] in mapping:
            expected = mapping[joints[parent]["kind"]]
            if rig.data.bones[native].parent is None or rig.data.bones[native].parent.name != expected:
                raise RuntimeError(f"{path}: parent mismatch at {semantic}")
    source_rest = {kind: rig.matrix_world @ rig.data.bones[name].matrix_local for kind, name in mapping.items()}
    distal = {"Pelvis": "SpineLower", "SpineLower": "SpineMid", "SpineMid": "Chest", "Chest": "Neck", "Neck": "Head"}
    for side in ("Left", "Right"):
        for start, end in (("Clavicle", "Shoulder"), ("Shoulder", "Elbow"), ("Elbow", "Wrist"),
                           ("Hip", "Knee"), ("Knee", "Ankle"), ("Ankle", "ToeBase")):
            distal[side + start] = side + end
    target_length = (binds["LeftHip"].translation - binds["LeftKnee"].translation).length
    source_length = (source_rest["LeftHip"].translation - source_rest["LeftKnee"].translation).length
    scale_mm = target_length / source_length
    first, last = map(int, rig.animation_data.action.frame_range)
    def canonical_basis(transforms, travel=None):
        up = Vector((0, 0, 1))
        forward = travel.copy() if travel is not None else sum((
            transforms[side + "ToeBase"].translation - transforms[side + "Ankle"].translation
            for side in ("Left", "Right")), Vector())
        forward.z = 0
        forward.normalize()
        right = forward.cross(up).normalized()
        return Matrix((right, forward, up))
    rest_basis = canonical_basis(source_rest)
    hand_names = {
        side: ["mixamorig:" + side + "Hand" + digit + "1" for digit in ("Middle", "Index", "Pinky")]
        for side in ("Left", "Right")
    }
    for side, names in hand_names.items():
        for name in names:
            if name not in rig.pose.bones:
                raise RuntimeError(f"{path}: missing anatomical hand-frame bone {name}")
        for digit in ("Middle", "Index", "Little"):
            if side + digit + "Proximal" not in binds:
                raise RuntimeError(f"{path}: body lacks {side}{digit}Proximal hand correspondence")
    def anatomical_hand_frame(wrist, middle, index, little):
        length = (middle - wrist).normalized()
        width = index - little
        width = (width - length * width.dot(length)).normalized()
        normal = length.cross(width).normalized()
        return Matrix((length, width, normal)).transposed()
    target_hands = {
        side: anatomical_hand_frame(binds[side + "Wrist"].translation,
            binds[side + "MiddleProximal"].translation, binds[side + "IndexProximal"].translation,
            binds[side + "LittleProximal"].translation)
        for side in ("Left", "Right")
    }
    target_forearms = {
        side: anatomical_hand_frame(binds[side + "Elbow"].translation,
            binds[side + "Wrist"].translation, binds[side + "IndexProximal"].translation,
            binds[side + "LittleProximal"].translation)
        for side in ("Left", "Right")
    }
    bpy.context.scene.frame_set(first)
    first_pose = {kind: rig.matrix_world @ rig.pose.bones[name].matrix for kind, name in mapping.items()}
    bpy.context.scene.frame_set(last)
    travel = (rig.matrix_world @ rig.pose.bones[mapping["Pelvis"]].matrix).translation - first_pose["Pelvis"].translation
    basis = canonical_basis(first_pose, travel if kind != "Idle" else None)
    fps = bpy.context.scene.render.fps / bpy.context.scene.render.fps_base
    duration = (last - first) / fps
    frames = []
    pelvis_start = None
    ankles = {"Left": [], "Right": []}
    for frame in range(first, last + 1):
        bpy.context.scene.frame_set(frame)
        source = {kind: rig.matrix_world @ rig.pose.bones[name].matrix for kind, name in mapping.items()}
        pelvis = basis @ source["Pelvis"].translation * scale_mm
        if pelvis_start is None:
            pelvis_start = pelvis.copy()
        delta = pelvis - pelvis_start
        desired = {}
        rotations = []
        for joint in joints:
            kind_name = joint["kind"]
            rest = binds[kind_name].to_3x3()
            parent = joint["parentIndex"]
            parent_rotation = desired[joints[parent]["kind"]] if parent is not None else Matrix.Identity(3)
            rest_parent = binds[joints[parent]["kind"]].to_3x3() if parent is not None else Matrix.Identity(3)
            rest_local = rest_parent.inverted() @ rest
            if kind_name in mapping:
                motion = source[kind_name].to_3x3() @ source_rest[kind_name].to_3x3().inverted()
                desired[kind_name] = basis @ motion @ rest_basis.inverted() @ rest
                if kind_name.endswith("Clavicle"):
                    # Mixamo's shoulder anchor differs from our anatomical clavicle.
                    # Transport local link swing through the actual parent rest frame.
                    # Axial bone roll is not a clavicle direction change.
                    parent_kind = joints[parent]["kind"]
                    child = distal[kind_name]
                    source_parent_rest = source_rest[parent_kind].to_3x3()
                    source_from_target = source_parent_rest.inverted() @ rest_basis.inverted() @ rest_parent
                    before = source_parent_rest.inverted() @ (source_rest[child].translation - source_rest[kind_name].translation)
                    after = source[parent_kind].to_3x3().inverted() @ (source[child].translation - source[kind_name].translation)
                    before = source_from_target.inverted() @ before
                    after = source_from_target.inverted() @ after
                    swing = before.normalized().rotation_difference(after.normalized())
                    desired[kind_name] = parent_rotation @ swing.to_matrix() @ rest_local
                elif kind_name in distal:
                    if any(kind_name.endswith(stem) for stem in ("Shoulder", "Elbow", "Hip", "Knee", "Ankle")):
                        # Limb roll is rig-specific. Transport the anatomical link direction
                        # with the shortest swing under the posed parent, preserving our rest roll.
                        desired[kind_name] = parent_rotation @ rest_local
                    child = distal[kind_name]
                    rest_direction = rest.inverted() @ (binds[child].translation - binds[kind_name].translation)
                    current_direction = desired[kind_name] @ rest_direction
                    target_direction = basis.to_3x3() @ (source[child].translation - source[kind_name].translation)
                    correction = current_direction.normalized().rotation_difference(target_direction.normalized())
                    desired[kind_name] = correction.to_matrix() @ desired[kind_name]
                    if kind_name.endswith("Elbow"):
                        # Collapse source forearm roll into our forearm joint rather than
                        # turning pronation into a large wrist bend across unlike bone frames.
                        side = "Left" if kind_name.startswith("Left") else "Right"
                        fingers = [(basis @ (rig.matrix_world @ rig.pose.bones[name].matrix).translation)
                                   for name in hand_names[side]]
                        source_forearm = anatomical_hand_frame(basis @ source[kind_name].translation,
                            basis @ source[side + "Wrist"].translation, fingers[1], fingers[2])
                        desired[kind_name] = source_forearm @ target_forearms[side].inverted() @ rest
                elif kind_name.endswith("Wrist"):
                    side = "Left" if kind_name.startswith("Left") else "Right"
                    fingers = [(basis @ (rig.matrix_world @ rig.pose.bones[name].matrix).translation)
                               for name in hand_names[side]]
                    source_hand = anatomical_hand_frame(basis @ source[kind_name].translation, *fingers)
                    desired[kind_name] = source_hand @ target_hands[side].inverted() @ rest
                elif parent is not None and joints[parent]["kind"] in mapping:
                    # End joints retain their source local motion under the corrected parent swing.
                    parent_kind = joints[parent]["kind"]
                    source_local = source[parent_kind].to_3x3().inverted() @ source[kind_name].to_3x3()
                    source_local_rest = source_rest[parent_kind].to_3x3().inverted() @ source_rest[kind_name].to_3x3()
                    desired[kind_name] = parent_rotation @ rest_local @ (source_local_rest.inverted() @ source_local)
                pose_local = parent_rotation.inverted() @ desired[kind_name]
                rotation = (rest_local.inverted() @ pose_local).to_quaternion().normalized()
                rotations.append({"joint": kind_name, "localRotation": quaternion(rotation)})
            else:
                desired[kind_name] = parent_rotation @ rest_local
        # Horizontal forward travel belongs to the collision motor. Keep lateral sway and vertical bob in the pose.
        frames.append({"seconds": (frame - first) / fps, "rotations": rotations,
                       "offsetMm": vector(Vector((delta.x, 0, delta.z))), "rootDistance": delta.y / 1000,
                       "leftContact": kind == "Idle", "rightContact": kind == "Idle"})
        for side in ankles:
            ankles[side].append(basis @ source[side + "Ankle"].translation * (scale_mm / 1000))
    travel = frames[-1]["rootDistance"]
    if kind != "Idle" and travel <= .1:
        raise RuntimeError(f"{path}: forward-travelling locomotion required; got {travel} m")
    for side, positions in ankles.items():
        minimum = min(position.z for position in positions)
        for index, position in enumerate(positions):
            before = positions[max(0, index - 1)]
            after = positions[min(len(positions) - 1, index + 1)]
            interval = (min(len(positions) - 1, index + 1) - max(0, index - 1)) / fps
            horizontal_speed = Vector((after.x - before.x, after.y - before.y)).length / interval
            frames[index][side.lower() + "Contact"] = kind == "Idle" or (
                position.z <= minimum + .055 and horizontal_speed < max(.55, travel / duration * .4))
    seam_angle = max(2 * math.acos(min(1, abs(sum(
        frames[0]["rotations"][index]["localRotation"][axis] *
        frames[-1]["rotations"][index]["localRotation"][axis] for axis in ("x", "y", "z", "w")))))
        * 180 / math.pi for index in range(len(frames[0]["rotations"])))
    # Explicitly close the periodic pose; root distance retains its cumulative endpoint.
    frames[-1]["rotations"] = frames[0]["rotations"]
    frames[-1]["offsetMm"] = frames[0]["offsetMm"]
    frames[-1]["leftContact"] = frames[0]["leftContact"]
    frames[-1]["rightContact"] = frames[0]["rightContact"]
    return {"gait": kind, "id": Path(path).stem, "duration": duration, "frames": frames,
            "sourceSha256": sha(path), "sourceUnitsToMm": scale_mm,
            "sourcePoseSeamDegrees": seam_angle, "forwardTravelMetres": travel}


args = arguments()
body = json.loads(Path(args.body).read_text())
clips = [bake(path, gait, body) for path, gait in ((args.idle, "Idle"), (args.walk, "Walk"), (args.run, "Run"))]
result = {"schema": "aurelian.humanoid.locomotion.v1", "bodySha256": sha(args.body),
          "skeletonId": body["skeleton"]["skeletonId"], "restPoseId": body["skeleton"]["restPoseId"],
          "clips": clips}
out = Path(args.out)
out.parent.mkdir(parents=True, exist_ok=True)
out.write_text(json.dumps(result, separators=(",", ":")))
print("LOCOMOTION_BAKED " + json.dumps({"path": str(out), "clips": [
    {key: clip[key] for key in ("gait", "duration", "sourceUnitsToMm", "sourcePoseSeamDegrees", "forwardTravelMetres")}
    for clip in clips]}))
