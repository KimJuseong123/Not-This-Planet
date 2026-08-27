"""Render catalog preview thumbnails for every FBX under Assets/_Project/Models.

Run from the repository root with Blender 5.x:

    & 'C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe' --background \\
        --python 'METAGAME/Tools/Blender/generate_asset_previews.py'

Output: docs/assets/previews/<fbx-stem>.png (512px, transparent background).
Re-run after adding or replacing an FBX, then update docs/asset-catalog.md.

The renders are deterministic: fixed 3/4 orthographic view, fixed three-point
lighting, and materials rebuilt from the BaseColor texture that the Unity
Material actually references (resolved through .meta GUIDs, not filename
guessing). FBX-embedded materials are discarded because they import with stray
alpha and render as translucent magenta.

Only Blender's own Python API is used — no extra packages.
"""

from __future__ import annotations

import math
import re
import sys
from pathlib import Path

import bpy
from mathutils import Vector

RES = 512
VIEW_DIR = Vector((1.0, -1.0, 0.62)).normalized()  # front-right, slightly above
MARGIN = 1.12                                       # ortho frame padding
NEUTRAL = (0.72, 0.72, 0.74, 1.0)                   # untextured fallback

SCRIPT_PATH = Path(__file__).resolve()
REPO_ROOT = SCRIPT_PATH.parents[3]                  # …/<repo>
ASSETS_ROOT = REPO_ROOT / "METAGAME" / "Assets"
MODELS_ROOT = ASSETS_ROOT / "_Project" / "Models"
OUT_DIR = REPO_ROOT / "docs" / "assets" / "previews"

BASECOLOR_RE = re.compile(r"basecolor|_color|albedo|diffuse", re.I)
NOT_BASECOLOR_RE = re.compile(r"normal|metallic|roughness|height|_ao$", re.I)
IMAGE_SUFFIXES = {".jpg", ".jpeg", ".png", ".tga"}


# --------------------------------------------------------------------------
# Unity asset graph: FBX -> BaseColor texture, via .meta GUIDs
# --------------------------------------------------------------------------

def build_guid_index() -> dict[str, Path]:
    index: dict[str, Path] = {}
    for meta in ASSETS_ROOT.rglob("*.meta"):
        text = meta.read_text(encoding="utf-8", errors="ignore")
        found = re.search(r"^guid:\s*([0-9a-f]{32})", text, re.M)
        if found:
            index[found.group(1)] = meta.with_suffix("")
    return index


def referenced_guids(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8", errors="ignore")
    return re.findall(r"guid:\s*([0-9a-f]{32})", text)


def build_texture_map() -> dict[str, Path | None]:
    """Map each FBX stem to the BaseColor texture it should render with."""
    guid_index = build_guid_index()

    # Material -> BaseColor texture
    material_basecolor: dict[Path, Path] = {}
    for mat in ASSETS_ROOT.rglob("*.mat"):
        for guid in referenced_guids(mat):
            target = guid_index.get(guid)
            if target and BASECOLOR_RE.search(target.stem) \
                    and not NOT_BASECOLOR_RE.search(target.stem):
                material_basecolor[mat.resolve()] = target.resolve()
                break

    # Prefab -> (FBX, Material) -> texture
    tex_map: dict[str, Path | None] = {}
    for prefab in ASSETS_ROOT.rglob("*.prefab"):
        fbx = mat = None
        for guid in referenced_guids(prefab):
            target = guid_index.get(guid)
            if target is None:
                continue
            if target.suffix.lower() == ".fbx":
                fbx = target.resolve()
            elif target.suffix.lower() == ".mat":
                mat = target.resolve()
        if fbx:
            tex_map[fbx.stem] = material_basecolor.get(mat) if mat else None

    # Fall back to the FBX's own .fbm embedded texture folder
    for fbx in MODELS_ROOT.rglob("*.fbx"):
        if tex_map.get(fbx.stem):
            continue
        fbm = fbx.with_suffix(".fbm")
        if fbm.is_dir():
            candidates = [
                p for p in sorted(fbm.iterdir())
                if p.suffix.lower() in IMAGE_SUFFIXES
                and BASECOLOR_RE.search(p.stem)
                and not NOT_BASECOLOR_RE.search(p.stem)
            ]
            if candidates:
                tex_map[fbx.stem] = candidates[0].resolve()
                continue
        tex_map.setdefault(fbx.stem, None)

    return tex_map


# --------------------------------------------------------------------------
# Scene setup
# --------------------------------------------------------------------------

def pick_engine() -> str:
    prop = bpy.types.RenderSettings.bl_rna.properties["engine"]
    available = {item.identifier for item in prop.enum_items}
    for name in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
        if name in available:
            return name
    return next(iter(available))


def setup_render() -> None:
    scene = bpy.context.scene
    scene.render.engine = pick_engine()
    scene.render.resolution_x = RES
    scene.render.resolution_y = RES
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = True
    if scene.render.engine == "CYCLES":
        scene.cycles.samples = 48


def setup_world() -> None:
    world = bpy.data.worlds.new("PreviewWorld")
    world.use_nodes = True
    background = world.node_tree.nodes["Background"]
    background.inputs[0].default_value = (0.42, 0.44, 0.48, 1.0)
    background.inputs[1].default_value = 0.9
    bpy.context.scene.world = world


def mesh_bounds() -> tuple[Vector, Vector, float]:
    """World-space bounding box: (centre, dimensions, largest dimension)."""
    lo = Vector((math.inf,) * 3)
    hi = Vector((-math.inf,) * 3)
    found = False
    for obj in bpy.context.scene.objects:
        if obj.type not in {"MESH", "CURVE", "SURFACE", "FONT"}:
            continue
        for corner in obj.bound_box:
            point = obj.matrix_world @ Vector(corner)
            lo = Vector(min(lo[i], point[i]) for i in range(3))
            hi = Vector(max(hi[i], point[i]) for i in range(3))
            found = True
    if not found:
        return Vector((0, 0, 0)), Vector((0, 0, 0)), 1.0
    return (lo + hi) / 2.0, hi - lo, max(max(hi - lo), 1e-4)


def add_camera(target: Vector, size: float) -> None:
    data = bpy.data.cameras.new("PreviewCam")
    data.type = "ORTHO"
    data.ortho_scale = size * MARGIN
    cam = bpy.data.objects.new("PreviewCam", data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = target + VIEW_DIR * (size * 3.0)
    # Cameras look down local -Z, so aim local +Z back along VIEW_DIR.
    cam.rotation_euler = VIEW_DIR.to_track_quat("Z", "Y").to_euler()
    bpy.context.scene.camera = cam


def add_lights(target: Vector, size: float) -> None:
    setup = [
        ("Key", Vector((1.1, -1.3, 1.5)), 4.2),
        ("Fill", Vector((-1.5, -0.7, 0.5)), 1.5),
        ("Rim", Vector((-0.4, 1.4, 0.9)), 2.0),
    ]
    for name, direction, power in setup:
        d = direction.normalized()
        data = bpy.data.lights.new(name, type="AREA")
        data.energy = power * (size ** 2) * 12.0
        data.size = size * 1.6
        light = bpy.data.objects.new(name, data)
        bpy.context.scene.collection.objects.link(light)
        light.location = target + d * (size * 2.4)
        # Area lights emit along local -Z; aim local +Z away from the target.
        light.rotation_euler = d.to_track_quat("Z", "Y").to_euler()


def make_preview_material(tex_path: Path | None) -> bpy.types.Material:
    mat = bpy.data.materials.new("PreviewMat")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.45
    bsdf.inputs["Metallic"].default_value = 0.0

    if tex_path and tex_path.is_file():
        node = mat.node_tree.nodes.new("ShaderNodeTexImage")
        node.image = bpy.data.images.load(str(tex_path))
        node.location = (-400, 0)
        mat.node_tree.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
    else:
        bsdf.inputs["Base Color"].default_value = NEUTRAL
    return mat


# --------------------------------------------------------------------------

def render_one(fbx: Path, out_png: Path, tex_path: Path | None) -> dict:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    setup_render()
    setup_world()

    bpy.ops.import_scene.fbx(filepath=str(fbx))

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    fbx_materials = len({m.name for o in meshes for m in o.data.materials if m})
    triangles = sum(len(o.data.loop_triangles) or len(o.data.polygons) for o in meshes)

    preview_mat = make_preview_material(tex_path)
    for obj in meshes:
        obj.data.materials.clear()
        obj.data.materials.append(preview_mat)

    centre, dims, size = mesh_bounds()
    add_camera(centre, size)
    add_lights(centre, size)

    out_png.parent.mkdir(parents=True, exist_ok=True)
    bpy.context.scene.render.filepath = str(out_png)
    bpy.ops.render.render(write_still=True)

    return {
        "file": fbx.name,
        "meshes": len(meshes),
        "triangles": triangles,
        # Blender is Z-up: report width x height x depth in metres.
        "dims": (round(dims.x, 2), round(dims.z, 2), round(dims.y, 2)),
        "materials": fbx_materials,
        "texture": tex_path.name if tex_path else "(none - neutral grey)",
    }


def main() -> None:
    argv = sys.argv
    only = argv[argv.index("--") + 1:] if "--" in argv else []

    tex_map = build_texture_map()
    fbx_files = sorted(MODELS_ROOT.rglob("*.fbx"))
    if only:
        fbx_files = [f for f in fbx_files if f.stem in only]

    print(f"\n[preview] engine={pick_engine()} models={len(fbx_files)} out={OUT_DIR}")

    rows = []
    for fbx in fbx_files:
        out_png = OUT_DIR / f"{fbx.stem}.png"
        try:
            rows.append(render_one(fbx, out_png, tex_map.get(fbx.stem)))
            print(f"[preview] OK   {fbx.stem}")
        except Exception as exc:  # keep going, report at the end
            rows.append({"file": fbx.name, "error": str(exc)})
            print(f"[preview] FAIL {fbx.stem}: {exc}")

    print("\n[preview] measurements (paste into docs/asset-catalog.md)")
    for row in rows:
        if "error" in row:
            print(f"  {row['file']}: ERROR {row['error']}")
            continue
        w, h, d = row["dims"]
        print(
            f"  {row['file']}: {w}x{h}x{d} m | meshes {row['meshes']} "
            f"| tris {row['triangles']} | fbx-mats {row['materials']} "
            f"| tex {row['texture']}"
        )


if __name__ == "__main__":
    main()
