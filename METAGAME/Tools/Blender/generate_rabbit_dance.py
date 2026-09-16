"""Author the dance action on the existing rabbit rig from captured motion.

Run from the repository root with Blender 5.x:

    blender.exe --background METAGAME/BlenderSource/SpaceFlop/SF_Rabbit_Base.blend \
        --python METAGAME/Tools/Blender/generate_rabbit_dance.py -- --preview <dir>

Output: Assets/_Project/Animations/Characters/Rabbit/Source/Rabbit_Dance.fbx
        (+ preview frames when --preview is given)

Unity turns the FBX into Resources/RabbitLocomotion/Rabbit_Dance.anim via
Metagame.EditorTools.RabbitDanceAssets - same route as the toss
(generate_rabbit_toss.py -> RabbitTossAssets).

**Where the motion comes from.** data/rabbit_dance_bichon.json holds one bone
direction per frame, lifted off a reference video. The extraction is not run
here: it needed a pose model and the source clip, and neither belongs in the
repo. What is committed is its result, so this file is reproducible offline.
The extraction is written up in docs/records/S15P21B202-563-dance-emote.md.

Directions live in armature space, measured from this rig, not guessed:
    +X = the character's left (L_Thigh x=+0.104, R_Thigh x=-0.105)
    +Z = up                   (Root z=0, Head z=0.49, top 0.979)
    -Y = forward              (the front-view camera sits at (0, -d, 0))
A bone is aimed by its own +Y axis, which runs along the bone.

**Six bones carry no captured data** - both clavicles, hands and feet. They are
driven off their parent limb here rather than left still, because
RabbitDanceAssets refuses to bake a clip in which any of the 18 bones stays
put: a still bone in the source is indistinguishable from a broken name match,
and that failure is silent in game. Shoulders follow the arm a little, wrists
lag it (follow-through), ankles keep the foot from swinging with the shin.

Only Blender's own Python API is used - no extra packages.
"""

from __future__ import annotations

import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector

SCRIPT = Path(__file__).resolve()
REPO_ROOT = SCRIPT.parents[3]
DATA = SCRIPT.parent / "data" / "rabbit_dance_bichon.json"
FBX_OUT = (REPO_ROOT / "METAGAME" / "Assets" / "_Project" / "Animations"
           / "Characters" / "Rabbit" / "Source" / "Rabbit_Dance.fbx")

ACTION = "Rabbit_Dance"

FORWARD = Vector((0.0, -1.0, 0.0))

# RabbitLocomotion.TossBoneNames 와 같은 18개. 이 전부가 움직여야 굽기가 통과한다.
BONES = ["Waist", "Spine01", "Spine02", "Head",
         "L_Clavicle", "L_Upperarm", "L_Forearm", "L_Hand",
         "R_Clavicle", "R_Upperarm", "R_Forearm", "R_Hand",
         "L_Thigh", "L_Calf", "L_Foot", "R_Thigh", "R_Calf", "R_Foot"]

SPINE = ["Waist", "Spine01", "Spine02"]
# 굽힘을 마디마다 얼마씩 지는가. 위로 갈수록 많이 휜다(사람 등과 같다).
SPINE_SHARE = [0.45, 0.75, 1.0]
LIMBS = [("R_Upperarm", "R_Forearm", "R_Clavicle", "R_Hand"),
         ("L_Upperarm", "L_Forearm", "L_Clavicle", "L_Hand")]
LEGS = [("R_Thigh", "R_Calf", "R_Foot"), ("L_Thigh", "L_Calf", "L_Foot")]

CLAVICLE_FOLLOW = 0.22      # 어깨가 팔을 따라가는 정도
WRIST_LAG = 2               # 손목이 팔뚝보다 늦는 프레임 수
FOOT_LEVEL = 0.62           # 발이 정강이를 안 따라가고 앞을 보게 하는 정도


def load():
    with DATA.open(encoding="utf-8") as f:
        data = json.load(f)
    need = {"Spine", "R_Upperarm", "L_Upperarm", "R_Forearm", "L_Forearm",
            "R_Thigh", "L_Thigh", "R_Calf", "L_Calf"}
    missing = need - set(data["dirs"])
    if missing:
        raise SystemExit("모션 데이터에 없는 뼈: " + ", ".join(sorted(missing)))
    return data


def blend(a, b, t):
    """방향 두 개를 섞는다. 둘 다 단위벡터라 더하고 정규화하면 충분하다."""
    v = a * (1.0 - t) + b * t
    return v.normalized() if v.length > 1e-6 else a


class Poser:
    """뼈를 '방향' 으로 맞춘다. 부모부터 차례로 해야 자식의 현재 방향이 정해진다."""

    def __init__(self, arm):
        self.arm = arm
        bpy.context.view_layer.objects.active = arm
        arm.animation_data_clear()
        for old in list(bpy.data.actions):
            bpy.data.actions.remove(old)
        for bone in arm.pose.bones:
            bone.rotation_mode = 'QUATERNION'
        missing = [b for b in BONES if arm.pose.bones.get(b) is None]
        if missing:
            raise SystemExit("리그에 없는 뼈: " + ", ".join(missing))

    def reset(self):
        # **위치·크기도 되돌린다.** pose_bone.matrix 대입은 회전만이 아니라 location 과
        # scale 도 쓴다. 회전만 지웠더니 남은 이동이 427프레임 동안 쌓여 캐릭터 키가
        # 4505m 가 됐다(실측). 회전만 키를 찍으므로 여기서 지워야 한다.
        for bone in self.arm.pose.bones:
            bone.rotation_quaternion = (1, 0, 0, 0)
            bone.location = (0.0, 0.0, 0.0)
            bone.scale = (1.0, 1.0, 1.0)
        bpy.context.view_layer.update()

    def axis(self, name):
        pb = self.arm.pose.bones[name]
        return (pb.matrix.to_3x3() @ Vector((0.0, 1.0, 0.0))).normalized()

    def _apply(self, name, rot):
        pb = self.arm.pose.bones[name]
        matrix = pb.matrix.copy()
        loc = matrix.translation.copy()
        matrix = rot.to_matrix().to_4x4() @ matrix
        matrix.translation = loc
        pb.matrix = matrix
        bpy.context.view_layer.update()

    def aim(self, name, target):
        target = Vector(target)
        if target.length < 1e-6:
            return
        self._apply(name, self.axis(name).rotation_difference(target.normalized()))

    def twist(self, name, degrees):
        """뼈 자신의 축을 중심으로 비튼다. 방향만으로는 안 잡히는 자유도다."""
        self._apply(name, Quaternion(self.axis(name), math.radians(degrees)))

    def drift(self):
        return max(b.location.length for b in self.arm.pose.bones)

    def key(self, frame):
        for name in BONES:
            self.arm.pose.bones[name].keyframe_insert("rotation_quaternion", frame=frame)


def pose_frame(poser, data, i):
    dirs = data["dirs"]
    poser.reset()

    # **굽힘을 세 마디에 나눠 준다.** 처음에는 셋을 전부 같은 방향으로 겨눴는데,
    # 부모(Waist)가 이미 그 방향이면 자식의 로컬 회전이 항상 0 이 된다 — 굽기가
    # "Spine01 이 안 움직인다" 로 잡아냈다. 마디마다 몫이 달라야 실제로 휜다.
    up = Vector((0.0, 0.0, 1.0))
    spine = Vector(dirs["Spine"][i])
    yaw = data["body_yaw"][i]
    for name, share in zip(SPINE, SPINE_SHARE):
        poser.aim(name, blend(up, spine, share))
        poser.twist(name, yaw / len(SPINE))

    # 머리는 방향 벡터가 아니라 세 각도로 얹는다. 어깨중심->귀중심으로 잡았더니
    # 고개를 휙 흔드는 구간에서 117도(수평보다 더) 숙었다 - 그 구간은 모션이
    # 뭉개져 어깨 추정이 머리 쪽으로 딸려 올라간다.
    head = spine.copy()
    head.rotate(Quaternion(FORWARD, math.radians(data["head_roll"][i])))
    head.rotate(Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(data["head_pitch"][i])))
    poser.aim("Head", head)
    poser.twist("Head", data["head_yaw"][i])

    for upper, fore, clavicle, hand in LIMBS:
        arm_dir = Vector(dirs[upper][i])
        poser.aim(clavicle, blend(poser.axis(clavicle), arm_dir, CLAVICLE_FOLLOW))
        poser.aim(upper, arm_dir)
        fore_dir = Vector(dirs[fore][i])
        poser.aim(fore, fore_dir)
        lag = Vector(dirs[fore][max(0, i - WRIST_LAG)])
        poser.aim(hand, blend(fore_dir, lag, 0.85))

    for thigh, calf, foot in LEGS:
        poser.aim(thigh, Vector(dirs[thigh][i]))
        calf_dir = Vector(dirs[calf][i])
        poser.aim(calf, calf_dir)
        poser.aim(foot, blend(calf_dir, FORWARD, FOOT_LEVEL))


def build(arm, data):
    poser = Poser(arm)
    frames = data["n"]
    drift = 0.0
    for i in range(frames):
        pose_frame(poser, data, i)
        # 방향만 맞췄으면 뼈의 로컬 이동은 0 이어야 한다. 아니면 위에서 놓친 것이 있다.
        drift = max(drift, poser.drift())
        poser.key(1 + i)
    if drift > 1e-4:
        raise SystemExit("뼈가 제자리에 안 있다 - 남은 이동 %.5f m" % drift)

    action = arm.animation_data.action
    action.name = ACTION
    action.use_fake_user = True

    curves = sum(len(c.keyframe_points) for c in all_fcurves(action))
    if curves == 0:
        raise SystemExit("커브를 하나도 못 찾았다 - 슬롯 액션 순회가 틀렸다")

    scene = bpy.context.scene
    scene.render.fps = int(round(data["fps"]))
    scene.frame_start, scene.frame_end = 1, frames
    print("[dance] %s: 뼈 %d개 · 키 %d개 · %d프레임 %.2f초"
          % (ACTION, len(BONES), curves, frames, frames / data["fps"]))


def all_fcurves(action):
    """Blender 5.x 는 슬롯 액션이라 action.fcurves 가 없다."""
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for curve in bag.fcurves:
                    yield curve


def export(arm):
    FBX_OUT.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
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
    print("[dance] FBX: %s" % FBX_OUT)


def preview(out_dir, data):
    scene = bpy.context.scene
    scene.frame_step = 4
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.render.resolution_x, scene.render.resolution_y = 320, 430
    shading = scene.display.shading
    shading.light, shading.color_type = 'STUDIO', 'SINGLE'
    shading.single_color = (0.80, 0.78, 0.84)
    shading.show_shadows = shading.show_cavity = True
    scene.world.color = (0.12, 0.11, 0.18)

    for obj in [o for o in bpy.data.objects if o.type in {'CAMERA', 'LIGHT'}]:
        bpy.data.objects.remove(obj, do_unlink=True)
    zs = []
    for obj in bpy.data.objects:
        if obj.type == 'MESH':
            zs += [(obj.matrix_world @ Vector(c)).z for c in obj.bound_box]
    height, centre = (max(zs) - min(zs), (max(zs) + min(zs)) * 0.5) if zs else (1.0, 0.5)

    data_cam = bpy.data.cameras.new("Preview")
    camera = bpy.data.objects.new("Preview", data_cam)
    scene.collection.objects.link(camera)
    scene.camera = camera
    data_cam.lens = 42
    # 거리를 크기에서 계산한다. 눈대중으로 두었다가 캐릭터가 화면의 1/4 만 찬 적이 있다.
    distance = (height * 1.5 * 0.5) / math.tan(math.atan(18.0 / data_cam.lens))
    target = Vector((0, 0, centre))
    camera.location = target + Vector((-distance * 0.5, -distance * 0.85, 0.04))
    camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.image_settings.file_format = 'PNG'
    scene.render.filepath = out_dir.rstrip("/") + "/dance_"
    bpy.ops.render.render(animation=True)
    print("[dance] 미리보기: %s" % out_dir)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    data = load()
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    build(arm, data)
    export(arm)
    if "--preview" in argv:
        preview(argv[argv.index("--preview") + 1], data)


main()
