using UnityEngine;

namespace Metagame.Gameplay
{
    [DefaultExecutionOrder(235)]
    public sealed class AstraBouncePad : MonoBehaviour
    {
        readonly Collider[] hits=new Collider[48];
        readonly Rigidbody[] recent=new Rigidbody[64];
        readonly float[] nextTime=new float[64];
        int next;
        public int LaunchCount {get;private set;}
        void FixedUpdate()
        {
            if(WorldEvents.RemoteDecides)return;
            int count=Physics.OverlapBoxNonAlloc(transform.position+Vector3.up*.38f,new Vector3(1.7f,.28f,1.7f),hits,Quaternion.identity,~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++)
            {
                var body=hits[i].attachedRigidbody;
                if(body==null || body.isKinematic || body.transform.IsChildOf(transform))continue;
                var player=body.GetComponentInParent<PlayerRig>();
                if(player!=null)
                {
                    if(player.Downed || player.Doll==null || player.Position.y>transform.position.y+1f)continue;
                    foreach(var limb in player.Doll.Bodies)Launch(limb);
                }
                else Launch(body);
            }
        }
        public bool Launch(Rigidbody body)
        {
            if(WorldEvents.RemoteDecides || body==null || body.isKinematic)return false;
            for(int i=0;i<recent.Length;i++)if(recent[i]==body && Time.time<nextTime[i])return false;
            recent[next]=body;nextTime[next]=Time.time+1f;next=(next+1)%recent.Length;
            var v=body.linearVelocity;v.y=Mathf.Max(v.y,11f);body.linearVelocity=v;
            LaunchCount++;return true;
        }
    }
}
