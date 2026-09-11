"""Re-import and validate the generated FBX files in a clean Blender scene."""

from __future__ import annotations

import json
import sys
from pathlib import Path

import bpy


PROJECT_ROOT = Path(__file__).resolve().parents[2]
MANIFEST_PATH = PROJECT_ROOT / "BlenderSource" / "SpaceFlop" / "model_manifest.json"
REQUIRED_BONES = {
    "Root", "Hips", "Spine", "Chest", "Neck", "Head",
    "LeftUpperArm", "LeftLowerArm", "LeftHand",
    "RightUpperArm", "RightLowerArm", "RightHand",
    "LeftUpperLeg", "LeftLowerLeg", "LeftFoot",
    "RightUpperLeg", "RightLowerLeg", "RightFoot",
}


def clear() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)


def fail(message: str) -> None:
    raise RuntimeError(message)


def validate_asset(asset: dict) -> dict:
    clear()
    fbx = PROJECT_ROOT / asset["fbx"]
    if not fbx.exists() or fbx.stat().st_size < 1024:
        fail(f"Missing or empty FBX: {fbx}")

    bpy.ops.import_scene.fbx(filepath=str(fbx), use_custom_props=True)
    objects = list(bpy.context.scene.objects)
    meshes = [obj for obj in objects if obj.type == "MESH"]
    armatures = [obj for obj in objects if obj.type == "ARMATURE"]
    material_names = {
        slot.material.name
        for obj in meshes
        for slot in obj.material_slots
        if slot.material is not None
    }

    if len(meshes) < asset["mesh_objects"] * 0.90:
        fail(f"{asset['name']}: expected about {asset['mesh_objects']} meshes, imported {len(meshes)}")
    if len(material_names) < 8:
        fail(f"{asset['name']}: materials collapsed ({sorted(material_names)})")

    result = {
        "name": asset["name"],
        "fbx_bytes": fbx.stat().st_size,
        "mesh_objects_imported": len(meshes),
        "materials_imported": len(material_names),
        "armatures_imported": len(armatures),
    }

    if asset["name"] == "SF_Astronaut_Base":
        if len(armatures) != 1:
            fail(f"Character expected one armature, got {len(armatures)}")
        imported_bones = {bone.name for bone in armatures[0].data.bones}
        missing = sorted(REQUIRED_BONES - imported_bones)
        if missing:
            fail(f"Character is missing bones: {missing}")
        weighted = sum(1 for obj in meshes if obj.vertex_groups)
        if weighted < 45:
            fail(f"Character rigid skin weights missing: only {weighted} weighted meshes")
        result["bones"] = len(imported_bones)
        result["weighted_meshes"] = weighted
    else:
        role_props = sum(1 for obj in objects if obj.get("asset_role") is not None)
        repair_props = sum(1 for obj in objects if obj.get("repair_socket_id") is not None)
        if role_props < 70:
            fail(f"Ship custom role metadata missing: {role_props}")
        if repair_props < 10:
            fail(f"Ship repair metadata missing: {repair_props}")
        result["role_metadata_objects"] = role_props
        result["repair_metadata_objects"] = repair_props

    print("[VALID]", json.dumps(result, ensure_ascii=False))
    return result


def main() -> None:
    manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
    results = [validate_asset(asset) for asset in manifest["assets"]]
    report_path = MANIFEST_PATH.with_name("validation_report.json")
    report_path.write_text(json.dumps({"status": "PASS", "assets": results}, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"[VALID] PASS -> {report_path}")


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print(f"[VALID] FAIL: {exc}", file=sys.stderr)
        raise
