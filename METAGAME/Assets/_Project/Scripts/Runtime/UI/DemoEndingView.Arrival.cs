using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using UnityEngine;

namespace Metagame.UI
{
    public sealed partial class DemoEndingView
    {
        Transform _arrival, _earth, _meteor, _burst;
        readonly List<Transform> _wreckParts = new(), _fragments = new(), _entryFire = new();
        readonly List<Vector3> _partHomes = new();
        readonly List<Quaternion> _partRotations = new();
        Vector3 _shipCenter;
        float _shipFit, _previousArrival = -1;
        AudioSource _arrivalAudio;
        AudioClip _impactSound, _warningSound;
        Mesh _meteorMesh;
        Texture2D _earthTexture;
        Material _earthMaterial;
        GUIStyle _arrivalCaption;

        void PrepareArrival()
        {
            if (_arrival != null) return;
            _arrival=NewRoot("Stage2 arrival cinematic",_stage);
            _shipCenter=_bounds.center-_launchBase;
            _shipFit=Mathf.Clamp(11f/Mathf.Max(1,_bounds.size.magnitude),.08f,2f);
            foreach(Transform part in _ship)
                if(part!=_flames){_wreckParts.Add(part);_partHomes.Add(part.localPosition);_partRotations.Add(part.localRotation);}

            _earth=MakeShape(PrimitiveType.Sphere,_arrival,new Vector3(37,-16,100),Vector3.one*88,new Color(.10f,.29f,.43f));
            _earth.name="Earth - nearest emergency landing planet";
            BuildEarthSurface();
            var starMaterial=GlowMaterial(new Color(.75f,.85f,1));
            foreach(Transform child in _stage)
                if(child.localScale.x<.5f&&child.GetComponent<MeshRenderer>()!=null)child.GetComponent<MeshRenderer>().sharedMaterial=starMaterial;
            _meteor=MakeShape(PrimitiveType.Sphere,_arrival,Vector3.zero,Vector3.one*2.8f,new Color(.20f,.14f,.12f));_meteor.name="Incoming meteor";
            var filter=_meteor.GetComponent<MeshFilter>();_meteorMesh=Instantiate(filter.sharedMesh);
            var vertices=_meteorMesh.vertices;
            for(int i=0;i<vertices.Length;i++){var v=vertices[i];vertices[i]*=1+.20f*Mathf.Sin(v.x*19+v.y*11)*Mathf.Cos(v.z*17);}
            _meteorMesh.vertices=vertices;_meteorMesh.RecalculateNormals();filter.sharedMesh=_meteorMesh;
            _burst=MakeShape(PrimitiveType.Sphere,_arrival,Vector3.zero,Vector3.one,Color.white);_burst.name="Hull impact flash";
            _burst.GetComponent<Renderer>().sharedMaterial=GlowMaterial(new Color(1,.57f,.13f));
            var hot=GlowMaterial(new Color(1,.28f,.035f));
            for(int i=0;i<52;i++)
            {
                var piece=MakeShape(PrimitiveType.Cube,_arrival,Vector3.zero,new Vector3(.13f+(i%4)*.09f,.08f,.25f+(i%7)*.08f),i%3==0?new Color(.75f,.57f,.27f):new Color(.43f,.51f,.55f));
                piece.name="Hull fragment "+i;_fragments.Add(piece);
            }
            for(int i=0;i<_wreckParts.Count;i++)
            {
                var fire=MakeShape(PrimitiveType.Sphere,_arrival,Vector3.zero,Vector3.one,Color.red);fire.name="Reentry flame "+i;
                fire.GetComponent<Renderer>().sharedMaterial=hot;_entryFire.Add(fire);
            }
            _arrivalAudio=_arrival.gameObject.AddComponent<AudioSource>();_arrivalAudio.playOnAwake=false;_arrivalAudio.spatialBlend=0;_arrivalAudio.volume=.45f;
            _impactSound=ArrivalSound("Hull impact",1.1f,true);_warningSound=ArrivalSound("Emergency beacon",.45f,false);
        }

        void BuildEarthSurface()
        {
            const int width=512,height=256;_earthTexture=new Texture2D(width,height,TextureFormat.RGB24,true);_earthTexture.name="Earth cinematic surface";
            var pixels=new Color[width*height];
            var continents=new[]{new Vector4(-109,43,34,30),new Vector4(-60,-17,18,37),new Vector4(18,1,24,36),new Vector4(63,47,72,23),new Vector4(112,17,23,23),new Vector4(135,-26,22,15),new Vector4(-43,73,18,12)};
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                float lon=(float)x/(width-1)*360-180,lat=(float)y/(height-1)*180-90;
                float noise=Mathf.PerlinNoise((lon+190)*.10f,(lat+100)*.10f),land=-10;
                foreach(var c in continents){float dx=Mathf.DeltaAngle(c.x,lon)/c.z,dy=(lat-c.y)/c.w;land=Mathf.Max(land,1-dx*dx-dy*dy+(noise-.5f)*.75f);}
                var sea=Color.Lerp(new Color(.035f,.12f,.23f),new Color(.10f,.29f,.40f),noise);
                var soil=Color.Lerp(new Color(.18f,.26f,.13f),new Color(.42f,.39f,.24f),Mathf.Clamp01(1-Mathf.Abs(lat-24)/23));
                Color color=Color.Lerp(sea,soil,Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.06f,.06f,land)));
                if(Mathf.Abs(lat)>72+noise*8)color=Color.Lerp(color,new Color(.77f,.83f,.82f),.9f);
                float clouds=Mathf.PerlinNoise((lon+190)*.026f,(lat+100)*.057f);
                color=Color.Lerp(color,new Color(.79f,.83f,.84f),Mathf.Clamp01((clouds-.57f)*3)*.7f);pixels[y*width+x]=color;
            }
            _earthTexture.SetPixels(pixels);_earthTexture.Apply();_earthTexture.wrapModeU=TextureWrapMode.Repeat;_earthTexture.wrapModeV=TextureWrapMode.Clamp;
            _earthMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="Cinematic Earth"};_earthMaterial.SetTexture("_BaseMap",_earthTexture);_earthMaterial.SetColor("_BaseColor",Color.white);_earthMaterial.SetFloat("_Smoothness",.12f);
            _earth.GetComponent<Renderer>().sharedMaterial=_earthMaterial;_earth.localRotation=Quaternion.Euler(8,120,-14);
        }

        static AudioClip ArrivalSound(string name,float duration,bool impact)
        {
            const int rate=22050;var samples=new float[(int)(rate*duration)];var random=new System.Random(603);
            for(int i=0;i<samples.Length;i++)
            {
                float t=(float)i/rate,envelope=Mathf.Min(1,t/.008f)*Mathf.Exp(-t*(impact?4:7));
                samples[i]=envelope*(impact?((float)random.NextDouble()*2-1)*.65f+Mathf.Sin(t*2*Mathf.PI*62)*.3f:Mathf.Sin(t*2*Mathf.PI*740)*.3f);
            }
            var clip=AudioClip.Create(name,samples.Length,1,rate,false);clip.SetData(samples,0);return clip;
        }

        // Every pose is sampled from shared elapsed time: no physics, random frame increments or local transition decisions.
        void ShowArrival(float elapsed)
        {
            PrepareArrival();float t=Mathf.Clamp(elapsed,0,DemoEnding.ArrivalSeconds);
            _camera.cullingMask=1<<StageLayer;_camera.backgroundColor=new Color(.004f,.008f,.021f);_camera.fieldOfView=48;
            _ground.gameObject.SetActive(false);_planet.gameObject.SetActive(false);_flames.gameObject.SetActive(false);
            foreach(var actor in _actors)actor.Root.gameObject.SetActive(false);
            float after=Mathf.Max(0,t-8),descent=Mathf.Clamp01((t-14)/9);
            Vector3 center=t<8?new Vector3(Mathf.Lerp(-10,0,t/8),16+Mathf.Sin(t*.45f)*.6f,0):new Vector3(0,16-descent*24,descent*45);
            Quaternion rotation=Quaternion.Euler(12+after*7,18+after*10,-78-after*15);
            _ship.localScale=Vector3.one*_shipFit;_ship.localRotation=rotation;_ship.localPosition=center-rotation*(_shipCenter*_shipFit);
            for(int i=0;i<_wreckParts.Count;i++)
            {
                float spread=Mathf.Min(after,6)*.85f;
                var direction=new Vector3(i%2==0?-1:1,.45f+(i%3)*.3f,(i-1.5f)*.4f);
                _wreckParts[i].localPosition=_partHomes[i]+direction*spread/_shipFit;
                _wreckParts[i].localRotation=Quaternion.Euler(after*(i+1)*17,after*(i%2==0?25:-29),after*19)*_partRotations[i];
                _entryFire[i].gameObject.SetActive(t>=14&&t<22);
                _entryFire[i].position=_wreckParts[i].position+new Vector3(-1,.8f,-1);
                _entryFire[i].localScale=new Vector3(.7f,1,3.2f+Mathf.Sin(t*25+i)*.4f)*(1+descent);
                _entryFire[i].localRotation=Quaternion.Euler(-25,-30,0);
            }
            _earth.localPosition=Vector3.Lerp(new Vector3(37,-16,100),new Vector3(0,-197,70),descent);
            _earth.localScale=Vector3.one*Mathf.Lerp(88,420,descent);
            _meteor.gameObject.SetActive(t>5.4f&&t<9.2f);
            _meteor.localPosition=t<8?Vector3.Lerp(new Vector3(36,42,-8),new Vector3(0,16,0),Mathf.Clamp01((t-5.4f)/2.6f)):new Vector3(-(t-8)*19,16-(t-8)*8,(t-8)*5);
            _meteor.localRotation=Quaternion.Euler(t*63,t*91,t*37);
            _burst.gameObject.SetActive(t>=8&&t<8.6f);_burst.localPosition=new Vector3(0,16,0);_burst.localScale=Vector3.one*Mathf.Lerp(2,13,Mathf.Clamp01(after/.6f));
            for(int i=0;i<_fragments.Count;i++)
            {
                var fragment=_fragments[i];fragment.gameObject.SetActive(t>=8&&t<22);
                float angle=i*2.399963f;
                var direction=new Vector3(Mathf.Cos(angle),Mathf.Sin(angle)*.8f,Mathf.Sin(i*3.7f));
                fragment.localPosition=new Vector3(0,16,0)+direction*(after*(2+i%7)*.5f)+new Vector3(0,-descent*18,descent*36);
                fragment.localRotation=Quaternion.Euler(after*(47+i),after*83,after*61);
            }
            float shake=Mathf.Exp(-after*3)*(t>=8?1:0)+descent*.13f;
            _camera.transform.localPosition=Vector3.Lerp(new Vector3(0,21,-34),new Vector3(10,11,8),descent)+new Vector3(Mathf.Sin(t*57),Mathf.Sin(t*71),0)*shake;
            _camera.transform.LookAt(StageOrigin+center);
            if(_previousArrival<8&&t>=8&&t<8.25f)_arrivalAudio.PlayOneShot(_impactSound);
            if(_previousArrival<14&&t>=14&&t<14.25f)_arrivalAudio.PlayOneShot(_warningSound);
            if(_previousArrival<21.6f&&t>=21.6f&&t<21.85f)_arrivalAudio.PlayOneShot(_impactSound);
            _previousArrival=t;
        }

        void DrawArrivalOverlay(float t)
        {
            var saved=GUI.color;int depth=GUI.depth;GUI.depth=-900;
            GUI.color=Color.black;float bar=Screen.height*.10f;
            GUI.DrawTexture(new Rect(0,0,Screen.width,bar),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0,Screen.height-bar,Screen.width,bar),Texture2D.whiteTexture);
            _arrivalCaption??=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter,fontSize=24};_arrivalCaption.normal.textColor=Color.white;
            _arrivalCaption.fontSize=Mathf.Clamp(Screen.height/32,16,36);GUI.color=Color.white;
            string caption=t<5.5f?"우주 항해 중":t<8?"충돌 경보":t<14?"선체 파손 · 항로 이탈":"가장 가까운 행성: 지구 · 비상 착륙";
            GUI.Label(new Rect(0,Screen.height-bar,Screen.width,bar),caption,_arrivalCaption);
            float fade=Mathf.Max(1-Mathf.Clamp01(t/.8f),Mathf.Clamp01((t-21.7f)/1.3f));
            float flash=t>=8&&t<8.2f?1-(t-8)/.2f:t>=21.6f&&t<21.85f?1-(t-21.6f)/.25f:0;
            if(flash>0){GUI.color=new Color(1,.72f,.34f,flash*.7f);GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture);}
            if(fade>0){GUI.color=new Color(0,0,0,fade);GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture);}
            GUI.color=saved;GUI.depth=depth;
        }
        void DisposeArrival()
        {if(_meteorMesh!=null)Destroy(_meteorMesh);if(_impactSound!=null)Destroy(_impactSound);if(_warningSound!=null)Destroy(_warningSound);if(_earthTexture!=null)Destroy(_earthTexture);if(_earthMaterial!=null)Destroy(_earthMaterial);}
    }
}
