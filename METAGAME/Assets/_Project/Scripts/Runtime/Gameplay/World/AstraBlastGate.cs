using System;
using UnityEngine;

namespace Metagame.Gameplay
{
    public sealed class AstraBlastGate : MonoBehaviour
    {
        [SerializeField] Rigidbody charge;
        [SerializeField] GameObject rocks;
        [SerializeField] Grabbable detonator;
        [SerializeField] LineRenderer cable;
        [SerializeField] Transform blast;
        [SerializeField] Transform[] fragments;
        Vector3 home;Quaternion rotation;Vector3 explosion;
        bool cleared,spent;float age=99;
        [Serializable]public struct State{public bool Cleared,Spent;public float Age;public Vector3 Explosion;}
        public State Snapshot=>new State{Cleared=cleared,Spent=spent,Age=age,Explosion=explosion};
        public bool Cleared=>cleared;
        public bool ChargePlacedForClear=>!spent&&!cleared&&charge!=null&&rocks!=null
            &&Vector3.Distance(charge.worldCenterOfMass,rocks.transform.position)<3.5f
            &&(charge.GetComponent<Grabbable>()==null||charge.GetComponent<Grabbable>().HandCount==0);
        public void Configure(Rigidbody tnt,GameObject rubble,Grabbable button,LineRenderer wire,Transform flash,Transform[] chips)
        {charge=tnt;rocks=rubble;detonator=button;cable=wire;blast=flash;fragments=chips;}
        void Awake(){if(charge!=null){home=charge.position;rotation=charge.rotation;}}
        void OnEnable()=>Grabbable.AnyGrabbed+=OnGrabbed;
        void OnDisable()=>Grabbable.AnyGrabbed-=OnGrabbed;
        void OnGrabbed(Grabbable target,Hand hand)
        {if(target==detonator&&hand!=null)TryDetonate(hand.GetComponentInParent<PlayerRig>());}
        public bool TryDetonate(PlayerRig player)
        {
            if(WorldEvents.RemoteDecides||spent||cleared||charge==null||player==null||player.Downed||player.IsRiding||detonator==null
                ||Vector3.Distance(player.Position,detonator.transform.position)>3.8f)return false;
            var grab=charge.GetComponent<Grabbable>();if(grab!=null&&grab.HandCount>0)return false;
            explosion=charge.worldCenterOfMass;spent=true;age=0;
            cleared=rocks!=null&&Vector3.Distance(explosion,rocks.transform.position)<3.5f;
            return true;
        }
        public void ApplySnapshot(State value){cleared=value.Cleared;spent=value.Spent;age=Mathf.Max(0,value.Age);explosion=value.Explosion;}
        void Update()
        {
            age+=Time.deltaTime;
            if(!WorldEvents.RemoteDecides&&spent&&!cleared&&age>=4)
            {spent=false;charge.position=home;charge.rotation=rotation;charge.linearVelocity=Vector3.zero;charge.angularVelocity=Vector3.zero;}
            if(charge!=null&&charge.gameObject.activeSelf==spent)charge.gameObject.SetActive(!spent);
            if(rocks!=null)rocks.SetActive(!cleared);
            if(cable!=null&&charge!=null){cable.enabled=!spent;cable.SetPosition(0,detonator.transform.position);cable.SetPosition(1,charge.position);}
            if(blast!=null){blast.gameObject.SetActive(spent&&age<.45f);blast.position=explosion;blast.localScale=Vector3.one*(.2f+Mathf.Clamp01(age/.45f)*6);}
            if(fragments!=null)for(int i=0;i<fragments.Length;i++)
            {
                var chip=fragments[i];chip.gameObject.SetActive(cleared&&age<2);
                float a=i*2.39996f;chip.position=explosion+new Vector3(Mathf.Cos(a)*age*3,1+age*4-age*age*3,Mathf.Sin(a)*age*3);
                chip.rotation=Quaternion.Euler(i*31+age*90,age*120,i*13);
            }
            if(detonator!=null)detonator.Label=cleared?"통로 개방 완료":spent?"TNT 보충 중":"원격 기폭 스위치 · TNT를 바위 앞에 놓으세요";
        }
    }
}
