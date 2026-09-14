using System;
using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay.Parts;

namespace Metagame.Gameplay
{
    [DefaultExecutionOrder(230)]
    public sealed class AstraGrappleTool : AstraHandTool
    {
        public const float Range = 26f;
        [Serializable] public struct State { public bool Active; public Vector3 End; }
        [SerializeField] LineRenderer cable;
        Rigidbody target;
        Collider surface;
        Vector3 localPoint;
        State state;
        float age;
        public State Snapshot => state;
        public bool Attached => state.Active;
        public override string UseHint => Attached ? "누르고 있기 · 당기기 / 놓기 · 연결 해제" : "클릭 유지 · 벽으로 이동 / 물건 당기기 · 26m";
        public override byte ViewAction => Attached ? (byte)1 : (byte)0;
        public void ConfigureCable(LineRenderer value) => cable = value;
        public void ApplySnapshot(State value) => state = value;
        public bool Attach(Vector3 direction)
        {
            if(WorldEvents.RemoteDecides || !IsEquipped || Owner == null || Owner.Downed || Owner.IsRiding
                || direction.sqrMagnitude < .001f || !FindSurface(direction.normalized, Range, out var hit)) return false;
            // Never turn another player or a held tool into a tow target.
            if(hit.collider.GetComponentInParent<PlayerRig>() != null) return false;
            var tool=hit.collider.GetComponentInParent<ToolBase>();
            if(tool != null && tool.IsHeldForView) return false;
            target=hit.rigidbody; surface=hit.collider;
            localPoint=surface.transform.InverseTransformPoint(hit.point);
            state=new State{Active=true,End=hit.point};age=0f;
            return true;
        }
        public void Release() { state.Active=false; target=null; surface=null; }
        protected override void OnToolInput(ToolInputFrame input,ToolAimContext aim)
        {
            if(input.PrimaryPressed) Attach(aim.Direction);
            if(!input.PrimaryHeld) Release();
        }
        protected override void OnUnequipped() => Release();
        void OnDisable() => Release();
        void FixedUpdate() => Step(Time.fixedDeltaTime);
        public void Step(float dt)
        {
            if(WorldEvents.RemoteDecides || !Attached || dt<=0f) return;
            if(!IsEquipped || Owner==null || Owner.Downed || Owner.IsRiding || surface==null || (age+=dt)>8f)
            {Release();return;}
            state.End=surface.transform.TransformPoint(localPoint);
            Vector3 delta=state.End-AimOrigin;
            if(delta.magnitude>Range+3 || delta.magnitude<1.8f) {Release();return;}
            if(FindSurface(delta.normalized,delta.magnitude+.15f,out var obstruction)
                && obstruction.collider != surface && (target==null || obstruction.rigidbody!=target)
                && obstruction.distance<delta.magnitude-.3f) {Release();return;}
            if(target!=null)
            {
                // Pull through physics only: pins, locked and assembled parts keep their state.
                var part=target.GetComponent<SpaceshipPart>();
                if(part!=null && (part.State==PartState.Locked || part.State==PartState.Welded || part.IsDockedToPad))return;
                if(target.isKinematic)return;
                var held=target.GetComponent<ToolBase>();
                if(held!=null && held.IsHeldForView){Release();return;}
                Vector3 desired=-delta.normalized*Mathf.Min(5f,delta.magnitude);
                Vector3 force=Vector3.ClampMagnitude((desired-target.linearVelocity)*target.mass*4f,1800f);
                target.AddForceAtPosition(force,state.End);
            }
            else if(Owner.Doll!=null)
                foreach(var body in Owner.Doll.Bodies)
                    if(body!=null && !body.isKinematic)
                        body.linearVelocity=Vector3.MoveTowards(body.linearVelocity,delta.normalized*11f,32f*dt);
        }
        protected override void LateUpdate()
        {
            base.LateUpdate();
            if(cable==null)return;
            cable.enabled=state.Active && IsHeldForView;
            if(cable.enabled){cable.SetPosition(0,Muzzle.position);cable.SetPosition(1,state.End);}
        }
    }
}
