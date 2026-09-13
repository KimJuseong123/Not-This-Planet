using Metagame.Gameplay.Parts;
using UnityEngine;

namespace Metagame.Bootstrap
{
    /// <summary>Stage1_Ending 씬의 표시 에셋. 실제 배치·용접·참가자는 서버 스냅샷에서 받는다.</summary>
    public sealed class Stage1EndingBootstrap : MonoBehaviour
    {
        [Tooltip("엔진, 부양, 거주, 견인 순서의 메시 원본 프리팹")]
        [SerializeField] GameObject[] partVisuals = new GameObject[4];
        public GameObject[] PartVisuals => partVisuals;

        void Start() => EndingSceneFlow.Ensure().Attach(this);

        // Mirror의 OnClientSceneChanged가 실행되기 전에 게임 캐릭터 자동 생성을 끈다.
        void Awake()
        {
            EndingSceneFlow.Ensure().AttachBeforeReady();
        }

        public DemoEndingSnapshot CreatePreview()
        {
            var snapshot = new DemoEndingSnapshot
            {
                Parts = new DemoEndingPart[4], Players = new DemoEndingPlayer[4]
            };
            var height = 0f;
            for (var i = 0; i < 4; i++)
            {
                var spec = PartSpec.All[i];
                snapshot.Parts[i] = new DemoEndingPart
                {
                    Name = spec.Name, Position = Vector3.up * (height + spec.Height * 0.5f),
                    Rotation = Quaternion.identity, Scale = Vector3.one,
                    Beads = System.Array.Empty<WeldBead.ChainState>(), HelpfulBeads = System.Array.Empty<bool>()
                };
                height += spec.Height;
                snapshot.Players[i] = new DemoEndingPlayer
                {
                    Name = "Player " + (i + 1), Suit = new Color(0.93f, 0.94f, 0.96f),
                    Accent = Color.HSVToRGB(i / 4f, 0.8f, 0.9f)
                };
            }
            return snapshot;
        }
    }
}
