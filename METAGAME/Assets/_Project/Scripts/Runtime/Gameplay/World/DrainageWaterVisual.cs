using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Metagame.Gameplay
{
    [RequireComponent(typeof(DrainageWater), typeof(MeshRenderer))]
    public sealed class DrainageWaterVisual : MonoBehaviour
    {
        static readonly int Ripples = Shader.PropertyToID("_WaterRipples");
        readonly Vector4[] ripples = new Vector4[4];
        MaterialPropertyBlock properties;
        MeshRenderer surface;
        DrainageWater water;
        Camera camera;
        UniversalAdditionalCameraData data;
        CameraOverrideOption oldDepth, oldOpaque;
        void Awake()
        {
            water=GetComponent<DrainageWater>();surface=GetComponent<MeshRenderer>();properties=new MaterialPropertyBlock();
        }
        void LateUpdate()
        {
            var player=GameSession.Instance?.LocalPlayer;
            var next=player?.Cam != null ? player.Cam.GetComponent<Camera>() : null;
            if(next!=camera)
            {
                RestoreCamera();camera=next;
                if(camera!=null)
                {
                    data=camera.GetUniversalAdditionalCameraData();oldDepth=data.requiresDepthOption;oldOpaque=data.requiresColorOption;
                    data.requiresDepthOption=CameraOverrideOption.On;data.requiresColorOption=CameraOverrideOption.On;
                }
            }
            int count=0;
            foreach(var rig in PlayerRig.All)
            {
                if(count==4)break;
                if(rig==null||rig.Doll==null||!water.Contains(rig.Position))continue;
                var p=rig.Position;float speed=rig.Doll.Hips.linearVelocity.magnitude;
                ripples[count++]=new Vector4(p.x,p.z,Mathf.Clamp01(speed+.15f),0);
            }
            while(count<4)ripples[count++]=Vector4.zero;
            properties.SetVectorArray(Ripples,ripples);surface.SetPropertyBlock(properties);
        }
        void RestoreCamera()
        {
            if(data!=null){data.requiresDepthOption=oldDepth;data.requiresColorOption=oldOpaque;}
            data=null;camera=null;
        }
        void OnDisable()=>RestoreCamera();
        void OnGUI()
        {
            if(camera==null||!water.Contains(camera.transform.position)||camera.transform.position.y>=water.Surface)return;
            var old=GUI.color;GUI.color=new Color(.035f,.18f,.15f,.23f);
            GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture);GUI.color=old;
        }
    }
}
