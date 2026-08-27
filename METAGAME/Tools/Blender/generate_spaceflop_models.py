"""Generate Unity-ready SpaceFlop astronaut and capsule spaceship assets.

Run with Blender 5.x:
    blender --background --python Tools/Blender/generate_spaceflop_models.py

Outputs are deterministic and use only procedural geometry/materials, so there
are no external texture or font dependencies.
"""

from __future__ import annotations

import json
import math
import os
import sys
from pathlib import Path

import bpy
from mathutils import Vector


SCRIPT_PATH = Path(__file__).resolve()
PROJECT_ROOT = SCRIPT_PATH.parents[2]
SOURCE_ROOT = PROJECT_ROOT / "BlenderSource" / "SpaceFlop"
MODEL_ROOT = PROJECT_ROOT / "Assets" / "SpaceFlop" / "Art" / "Models"
CHARACTER_FBX = MODEL_ROOT / "Characters" / "SF_Astronaut_Base.fbx"
SHIP_FBX = MODEL_ROOT / "Spaceships" / "SF_Spaceship_Capsule.fbx"
CHARACTER_BLEND = SOURCE_ROOT / "SF_Astronaut_Base.blend"
SHIP_BLEND = SOURCE_ROOT / "SF_Spaceship_Capsule.blend"
PREVIEW_ROOT = SOURCE_ROOT / "Previews"
MANIFEST_PATH = SOURCE_ROOT / "model_manifest.json"

TAU = math.tau


def ensure_dirs() -> None:
    for path in (CHARACTER_FBX.parent, SHIP_FBX.parent, SOURCE_ROOT, PREVIEW_ROOT):
        path.mkdir(parents=True, exist_ok=True)


def clear_scene() -> None:
    bpy.ops.object.mode_set(mode="OBJECT") if bpy.context.object and bpy.context.object.mode != "OBJECT" else None
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (
        bpy.data.meshes,
        bpy.data.curves,
        bpy.data.armatures,
        bpy.data.cameras,
        bpy.data.lights,
        bpy.data.materials,
    ):
        for block in list(datablocks):
            if block.users == 0:
                datablocks.remove(block)


def collection(name: str) -> bpy.types.Collection:
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(c)
    return c


def move_to_collection(obj: bpy.types.Object, target: bpy.types.Collection) -> None:
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    target.objects.link(obj)


def set_active(obj: bpy.types.Object) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def smooth(obj: bpy.types.Object) -> None:
    if obj.type == "MESH":
        for polygon in obj.data.polygons:
            polygon.use_smooth = True


def apply_transform(obj: bpy.types.Object) -> None:
    set_active(obj)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)


def make_material(
    name: str,
    color: tuple[float, float, float, float],
    metallic: float = 0.0,
    roughness: float = 0.5,
    emission: tuple[float, float, float, float] | None = None,
    emission_strength: float = 0.0,
) -> bpy.types.Material:
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.diffuse_color = color
    mat.metallic = metallic
    mat.roughness = roughness
    # Blender may localize/rename the display name; the node type is stable.
    bsdf = next((node for node in mat.node_tree.nodes if node.type == "BSDF_PRINCIPLED"), None)
    if bsdf:
        bsdf.inputs["Base Color"].default_value = color
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
        if emission and "Emission Color" in bsdf.inputs:
            bsdf.inputs["Emission Color"].default_value = emission
            bsdf.inputs["Emission Strength"].default_value = emission_strength
    mat["unity_shader_hint"] = "Standard"
    return mat


def assign_material(obj: bpy.types.Object, mat: bpy.types.Material) -> None:
    if obj.data and hasattr(obj.data, "materials"):
        obj.data.materials.append(mat)


def bevel(obj: bpy.types.Object, width: float, segments: int = 3) -> None:
    modifier = obj.modifiers.new("Soft_Bevel", "BEVEL")
    modifier.width = width
    modifier.segments = segments
    modifier.limit_method = "ANGLE"
    set_active(obj)
    bpy.ops.object.modifier_apply(modifier=modifier.name)


def uv_sphere(
    name: str,
    location: tuple[float, float, float],
    radii: tuple[float, float, float],
    mat: bpy.types.Material,
    target_collection: bpy.types.Collection,
    segments: int = 32,
    rings: int = 20,
) -> bpy.types.Object:
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.scale = radii
    apply_transform(obj)
    smooth(obj)
    assign_material(obj, mat)
    move_to_collection(obj, target_collection)
    return obj


def rounded_cube(
    name: str,
    location: tuple[float, float, float],
    size: tuple[float, float, float],
    mat: bpy.types.Material,
    target_collection: bpy.types.Collection,
    bevel_width: float = 0.03,
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cube_add(location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.scale = (size[0] * 0.5, size[1] * 0.5, size[2] * 0.5)
    apply_transform(obj)
    if bevel_width > 0:
        bevel(obj, min(bevel_width, min(size) * 0.24), 3)
    smooth(obj)
    assign_material(obj, mat)
    move_to_collection(obj, target_collection)
    return obj


def cylinder(
    name: str,
    location: tuple[float, float, float],
    radius: float,
    depth: float,
    mat: bpy.types.Material,
    target_collection: bpy.types.Collection,
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
    vertices: int = 32,
    bevel_width: float = 0.015,
) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    apply_transform(obj)
    if bevel_width > 0:
        bevel(obj, bevel_width, 2)
    smooth(obj)
    assign_material(obj, mat)
    move_to_collection(obj, target_collection)
    return obj


def cone(
    name: str,
    location: tuple[float, float, float],
    radius1: float,
    radius2: float,
    depth: float,
    mat: bpy.types.Material,
    target_collection: bpy.types.Collection,
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
    vertices: int = 32,
) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices,
        radius1=radius1,
        radius2=radius2,
        depth=depth,
        location=location,
        rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name
    apply_transform(obj)
    bevel(obj, min(0.018, depth * 0.08), 2)
    smooth(obj)
    assign_material(obj, mat)
    move_to_collection(obj, target_collection)
    return obj


def torus(
    name: str,
    location: tuple[float, float, float],
    major_radius: float,
    minor_radius: float,
    mat: bpy.types.Material,
    target_collection: bpy.types.Collection,
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
    scale: tuple[float, float, float] = (1.0, 1.0, 1.0),
) -> bpy.types.Object:
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major_radius,
        minor_radius=minor_radius,
        major_segments=40,
        minor_segments=10,
        location=location,
        rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    apply_transform(obj)
    smooth(obj)
    assign_material(obj, mat)
    move_to_collection(obj, target_collection)
    return obj


def capsule_between(
    name: str,
    start: tuple[float, float, float],
    end: tuple[float, float, float],
    radius: float,
    mat: bpy.types.Material,
    target_collection: bpy.types.Collection,
) -> bpy.types.Object:
    a, b = Vector(start), Vector(end)
    delta = b - a
    length = delta.length
    midpoint = (a + b) * 0.5
    obj = uv_sphere(name, tuple(midpoint), (radius, radius, max(length * 0.5 + radius * 0.28, radius)), mat, target_collection, 24, 14)
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = Vector((0.0, 0.0, 1.0)).rotation_difference(delta.normalized())
    apply_transform(obj)
    return obj


def tube_curve(
    name: str,
    points: list[tuple[float, float, float]],
    radius: float,
    mat: bpy.types.Material,
    target_collection: bpy.types.Collection,
    cyclic: bool = False,
) -> bpy.types.Object:
    curve = bpy.data.curves.new(name + "_Curve", "CURVE")
    curve.dimensions = "3D"
    curve.resolution_u = 2
    curve.bevel_depth = radius
    curve.bevel_resolution = 3
    spline = curve.splines.new("BEZIER")
    spline.bezier_points.add(len(points) - 1)
    for bp, co in zip(spline.bezier_points, points):
        bp.co = co
        bp.handle_left_type = "AUTO"
        bp.handle_right_type = "AUTO"
    spline.use_cyclic_u = cyclic
    obj = bpy.data.objects.new(name, curve)
    target_collection.objects.link(obj)
    assign_material(obj, mat)
    set_active(obj)
    bpy.ops.object.convert(target="MESH")
    smooth(obj)
    return obj


def add_text_mesh(
    name: str,
    body: str,
    location: tuple[float, float, float],
    size: float,
    extrude: float,
    mat: bpy.types.Material,
    target_collection: bpy.types.Collection,
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
    align: str = "CENTER",
) -> bpy.types.Object:
    curve = bpy.data.curves.new(name + "_Font", "FONT")
    curve.body = body
    curve.align_x = align
    curve.align_y = "CENTER"
    curve.size = size
    curve.extrude = extrude
    curve.bevel_depth = extrude * 0.18
    obj = bpy.data.objects.new(name, curve)
    target_collection.objects.link(obj)
    obj.location = location
    obj.rotation_euler = rotation
    assign_material(obj, mat)
    set_active(obj)
    bpy.ops.object.convert(target="MESH")
    return obj


def parent_keep_world(child: bpy.types.Object, parent: bpy.types.Object) -> None:
    world = child.matrix_world.copy()
    child.parent = parent
    child.matrix_world = world


def bind_rigid(obj: bpy.types.Object, armature: bpy.types.Object, bone_name: str) -> None:
    if obj.type != "MESH":
        return
    group = obj.vertex_groups.new(name=bone_name)
    group.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")
    modifier = obj.modifiers.new("Armature_Deform", "ARMATURE")
    modifier.object = armature
    obj["bound_bone"] = bone_name


def create_astronaut_armature(target_collection: bpy.types.Collection) -> bpy.types.Object:
    data = bpy.data.armatures.new("SF_Astronaut_Skeleton")
    rig = bpy.data.objects.new("SF_Astronaut_Rig", data)
    target_collection.objects.link(rig)
    rig.show_in_front = True
    rig["unity_rig_type"] = "Generic"
    rig["character_height_m"] = 1.33
    rig["forward_axis_blender"] = "-Y"
    rig["forward_axis_unity"] = "+Z"
    set_active(rig)
    bpy.ops.object.mode_set(mode="EDIT")

    specs = {
        "Root": ((0, 0, 0.00), (0, 0, 0.16), None),
        "Hips": ((0, 0, 0.45), (0, 0, 0.59), "Root"),
        "Spine": ((0, 0, 0.56), (0, 0, 0.69), "Hips"),
        "Chest": ((0, 0, 0.67), (0, 0, 0.82), "Spine"),
        "Neck": ((0, 0, 0.81), (0, 0, 0.89), "Chest"),
        "Head": ((0, 0, 0.87), (0, 0, 1.18), "Neck"),
        "LeftUpperArm": ((-0.20, 0, 0.77), (-0.40, 0, 0.64), "Chest"),
        "LeftLowerArm": ((-0.40, 0, 0.64), (-0.57, 0, 0.52), "LeftUpperArm"),
        "LeftHand": ((-0.57, 0, 0.52), (-0.67, 0, 0.45), "LeftLowerArm"),
        "RightUpperArm": ((0.20, 0, 0.77), (0.40, 0, 0.64), "Chest"),
        "RightLowerArm": ((0.40, 0, 0.64), (0.57, 0, 0.52), "RightUpperArm"),
        "RightHand": ((0.57, 0, 0.52), (0.67, 0, 0.45), "RightLowerArm"),
        "LeftUpperLeg": ((-0.10, 0, 0.50), (-0.10, 0, 0.31), "Hips"),
        "LeftLowerLeg": ((-0.10, 0, 0.31), (-0.10, 0, 0.12), "LeftUpperLeg"),
        "LeftFoot": ((-0.10, 0, 0.12), (-0.10, -0.12, 0.055), "LeftLowerLeg"),
        "RightUpperLeg": ((0.10, 0, 0.50), (0.10, 0, 0.31), "Hips"),
        "RightLowerLeg": ((0.10, 0, 0.31), (0.10, 0, 0.12), "RightUpperLeg"),
        "RightFoot": ((0.10, 0, 0.12), (0.10, -0.12, 0.055), "RightLowerLeg"),
    }
    created: dict[str, bpy.types.EditBone] = {}
    for name, (head, tail, _) in specs.items():
        bone = data.edit_bones.new(name)
        bone.head = head
        bone.tail = tail
        created[name] = bone
    for name, (_, _, parent_name) in specs.items():
        if parent_name:
            created[name].parent = created[parent_name]
            created[name].use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    return rig


def build_astronaut() -> dict:
    clear_scene()
    visual = collection("ASTRONAUT_VISUAL")
    stage = collection("PRESENTATION")

    suit = make_material("MAT_Astro_Suit_Ivory", (0.91, 0.92, 0.90, 1), 0.0, 0.62)
    suit_shadow = make_material("MAT_Astro_Suit_Gray", (0.48, 0.52, 0.57, 1), 0.15, 0.48)
    accent = make_material("MAT_Astro_Accent_Orange", (0.92, 0.31, 0.08, 1), 0.05, 0.45)
    accent_dark = make_material("MAT_Astro_Accent_Dark", (0.42, 0.11, 0.04, 1), 0.08, 0.55)
    rubber = make_material("MAT_Astro_Rubber", (0.055, 0.065, 0.085, 1), 0.0, 0.4)
    visor = make_material("MAT_Astro_Visor", (0.018, 0.025, 0.04, 1), 0.55, 0.12)
    glass_trim = make_material("MAT_Astro_Glass_Trim", (0.18, 0.55, 0.72, 1), 0.65, 0.18)
    light = make_material("MAT_Astro_Display_Light", (0.58, 0.92, 1.0, 1), 0.0, 0.24, (0.4, 0.8, 1, 1), 2.5)
    green = make_material("MAT_Astro_Status_Green", (0.14, 0.75, 0.35, 1), 0.0, 0.28, (0.1, 0.8, 0.25, 1), 1.8)
    yellow = make_material("MAT_Astro_Status_Yellow", (0.98, 0.65, 0.12, 1), 0.0, 0.3, (1.0, 0.4, 0.05, 1), 1.4)

    rig = create_astronaut_armature(visual)
    parts: list[tuple[bpy.types.Object, str]] = []

    def part(obj: bpy.types.Object, bone: str, role: str) -> bpy.types.Object:
        obj["asset_role"] = role
        parts.append((obj, bone))
        return obj

    # Core proportions match the current runtime ragdoll constants (1.33 m tall).
    part(uv_sphere("Body_Hips", (0, 0, 0.50), (0.19, 0.145, 0.13), suit, visual), "Hips", "body")
    part(uv_sphere("Body_Chest", (0, 0, 0.69), (0.225, 0.175, 0.19), suit, visual), "Chest", "body")
    part(torus("Waist_Seal", (0, 0, 0.555), 0.155, 0.025, accent, visual, scale=(1.0, 0.84, 1.0)), "Spine", "detail")
    part(torus("Neck_Seal", (0, 0, 0.825), 0.17, 0.028, suit_shadow, visual, scale=(1.0, 0.90, 1.0)), "Neck", "detail")

    # Helmet, black display visor, friendly face and antenna.
    part(uv_sphere("Helmet_Shell", (0, 0.01, 1.02), (0.325, 0.305, 0.31), suit, visual, 40, 24), "Head", "helmet")
    part(uv_sphere("Helmet_Rear_Cap", (0, 0.12, 1.02), (0.29, 0.20, 0.27), suit_shadow, visual), "Head", "helmet_detail")
    part(uv_sphere("Visor_Black", (0, -0.276, 1.025), (0.245, 0.050, 0.205), visor, visual, 40, 24), "Head", "visor")
    part(torus("Visor_Rim", (0, -0.318, 1.025), 0.225, 0.022, glass_trim, visual, rotation=(math.radians(90), 0, 0), scale=(1.0, 1.0, 0.84)), "Head", "visor_rim")
    for side, x in (("L", -0.070), ("R", 0.070)):
        part(uv_sphere(f"Face_Eye_{side}", (x, -0.333, 1.055), (0.026, 0.012, 0.044), light, visual, 20, 12), "Head", "face")
        part(uv_sphere(f"Face_Eye_Glint_{side}", (x - 0.008, -0.345, 1.071), (0.006, 0.006, 0.010), suit, visual, 16, 8), "Head", "face")
    part(tube_curve("Face_Smile", [(-0.040, -0.339, 0.988), (0, -0.345, 0.975), (0.040, -0.339, 0.988)], 0.007, light, visual), "Head", "face")
    part(capsule_between("Helmet_Antenna_Stem", (0.195, 0.02, 1.25), (0.225, 0.02, 1.40), 0.014, rubber, visual), "Head", "antenna")
    part(uv_sphere("Helmet_Antenna_Lamp", (0.227, 0.02, 1.405), (0.035, 0.035, 0.035), accent, visual, 20, 12), "Head", "antenna")
    part(rounded_cube("Helmet_Side_Module_L", (-0.303, -0.015, 1.035), (0.055, 0.16, 0.14), suit_shadow, visual, 0.02), "Head", "helmet_detail")
    part(rounded_cube("Helmet_Side_Module_R", (0.303, -0.015, 1.035), (0.055, 0.16, 0.14), suit_shadow, visual, 0.02), "Head", "helmet_detail")

    # A-pose limbs: rounded, segmented, and rigid-weighted for stable Unity deformation.
    arm_defs = {
        "L": ((-0.20, 0, 0.76), (-0.40, 0, 0.64), (-0.57, 0, 0.52), (-0.65, -0.005, 0.46), "Left"),
        "R": ((0.20, 0, 0.76), (0.40, 0, 0.64), (0.57, 0, 0.52), (0.65, -0.005, 0.46), "Right"),
    }
    for side, (shoulder, elbow, wrist, hand, prefix) in arm_defs.items():
        part(uv_sphere(f"Shoulder_Pad_{side}", shoulder, (0.115, 0.125, 0.115), accent, visual), "Chest", "armor")
        part(capsule_between(f"UpperArm_{side}", shoulder, elbow, 0.077, suit, visual), f"{prefix}UpperArm", "limb")
        part(capsule_between(f"LowerArm_{side}", elbow, wrist, 0.070, suit_shadow, visual), f"{prefix}LowerArm", "limb")
        part(uv_sphere(f"Elbow_Joint_{side}", elbow, (0.080, 0.080, 0.080), accent_dark, visual), f"{prefix}LowerArm", "joint")
        part(uv_sphere(f"Glove_{side}", hand, (0.095, 0.085, 0.095), rubber, visual), f"{prefix}Hand", "hand")
        part(torus(f"Wrist_Ring_{side}", tuple(wrist), 0.071, 0.014, accent, visual, rotation=(0, math.radians(56 if side == 'L' else -56), 0)), f"{prefix}LowerArm", "detail")

    leg_defs = {"L": (-0.10, "Left"), "R": (0.10, "Right")}
    for side, (x, prefix) in leg_defs.items():
        part(capsule_between(f"Thigh_{side}", (x, 0, 0.49), (x, 0, 0.31), 0.092, suit, visual), f"{prefix}UpperLeg", "limb")
        part(uv_sphere(f"Knee_Pad_{side}", (x, -0.055, 0.305), (0.090, 0.075, 0.075), accent, visual), f"{prefix}LowerLeg", "armor")
        part(capsule_between(f"Shin_{side}", (x, 0, 0.29), (x, 0, 0.13), 0.078, suit_shadow, visual), f"{prefix}LowerLeg", "limb")
        part(rounded_cube(f"Boot_{side}", (x, -0.035, 0.07), (0.17, 0.25, 0.13), rubber, visual, 0.04), f"{prefix}Foot", "boot")
        part(rounded_cube(f"Boot_Sole_{side}", (x, -0.045, 0.018), (0.18, 0.27, 0.045), accent_dark, visual, 0.015), f"{prefix}Foot", "boot")

    # Chest controls, backpack and life-support details.
    part(rounded_cube("Chest_Control_Box", (0, -0.178, 0.68), (0.225, 0.055, 0.145), rubber, visual, 0.018), "Chest", "control_panel")
    for idx, (x, mat) in enumerate(((-0.060, green), (0.000, yellow), (0.060, accent))):
        part(uv_sphere(f"Chest_Status_{idx+1}", (x, -0.211, 0.710), (0.017, 0.008, 0.017), mat, visual, 16, 8), "Chest", "indicator")
    part(rounded_cube("Chest_Nameplate", (0, -0.210, 0.650), (0.135, 0.012, 0.028), glass_trim, visual, 0.005), "Chest", "nameplate")
    label = add_text_mesh("Chest_Label_01", "01", (0, -0.220, 0.650), 0.025, 0.003, light, visual, rotation=(math.radians(90), 0, 0))
    part(label, "Chest", "nameplate")
    part(rounded_cube("Backpack_Main", (0, 0.195, 0.69), (0.34, 0.19, 0.35), rubber, visual, 0.035), "Chest", "backpack")
    for side, x in (("L", -0.105), ("R", 0.105)):
        part(cylinder(f"Backpack_Tank_{side}", (x, 0.305, 0.70), 0.060, 0.29, suit_shadow, visual), "Chest", "oxygen_tank")
        part(cone(f"Backpack_Thruster_{side}", (x, 0.305, 0.525), 0.050, 0.028, 0.080, rubber, visual), "Chest", "thruster")
    part(tube_curve("Backpack_Hose_L", [(-0.11, 0.24, 0.74), (-0.24, 0.16, 0.69), (-0.23, -0.02, 0.62)], 0.016, accent_dark, visual), "Chest", "hose")
    part(tube_curve("Backpack_Hose_R", [(0.11, 0.24, 0.74), (0.24, 0.16, 0.69), (0.23, -0.02, 0.62)], 0.016, accent_dark, visual), "Chest", "hose")

    for obj, bone_name in parts:
        bind_rigid(obj, rig, bone_name)

    # Helpful LOD/group metadata for later prefab tooling.
    for obj, _ in parts:
        obj["lod_group"] = "LOD0"
        obj["units"] = "meters"

    setup_presentation(stage, target=(0, 0, 0.70), camera=(2.25, -3.15, 1.75), focal_length=58, ground_size=5.5)
    configure_render(PREVIEW_ROOT / "SF_Astronaut_Base.png", 900, 1100)
    bpy.context.scene["asset_name"] = "SF_Astronaut_Base"
    bpy.context.scene["asset_version"] = "1.0"
    bpy.context.scene["unity_scale"] = 1.0
    bpy.ops.wm.save_as_mainfile(filepath=str(CHARACTER_BLEND))
    bpy.ops.render.render(write_still=True)
    export_fbx(CHARACTER_FBX, list(visual.objects), armature=rig)
    return asset_stats("SF_Astronaut_Base", visual, CHARACTER_BLEND, CHARACTER_FBX, (0.70, 0.62, 1.42))


def build_ship() -> dict:
    clear_scene()
    visual = collection("SPACESHIP_VISUAL")
    stage = collection("PRESENTATION")

    hull = make_material("MAT_Ship_Hull_Ivory", (0.53, 0.49, 0.42, 1), 0.52, 0.48)
    hull_light = make_material("MAT_Ship_Hull_Light", (0.72, 0.68, 0.59, 1), 0.45, 0.42)
    hull_dark = make_material("MAT_Ship_Hull_Dark", (0.16, 0.17, 0.19, 1), 0.72, 0.33)
    orange = make_material("MAT_Ship_Repair_Orange", (0.72, 0.20, 0.055, 1), 0.52, 0.46)
    orange_dark = make_material("MAT_Ship_Repair_Orange_Dark", (0.30, 0.075, 0.025, 1), 0.55, 0.58)
    brass = make_material("MAT_Ship_Brass", (0.58, 0.34, 0.10, 1), 0.82, 0.32)
    window = make_material("MAT_Ship_Window_Blue", (0.055, 0.25, 0.42, 1), 0.65, 0.12, (0.02, 0.18, 0.34, 1), 0.55)
    window_glow = make_material("MAT_Ship_Window_Glint", (0.28, 0.73, 1.0, 1), 0.15, 0.18, (0.1, 0.52, 1.0, 1), 2.5)
    green = make_material("MAT_Ship_Status_Green", (0.13, 0.85, 0.38, 1), 0.05, 0.25, (0.05, 0.8, 0.22, 1), 2.3)
    red = make_material("MAT_Ship_Status_Red", (0.95, 0.10, 0.05, 1), 0.05, 0.28, (1.0, 0.03, 0.01, 1), 2.0)

    root = bpy.data.objects.new("SF_Spaceship_Capsule_ROOT", None)
    visual.objects.link(root)
    root.empty_display_type = "PLAIN_AXES"
    root["asset_type"] = "repairable_spaceship"
    root["units"] = "meters"
    root["forward_axis_blender"] = "-Y"
    root["forward_axis_unity"] = "+Z"
    root["recommended_main_collider"] = "CapsuleCollider center(0,2.0,0) radius1.42 height3.35"

    parts: list[bpy.types.Object] = []

    def ship_part(obj: bpy.types.Object, role: str, repair_id: str | None = None) -> bpy.types.Object:
        obj["asset_role"] = role
        if repair_id:
            obj["repair_socket_id"] = repair_id
            obj["repairable"] = True
        parent_keep_world(obj, root)
        parts.append(obj)
        return obj

    # Main egg hull and layered armor bands.
    ship_part(uv_sphere("Hull_Main", (0, 0, 2.02), (1.44, 1.32, 1.62), hull, visual, 48, 32), "hull")
    ship_part(uv_sphere("Hull_Lower_Armor", (0, 0.08, 1.08), (1.20, 1.12, 0.56), hull_dark, visual, 40, 24), "armor")
    ship_part(torus("Hull_Band_Upper", (0, 0, 2.62), 1.20, 0.075, orange, visual, scale=(1.05, 0.96, 1.0)), "armor_band", "HULL_BAND")
    ship_part(torus("Hull_Band_Lower", (0, 0, 1.39), 1.20, 0.060, hull_dark, visual, scale=(1.02, 0.94, 1.0)), "armor_band")
    ship_part(torus("Hull_Seam_Mid", (0, 0, 2.00), 1.39, 0.023, brass, visual, scale=(1.0, 0.91, 1.0)), "panel_seam")

    # Large forward porthole; front is Blender -Y and arrives as Unity +Z.
    front_rot = (math.radians(90), 0, 0)
    ship_part(cylinder("Porthole_Glass", (0, -1.306, 2.22), 0.59, 0.075, window, visual, front_rot, 48, 0.018), "window", "PORTHOLE")
    ship_part(torus("Porthole_Frame_Outer", (0, -1.375, 2.22), 0.66, 0.105, hull_dark, visual, front_rot, scale=(1.0, 1.0, 0.92)), "window_frame")
    ship_part(torus("Porthole_Frame_Inner", (0, -1.440, 2.22), 0.55, 0.040, brass, visual, front_rot, scale=(1.0, 1.0, 0.92)), "window_frame")
    ship_part(uv_sphere("Porthole_Glint", (-0.23, -1.402, 2.43), (0.14, 0.025, 0.07), window_glow, visual, 20, 12), "window_glint")
    for i in range(12):
        angle = TAU * i / 12
        x = math.cos(angle) * 0.73
        z = 2.22 + math.sin(angle) * 0.67
        ship_part(uv_sphere(f"Porthole_Bolt_{i+1:02d}", (x, -1.455, z), (0.038, 0.025, 0.038), brass, visual, 16, 8), "fastener")

    # Top dome, service hatch and beacon.
    ship_part(uv_sphere("Top_Dome", (0, 0.02, 3.45), (0.78, 0.72, 0.46), orange, visual, 36, 20), "top_hatch", "TOP_HATCH")
    ship_part(torus("Top_Hatch_Ring", (0, 0.02, 3.31), 0.69, 0.075, hull_dark, visual, scale=(1.0, 0.93, 1.0)), "top_hatch_frame")
    ship_part(cylinder("Top_Antenna_Base", (0, 0.02, 3.92), 0.16, 0.16, hull_dark, visual), "antenna")
    ship_part(capsule_between("Top_Antenna_Mast", (0, 0.02, 3.95), (0.08, 0.02, 4.34), 0.025, brass, visual), "antenna", "ANTENNA")
    ship_part(uv_sphere("Top_Beacon", (0.085, 0.02, 4.37), (0.075, 0.075, 0.075), red, visual, 20, 12), "beacon")
    ship_part(tube_curve("Top_Antenna_Loop", [(-0.18, 0.03, 3.93), (0, 0.03, 4.12), (0.18, 0.03, 3.93)], 0.022, brass, visual), "antenna")

    # Side hatch and rotating locking wheel.
    side_rot = (0, math.radians(90), 0)
    ship_part(cylinder("Side_Hatch_Door", (1.355, 0.06, 2.10), 0.48, 0.10, orange, visual, side_rot, 40, 0.018), "hatch", "SIDE_HATCH")
    ship_part(torus("Side_Hatch_Frame", (1.425, 0.06, 2.10), 0.52, 0.075, hull_dark, visual, side_rot), "hatch_frame")
    ship_part(torus("Side_Hatch_Wheel", (1.485, -0.02, 2.10), 0.19, 0.030, brass, visual, side_rot), "hatch_handle")
    for i in range(6):
        angle = TAU * i / 6
        ship_part(capsule_between(f"Hatch_Wheel_Spoke_{i+1}", (1.49, -0.02, 2.10), (1.49, -0.02 + math.cos(angle) * 0.17, 2.10 + math.sin(angle) * 0.17), 0.012, brass, visual), "hatch_handle")

    # Patchwork armor plates give the handmade, repair-focused silhouette.
    plate_specs = [
        ("Plate_Front_L", (-0.82, -1.02, 2.78), (0.48, 0.06, 0.38), (math.radians(12), 0, math.radians(-12)), hull_light),
        ("Plate_Front_R", (0.86, -0.98, 2.73), (0.42, 0.06, 0.34), (math.radians(10), 0, math.radians(15)), orange_dark),
        ("Plate_Lower_L", (-0.72, -1.02, 1.35), (0.52, 0.06, 0.32), (math.radians(-12), 0, math.radians(8)), orange),
        ("Plate_Lower_R", (0.71, -1.05, 1.32), (0.46, 0.06, 0.28), (math.radians(-10), 0, math.radians(-9)), hull_light),
        ("Plate_Side_L", (-1.35, -0.20, 2.42), (0.05, 0.55, 0.42), (0, math.radians(8), math.radians(4)), hull_light),
    ]
    for idx, (name, loc, size, rot, mat) in enumerate(plate_specs):
        ship_part(rounded_cube(name, loc, size, mat, visual, 0.035, rot), "repair_plate", f"PLATE_{idx+1:02d}")

    # Small fasteners across the front patch plates.
    rivet_coords = [
        (-1.02, -1.07, 2.90), (-0.62, -1.12, 2.90), (-1.02, -1.08, 2.64), (-0.62, -1.13, 2.64),
        (0.70, -1.10, 2.84), (1.00, -1.04, 2.82), (0.70, -1.13, 2.60), (1.00, -1.06, 2.60),
        (-0.94, -1.10, 1.46), (-0.52, -1.15, 1.45), (-0.94, -1.08, 1.24), (-0.52, -1.13, 1.23),
    ]
    for i, loc in enumerate(rivet_coords):
        ship_part(uv_sphere(f"Hull_Rivet_{i+1:02d}", loc, (0.035, 0.020, 0.035), brass, visual, 16, 8), "fastener")

    # Engines: one main bell and two attitude/side pods.
    ship_part(cylinder("Engine_Main_Collar", (0, 0.10, 0.51), 0.48, 0.28, hull_dark, visual), "engine_mount")
    ship_part(cone("Engine_Main_Bell", (0, 0.10, 0.19), 0.43, 0.27, 0.54, hull_dark, visual), "engine", "MAIN_ENGINE")
    ship_part(torus("Engine_Main_Rim", (0, 0.10, -0.08), 0.40, 0.045, orange, visual), "engine_rim")
    for side, x in (("L", -1.29), ("R", 1.29)):
        ship_part(cylinder(f"RCS_Pod_{side}", (x, 0.25, 1.45), 0.23, 0.50, hull_dark, visual, side_rot), "rcs_mount")
        ship_part(cone(f"RCS_Nozzle_{side}", (x + (-0.26 if side == 'L' else 0.26), 0.25, 1.45), 0.19, 0.11, 0.32, orange_dark, visual, side_rot), "rcs_thruster", f"RCS_{side}")

    # Four landing legs. Each pad and strut remains separately selectable.
    leg_specs = [
        ("FL", (-0.82, -0.65, 0.72), (-1.26, -1.02, 0.08)),
        ("FR", (0.82, -0.65, 0.72), (1.26, -1.02, 0.08)),
        ("BL", (-0.82, 0.58, 0.72), (-1.24, 0.92, 0.08)),
        ("BR", (0.82, 0.58, 0.72), (1.24, 0.92, 0.08)),
    ]
    for name, start, end in leg_specs:
        ship_part(capsule_between(f"Landing_Strut_{name}", start, end, 0.075, hull_dark, visual), "landing_strut", f"LANDING_{name}")
        ship_part(rounded_cube(f"Landing_Foot_{name}", end, (0.44, 0.34, 0.12), hull_dark, visual, 0.035), "landing_foot")
        ship_part(capsule_between(f"Landing_Piston_{name}", tuple(Vector(start) * 0.75 + Vector(end) * 0.25), tuple(Vector(start) * 0.25 + Vector(end) * 0.75), 0.035, brass, visual), "landing_piston")

    # Pipes, hoses, sockets and status lamps communicate repair gameplay.
    ship_part(tube_curve("Hull_Hose_Left", [(-1.10, -0.68, 2.74), (-1.35, -0.25, 2.43), (-1.34, 0.24, 2.06), (-1.12, 0.58, 1.72)], 0.045, orange_dark, visual), "hose", "COOLANT_HOSE")
    ship_part(tube_curve("Hull_Hose_Front", [(0.54, -1.18, 1.04), (0.76, -1.12, 0.86), (0.52, -0.91, 0.66), (0.30, -0.80, 0.57)], 0.035, brass, visual), "hose", "FUEL_LINE")
    socket_specs = [
        ("POWER", (-1.10, -0.82, 1.78), green),
        ("FUEL", (1.02, -0.82, 1.56), red),
        ("OXYGEN", (-0.97, -0.85, 2.22), window_glow),
    ]
    for i, (label_text, loc, mat) in enumerate(socket_specs):
        ship_part(cylinder(f"Repair_Socket_{label_text}", loc, 0.115, 0.08, hull_dark, visual, front_rot, 28, 0.012), "repair_socket", label_text)
        ship_part(torus(f"Repair_Socket_Ring_{label_text}", (loc[0], loc[1] - 0.06, loc[2]), 0.105, 0.025, mat, visual, front_rot), "repair_socket_light")
        for k in range(3):
            x = loc[0] + (k - 1) * 0.045
            ship_part(uv_sphere(f"Repair_Pin_{label_text}_{k+1}", (x, loc[1] - 0.105, loc[2]), (0.012, 0.010, 0.012), brass, visual, 12, 6), "connector_pin")

    # Front nameplate and identification marking.
    ship_part(rounded_cube("Ship_Nameplate", (0.72, -1.23, 3.08), (0.45, 0.045, 0.17), hull_dark, visual, 0.018, (math.radians(8), 0, 0)), "nameplate")
    text_obj = add_text_mesh("Ship_Label_SF01", "SF-01", (0.72, -1.275, 3.085), 0.11, 0.008, hull_light, visual, rotation=(math.radians(90), 0, 0))
    ship_part(text_obj, "nameplate")

    # Add geometry-only collider guides to the .blend, excluded from Unity FBX.
    guides = collection("UNITY_COLLIDER_GUIDES_NOT_EXPORTED")
    guide_mat = make_material("MAT_Collider_Guide", (0.08, 0.9, 0.35, 0.18), 0.0, 0.8)
    guide_hull = uv_sphere("COL_Hull_Capsule_Guide", (0, 0, 2.0), (1.42, 1.30, 1.64), guide_mat, guides, 16, 10)
    guide_hull.display_type = "WIRE"
    guide_hull.hide_render = True
    guide_hull["unity_component"] = "CapsuleCollider"
    for name, _, end in leg_specs:
        guide = rounded_cube(f"COL_LandingFoot_{name}", end, (0.46, 0.36, 0.14), guide_mat, guides, 0.02)
        guide.display_type = "WIRE"
        guide.hide_render = True
        guide["unity_component"] = "BoxCollider"

    setup_presentation(stage, target=(0, 0, 1.95), camera=(5.6, -7.6, 4.7), focal_length=58, ground_size=10)
    configure_render(PREVIEW_ROOT / "SF_Spaceship_Capsule.png", 1100, 1100)
    bpy.context.scene["asset_name"] = "SF_Spaceship_Capsule"
    bpy.context.scene["asset_version"] = "1.0"
    bpy.context.scene["unity_scale"] = 1.0
    bpy.ops.wm.save_as_mainfile(filepath=str(SHIP_BLEND))
    bpy.ops.render.render(write_still=True)
    export_fbx(SHIP_FBX, [root] + parts)
    return asset_stats("SF_Spaceship_Capsule", visual, SHIP_BLEND, SHIP_FBX, (3.25, 3.0, 4.45))


def setup_presentation(
    target_collection: bpy.types.Collection,
    target: tuple[float, float, float],
    camera: tuple[float, float, float],
    focal_length: float,
    ground_size: float,
) -> None:
    ground_mat = make_material("MAT_Preview_Ground", (0.055, 0.065, 0.085, 1), 0.15, 0.66)
    bpy.ops.mesh.primitive_plane_add(size=ground_size, location=(0, 0, -0.09))
    ground = bpy.context.object
    ground.name = "Preview_Ground"
    assign_material(ground, ground_mat)
    move_to_collection(ground, target_collection)

    bpy.ops.object.camera_add(location=camera)
    cam = bpy.context.object
    cam.name = "Preview_Camera"
    cam.data.lens = focal_length
    cam.data.sensor_width = 36
    point_camera(cam, Vector(target))
    move_to_collection(cam, target_collection)
    bpy.context.scene.camera = cam

    lights = [
        ("Key_Light", (3.5, -4.5, 6.0), 220, 4.0, (1.0, 0.68, 0.42)),
        ("Fill_Light", (-4.0, -2.0, 3.5), 130, 3.5, (0.28, 0.52, 1.0)),
        ("Rim_Light", (1.0, 4.0, 5.2), 180, 3.0, (0.58, 0.34, 1.0)),
    ]
    for name, loc, energy, size, color in lights:
        bpy.ops.object.light_add(type="AREA", location=loc)
        light_obj = bpy.context.object
        light_obj.name = name
        light_obj.data.energy = energy
        light_obj.data.shape = "DISK"
        light_obj.data.size = size
        light_obj.data.color = color
        point_camera(light_obj, Vector(target))
        move_to_collection(light_obj, target_collection)


def point_camera(obj: bpy.types.Object, target: Vector) -> None:
    direction = target - obj.location
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def configure_render(output: Path, width: int, height: int) -> None:
    scene = bpy.context.scene
    # Blender 5.2 exposes Eevee as BLENDER_EEVEE (4.x used EEVEE_NEXT).
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = width
    scene.render.resolution_y = height
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = False
    scene.render.filepath = str(output)
    scene.render.image_settings.color_depth = "8"
    scene.world.color = (0.006, 0.008, 0.02)
    scene.view_settings.look = "AgX - Medium High Contrast"
    scene.view_settings.exposure = -0.65


def export_fbx(path: Path, objects: list[bpy.types.Object], armature: bpy.types.Object | None = None) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    exportable = []
    for obj in objects:
        if obj and obj.name in bpy.context.view_layer.objects and obj.type in {"MESH", "ARMATURE", "EMPTY"}:
            obj.select_set(True)
            exportable.append(obj)
    if armature:
        bpy.context.view_layer.objects.active = armature
    elif exportable:
        bpy.context.view_layer.objects.active = exportable[0]
    bpy.ops.export_scene.fbx(
        filepath=str(path),
        use_selection=True,
        object_types={"ARMATURE", "MESH", "EMPTY"},
        apply_scale_options="FBX_SCALE_UNITS",
        use_space_transform=True,
        bake_space_transform=False,
        axis_forward="-Z",
        axis_up="Y",
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        use_tspace=True,
        add_leaf_bones=False,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
        use_armature_deform_only=True,
        bake_anim=False,
        path_mode="AUTO",
        use_custom_props=True,
    )


def asset_stats(
    name: str,
    source_collection: bpy.types.Collection,
    blend: Path,
    fbx: Path,
    dimensions_m: tuple[float, float, float],
) -> dict:
    meshes = [o for o in source_collection.all_objects if o.type == "MESH"]
    triangles = 0
    vertices = 0
    for obj in meshes:
        vertices += len(obj.data.vertices)
        triangles += sum(len(poly.vertices) - 2 for poly in obj.data.polygons)
    return {
        "name": name,
        "dimensions_m_xyz": dimensions_m,
        "mesh_objects": len(meshes),
        "vertices_source": vertices,
        "triangles_source": triangles,
        "materials": len({slot.material.name for obj in meshes for slot in obj.material_slots if slot.material}),
        "blend": str(blend.relative_to(PROJECT_ROOT)).replace("\\", "/"),
        "fbx": str(fbx.relative_to(PROJECT_ROOT)).replace("\\", "/"),
        "preview": f"BlenderSource/SpaceFlop/Previews/{name}.png",
    }


def main() -> None:
    ensure_dirs()
    print(f"[SpaceFlop] Blender {bpy.app.version_string}")
    print(f"[SpaceFlop] Project root: {PROJECT_ROOT}")
    character = build_astronaut()
    print("[SpaceFlop] Character complete:", character)
    ship = build_ship()
    print("[SpaceFlop] Ship complete:", ship)
    manifest = {
        "generator": str(SCRIPT_PATH.relative_to(PROJECT_ROOT)).replace("\\", "/"),
        "blender_version": bpy.app.version_string,
        "units": "meters",
        "unity_editor": "6000.5.4f1",
        "assets": [character, ship],
    }
    MANIFEST_PATH.write_text(json.dumps(manifest, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"[SpaceFlop] Manifest: {MANIFEST_PATH}")


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print(f"[SpaceFlop] ERROR: {exc}", file=sys.stderr)
        raise
