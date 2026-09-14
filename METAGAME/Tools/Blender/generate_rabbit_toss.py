"""Author the two-handed upward toss action on the existing rabbit rig.

Run from the repository root with Blender 5.x:

    blender.exe --background METAGAME/BlenderSource/SpaceFlop/SF_Rabbit_Base.blend \
        --python METAGAME/Tools/Blender/generate_rabbit_toss.py -- --preview <dir>

Output: Assets/_Project/Animations/Characters/Rabbit/Source/Rabbit_TossUp.fbx
        (+ preview frames when --preview is given)

The action is written here rather than stored in the .blend so the motion stays
a reviewable text diff instead of a binary blob. Unity turns the FBX into
Resources/RabbitLocomotion/Rabbit_TossUp.anim via
Metagame.EditorTools.RabbitTossAssets.

Bone local axes were measured, not guessed:
    right arm  +X = forward, +Z = down      left arm  +X = forward, +Z = up
    spine      +X = lean back, +Y = twist   legs      +X = forward, calf -X = knee
Only rotation is keyed. RabbitLocomotion ignores animated positions and uses
each bone's rest position instead, so position keys would be dead weight.

Only Blender's own Python API is used - no extra packages.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy
from mathutils import Euler, Vector

D = math.radians
SCRIPT = Path(__file__).resolve()
REPO_ROOT = SCRIPT.parents[3]
FBX_OUT = (REPO_ROOT / "METAGAME" / "Assets" / "_Project" / "Animations"
           / "Characters" / "Rabbit" / "Source" / "Rabbit_TossUp.fbx")

ACTION = "Rabbit_TossUp"
FRAMES = [1, 8, 14, 18, 26]          # grip . lift . heave . follow-through . settle
FAST_FROM = 8                        # linear from here: the heave must be quick

# The power comes from the legs. The arms only carry.
POSES = {
    "Waist":      [(-22, 0, 0), (-10, 0, 0), (  6, 0, 0), (-10, 0, 0), (0, 0, 0)],
    "Spine01":    [(-14, 0, 0), ( -6, 0, 0), (  5, 0, 0), ( -7, 0, 0), (0, 0, 0)],
    "Spine02":    [( -8, 0, 0), ( -4, 0, 0), (  3, 0, 0), ( -4, 0, 0), (0, 0, 0)],
    "Head":       [( 26, 0, 0), (  8, 0, 0), (-14, 0, 0), ( -4, 0, 0), (0, 0, 0)],

    # Mirrored arms: +Z is down on the right, up on the left.
    "R_Clavicle": [( 14, 0,  8), ( 10, 0,  2), (-10, 0, -18), ( -6, 0, -12), (0, 0,  0)],
    "L_Clavicle": [( 14, 0, -8), ( 10, 0, -2), (-10, 0,  18), ( -6, 0,  12), (0, 0,  0)],
    "R_Upperarm": [( 72, 0, 48), ( 66, 0,  8), ( 30, 0, -98), ( 34, 0, -80), (10, 0,  72)],
    "L_Upperarm": [( 72, 0,-48), ( 66, 0, -8), ( 30, 0,  98), ( 34, 0,  80), (10, 0, -72)],
    "R_Forearm":  [( 40, 0, 0), ( 75, 0, 0), ( 10, 0, 0), ( 18, 0, 0), (20, 0, 0)],
    "L_Forearm":  [( 40, 0, 0), ( 75, 0, 0), ( 10, 0, 0), ( 18, 0, 0), (20, 0, 0)],
    "R_Hand":     [( 15, 0, 0), ( 20, 0, 0), (-22, 0, 0), (-12, 0, 0), ( 0, 0, 0)],
    "L_Hand":     [( 15, 0, 0), ( 20, 0, 0), (-22, 0, 0), (-12, 0, 0), ( 0, 0, 0)],

    "R_Thigh":    [( 36, 0, 0), ( 18, 0, 0), ( -8, 0, 0), ( -3, 0, 0), (0, 0, 0)],
    "L_Thigh":    [( 36, 0, 0), ( 18, 0, 0), ( -8, 0, 0), ( -3, 0, 0), (0, 0, 0)],
    "R_Calf":     [(-56, 0, 0), (-28, 0, 0), ( -2, 0, 0), ( -8, 0, 0), (-5, 0, 0)],
    "L_Calf":     [(-56, 0, 0), (-28, 0, 0), ( -2, 0, 0), ( -8, 0, 0), (-5, 0, 0)],
    "R_Foot":     [( 18, 0, 0), ( 10, 0, 0), (-24, 0, 0), ( -8, 0, 0), (0, 0, 0)],
    "L_Foot":     [( 18, 0, 0), ( 10, 0, 0), (-24, 0, 0), ( -8, 0, 0), (0, 0, 0)],
}

# Preview only: where the thrown player goes. In game the ragdoll draws this.
VICTIM_POSE = {
    "Waist": (-24, 0, 0), "Spine01": (-14, 0, 0), "Head": (34, 0, 0),
    "R_Thigh": (64, 0, 0), "L_Thigh": (58, 0, 0),
    "R_Calf": (-86, 0, 0), "L_Calf": (-78, 0, 0),
    "R_Upperarm": (24, 0, -46), "L_Upperarm": (24, 0, 46),
    "R_Forearm": (58, 0, 0), "L_Forearm": (52, 0, 0),
}
# High and forward (-Y) so the two silhouettes never overlap.
VICTIM_FLIGHT = [
    (1,  (0, -0.02, 0.26), ( 92, 0, 0)),
    (8,  (0, -0.02, 0.54), ( 99, 0, 0)),
    (14, (0, -0.18, 0.98), (116, 0, 0)),
    (18, (0, -0.90, 1.52), (168, 0, 0)),
    (26, (0, -2.35, 2.35), (330, 0, 0)),
]


def all_fcurves(action):
    """Blender 5.x uses slotted actions, so action.fcurves no longer exists."""
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for curve in bag.fcurves:
                    yield curve


def build_action(arm):
    bpy.context.view_layer.objects.active = arm
    arm.animation_data_clear()
    for old in list(bpy.data.actions):
        bpy.data.actions.remove(old)
    for bone in arm.pose.bones:
        bone.rotation_mode = 'XYZ'
        bone.rotation_euler = (0, 0, 0)

    missing = [name for name in POSES if arm.pose.bones.get(name) is None]
    if missing:
        raise SystemExit("bones missing from the rig: " + ", ".join(missing))

    for name, poses in POSES.items():
        bone = arm.pose.bones[name]
        for frame, (rx, ry, rz) in zip(FRAMES, poses):
            bone.rotation_euler = Euler((D(rx), D(ry), D(rz)), 'XYZ')
            bone.keyframe_insert("rotation_euler", frame=frame)

    action = arm.animation_data.action
    action.name = ACTION
    action.use_fake_user = True

    linear = 0
    for curve in all_fcurves(action):
        for key in curve.keyframe_points:
            if abs(key.co.x - FAST_FROM) < 0.01:
                key.interpolation = 'LINEAR'
                linear += 1
    if linear == 0:
        raise SystemExit("no interpolation was changed - the curves were not found")
    print("[toss] %s: %d bones, %d linear keys" % (action.name, len(POSES), linear))
    return action


def export_fbx(arm):
    FBX_OUT.parent.mkdir(parents=True, exist_ok=True)

    # The scene range is what gets baked, not the action's own keys. Leaving the
    # default 1-250 exported a 10.4 second clip of mostly nothing.
    bpy.context.scene.frame_start = FRAMES[0]
    bpy.context.scene.frame_end = FRAMES[-1]

    for obj in bpy.context.scene.objects:
        obj.select_set(False)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=str(FBX_OUT),
        use_selection=True,
        object_types={'ARMATURE'},
        add_leaf_bones=False,          # leaf bones would add _end bones and break paths
        bake_anim=True,
        bake_anim_use_all_actions=False,
        bake_anim_use_nla_strips=False,
        bake_anim_simplify_factor=0.0,
        apply_unit_scale=True,
        bake_space_transform=False,
    )
    print("[toss] FBX: %s" % FBX_OUT)


def add_victim(arm, mesh):
    scene = bpy.context.scene
    clone = arm.copy()
    clone.data = arm.data.copy()
    clone.animation_data_clear()
    scene.collection.objects.link(clone)
    skin = mesh.copy()
    skin.data = mesh.data.copy()
    scene.collection.objects.link(skin)
    skin.parent = clone
    for modifier in skin.modifiers:
        if modifier.type == 'ARMATURE':
            modifier.object = clone
    for bone in clone.pose.bones:
        bone.rotation_mode = 'XYZ'
        bone.rotation_euler = (0, 0, 0)
    for name, (rx, ry, rz) in VICTIM_POSE.items():
        bone = clone.pose.bones.get(name)
        if bone:
            bone.rotation_euler = Euler((D(rx), D(ry), D(rz)), 'XYZ')
    clone.rotation_mode = 'XYZ'
    for frame, location, rotation in VICTIM_FLIGHT:
        clone.location = Vector(location)
        clone.rotation_euler = Euler([D(v) for v in rotation], 'XYZ')
        clone.keyframe_insert("location", frame=frame)
        clone.keyframe_insert("rotation_euler", frame=frame)
    return clone


def render_preview(out_dir):
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = FRAMES[0], FRAMES[-1]
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.render.resolution_x, scene.render.resolution_y = 320, 480
    shading = scene.display.shading
    shading.light = 'STUDIO'
    shading.color_type = 'SINGLE'
    shading.single_color = (0.74, 0.74, 0.77)
    shading.show_shadows = True
    shading.show_cavity = True
    scene.world.color = (0.16, 0.16, 0.18)
    for obj in [o for o in bpy.data.objects if o.type in {'CAMERA', 'LIGHT'}]:
        bpy.data.objects.remove(obj, do_unlink=True)
    data = bpy.data.cameras.new("Preview")
    camera = bpy.data.objects.new("Preview", data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    data.lens = 36
    target = Vector((0, -0.85, 1.05))
    views = {"side": Vector((-3.05, -0.10, 0.05)), "q34": Vector((-2.30, -2.00, 0.15))}
    scene.render.image_settings.file_format = 'PNG'
    for name, offset in views.items():
        camera.location = target + offset
        camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = "%s/%s_" % (out_dir, name)
        bpy.ops.render.render(animation=True)
    print("[toss] preview rendered")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    mesh = next(o for o in bpy.data.objects if o.type == 'MESH')
    build_action(arm)
    export_fbx(arm)
    if "--preview" in argv:
        add_victim(arm, mesh)
        render_preview(argv[argv.index("--preview") + 1])


main()
