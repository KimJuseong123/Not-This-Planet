using System.Collections.Generic;
using Metagame.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 사람과 그가 나르는 것이 안개를 받지 않는지 검증한다 (S15P21B202-540).
    ///
    /// <para><b>무엇이 문제였나.</b> Stage1 의 안개는 <c>Linear</c> 이고 색이 중간
    /// 회색(0.443)이며 10m 에서 시작해 60m 에서 꽉 찬다. 그래서 35m 만 떨어져도
    /// 팀원이 <b>절반은 회색</b>이 되고, 고정 4색으로 서로를 구분하게 해 둔 것
    /// (<c>-375</c>)이 무의미해졌다.</para>
    ///
    /// <para><b>셰이더에는 이미 길이 있었다.</b> <see cref="Prim.Dough"/> 와
    /// <see cref="Prim.Ghost"/> 가 <c>fogExempt</c> 를 받아 <c>_FogStrength</c> 를 0 으로
    /// 두고 Haze 전체화면 패스의 스텐실 비트도 피한다. 그런데 사람 몸을 찍는
    /// 프리미티브 경로가 그 인자를 노출하지 않아 기본값 <c>false</c> 로만 불렸다.</para>
    /// </summary>
    public sealed class PlayerFogExemptionTests
    {
        readonly List<GameObject> _spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── 훑기 자체 ───────────────────────────────────────────

        [Test]
        public void ItExemptsEveryRendererUnderTheRoot()
        {
            var root = Track(Prim.Box("몸통", Vector3.zero, Vector3.one, Color.white));
            var child = Prim.Ball("장식", Vector3.zero, 0.2f, Color.red);
            child.transform.SetParent(root.transform, false);
            Track(child);

            Prim.SetFogExempt(root, true);

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                Assert.Zero(renderer.sharedMaterial.GetFloat("_FogStrength"),
                    renderer.name + " 이(가) 남으면 몸의 한 조각만 회색으로 뜬다.");
        }

        /// <summary>
        /// <b>장식이 나중에 늘어도 빠지지 않는다.</b> 생성 호출마다 인자를 다는 대신
        /// 다 만든 뒤 훑는 이유가 이것이다 — 팔 띠와 무릎테가 실제로 나중에 붙은
        /// 자리다.
        /// </summary>
        [Test]
        public void ADecorationAddedLaterIsCoveredToo()
        {
            var root = Track(Prim.Box("몸통", Vector3.zero, Vector3.one, Color.white));
            Prim.SetFogExempt(root, true);

            var late = Prim.Ball("나중에 붙인 띠", Vector3.zero, 0.1f, Color.yellow);
            late.transform.SetParent(root.transform, false);
            Track(late);
            Prim.SetFogExempt(root, true);      // 다시 훑는다

            Assert.Zero(late.GetComponent<Renderer>().sharedMaterial.GetFloat("_FogStrength"));
        }

        [Test]
        public void ItGoesBack()
        {
            var root = Track(Prim.Box("몸통", Vector3.zero, Vector3.one, Color.white));
            var before = root.GetComponent<Renderer>().sharedMaterial;

            Prim.SetFogExempt(root, true);
            Assert.AreNotSame(before, root.GetComponent<Renderer>().sharedMaterial,
                "예외로 바뀌어야 한다.");

            Prim.SetFogExempt(root, false);

            Assert.AreSame(before, root.GetComponent<Renderer>().sharedMaterial,
                "되돌리면 원본이어야 한다 — 사본이 쌓이면 드로우콜이 늘어난다.");
        }

        /// <summary>쌍둥이는 재질마다 하나만 만든다. 매번 만들면 배칭이 깨진다.</summary>
        [Test]
        public void TheTwinIsShared()
        {
            var a = Track(Prim.Box("가", Vector3.zero, Vector3.one, Color.white, 0.55f));
            var b = Track(Prim.Box("나", Vector3.zero, Vector3.one, Color.white, 0.55f));
            Assert.AreSame(a.GetComponent<Renderer>().sharedMaterial,
                           b.GetComponent<Renderer>().sharedMaterial,
                           "Dough 캐시가 원본을 공유하는 것이 전제다.");

            Prim.SetFogExempt(a, true);
            Prim.SetFogExempt(b, true);

            Assert.AreSame(a.GetComponent<Renderer>().sharedMaterial,
                           b.GetComponent<Renderer>().sharedMaterial,
                           "같은 원본이면 쌍둥이도 하나여야 한다.");
        }

        [Test]
        public void ANullRootIsFine()
        {
            Assert.DoesNotThrow(() => Prim.SetFogExempt(null, true));
        }

        // ── 실제 경로에 붙어 있는가 ────────────────────────────

        /// <summary>
        /// <b>이 검증이 이 이슈 자체다.</b> 사람 몸 전체가 안개를 받지 않아야 한다.
        ///
        /// <para>로컬과 원격이 같은 경로다 — <c>RemoteAvatar</c> 는
        /// <c>PlayerRig.Doll</c> 을 그대로 쓴다. 그래서 여기 한 곳이면 양쪽 다 덮는다.</para>
        /// </summary>
        [Test]
        public void ABuiltDollTakesNoFog()
        {
            LogAssert.ignoreFailingMessages = true;
            Doll doll;
            try
            {
                doll = RagdollBuilder.Build("검증용", Vector3.zero, Quaternion.identity,
                                            new Color(0.2f, 0.5f, 0.9f), Color.yellow);
            }
            finally { LogAssert.ignoreFailingMessages = false; }

            Track(doll.Root.gameObject);

            int checkedCount = 0;
            foreach (var renderer in doll.Root.GetComponentsInChildren<Renderer>(true))
            {
                var material = renderer.sharedMaterial;
                if (material == null || !material.HasProperty("_FogStrength")) continue;

                checkedCount++;
                Assert.Zero(material.GetFloat("_FogStrength"),
                    renderer.name + " 이(가) 안개를 받으면 60m 밖에서 회색으로 뭉갠다.");
            }

            Assert.Greater(checkedCount, 5, "몸 조각을 실제로 훑었는지 확인한다.");
        }

        /// <summary>
        /// 배선이 실제로 있는가. 위의 훑기 검증이 다 통과해도 부르는 자리가 없으면
        /// 게임에서는 아무 일이 일어나지 않는다.
        ///
        /// <para><b>파일 전체가 아니라 해당 메서드 안을 본다</b> — <c>-505</c> 에서
        /// 파일 전체를 보다가 호출을 지워도 통과하는 검증을 만들었다.</para>
        /// </summary>
        [Test]
        public void TheCarriedThingsAreWiredToo()
        {
            var tool = TestSource.Blank(TestSource.Read("Runtime/Gameplay/Tools/ToolBase.cs"));
            Assert.That(tool, Does.Contain("Prim.SetFogExempt(gameObject, true)"),
                "도구가 빠지면 사람만 또렷하고 든 방망이만 회색이다.");

            var part = TestSource.Blank(TestSource.Read("Runtime/Gameplay/Parts/SpaceshipPart.cs"));
            Assert.That(part, Does.Contain("Prim.SetFogExempt(gameObject, true)"),
                "파츠가 빠지면 나르는 목표물만 회색이다.");

            var doll = TestSource.Blank(TestSource.Read("Runtime/Gameplay/Player/RagdollBuilder.cs"));
            Assert.That(doll, Does.Contain("Prim.SetFogExempt(root, true)"),
                "사람이 빠지면 이 이슈가 그대로 남는다.");
        }

        // ── 모델을 쓰는 아바타 (실제로 보이는 그것) ────────────

        /// <summary>
        /// <b>여기가 진짜 함정이었다.</b> 프리미티브 몸은 <c>SpaceSuit.Available</c> 이면
        /// 숨겨지고 그 위에 블렌더 모델이 씌워진다(<c>RagdollBuilder</c> 의
        /// <c>primitiveLook</c>). 그 모델의 재질은 <c>CharacterSuitTint</c> 로 만드는데
        /// 이 셰이더에는 안개를 끌 손잡이가 아예 없었다 — <c>MixFog</c> 를 무조건 불렀다.
        /// 그래서 <b>사람들이 실제로 보는 아바타</b>는 몸을 아무리 예외로 돌려도
        /// 회색이었다.
        /// </summary>
        [Test]
        public void TheSuitTintShaderCanSkipFog()
        {
            var shader = Resources.Load<Shader>("CharacterSuitTint");
            Assert.IsNotNull(shader, "셰이더가 Resources 에 있어야 런타임에 잡힌다.");

            var material = new Material(shader);
            try
            {
                Assert.IsTrue(material.HasProperty("_FogStrength"),
                    "손잡이가 없으면 모델을 쓰는 아바타는 회색으로 남는다.");
                Assert.IsTrue(material.HasProperty("_AtmosphereFogExemptMask"),
                    "Haze 전체화면 패스도 같은 스텐실 비트로 피해야 한다 — "
                    + "DoughSurface 와 비트가 갈리면 몸의 절반만 빠진다.");
            }
            finally { Object.DestroyImmediate(material); }
        }

        /// <summary>
        /// <c>PlayerAppearance</c> 는 <c>RagdollBuilder</c> 가 훑은 <b>뒤에</b> 불리면서
        /// (<c>SpaceBootstrap</c> · <c>NetPlayer</c> 가 <c>Apply</c> 를 나중에 부른다)
        /// 모델의 재질을 통째로 새로 만들어 덮는다. 그래서 거기서도 걸어야 한다.
        ///
        /// <para><b>메서드 안만 본다</b> — 파일 전체를 보면 아래 <c>Piece</c> 의
        /// <c>fogExempt</c> 에 걸려 이 배선을 지워도 통과한다(<c>-505</c>).</para>
        ///
        /// <para><b>상수 이름으로 찾는다.</b> <see cref="TestSource.Blank"/> 가 문자열
        /// 리터럴을 지우므로 <c>"_FogStrength"</c> 를 글자로 찾으면 <b>영영 실패한다.</b>
        /// 셰이더 속성 이름을 <see cref="Prim.FogStrengthProperty"/> 로 모아 둔 덕에
        /// 코드로 남고, 덤으로 두 곳에 같은 글자를 박지 않게 된다.</para>
        /// </summary>
        [Test]
        public void TheAppearancePassKeepsTheExemption()
        {
            var source = TestSource.Read("Runtime/Gameplay/Player/PlayerAppearance.cs");
            var swap = TestSource.Blank(
                TestSource.Method(source, "void UseWhiteOnlyTintMaterials()"));

            Assert.That(swap, Does.Contain("Prim.FogStrengthProperty"),
                "새로 만든 재질이 기본값 1 로 남으면 모델 아바타만 다시 회색이 된다.");
        }

        /// <summary>커스터마이저 장식(헬멧 뿔·배지)도 몸과 같이 빠져야 한다.</summary>
        [Test]
        public void TheCustomizerTrinketsAreExemptToo()
        {
            var source = TestSource.Read("Runtime/Gameplay/Player/PlayerAppearance.cs");
            var piece = TestSource.Blank(TestSource.Method(source,
                "static Transform Piece(Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Color color)"));

            Assert.That(piece, Does.Contain("fogExempt"),
                "몸만 또렷하고 헬멧 뿔·배지만 회색이면 그것대로 눈에 띈다.");
        }

        GameObject Track(GameObject go)
        {
            _spawned.Add(go);
            return go;
        }
    }
}
