using System;
using UnityEngine;

namespace Metagame.Gameplay
{
    [DefaultExecutionOrder(240)]
    public sealed class AstraPlayerCannon : MonoBehaviour
    {
        public const float FuseSeconds=3f;
        [SerializeField] Rigidbody seat;
        [SerializeField] Transform launch,exit;
        [SerializeField] Grabbable entry,igniter;
        [SerializeField] LineRenderer fuse;
        [SerializeField] Transform spark;
        [SerializeField] Vector3 fuseStart,fuseEnd;
        PlayerRig rider,flying;
        float remaining,flight,waiting;
        Vector3 velocity,lastFlightPosition;
        bool burning;
        [Serializable]public struct State{public bool Occupied,Burning;public float Remaining;}
        State remote;
        public State Snapshot=>new State{Occupied=rider!=null,Burning=burning,Remaining=remaining};
        public PlayerRig Rider=>rider;
        public bool Burning=>burning;
        public void Configure(Rigidbody body,Transform muzzle,Transform wayOut,Grabbable mouth,Grabbable rear,LineRenderer line,Transform ember)
        {seat=body;launch=muzzle;exit=wayOut;entry=mouth;igniter=rear;fuse=line;spark=ember;fuseStart=line.GetPosition(0);fuseEnd=line.GetPosition(1);}
        public void ApplySnapshot(State value)=>remote=value;
        void OnEnable()=>Grabbable.AnyGrabbed+=OnGrabbed;
        void OnDisable(){Grabbable.AnyGrabbed-=OnGrabbed;Evacuate();flying=null;}
        void OnGrabbed(Grabbable target,Hand hand)
        {
            if(WorldEvents.RemoteDecides||hand==null)return;
            var player=hand.GetComponentInParent<PlayerRig>();
            if(target==entry)TryEnter(player);
            else if(target==igniter)TryIgnite(player);
        }
        public bool TryEnter(PlayerRig player)
        {
            if(WorldEvents.RemoteDecides||player==null||player.Downed||player.IsRiding||rider!=null||burning||seat==null||entry==null
                ||Vector3.Distance(player.Position,entry.transform.position)>3.8f)return false;
            if(!player.TryBoardShip(seat,seat.position))return false;
            if(player.Cam!=null&&launch!=null)
            {player.Cam.Yaw=launch.eulerAngles.y;player.Cam.Pitch=-Mathf.Asin(launch.forward.y)*Mathf.Rad2Deg;player.Cam.SnapToTarget();}
            rider=player;waiting=0;return true;
        }
        public bool TryIgnite(PlayerRig player)
        {
            if(WorldEvents.RemoteDecides||player==null||player.Downed||player.IsRiding||rider==null||burning||igniter==null
                ||Vector3.Distance(player.Position,igniter.transform.position)>3.8f)return false;
            burning=true;remaining=FuseSeconds;return true;
        }
        public void Evacuate()
        {
            if(!WorldEvents.RemoteDecides&&rider!=null&&rider.RidingBody==seat)
                rider.LeaveShip(exit!=null?exit.position:transform.position+Vector3.right*3,Quaternion.identity);
            rider=null;burning=false;remaining=0;
        }
        void FixedUpdate()=>Step(Time.fixedDeltaTime);
        public void Step(float dt)
        {
            if(WorldEvents.RemoteDecides||dt<=0)return;
            if(rider!=null&&(rider.Downed||rider.RidingBody!=seat))Evacuate();
            if(rider!=null&&!burning&&(waiting+=dt)>30f)Evacuate();
            if(burning)
            {
                remaining=Mathf.Max(0,remaining-dt);
                if(remaining<=0)
                {
                    burning=false;var passenger=rider;rider=null;
                    if(passenger!=null&&passenger.RidingBody==seat)
                    {
                        passenger.EjectFromShip(launch.forward);
                        passenger.Doll.Teleport(launch.position,Quaternion.LookRotation(Vector3.ProjectOnPlane(launch.forward,Vector3.up)));
                        flying=passenger;flight=1.1f;velocity=launch.forward*25f;lastFlightPosition=passenger.Position;
                    }
                }
            }
            if(flying!=null&&flight>0)
            {
                if(flying.Downed||flying.IsRiding||Vector3.Distance(flying.Position,lastFlightPosition)>5f){flying=null;return;}
                lastFlightPosition=flying.Position;
                velocity+=Physics.gravity*dt;
                foreach(var body in flying.Doll.Bodies)if(body!=null&&!body.isKinematic)body.linearVelocity=velocity;
                flight-=dt;if(flight<=0)flying=null;
            }
        }
        void LateUpdate()
        {
            var state=WorldEvents.RemoteDecides?remote:Snapshot;
            if(fuse!=null){fuse.enabled=!state.Occupied||!state.Burning||state.Remaining>0;fuse.SetPosition(0,fuseStart);fuse.SetPosition(1,Vector3.Lerp(fuseStart,fuseEnd,state.Burning?state.Remaining/FuseSeconds:1));}
            if(spark!=null){spark.gameObject.SetActive(state.Burning);spark.position=Vector3.Lerp(fuseStart,fuseEnd,state.Remaining/FuseSeconds);}
            if(entry!=null)entry.Label=state.Occupied?"대포 · 탑승 중":"대포 입구 · 잡아서 탑승";
            if(igniter!=null)igniter.Label=state.Burning?"도화선 · 3초 후 발사":state.Occupied?"대포 뒤 · 잡아서 점화":"먼저 대포에 탑승하세요";
        }
    }
}
