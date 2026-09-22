using UnityEngine;

namespace Metagame.Gameplay.Stage2
{
    /// <summary>Cosmetic followers of the existing replicated yard. Never writes gameplay state.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class Stage2ArtMotion : MonoBehaviour
    {
        [SerializeField] Stage2Yard _yard;
        [SerializeField] Transform _trolley, _hook, _ladderFrame;
        [SerializeField] Renderer[] _boltRenderers;
        float _trolleyHeight;
        MaterialPropertyBlock _block;
        public void Configure(Stage2Yard yard, Transform trolley, Transform hook, Transform ladder, Renderer[] bolts)
        { _yard=yard;_trolley=trolley;_hook=hook;_ladderFrame=ladder;_boltRenderers=bolts; }
        void Awake() { _block=new MaterialPropertyBlock();_trolleyHeight=_trolley.position.y; }
        void LateUpdate()
        {
            if(_yard==null)return;
            _trolley.position=new Vector3(_hook.position.x,_trolleyHeight,_hook.position.z);
            float height=_yard.State.LadderDeployed?_yard.State.LadderHeight:.8f;
            if (_yard.DemoCinematic)
            {
                int part = Mathf.Clamp((int)((_yard.DemoElapsed - Stage2Yard.DemoMoveSeconds - Stage2Yard.DemoOverviewSeconds)
                    / Stage2Yard.DemoAssemblySeconds), 0, _yard.DemoPartCount - 1);
                height = _yard.DemoSocket(part).y - .8f;
            }
            _ladderFrame.localScale=new Vector3(1.55f,Mathf.Max(.2f,height/4f),1.1f);
            for(int i=0;i<_boltRenderers.Length;i++)
            {
                if(_boltRenderers[i]==null)continue;
                var part=_yard.State.Parts[i/3];float progress=i%3==0?part.A:i%3==1?part.B:part.C;
                if (_yard.DemoCinematic) progress = _yard.DemoBoltProgress(i);
                _block.SetColor("_BaseColor",Color.Lerp(Color.white,new Color(.25f,1f,.45f),progress));
                _boltRenderers[i].SetPropertyBlock(_block);
            }
        }
    }
}
