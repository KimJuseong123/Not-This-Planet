using UnityEngine;

namespace Metagame.Gameplay.Stage2
{
    public sealed class Stage2PressurePump : MonoBehaviour
    {
        public Stage2Yard Yard;
        public int Index;
        public Transform Nozzle, Needle, Wheel, Flap;
        public ParticleSystem Jet;
        public Light Beacon;
        public LineRenderer Warning;
        public const float Range=8f;
        public Vector3 Direction => Nozzle.forward;
        Quaternion _needle,_wheel,_flap;
        int _lastPhase=-1;
        void Awake()
        {
            _needle=Needle.localRotation;_wheel=Wheel.localRotation;_flap=Flap.localRotation;
        }
        public bool Contains(Vector3 point)
        {
            Vector3 d=point-Nozzle.position;float along=Vector3.Dot(d,Direction);
            return along>=0 && along<=Range && (d-Direction*along).sqrMagnitude<Mathf.Pow(.6f+along*.045f,2);
        }
        public bool ClearTo(PlayerRig rig)
        {
            var d=rig.Position-Nozzle.position;
            foreach(var hit in Physics.RaycastAll(Nozzle.position,d.normalized,d.magnitude,~0,QueryTriggerInteraction.Ignore))
                if(!hit.transform.IsChildOf(transform) && hit.collider.GetComponentInParent<PlayerRig>()!=rig)return false;
            return true;
        }
        void Update()
        {
            if(Yard==null)return;
            int phase=Yard.PressurePhase(Index);
            bool relieved=Yard.State.PressureRelief!=null && Index<Yard.State.PressureRelief.Length && Yard.State.PressureRelief[Index]>0;
            float clock=Yard.State.PressureClock;
            Needle.localRotation=_needle*Quaternion.AngleAxis(phase==1?Mathf.InverseLerp(12,18,clock%42)*-160:phase==2?-160:0,Vector3.forward);
            Wheel.localRotation=_wheel*Quaternion.AngleAxis(relieved?90:0,Vector3.forward);
            Flap.localRotation=_flap*Quaternion.AngleAxis(phase==2?0:165,Vector3.right);
            Beacon.enabled=phase>0 && (phase==2 || Mathf.Repeat(clock,.5f)<.25f);
            Warning.enabled=phase>0;
            if(phase!=_lastPhase)
            {
                if(phase==2)Jet.Play();else Jet.Stop(true,ParticleSystemStopBehavior.StopEmitting);
                _lastPhase=phase;
            }
        }
        void OnDisable()
        {
            if(Jet!=null)Jet.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            if(Beacon!=null)Beacon.enabled=false;
            if(Warning!=null)Warning.enabled=false;
            _lastPhase=-1;
        }
    }
}
