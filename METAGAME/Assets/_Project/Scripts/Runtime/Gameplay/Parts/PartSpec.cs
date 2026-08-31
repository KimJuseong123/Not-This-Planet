using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 콜라이더 상자 하나. 높이는 파츠 <b>바닥</b>을 0 으로 재고, 가로·세로는 x·z 다.
    /// </summary>
    public readonly struct PartBox
    {
        public readonly float Bottom;
        public readonly float Top;
        public readonly float Width;    // x
        public readonly float Depth;    // z
        public readonly float CenterX;
        public readonly float CenterZ;

        public PartBox(float bottom, float top, float width, float depth,
                       float centerX = 0f, float centerZ = 0f)
        {
            Bottom = bottom;
            Top = top;
            Width = width;
            Depth = depth;
            CenterX = centerX;
            CenterZ = centerZ;
        }

        public float Height => Top - Bottom;
        public float Volume => Width * Depth * Height;

        /// <summary>상자 크기.</summary>
        public Vector3 Size => new(Width, Height, Depth);

        /// <summary>파츠 <b>중심</b>을 원점으로 본 상자 중심. 파츠 루트가 중심에 있기 때문이다.</summary>
        public Vector3 CenterFromPartCenter(float partHeight) =>
            new(CenterX, (Bottom + Top) * 0.5f - partHeight * 0.5f, CenterZ);
    }

    /// <summary>
    /// 우주선 파츠 한 종류의 물리 명세 — 실제 형태를 따르는 콜라이더 상자들과 질량.
    ///
    /// <b>왜 이 표가 필요한가.</b> 예전에는 네 파츠가 모두 같은 3 x 3.7 x 3m 상자와 같은
    /// 질량(500kg)을 썼다. 상세 기획(S15P21B202-107)은 그것을 두 곳에서 금지한다 —
    /// §7.1 "모든 파츠를 동일한 3 x 3.7 x 3m 상자로 늘리지 않는다", §25.3 "동일 크기 상자가
    /// 아닌 파츠별 Compound Collider 사용 · 중심 질량이 외형에 맞게 작동하도록 설정".
    ///
    /// 같은 상자를 쓰면 비뚤어진 적층이 만들어지지 않는다. 그러면 §22.3 의 발사 하중 시험도,
    /// §22.4 의 붕괴도 일어날 수 없다 — 기획의 실패 축 하나가 통째로 사라진다.
    ///
    /// <b>숫자의 출처.</b> 전부 프리팹 메시를 직접 재서 얻었다. 모델을 높이 10구간으로 잘라
    /// 각 구간의 x·z 폭을 재고, 폭이 크게 달라지는 경계에서 상자를 나눴다. 카탈로그
    /// (docs/asset-catalog.md §4) 의 표기 크기와도 일치한다.
    ///
    /// <b>중심 질량은 따로 지정하지 않는다.</b> Unity 가 붙어 있는 콜라이더들로부터 계산하므로,
    /// 상자가 실제 형태를 따르면 중심 질량과 관성 텐서가 저절로 외형을 따른다. 손으로 값을
    /// 넣으면 모델이 바뀔 때마다 어긋난다.
    /// </summary>
    public sealed class PartSpec
    {
        /// <summary>씬의 오브젝트 이름. 조립 순서와 같다.</summary>
        public readonly string Name;

        /// <summary>기획 §7.1 의 게임 내 이름.</summary>
        public readonly string DisplayName;

        /// <summary>스킨으로 얹는 프리팹의 이름. 경로는 에디터 도구가 안다.</summary>
        public readonly string SkinPrefab;

        public readonly PartBox[] Boxes;

        PartSpec(string name, string displayName, string skinPrefab, PartBox[] boxes)
        {
            Name = name;
            DisplayName = displayName;
            SkinPrefab = skinPrefab;
            Boxes = boxes;
        }

        /// <summary>바닥에서 꼭대기까지. 쌓았을 때의 층 높이가 이 값이다.</summary>
        public float Height
        {
            get
            {
                var top = 0f;
                for (var i = 0; i < Boxes.Length; i++) top = Mathf.Max(top, Boxes[i].Top);
                return top;
            }
        }

        /// <summary>가장 넓은 상자의 가로·세로. 배치 간격과 키아웃 반경에 쓴다.</summary>
        public Vector2 Footprint
        {
            get
            {
                float w = 0f, d = 0f;
                for (var i = 0; i < Boxes.Length; i++)
                {
                    w = Mathf.Max(w, Boxes[i].Width);
                    d = Mathf.Max(d, Boxes[i].Depth);
                }

                return new Vector2(w, d);
            }
        }

        public float Volume
        {
            get
            {
                var sum = 0f;
                for (var i = 0; i < Boxes.Length; i++) sum += Boxes[i].Volume;
                return sum;
            }
        }

        /// <summary>
        /// 부피에 밀도를 곱한 값. 파츠마다 손으로 정한 숫자를 넣지 않는 이유는, 그러면
        /// 모델이 바뀔 때 네 값을 따로 고쳐야 하고 서로의 비율이 슬그머니 어긋나기 때문이다.
        /// 튜닝은 <see cref="PhysicsScale.PartDensity"/> 하나만 만진다.
        /// </summary>
        public float Mass => Volume * PhysicsScale.PartDensity;

        /// <summary>
        /// 기획 §7.1 의 조립 순서 — 1층 주 추진 엔진, 2층 부양·안정화, 3층 승무원 거주,
        /// 4층 견인 조종. 예전 스킨 표는 이 순서가 정해지기 전에 시연 편의로 정한 것이라
        /// 1층과 2층이 뒤바뀌어 있었다.
        /// </summary>
        public static readonly PartSpec[] All =
        {
            // 실측 2.297 x 2.163 x 1.663. 아래가 좁은 받침, 가운데가 가장 넓고, 위로 좁아진다.
            new("Part_1", "주 추진 엔진 모듈", "PF_Stage1_Spaceship_Robot", new[]
            {
                new PartBox(0.00f, 0.22f, 0.94f, 0.94f),
                new PartBox(0.22f, 1.30f, 2.30f, 1.66f),
                new PartBox(1.30f, 2.163f, 1.60f, 1.63f),
            }),

            // 실측 2.299 x 1.330 x 2.292. 좁은 다리 위에 넓은 원반이 얹힌 형태다.
            new("Part_2", "부양·안정화 모듈", "PF_Stage1_Spaceship_HoverPod", new[]
            {
                new PartBox(0.00f, 0.27f, 1.42f, 1.42f),
                new PartBox(0.27f, 1.330f, 2.30f, 2.29f),
            }),

            // 실측 2.162 x 2.217 x 2.546. 아래가 앞뒤로 길고, 윗몸통은 뒤로 0.17m 치우쳐 있다.
            new("Part_3", "승무원 거주 모듈", "PF_Stage1_Spaceship_Capsule", new[]
            {
                new PartBox(0.00f, 0.44f, 2.16f, 2.55f),
                new PartBox(0.44f, 2.217f, 2.15f, 2.15f, centerZ: -0.17f),
            }),

            // 실측 1.902 x 2.448 x 1.848. 접시 위에 동체, 돔, 얇은 첨탑이 차례로 올라간다.
            new("Part_4", "견인 조종 모듈", "PF_Stage1_TractorCraft", new[]
            {
                new PartBox(0.00f, 0.98f, 1.90f, 1.85f),
                new PartBox(0.98f, 1.71f, 1.73f, 1.64f),
                new PartBox(1.71f, 1.96f, 0.92f, 0.92f),
                new PartBox(1.96f, 2.448f, 0.37f, 0.37f),
            }),
        };

        /// <summary>이름으로 찾는다. 없으면 null 이다.</summary>
        public static PartSpec Find(string name)
        {
            for (var i = 0; i < All.Length; i++)
                if (All[i].Name == name)
                    return All[i];

            return null;
        }

        /// <summary>네 파츠를 순서대로 쌓았을 때의 전체 높이. 기획 §7.1 은 약 8.2m 라고 적었다.</summary>
        public static float StackedHeight
        {
            get
            {
                var sum = 0f;
                for (var i = 0; i < All.Length; i++) sum += All[i].Height;
                return sum;
            }
        }
    }
}
