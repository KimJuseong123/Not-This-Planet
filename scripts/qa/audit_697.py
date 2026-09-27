"""Read-only serialized inventory and input mailbox counterexample; no Unity execution."""
import collections
import json
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parents[2]
ASSETS = ROOT / 'METAGAME/Assets'


def inventory(path):
    text = path.read_text(encoding='utf-8-sig')
    blocks = re.split(r'^--- !u!', text, flags=re.M)[1:]
    counts = collections.Counter()
    static = collections.Counter()
    materials = collections.Counter()
    for block in blocks:
        kind = block.split(' ', 1)[0]
        counts[kind] += 1
        if kind == '1':
            match = re.search(r'm_StaticEditorFlags: (\d+)', block)
            if match:
                static[match[1]] += 1
        if kind in ('23', '137'):
            match = re.search(r'm_Materials:\s*\n((?:  - .*\n)+)', block)
            if match:
                for guid in re.findall(r'guid: ([a-f0-9]+)', match[1]):
                    materials[guid] += 1
    names = {'1': 'game_objects', '23': 'mesh_renderers', '137': 'skinned_renderers',
             '54': 'rigidbodies', '64': 'mesh_colliders', '65': 'box_colliders',
             '135': 'sphere_colliders', '136': 'capsule_colliders',
             '108': 'lights', '205': 'lod_groups', '1001': 'prefab_instances'}
    return {'path': path.relative_to(ROOT).as_posix(),
            'serialized_records': {name: counts[kind] for kind, name in names.items()},
            'static_flags_histogram': dict(static),
            'unique_direct_renderer_material_guids': len(materials),
            'direct_renderer_material_slots': sum(materials.values()),
            'occlusion_data_assigned': not bool(re.search(r'm_OcclusionCullingData: \{fileID: 0\}', text))}


def mailbox_counterexample():
    # Exact assignment semantics of CmdSendInput -> FixedUpdate; NOT a network reproduction.
    delivered = [{'seq': 1, 'transport_action': True, 'move': 1},
                 {'seq': 2, 'transport_action': False, 'move': 0}]
    pending = None
    for frame in delivered:
        pending = frame
    assert pending['seq'] == 2 and not pending['transport_action'] and pending['move'] == 0
    return {'kind': 'synthetic_assignment_model_only', 'delivered_before_one_physics_step': delivered,
            'physics_consumed': pending, 'first_action_consumed': False}


if __name__ == '__main__':
    result = {
        'limitations': 'Direct YAML records only: inactive objects included; prefab contents, runtime spawns, imported meshes and actual visible draw calls are NOT counted.',
        'scenes': [inventory(ASSETS / 'Scenes' / (name + '.unity'))
                   for name in ['Stage1_Astra', 'Stage2_Earth', 'PracticeLobby']],
        'input_mailbox': mailbox_counterexample(),
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))
