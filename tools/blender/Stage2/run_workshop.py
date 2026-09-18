"""Called by Blender MCP; serial timer keeps per-asset progress durable during rendering."""
import bpy, importlib.util, json, traceback
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3]

def start(ids):
    key='_stage2_workshop_job'
    if bpy.app.driver_namespace.get(key,{}).get('running'):raise RuntimeError('Another Stage2 job is active')
    spec=importlib.util.spec_from_file_location('stage2_assets',ROOT/'Tools/Blender/Stage2/build_assets.py')
    s2=importlib.util.module_from_spec(spec);spec.loader.exec_module(s2)
    report={'running':True,'pending':list(ids),'done':[],'errors':[]}
    bpy.app.driver_namespace[key]=report
    path=ROOT/'artifacts/stage2-models/workshop-progress.json'
    def save():path.write_text(json.dumps(report,indent=2),encoding='utf-8')
    def step():
        if not report['pending']:
            report['running']=False;save();return None
        asset=report['pending'].pop(0);report['current']=asset;save()
        try:
            s2.build(asset);data=s2.save_asset(asset);qa=s2.validate_fbx(asset)
            report['done'].append({'asset':asset,'triangles':data['triangles'],'passed':qa['passed']})
        except Exception:
            report['errors'].append({'asset':asset,'error':traceback.format_exc()})
        save();return .25
    save();bpy.app.timers.register(step,first_interval=1)
    return {'started':list(ids),'progress':str(path)}
