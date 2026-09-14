using UnityEngine;

namespace Metagame.Gameplay
{
    public sealed class AstraFerrisWheel : MonoBehaviour
    {
        [SerializeField] Transform rotor;
        [SerializeField] Rigidbody[] cabins;
        [SerializeField] float radius=9f, degreesPerSecond=6f;
        float phase;
        public float Phase=>phase;
        public void Configure(Transform wheel,Rigidbody[] seats){rotor=wheel;cabins=seats;}
        public void ApplyPhase(float value)=>phase=Mathf.Repeat(value,360f);
        public static Vector3 CabinOffset(float angle,float radius)
        {float r=angle*Mathf.Deg2Rad;return new Vector3(0,-Mathf.Cos(r)*radius,Mathf.Sin(r)*radius);}
        void FixedUpdate()
        {
            // Remote peers extrapolate between authoritative phase snapshots.
            phase=Mathf.Repeat(phase+degreesPerSecond*Time.fixedDeltaTime,360f);
            if(rotor!=null)rotor.localRotation=Quaternion.Euler(-phase,0,0);
            if(cabins==null)return;
            for(int i=0;i<cabins.Length;i++)if(cabins[i]!=null)
            {
                cabins[i].MovePosition(transform.position+CabinOffset(phase+i*360f/cabins.Length,radius));
                cabins[i].MoveRotation(Quaternion.identity);
            }
        }
    }
}
