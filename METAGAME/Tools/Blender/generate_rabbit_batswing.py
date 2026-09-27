"""방망이 스윙 클립을 토끼 리그에 굽는다.

    blender.exe --background METAGAME/BlenderSource/SpaceFlop/SF_Rabbit_Base.blend \
        --python METAGAME/Tools/Blender/generate_rabbit_batswing.py -- [--ready] [--preview <dir>]

출력: .../Source/Rabbit_BatSwing.fbx (스윙 9프레임) · Rabbit_BatReady.fbx (--ready, 대기 코일)

**배트 궤적은 코드가 이미 갖고 있다.** `MeteorBat.SwingAim` 이 0.28초 동안 방향을
정하고(yaw +110 -> 0 -> -70, pitch +45 -> +5 -> -15), 팔은 그 방향을 따라간다.
그래서 이 클립은 **배트를 다시 정의하지 않는다** — 같은 식을 여기서도 계산해
몸이 그 궤적과 어긋나지 않게만 한다. 두 곳이 같은 공식을 쓰므로 어긋날 수 없다.

**실제 야구 스윙의 순서를 따른다.** 아래에서 위로 풀린다.

    앞발 착지 -> 골반 -> 몸통 -> 어깨 -> 팔

골반이 먼저 열리는데 어깨는 아직 감겨 있는 구간이 있고(분리, X-factor), 그것이
풀리면서 채찍처럼 배트가 나간다. 그래서 골반과 어깨에 **서로 다른 가속 곡선**을
쓴다 — 같은 곡선을 쓰면 몸이 통째로 도는 회전문이 된다.

    컨택 전  골반 e = (t/0.45)^2        어깨 e^1.7   (어깨가 늦게 따라온다)
    컨택 후  골반 e = 1-(1-u)^2         어깨 e^1.3   (어깨가 앞질러 나간다)

**컨택에서 앞다리가 펴진다**(front leg block). 몸이 앞으로 계속 나가면 회전축이
없어 배트 끝이 안 빨라진다.

**머리는 컨택까지 정면을 본다.** 가장 늦게 도는 부위다. 1인칭 카메라는 이 뼈를
쓰지 않으므로(Cam 이 따로 있다) 시점이 흔들리지 않는다.

리그 좌표 (generate_rabbit_sidewalk.py 와 같다):
    +X = 캐릭터의 왼쪽 · +Z = 위 · -Y = 앞
따라서 **캐릭터의 오른쪽은 -X** 이고, +Z 축 회전은 위에서 볼 때 왼쪽으로 돈다.
스윙은 오른쪽 뒤에서 시작하므로 부호를 뒤집어 쓴다.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector

SCRIPT = Path(__file__).resolve()
REPO_ROOT = SCRIPT.parents[3]
READY = "--ready" in sys.argv
NAME = "Rabbit_BatReady" if READY else "Rabbit_BatSwing"
FBX_OUT = (REPO_ROOT / "METAGAME" / "Assets" / "_Project" / "Animations"
           / "Characters" / "Rabbit" / "Source" / (NAME + ".fbx"))

FORWARD = Vector((0.0, -1.0, 0.0))
UP = Vector((0.0, 0.0, 1.0))
RIGHT = Vector((-1.0, 0.0, 0.0))        # 캐릭터의 오른쪽

BONES = ["Waist", "Spine01", "Spine02", "Head",
         "L_Clavicle", "L_Upperarm", "L_Forearm", "L_Hand",
         "R_Clavicle", "R_Upperarm", "R_Forearm", "R_Hand",
         "L_Thigh", "L_Calf", "L_Foot", "R_Thigh", "R_Calf", "R_Foot"]
SPINE = ["Waist", "Spine01", "Spine02"]
CLAVICLE_FOLLOW = 0.30
# 어깨에서 손까지. **와인드업에서는 짧다** — 실제 스윙에서 손은 뒤쪽 어깨에 붙어
# 있고 배트 머리만 크게 돈다. 고정으로 두었더니 손이 앞으로 나가 **배트가 얼굴을
# 관통했다.** 컨택에서 뻗고(확장) 팔로스루에서 조금 접힌다.
GRIP_TUCKED, GRIP_CONTACT, GRIP_END = 0.15, 0.31, 0.26

# **와인드업에서 손을 목에서 떼어 놓는다.**
#
# 그립을 가슴에서 배트 방향으로만 잡았더니, 로드에서 배트가 위를 보므로 손이
# 턱밑으로 올라왔다. 두 손으로 쥐는 스윙이라 **왼팔이 몸을 가로질러** 그 지점까지
# 가고, 그러면서 목을 지난다 — 실측으로 목 축에서 0.043m 였다(팔로스루는 0.144m).
#
# 실제 타자도 손을 턱 앞이 아니라 **뒤쪽 어깨 바깥, 그리고 아래**에 둔다.
# 그래서 로드에서만 오른쪽·앞·아래로 밀고, 컨택까지 0 으로 녹인다.
#
# **값은 쓸어 보고 골랐다. 그리고 한 번 잘못 골랐다.** 처음에는 아래로 내리는 것만
# 키웠다 — 숫자는 0.068 -> 0.113m 로 좋아졌는데 **손이 허리까지 떨어져 야구 로드가
# 아니게 됐다.** 한 숫자만 보고 민 탓이다.
#
# 듣는 것은 <b>앞으로 빼는 것</b>이었다. 왼팔이 몸을 가로지르는 것 자체는 두 손
# 스윙에서 피할 수 없고, 가로지르는 자리를 <b>목이 아니라 가슴 앞</b>으로 옮기면 된다.
#
#     (OUT, BACK, DOWN)          왼팔    배트가 선 높이
#     0.115,  0.055, 0.075       0.068   (지금 것 — 목을 통과)
#     0.175,  0.000, 0.300       0.113   손이 허리까지 내려감
#     0.175, -0.060, 0.120       0.119
#     0.210, -0.090, 0.110       0.128   <- 고른 값. 배트를 가장 높이 유지한다
#     0.150, -0.130, 0.130       0.133   손이 몸에서 너무 뜬다
#
# **어깨 자체가 목 축에서 0.140m 다.** 그것이 바닥값이라 0.128 이면 팔이 어깨보다
# 목에 더 가깝지 않다는 뜻이다.
GRIP_OUT, GRIP_BACK, GRIP_DOWN = 0.210, -0.090, 0.110
for _i, _a in enumerate(sys.argv):      # 값을 다시 쓸어 볼 수 있게 열어 둔다
    if _a == "--grip":
        GRIP_OUT, GRIP_BACK, GRIP_DOWN = (float(x) for x in sys.argv[_i + 1].split(","))

# **팔꿈치를 내린다.** 앞팔꿈치가 아래를 봐야 팔이 가슴 앞으로 돌아간다 — 실제
# 타자도 그렇다. 뒷팔꿈치는 반대로 조금 든다.
ELBOW_DROP_FRONT = 0.135
ELBOW_DROP_BACK = 0.045
FOOT_LEVEL = 0.62

FPS = 30
FRAMES = 9 if not READY else 25         # 스윙 0.28초 / 대기는 숨쉬기 고리

# MeteorBat.SwingAim 과 같은 값 (PhysicsScale)
CONTACT_AT = 0.45
WINDUP_YAW = 110.0
FOLLOW_YAW = -70.0
UP_PITCH = 45.0
CONTACT_PITCH = 5.0
DOWN_PITCH = -15.0

# 몸의 절대 yaw (도). + 가 감기는 쪽(오른쪽 뒤)이다.
HIP_LOAD, HIP_CONTACT, HIP_END = 25.0, -18.0, -35.0
CHEST_LOAD, CHEST_END = 50.0, -58.0
HEAD_END = -38.0


def n(v):
    v = Vector(v)
    return v.normalized() if v.length > 1e-6 else Vector((0, 0, -1))


def blend(a, b, t):
    v = Vector(a) * (1.0 - t) + Vector(b) * t
    return v.normalized() if v.length > 1e-6 else Vector(a)


def lerp(a, b, t):
    return a + (b - a) * t


def bat_angles(t):
    """MeteorBat.SwingAim 과 같은 식. 코드가 진실이고 여기는 따라 적은 것이다."""
    t = min(max(t, 0.0), 1.0)
    if t <= CONTACT_AT:
        e = (t / CONTACT_AT) ** 2
        return lerp(WINDUP_YAW, 0.0, e), lerp(UP_PITCH, CONTACT_PITCH, e)
    u = (t - CONTACT_AT) / (1.0 - CONTACT_AT)
    e = 1.0 - (1.0 - u) ** 2
    return lerp(0.0, FOLLOW_YAW, e), lerp(CONTACT_PITCH, DOWN_PITCH, e)


def direction(yaw_deg, pitch_deg):
    """캐릭터 기준 yaw(+ 는 오른쪽) · pitch(+ 는 위) 를 리그 좌표 방향으로."""
    y, p = math.radians(yaw_deg), math.radians(pitch_deg)
    horizontal = FORWARD * math.cos(y) + RIGHT * math.sin(y)
    return n(horizontal * math.cos(p) + UP * math.sin(p))


def body_yaw(t):
    """골반 · 가슴 · 머리의 절대 yaw. **셋의 곡선이 서로 다르다.**"""
    t = min(max(t, 0.0), 1.0)
    if t <= CONTACT_AT:
        e = (t / CONTACT_AT) ** 2
        hip = lerp(HIP_LOAD, HIP_CONTACT, e)
        chest = lerp(CHEST_LOAD, 0.0, e ** 1.7)     # 어깨가 늦게 따라온다
        head = 0.0                                   # 컨택까지 정면을 본다
    else:
        u = (t - CONTACT_AT) / (1.0 - CONTACT_AT)
        e = 1.0 - (1.0 - u) ** 2
        hip = lerp(HIP_CONTACT, HIP_END, e)
        chest = lerp(0.0, CHEST_END, e ** 1.3)       # 어깨가 앞질러 나간다
        head = lerp(0.0, HEAD_END, e ** 1.6)         # 가장 늦게 돈다
    return hip, chest, head


def pose_legs(hip, side_sign, yaw_share, spread, bend, lifted):
    """
    **무릎이 실제로 굽어야 한다.** 허벅지와 종아리를 같은 방향으로 두면 다리가
    막대가 된다 — 첫 굽기에서 무릎이 3~11도밖에 안 굽어 다리가 안 움직이는 것으로
    보였다(재서 드러났다). 허벅지는 앞으로, 종아리는 뒤로 기울여 발목이 엉덩이
    아래로 오게 한다.
    """
    half = bend * 0.5
    thigh = direction(hip * yaw_share, -90.0 + half + lifted * 20.0)
    calf = direction(hip * yaw_share * 0.5, -90.0 - half)
    thigh = n(Vector(thigh) + RIGHT * (spread * side_sign))
    calf = n(Vector(calf) + RIGHT * (spread * side_sign * 0.4))
    return thigh, calf


def pose_at(t):
    """
    t 는 스윙 진행도 0~1. 대기 자세는 t = 0 을 쓰되 숨만 얹는다.
    """
    hip, chest, head = body_yaw(t)
    spine = lerp(hip, chest, 0.45)                    # 몸통은 골반과 어깨 사이
    bat_yaw, bat_pitch = bat_angles(t)

    # 앞발(왼발)은 들었다가 착지하고, 컨택부터 펴서 회전축이 된다.
    lift = max(0.0, 1.0 - t / 0.24)
    block = min(1.0, max(0.0, (t - CONTACT_AT) / 0.25))
    # 뒷발(오른발)은 컨택 뒤 뒤꿈치가 들리며 무릎이 안으로 말린다.
    heel = min(1.0, max(0.0, (t - CONTACT_AT) / 0.35))

    # 앞발(왼쪽): 로드에서 들고 굽었다가, 컨택부터 곧게 펴서 회전축이 된다.
    thighL, calfL = pose_legs(hip, -1.0, 0.70, 0.24,
                              lerp(26.0, 5.0, block) + 10.0 * lift, lift)
    # 뒷발(오른쪽): 내내 굽어 있고 팔로스루에서 더 말린다.
    thighR, calfR = pose_legs(hip, 1.0, 0.55, 0.22, lerp(32.0, 44.0, heel), 0.0)

    arm = direction(bat_yaw, bat_pitch)               # 배트가 뻗은 방향
    if t <= CONTACT_AT:
        e = (t / CONTACT_AT) ** 1.4
        reach = lerp(GRIP_TUCKED, GRIP_CONTACT, e)
        windup = 1.0 - e
    else:
        u = (t - CONTACT_AT) / (1.0 - CONTACT_AT)
        reach = lerp(GRIP_CONTACT, GRIP_END, u)
        windup = 0.0
    offset = (RIGHT * (GRIP_OUT * windup)
              - FORWARD * (GRIP_BACK * windup)
              - UP * (GRIP_DOWN * windup))
    return dict(
        bat_dir=arm, bat_yaw=bat_yaw, bat_pitch=bat_pitch,
        reach=reach, grip_offset=offset, windup=windup,
        hip=hip, spine=spine, chest=chest, head=head,
        thighL=thighL, thighR=thighR, calfL=calfL, calfR=calfR,
        arm=arm,
        # 팔꿈치는 배트보다 몸쪽에 남는다. 그래야 감았다 푸는 것으로 읽힌다.
        foreL=blend(arm, direction(bat_yaw * 0.45, bat_pitch * 0.55 - 12.0), 0.45),
        foreR=blend(arm, direction(bat_yaw * 0.60, bat_pitch * 0.55 - 6.0), 0.35),
        # 상체가 앞으로 살짝 숙는다. 컨택에서 가장 낮다.
        lean=6.0 + 7.0 * math.sin(min(t, 1.0) * math.pi),
        heel=heel,
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

    def world(self, name):
        return self.arm.matrix_world @ self.arm.pose.bones[name].matrix.translation

    def key(self, frame):
        for name in BONES:
            self.arm.pose.bones[name].keyframe_insert("rotation_quaternion", frame=frame)


def pose(poser, f):
    poser.reset()

    # 척추는 위를 향한 채 **비튼다.** 비틀기가 곧 yaw 다(축이 수직이므로).
    # 부모를 비틀면 자식도 따라 돌므로 **차이만** 넣는다.
    lean = math.radians(f["lean"])
    spine_dir = n((0.0, -math.sin(lean), math.cos(lean)))
    absolute = [f["hip"], f["spine"], f["chest"]]
    previous = 0.0
    for name, share, target in zip(SPINE, (0.45, 0.75, 1.0), absolute):
        poser.aim(name, blend(UP, spine_dir, share))
        poser.twist(name, -(target - previous))       # +Z 회전은 왼쪽이라 부호를 뒤집는다
        previous = target

    poser.aim("Head", blend(UP, spine_dir, 0.55))
    poser.twist("Head", -(f["head"] - f["chest"]))

    # **두 손이 한 점을 잡는다.** 각자 어깨에서 같은 방향으로 뻗으면 손이 어깨너비
    # 만큼 벌어져 배트를 함께 쥔 것으로 안 보인다 — 첫 굽기에서 그렇게 나왔다.
    grip = poser.world("Spine02") + f["bat_dir"] * f["reach"] + f["grip_offset"]
    f["grip"] = grip
    for side in ("L", "R"):
        clav, upper, fore, hand = (side + "_Clavicle", side + "_Upperarm",
                                   side + "_Forearm", side + "_Hand")
        # 위팔은 그립이 아니라 **그 아래 지점**을 향한다. 그래야 팔꿈치가 내려가
        # 팔이 목을 안 지난다. 아래팔이 거기서 그립을 잡으러 올라간다.
        drop = ELBOW_DROP_FRONT if side == "L" else ELBOW_DROP_BACK
        guide = grip - UP * (drop * (0.45 + 0.55 * f["windup"]))
        to_guide = n(guide - poser.world(upper))
        poser.aim(clav, blend(poser.axis(clav), to_guide, CLAVICLE_FOLLOW))
        poser.aim(upper, to_guide)
        poser.aim(fore, n(grip - poser.world(fore)))
        poser.aim(hand, blend(f["bat_dir"], n(grip - poser.world(hand)), 0.35))

    for side in ("L", "R"):
        thigh, calf, foot = side + "_Thigh", side + "_Calf", side + "_Foot"
        poser.aim(thigh, f["thigh" + side])
        cd = f["calf" + side]
        poser.aim(calf, cd)
        level = FOOT_LEVEL - (0.45 * f["heel"] if side == "R" else 0.0)
        poser.aim(foot, blend(cd, FORWARD, level))


BAT_LENGTH = 0.55
BAT_RADIUS = 0.022


def make_bat():
    """미리보기 전용 배트. **FBX 로는 안 나간다** — 실제 배트는 게임이 들고 있다."""
    mesh = bpy.data.meshes.new("PreviewBat")
    obj = bpy.data.objects.new("PreviewBat", mesh)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.ops.mesh.primitive_cylinder_add(radius=BAT_RADIUS, depth=BAT_LENGTH,
                                        location=(0, 0, 0))
    made = bpy.context.active_object
    bpy.data.objects.remove(obj, do_unlink=True)
    made.name = "PreviewBat"
    # 원통은 +Z 로 길다. 손잡이가 원점에 오게 반 길이만큼 밀어 둔다.
    made.data.transform(__import__("mathutils").Matrix.Translation((0, 0, BAT_LENGTH * 0.5)))
    return made


def key_bat(bat, f, frame):
    bat.location = f["grip"]
    bat.rotation_mode = 'QUATERNION'
    bat.rotation_quaternion = Vector((0.0, 0.0, 1.0)).rotation_difference(f["bat_dir"])
    bat.keyframe_insert("location", frame=frame)
    bat.keyframe_insert("rotation_quaternion", frame=frame)


def build(arm, bat=None, samples=0):
    """samples 를 주면 **미리보기용으로만** 촘촘히 뽑는다. 구워 나가는 FBX 는
    언제나 FRAMES 장이다 — 게임은 SwingProgress01 로 재생 위치를 정하므로
    장수가 아니라 자세가 진실이다."""
    poser = Poser(arm)
    if READY:
        # 대기 코일. 스윙 시작 자세를 유지하되 숨만 쉰다 — **멈춰 있으면 굽기가
        # "안 움직이는 뼈" 로 거부하고**, 보기에도 마네킹이 된다.
        for i in range(FRAMES + 1):
            p = (i % FRAMES) / FRAMES
            breath = math.sin(p * math.tau)
            # **다리도 움직여야 한다.** 숨만 넣었더니 굽기가 "안 움직이는 뼈 6개
            # (L_Thigh ... R_Foot)" 로 거부했다. 가만히 선 타자도 무릎을 까딱이며
            # 체중을 옮긴다 — 다리를 고정하면 마네킹이 된다.
            f = pose_at(0.0)
            rock = math.sin(p * math.tau + 0.8)
            hip0, _, _ = body_yaw(0.0)
            thighL, calfL = pose_legs(hip0, -1.0, 0.70, 0.24, 26.0 + 3.5 * rock, 0.0)
            thighR, calfR = pose_legs(hip0, 1.0, 0.55, 0.22, 32.0 - 3.5 * rock, 0.0)
            f["thighL"], f["calfL"] = thighL, calfL
            f["thighR"], f["calfR"] = thighR, calfR
            f["lean"] += 1.6 * breath
            f["hip"] += 1.2 * breath
            f["chest"] += 2.4 * breath
            f["head"] = 1.5 * breath
            f["arm"] = blend(f["arm"], UP, 0.03 + 0.02 * breath)
            f["bat_dir"] = f["arm"]
            pose(poser, f)
            poser.key(i + 1)
            if bat is not None: key_bat(bat, f, i + 1)
        return FRAMES + 1

    count = samples if samples else FRAMES
    for i in range(count):
        f = pose_at(i / (count - 1))
        pose(poser, f)
        poser.key(i + 1)
        if bat is not None: key_bat(bat, f, i + 1)
    return count


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
    print("[batswing] FBX: %s" % FBX_OUT)


def preview(out_dir, last):
    scene = bpy.context.scene
    # **범위를 여기서 정한다.** 안 정하면 blend 파일의 기본 범위(250장)가 돌아
    # 한 번에 500장을 뽑는다 — 실제로 그렇게 나왔다.
    scene.frame_start, scene.frame_end = 1, last
    scene.frame_step = 1
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.render.resolution_x, scene.render.resolution_y = 300, 400
    shading = scene.display.shading
    shading.light, shading.color_type = 'STUDIO', 'SINGLE'
    shading.single_color = (0.80, 0.78, 0.84)
    shading.show_shadows = shading.show_cavity = True
    scene.world.color = (0.10, 0.10, 0.16)

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
    distance = (height * 1.45 * 0.5) / math.tan(math.atan(18.0 / data_cam.lens))
    # 위에서 볼 때는 배트가 크게 도니 더 물러선다.
    target = Vector((0, 0, centre))
    # **투수 쪽에서 본다.** 스윙은 앞에서 봐야 골반과 어깨가 갈리는 것이 읽힌다.
    scene.render.image_settings.file_format = 'PNG'
    stem = "ready" if READY else "swing"
    # **두 각도로 본다.** 앞에서만 보면 몸통이 도는 것이 안 읽힌다 — 둥근 옷이
    # 비틀림을 가린다. 위에서 봐야 골반과 어깨가 갈리는 것이 보인다.
    for tag, offset in (("front", Vector((-distance * 0.45, -distance * 0.86, 0.10))),
                        ("top", Vector((-distance * 0.42, -distance * 0.58, distance * 1.25)))):
        camera.location = target + (offset * (1.45 if tag == "top" else 1.0))
        camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = out_dir.rstrip("/") + "/%s_%s_" % (stem, tag)
        bpy.ops.render.render(animation=True)
    print("[batswing] 미리보기: %s" % out_dir)


def measured_yaw(poser, name):
    """그 뼈가 실제로 세계에서 몇 도 돌아가 있는가. **쓴 값이 아니라 결과를 잰다.**"""
    m = poser.arm.pose.bones[name].matrix.to_3x3()
    side = (m @ Vector((1.0, 0.0, 0.0)))     # 뼈의 로컬 +X
    side.z = 0.0
    if side.length < 1e-6:
        return float("nan")
    side.normalize()
    # +X 가 캐릭터의 왼쪽이므로, 오른쪽으로 감기면 각도가 +가 되게 부호를 맞춘다
    return math.degrees(math.atan2(-side.y, side.x))


def unwrap(deg):
    """±360 을 접는다. 잰 값이 -330 으로 나와 하마터면 뒤집힌 줄 알 뻔했다."""
    while deg > 180.0:
        deg -= 360.0
    while deg < -180.0:
        deg += 360.0
    return deg


def rig_scale(poser):
    """리그의 실제 치수를 찍는다. **가정하지 말고 본다** — 머리 구를 두 번, 목
    굵기를 한 번 잘못 재고 나서야 내가 단위를 모르고 있다는 것을 알았다."""
    zs = []
    for obj in bpy.data.objects:
        if obj.type == 'MESH':
            zs += [(obj.matrix_world @ Vector(c)).z for c in obj.bound_box]
    print("[rig] 메시 높이 %.3f  (z %.3f ~ %.3f)" % (max(zs) - min(zs), min(zs), max(zs)))
    for b in ("Waist", "Spine01", "Spine02", "Head", "L_Upperarm", "L_Forearm", "L_Hand"):
        w = poser.world(b)
        print("[rig] %-12s z=%.3f  x=%+.3f y=%+.3f" % (b, w.z, w.x, w.y))


def neck_axis(poser):
    """목 축 — 가슴에서 머리로 가는 선분. **굵기는 추정하지 않는다.**

    굵기를 재려고 세 번 시도했고 세 번 다 몸통 값이 나왔다(0.695 -> 0.409 -> 0.375m.
    키가 1.147m 인 캐릭터에서 전부 몸 전체다). 띠를 잘라도 그 높이의 <b>모든</b>
    정점이 들어오기 때문이다. 그래서 <b>거리만 그대로 낸다</b> — 고치기 전후를
    비교하는 데에는 그것으로 충분하고, 부풀 곳이 없다.
    """
    return poser.world("Spine02"), poser.world("Head")


def segments_gap(p0, p1, q0, q1, steps=12):
    """두 선분 사이의 최단 거리(근사). 팔도 목도 선분이라 점-선분으로는 못 잰다."""
    best = 1e9
    for i in range(steps + 1):
        p = p0 + (p1 - p0) * (i / steps)
        best = min(best, segment_distance(q0, q1, p))
    return best


def segment_distance(a, b, point):
    ab = b - a
    if ab.length < 1e-6:
        return (point - a).length
    t = max(0.0, min(1.0, (point - a).dot(ab) / ab.length_squared))
    return (point - (a + ab * t)).length


def clearance(arm):
    """팔·배트가 목 축에서 얼마나 떨어져 있는가(m). 클수록 안 겹친다."""
    poser = Poser(arm)
    pose(poser, pose_at(0.0))
    rig_scale(poser)
    print("[clear] frame  t     왼위팔 왼아래팔 오른위팔 오른아래팔  배트   (목 축까지 m)")
    worst = (99.0, 0, "")
    for i in range(FRAMES):
        t = i / (FRAMES - 1)
        f = pose_at(t)
        pose(poser, f)
        a, b = neck_axis(poser)
        vals = []
        for side in ("L", "R"):
            up = poser.world(side + "_Upperarm")
            fo = poser.world(side + "_Forearm")
            ha = poser.world(side + "_Hand")
            # **마디를 쪼개 본다.** 합쳐서 최소만 보면 어느 마디가 붙는지 모르고,
            # 엉뚱한 곳을 고치게 된다 — 팔꿈치를 내렸는데 값이 0.067 -> 0.068 로
            # 소수점까지 그대로 나왔다.
            vals.append(segments_gap(up, fo, a, b))
            vals.append(segments_gap(fo, ha, a, b))
        grip = f["grip"]
        vals.append(segments_gap(grip, grip + f["bat_dir"] * BAT_LENGTH, a, b))
        for name, v in zip(("왼위팔", "왼아래팔", "오른위팔", "오른아래팔", "배트"), vals):
            if v < worst[0]:
                worst = (v, i + 1, name)
        print("[clear] f%-5d %.3f  %s" % (i + 1, t, "  ".join("%6.3f" % v for v in vals)))
    print("[clear] 가장 가까운 것: %s f%d  %.3f m" % (worst[2], worst[1], worst[0]))


def dump(arm):
    poser = Poser(arm)
    print("[dump] frame    t      골반(잰값)  가슴(잰값)   분리    앞무릎  뒷발")
    base = None
    for i in range(FRAMES):
        t = i / (FRAMES - 1)
        f = pose_at(t)
        pose(poser, f)
        hip = measured_yaw(poser, "Waist")
        chest = measured_yaw(poser, "Spine02")
        if base is None:
            base = (hip, chest)
        hip = unwrap(hip - base[0])
        chest = unwrap(chest - base[1])
        knee = math.degrees(Vector(f["thighL"]).angle(Vector(f["calfL"])))
        print("[dump] f%-6d %.3f  %8.1f  %9.1f  %7.1f  %6.1f  %5.2f"
              % (i + 1, t, hip, chest, chest - hip, knee, f["heel"]))
    print("[dump] 쓴 값과 비교 (절대 yaw):")
    for i in range(FRAMES):
        t = i / (FRAMES - 1)
        h, c, hd = body_yaw(t)
        print("[dump]   f%d  골반 %+6.1f  가슴 %+6.1f  머리 %+6.1f  분리 %+6.1f"
              % (i + 1, h, c, hd, c - h))


def arg(argv, name, fallback):
    return argv[argv.index(name) + 1] if name in argv else fallback


def main():
    argv = sys.argv
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    if "--dump" in argv:
        dump(arm)
        return
    if "--clearance" in argv:
        clearance(arm)
        return
    slow = int(arg(argv, "--slowmo", 0))
    if slow:
        # 눈으로 보려고 촘촘히 뽑는 길. FBX 는 굽지 않는다.
        bat = make_bat()
        last = build(arm, bat, samples=slow)
        preview(argv[argv.index("--preview") + 1], last)
        print("[batswing] 슬로모션 %d 장" % last)
        return
    bat = make_bat() if "--preview" in argv else None
    last = build(arm, bat)
    export_fbx(arm, last)          # use_selection 이라 배트는 안 나간다
    if "--preview" in argv:
        preview(argv[argv.index("--preview") + 1], last)
    print("[batswing] %s · %d 프레임 · %.3f초" % (NAME, last, last / FPS))


main()
