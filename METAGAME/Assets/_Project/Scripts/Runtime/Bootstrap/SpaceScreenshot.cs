using System.Collections;
using System.IO;
using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay;

namespace Metagame.Bootstrap
{
    /// <summary>
    /// 룩 확인용 스크린샷 모드.
    /// 실행:  SpaceFlop.exe -space-screenshot -space-shotdir "C:\out" -screen-width 1280 -screen-height 720
    /// 정해진 앵글을 찍고 스스로 종료한다.
    /// </summary>
    public class SpaceScreenshot : MonoBehaviour
    {
        struct Shot
        {
            public string name;
            public Vector3 pos;
            public Vector3 lookAt;
            public float fov;
            public bool followDoll;      // 인형을 따라가며 찍는다 (pos 는 인형 기준 오프셋)
            public bool walking;
            public bool jumping;
            public bool carrying;
        }

        static readonly Shot[] Shots =
        {
            new Shot { name = "01_landing",  pos = new Vector3(-11f, 4.0f, -9f),    lookAt = new Vector3(2f, 0.6f, 0f),    fov = 62f },
            new Shot { name = "02_suit",     pos = new Vector3(1.35f, 0.55f, -1.20f), lookAt = new Vector3(0f, 0.48f, 0f), fov = 42f, followDoll = true },
            new Shot { name = "03_face",     pos = new Vector3(1.05f, 0.62f, -0.58f),  lookAt = new Vector3(0f, 0.53f, 0f), fov = 38f, followDoll = true },
            new Shot { name = "04_carry",    pos = new Vector3(1.9f, 0.80f, -1.70f), lookAt = new Vector3(0.25f, 0.35f, 0f), fov = 46f, followDoll = true, carrying = true },
            new Shot { name = "05_jump",     pos = new Vector3(2.6f, 1.1f, -3.0f),  lookAt = new Vector3(0f, 0.55f, 0f),   fov = 50f, followDoll = true, jumping = true },
            new Shot { name = "06_craters",  pos = new Vector3(13f, 5.5f, -10f),    lookAt = new Vector3(26f, 1.2f, 0f),   fov = 60f },
            new Shot { name = "07_cliff",    pos = new Vector3(35f, 7.5f, -10f),    lookAt = new Vector3(44f, 3.8f, 0f),   fov = 58f },
            new Shot { name = "08_cables",   pos = new Vector3(56f, 10.5f, -15f),   lookAt = new Vector3(68f, 6.5f, 0f),   fov = 62f },
            new Shot { name = "09_plank",    pos = new Vector3(80f, 8.5f, -11f),    lookAt = new Vector3(86f, 5.0f, 0f),   fov = 56f },
            new Shot { name = "10_cargopad", pos = new Vector3(92f, 9.5f, -13f),    lookAt = new Vector3(101f, 4.8f, 0f),  fov = 60f },
            new Shot { name = "11_airlock",  pos = new Vector3(110f, 9.0f, -12f),   lookAt = new Vector3(119f, 6.0f, 0f),  fov = 58f },
            new Shot { name = "12_rocket",   pos = new Vector3(127f, 9.5f, -12f),   lookAt = new Vector3(136f, 5.5f, 0f),  fov = 58f },
            new Shot { name = "13_sky",      pos = new Vector3(50f, 6.0f, 0f),      lookAt = new Vector3(58f, 26f, 12f),   fov = 70f },
        };

        IEnumerator Start()
        {
            string dir = ArgValue("-space-shotdir");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.dataPath, "..", "Screenshots");
            Directory.CreateDirectory(dir);

            yield return new WaitForSeconds(2.6f);      // 물리가 가라앉을 시간

            var cam = Camera.main;
            if (cam == null) { Debug.Log("[SHOT] 카메라 없음"); Application.Quit(1); yield break; }

            var orbit = cam.GetComponent<OrbitCamera>();
            if (orbit != null) orbit.enabled = false;

            var session = GameSession.Instance;
            var rig = session != null ? session.LocalPlayer : null;
            if (session != null) session.ScriptedInput = true;

            GameObject carried = null;

            for (int i = 0; i < Shots.Length; i++)
            {
                var s = Shots[i];

                if (rig != null)
                {
                    var f = new InputFrame();
                    if (s.walking) f.Move = new Vector2(0f, 1f);
                    if (s.carrying) { f.GrabRight = true; f.GrabLeft = true; }
                    rig.SetInput(f);

                    if (s.carrying)
                    {
                        // 팔이 뻗는 방향은 카메라의 forward 다. 촬영 모드에서는 OrbitCamera 를 꺼놨으므로
                        // Yaw 를 넣어봐야 소용없다 — 카메라를 직접 돌려서 조준을 만들어 준다.
                        cam.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);

                        // 구르지 않게 상자로 놓는다 (공은 1.8초 안에 굴러가 버린다)
                        carried = Prim.Box("촬영용 화물 상자",
                                           rig.FootPosition + new Vector3(0.78f, 0.24f, 0f),
                                           new Vector3(0.46f, 0.46f, 0.46f),
                                           new Color(0.58f, 0.62f, 0.68f), 0.35f, null);
                        carried.GetComponent<Collider>().material = Prim.GroundPhysics;
                        var rb = carried.AddComponent<Rigidbody>();
                        rb.mass = 22f;
                        rb.interpolation = RigidbodyInterpolation.Interpolate;
                        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                        Grabbable.Mark(carried, "화물 상자", false, 1f, 1f);
                        yield return new WaitForSeconds(2.0f);
                    }
                    else if (s.jumping)
                    {
                        f.Jump = true;
                        rig.SetInput(f);
                        yield return new WaitForSeconds(0.85f);      // 뜨는 도중에 찍는다
                    }
                    else if (s.walking) yield return new WaitForSeconds(1.1f);
                }

                Vector3 pos = s.pos;
                Vector3 look = s.lookAt;
                if (s.followDoll && rig != null)
                {
                    Vector3 c = rig.Position;
                    pos = c + s.pos;
                    look = c + s.lookAt;
                }

                cam.transform.position = pos;
                cam.transform.rotation = Quaternion.LookRotation((look - pos).normalized, Vector3.up);
                cam.fieldOfView = s.fov;

                for (int k = 0; k < 4; k++) yield return null;   // 폰트 아틀라스가 자리잡을 시간
                yield return new WaitForEndOfFrame();

                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                var png = tex.EncodeToPNG();
                string path = Path.Combine(dir, s.name + ".png");
                File.WriteAllBytes(path, png);
                Destroy(tex);
                Debug.Log("[SHOT] " + path + "  (" + (png.Length / 1024) + "KB)");

                if (rig != null && (s.carrying || s.jumping))
                {
                    rig.SetInput(new InputFrame());
                    if (carried != null) { Destroy(carried); carried = null; }
                    if (session != null) session.Respawn();
                    yield return new WaitForSeconds(1.6f);
                }
            }

            Debug.Log("[SHOT] 완료");
            Application.Quit(0);
        }

        static string ArgValue(string key)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == key) return args[i + 1];
            return null;
        }
    }
}
