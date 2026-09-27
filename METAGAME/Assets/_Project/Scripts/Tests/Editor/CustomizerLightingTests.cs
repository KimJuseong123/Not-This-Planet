using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 커마 미리보기의 스포트라이트와 줌을 지킨다 (S15P21B202-631).
    ///
    /// <para><b>이 스포트라이트는 <c>Light</c> 컴포넌트가 아니라 그림이다.</b>
    /// 재 보니 이 씬에서는 광원이 캐릭터 표면에 전혀 기여하지 않았다 — 키 3.5,
    /// 필 2.5 를 전부 꺼도 렌더가 한 화소도 안 바뀌었다. 그리고 애초에 Unity 의
    /// 광원은 공기 중의 빛줄기를 그리지 않는다. 그래서 빛기둥·바닥 웅덩이·광원
    /// 원반을 가산 합성 메시로 직접 그린다.</para>
    /// </summary>
    public class CustomizerLightingTests
    {
        /// <summary>
        /// 연 씬을 반드시 닫는다 (S15P21B202-604). 열어 둔 채 끝나면 뒤이어 도는
        /// 검증들이 이 씬의 콜라이더를 만난다.
        /// </summary>
        [OneTimeTearDown]
        public void CloseAnyOpenScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static Texture2D shot;

        /// <summary>
        /// 미리보기 카메라가 그린 그림을 받아 온다.
        ///
        /// <para><b>URP 에서 <c>Camera.Render()</c> 를 직접 부르면 안 된다.</b>
        /// 그렇게 했더니 배경을 껐는데도 숫자가 한 글자도 안 바뀌었다 —
        /// 파이프라인이 그 호출을 타지 않아 내가 만든 RenderTexture 와 화면이
        /// 서로 다른 것을 보고 있었다. 대상 텍스처만 걸고 한 프레임 기다린다.</para>
        /// </summary>
        static IEnumerator Capture(Camera camera, bool backdropOn, bool characterOn)
        {
            // **GameObject.Find 는 꺼진 오브젝트를 못 찾는다.** 한 번 끄면 다시 켤 수
            // 없어, 배경 밝기를 잰다면서 카메라 단색을 쟀다. 자식에서 꺼진 것까지 훑는다.
            var backdrop = camera.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "Customizer Space Background");
            bool hadBackdrop = backdrop != null && backdrop.gameObject.activeSelf;
            if (backdrop != null) backdrop.gameObject.SetActive(backdropOn);

            var appearance = Object.FindAnyObjectByType<PlayerAppearance>();
            bool hadCharacter = appearance != null && appearance.gameObject.activeSelf;
            if (appearance != null) appearance.gameObject.SetActive(characterOn);

            var rt = new RenderTexture(360, 640, 24);
            var prevTarget = camera.targetTexture;
            var prevRect = camera.rect;
            camera.targetTexture = rt;
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            yield return null;
            yield return new WaitForEndOfFrame();

            var active = RenderTexture.active;
            RenderTexture.active = rt;
            shot = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            shot.Apply();
            RenderTexture.active = active;

            camera.targetTexture = prevTarget;
            camera.rect = prevRect;
            rt.Release();
            Object.DestroyImmediate(rt);
            if (backdrop != null) backdrop.gameObject.SetActive(hadBackdrop);
            if (appearance != null) appearance.gameObject.SetActive(hadCharacter);
        }

        static void Measure(out float mean, out float saturated, out float coverage)
        {
            int w = shot.width, h = shot.height;
            int x0 = w * 3 / 10, x1 = w * 7 / 10;
            int y0 = h * 2 / 10, y1 = h * 8 / 10;
            double total = 0; int lit = 0, blown = 0, seen = 0;
            var pixels = shot.GetPixels();
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    var c = pixels[y * w + x];
                    float l = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
                    seen++;
                    if (l < 0.12f) continue;
                    lit++;
                    total += l;
                    if (l > 0.97f) blown++;
                }
            mean = lit > 0 ? (float)(total / lit) : 0f;
            saturated = lit > 0 ? blown / (float)lit : 0f;
            coverage = seen > 0 ? lit / (float)seen : 0f;
            Object.DestroyImmediate(shot);
            shot = null;
        }

        static GameObject Find(string name) =>
            Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(g => g.name == name);

        [Category("Heavy")]
        [UnityTest]
        public IEnumerator TheStageSpotlightIsDrawnAndLinedUpWithTheCharacter()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Customizer.unity");
            yield return new EnterPlayMode();
            yield return null;
            yield return null;

            var beam = Find("Customizer Spot Beam");
            var pool = Find("Customizer Spot Pool");
            var lamp = Find("Customizer Spot Lamp");
            Assert.That(beam, Is.Not.Null, "빛기둥이 없다.");
            Assert.That(pool, Is.Not.Null, "바닥 웅덩이가 없다.");
            Assert.That(lamp, Is.Not.Null, "위쪽 광원이 없다.");

            // **축이 맞아야 한다.** 비스듬하면 정면에서는 옆으로, 옆에서는 뒤로
            // 쏠려 보인다 — 실제로 그렇게 보여서 고친 자리다.
            Assert.That(lamp.transform.position.x, Is.EqualTo(0f).Within(0.001f),
                "광원이 캐릭터의 세로축에서 벗어났다.");
            Assert.That(lamp.transform.position.z, Is.EqualTo(0f).Within(0.001f),
                "광원이 캐릭터의 세로축에서 벗어났다.");
            Assert.That(pool.transform.position.x, Is.EqualTo(lamp.transform.position.x).Within(0.001f),
                "웅덩이가 광원 바로 아래가 아니다 — 빛기둥이 기운다.");
            Assert.That(pool.transform.position.z, Is.EqualTo(lamp.transform.position.z).Within(0.001f),
                "웅덩이가 광원 바로 아래가 아니다 — 빛기둥이 기운다.");
            Assert.That(lamp.transform.position.y, Is.GreaterThan(1.3f),
                "광원이 캐릭터 키(1.147m)보다 낮다.");

            // 빛은 더해지는 것이라 가산 합성이어야 한다. 불투명이면 캐릭터를 가린다.
            foreach (var go in new[] { beam, pool, lamp })
            {
                var material = go.GetComponent<MeshRenderer>().sharedMaterial;
                Assert.That(material.shader.name, Is.EqualTo("NotThisPlanet/SpotlightGlow"),
                    go.name + " 가 다른 셰이더를 쓰고 있다.");
                Assert.That(material.GetTag("Queue", false, "Geometry"), Is.Not.EqualTo("Geometry"),
                    go.name + " 가 불투명 큐에 있다 — 캐릭터를 가린다.");
            }

            yield return new ExitPlayMode();
        }

        [Category("Heavy")]
        [UnityTest]
        public IEnumerator ThePreviewIsBrightEnoughToSeeAndNotBlownOut()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Customizer.unity");
            yield return new EnterPlayMode();
            yield return null;
            yield return null;

            var camera = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                .FirstOrDefault(c => c.name == "Customizer Preview Camera");
            Assert.That(camera, Is.Not.Null, "미리보기 카메라를 못 찾았다.");

            yield return Capture(camera, backdropOn: false, characterOn: true);
            Measure(out var mean, out var saturated, out var coverage);
            Debug.Log($"[커마 밝기] 평균 {mean:0.000} · 포화 {saturated:P1} · 차지 {coverage:P1}");

            // 차지가 너무 크면 **배경을 재고 있다는 뜻**이다. 처음에 배경을 안 끄고
            // 쟀더니 99.3% 가 나와 캐릭터가 아니라 배경 밝기를 재고 있었다.
            Assert.That(coverage, Is.InRange(0.05f, 0.75f),
                $"캐릭터가 차지하는 비율이 {coverage:P1} 다 — 너무 크면 배경을 재고 있다.");
            Assert.That(mean, Is.GreaterThan(0.34f), $"캐릭터가 너무 어둡다(평균 {mean:0.000}).");
            Assert.That(saturated, Is.LessThan(0.08f),
                $"하얗게 떴다(포화 {saturated:P1}) — 밝게 보이는 정도를 넘었다.");

            yield return new ExitPlayMode();
        }
    }
}
