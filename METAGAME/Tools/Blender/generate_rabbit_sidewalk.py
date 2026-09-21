"""옆걸음(게걸음) 클립을 토끼 리그에 굽는다 (S15P21B202-670).

    blender.exe --background METAGAME/BlenderSource/SpaceFlop/SF_Rabbit_Base.blend \
        --python METAGAME/Tools/Blender/generate_rabbit_sidewalk.py -- [--preview <dir>]

출력: .../Source/Rabbit_SideWalk.fbx (오른쪽) · Rabbit_SideWalkLeft.fbx (--mirror)

**접지 위상을 앞걸음에 맞춘다.** 네 방향 클립이 RabbitLocomotion 의 `_phase` 하나를
공유하므로, 같은 위상에서 같은 발이 땅에 닿아야 방향을 바꿀 때 다리가 안 미끄러진다.
기존 Rabbit_Walk 를 재서 얻은 값이다(에디터에서 허벅지 회전의 앞끝을 잼):

    왼 허벅지 앞끝  위상 0.717
    오른 허벅지 앞끝 위상 0.183

길이도 맞춘다 — 1.167초 · 30fps · 35프레임.

**좌우 두 벌을 굽는다.** 거울로 뒤집어 쓰면 접지 위상까지 뒤집혀서(0.183 -> 0.817)
앞걸음과 섞을 때 발이 미끄러진다. 두 벌이면 가는 쪽 발이 제 위상을 지킨다 —
오른쪽은 오른발이 0.183, 왼쪽은 왼발이 0.717 에 딛는다.

리그 좌표 (generate_rabbit_dance.py 와 같다):
    +X = 캐릭터의 왼쪽 · +Z = 위 · -Y = 앞
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector

SCRIPT = Path(__file__).resolve()
REPO_ROOT = SCRIPT.parents[3]
MIRROR = "--mirror" in sys.argv
NAME = "Rabbit_SideWalkLeft" if MIRROR else "Rabbit_SideWalk"
FBX_OUT = (REPO_ROOT / "METAGAME" / "Assets" / "_Project" / "Animations"
           / "Characters" / "Rabbit" / "Source" / (NAME + ".fbx"))

FORWARD = Vector((0.0, -1.0, 0.0))
UP = Vector((0.0, 0.0, 1.0))

BONES = ["Waist", "Spine01", "Spine02", "Head",
         "L_Clavicle", "L_Upperarm", "L_Forearm", "L_Hand",
         "R_Clavicle", "R_Upperarm", "R_Forearm", "R_Hand",
         "L_Thigh", "L_Calf", "L_Foot", "R_Thigh", "R_Calf", "R_Foot"]
SPINE = ["Waist", "Spine01", "Spine02"]
SPINE_SHARE = [0.45, 0.75, 1.0]
CLAVICLE_FOLLOW = 0.22
FOOT_LEVEL = 0.62

FPS = 30
FRAMES = 35                 # Rabbit_Walk 과 같은 길이(1.167초)
LEFT_PLANT = 0.717          # 앞걸음에서 왼 허벅지가 가장 앞에 오는 위상
RIGHT_PLANT = 0.183


def n(v):
    v = Vector(v)
    return v.normalized() if v.length > 1e-6 else Vector((0, 0, -1))


def blend(a, b, t):
    v = Vector(a) * (1.0 - t) + Vector(b) * t
    return v.normalized() if v.length > 1e-6 else Vector(a)


def swing(phase, plant):
    """그 발이 얼마나 벌어져 있는가(0~1). 접지 위상에서 1 이다."""
    return 0.5 * (1.0 + math.cos((phase - plant) * math.tau))


def pose_at(p):
    """
    게걸음 — 앞발이 진행 방향으로 나가고 뒷발이 따라붙는다. 다리가 안 꼬여 작게
    봐도 읽힌다(시안 A).

    **가는 쪽의 발이 앞발이고, 그 발은 제 접지 위상을 지킨다.** 오른쪽으로 갈 때는
    오른발(0.183), 왼쪽으로 갈 때는 왼발(0.717)이 앞발이다 — 그래야 앞걸음과 섞어도
    발이 안 미끄러진다. 거울로 뒤집어 쓰지 않고 두 벌을 굽는 이유가 이것이다.
    """
    s = 1.0 if MIRROR else -1.0        # 진행 방향의 X 부호 (+X 가 캐릭터의 왼쪽)
    lead = swing(p, LEFT_PLANT if MIRROR else RIGHT_PLANT)
    trail = swing(p, RIGHT_PLANT if MIRROR else LEFT_PLANT)
    bob = math.sin(p * math.tau * 2.0)

    # 앞발 / 뒷발을 진행 방향에 따라 고른다.
    leadBase, trailBase = (0.17, -0.17) if MIRROR else (-0.17, 0.17)
    thighLead = n((leadBase + s * 0.58 * lead, -0.10, -0.98))
    thighTrail = n((trailBase + s * 0.46 * trail, -0.10, -0.98))
    calfLead = blend(thighLead, (0, -0.15, -1), 0.45 + 0.30 * lead)
    calfTrail = blend(thighTrail, (0, -0.15, -1), 0.45 + 0.30 * trail)

    thighL, thighR = (thighLead, thighTrail) if MIRROR else (thighTrail, thighLead)
    calfL, calfR = (calfLead, calfTrail) if MIRROR else (calfTrail, calfLead)

    # 팔은 균형용으로 벌리되 **걸음에 맞춰 흔든다.** 고정하면 굽기가 "안 움직이는
    # 뼈" 로 거부하고(실제로 L_Hand 에서 걸렸다), 보기에도 팔만 굳어 보인다.
    armL = n((0.80 + 0.12 * trail, -0.20 + 0.10 * lead, -0.54 + 0.08 * bob))
    armR = n((-0.84 - 0.12 * lead, -0.22 + 0.10 * trail, -0.46 - 0.08 * bob))

    return dict(
        thighL=thighL, thighR=thighR, calfL=calfL, calfR=calfR,
        armL=armL, armR=armR,
        foreL=blend(armL, (0.35, -0.55, -0.75), 0.55),
        foreR=blend(armR, (-0.35, -0.55, -0.75), 0.55),
        spine=n((-s * 0.13, -0.05, 1.0)),      # 진행 방향으로 살짝 기운다
        head=(4.0, -s * 8.0, 2.0 * bob),       # 가는 쪽을 조금 본다
        yaw=3.0 * bob,
    )


class Poser:
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
        # 위치·크기도 되돌린다 — pose_bone.matrix 대입이 location 까지 쓴다.
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
        m = pb.matrix.copy()
        loc = m.translation.copy()
        m = rot.to_matrix().to_4x4() @ m
        m.translation = loc
        pb.matrix = m
        bpy.context.view_layer.update()

    def aim(self, name, target):
        target = Vector(target)
        if target.length < 1e-6:
            return
        self._apply(name, self.axis(name).rotation_difference(target.normalized()))

    def twist(self, name, degrees):
        self._apply(name, Quaternion(self.axis(name), math.radians(degrees)))

    def key(self, frame):
        for name in BONES:
            self.arm.pose.bones[name].keyframe_insert("rotation_quaternion", frame=frame)


def pose(poser, f):
    poser.reset()
    for name, share in zip(SPINE, SPINE_SHARE):
        poser.aim(name, blend(UP, f["spine"], share))
        poser.twist(name, f["yaw"] / len(SPINE))

    head = Vector(f["spine"])
    pitch, yaw, roll = f["head"]
    head.rotate(Quaternion(FORWARD, math.radians(roll)))
    head.rotate(Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(pitch)))
    poser.aim("Head", head)
    poser.twist("Head", yaw)

    for side in ("L", "R"):
        a = f["arm" + side]
        clav, upper, fore, hand = (side + "_Clavicle", side + "_Upperarm",
                                   side + "_Forearm", side + "_Hand")
        poser.aim(clav, blend(poser.axis(clav), a, CLAVICLE_FOLLOW))
        poser.aim(upper, a)
        fd = f["fore" + side]
        poser.aim(fore, fd)
        poser.aim(hand, blend(fd, a, 0.25))

    for side in ("L", "R"):
        thigh, calf, foot = side + "_Thigh", side + "_Calf", side + "_Foot"
        poser.aim(thigh, f["thigh" + side])
        cd = f["calf" + side]
        poser.aim(calf, cd)
        poser.aim(foot, blend(cd, FORWARD, FOOT_LEVEL))


def build(arm):
    poser = Poser(arm)
    # 마지막 프레임은 첫 프레임과 같아야 고리가 이어진다.
    for i in range(FRAMES + 1):
        pose(poser, pose_at((i % FRAMES) / FRAMES))
        poser.key(i + 1)
    return FRAMES + 1


def export_fbx(arm, last):
    FBX_OUT.parent.mkdir(parents=True, exist_ok=True)
    bpy.context.scene.frame_start = 1
    bpy.context.scene.frame_end = last
    bpy.context.scene.render.fps = FPS
    for obj in bpy.context.scene.objects:
        obj.select_set(False)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=str(FBX_OUT),
        use_selection=True,
        object_types={'ARMATURE'},
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_actions=False,
        bake_anim_use_nla_strips=False,
        bake_anim_simplify_factor=0.0,
        apply_unit_scale=True,
        bake_space_transform=False,
    )
    print("[sidewalk] FBX: %s" % FBX_OUT)


def main():
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    last = build(arm)
    export_fbx(arm, last)
    print("[sidewalk] %d 프레임 · %.3f초" % (last, FRAMES / FPS))


main()
