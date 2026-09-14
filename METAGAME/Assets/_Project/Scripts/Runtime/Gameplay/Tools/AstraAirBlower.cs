using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay.Parts;

namespace Metagame.Gameplay
{
    public sealed class AstraAirBlower : AstraHandTool
    {
        [SerializeField] LineRenderer stream;
        readonly Collider[] overlaps=new Collider[64];
        readonly Rigidbody[] visited=new Rigidbody[64];
        bool blowing;
        Vector3 direction=Vector3.forward;
        public override string UseHint => "클릭 유지 · 바람으로 공과 물건 밀기 · 8m";
        public override byte ViewAction => blowing ? (byte)1 : (byte)0;
        public void ConfigureStream(LineRenderer value)=>stream=value;
        protected override void ApplyRemoteVisualState(byte action,float progress,Vector3 aim)
        {blowing=action==1;direction=aim;}
        protected override void OnToolInput(ToolInputFrame input,ToolAimContext aim)
        {
            blowing=input.PrimaryHeld && Owner!=null && !Owner.Downed;
            direction=aim.Direction.normalized;
            if(blowing)Blow(direction);
        }
        public int Blow(Vector3 aim)
        {
            if(WorldEvents.RemoteDecides || !IsEquipped || Owner==null || Owner.Downed || aim.sqrMagnitude<.001f)return 0;
            aim.Normalize();var origin=AimOrigin;
            int count=Physics.OverlapSphereNonAlloc(origin+aim*4,4.5f,overlaps,~0,QueryTriggerInteraction.Ignore), unique=0, pushed=0;
            for(int i=0;i<count;i++)
            {
                var body=overlaps[i].attachedRigidbody;
                if(body==null || body.isKinematic || body==Body || body.GetComponentInParent<PlayerRig>()!=null)continue;
                bool duplicate=false;for(int j=0;j<unique;j++)if(visited[j]==body){duplicate=true;break;}
                if(duplicate)continue;visited[unique++]=body;
                var tool=body.GetComponent<ToolBase>();if(tool!=null && tool.IsHeldForView)continue;
                var part=body.GetComponent<SpaceshipPart>();
                if(part!=null && (part.State==PartState.Locked || part.State==PartState.Welded || part.IsDockedToPad))continue;
                var delta=body.worldCenterOfMass-origin;float forward=Vector3.Dot(delta,aim);
                if(forward<.4f || forward>8 || (delta-aim*forward).magnitude>.6f+forward*.22f)continue;
                if(FindSurface(delta.normalized,delta.magnitude+.2f,out var hit) && hit.rigidbody!=body)continue;
                body.AddForce(aim*Mathf.Min(1000f,body.mass*14f)*(1f-forward/10f));pushed++;
            }
            return pushed;
        }
        protected override void OnUnequipped(){blowing=false;}
        protected override void LateUpdate()
        {
            base.LateUpdate();if(stream==null)return;
            stream.enabled=blowing && IsHeldForView;
            if(stream.enabled){stream.SetPosition(0,Muzzle.position);stream.SetPosition(1,Muzzle.position+direction*8);}
        }
    }
}
