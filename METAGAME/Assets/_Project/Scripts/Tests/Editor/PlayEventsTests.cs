using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Metagame.Bootstrap;
using Metagame.Core;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 플레이 이벤트 (S15P21B202-498).
    ///
    /// <para><b>무엇을 막는 검증인가.</b> 이 기록은 배포판에서만 쌓이고 우리가 직접 볼
    /// 일이 거의 없다 — 조용히 틀리면 <b>틀린 줄이 그대로 분석에 들어간다.</b> 평문
    /// 로그와 달리 사람이 읽고 이상하다고 느낄 기회가 없다.
    ///
    /// <para><b>파일 쓰기도 검사한다.</b> 배포판 기본값은 에디터에서 아무것도 열지
    /// 않지만(<see cref="PlayEvents.DefaultFolder"/>), 그 판단만 임시 폴더로 바꿔 끼우면
    /// 나머지는 배포판과 같은 길을 간다. 전에는 이 갈래가 없어 <b>상한·종료처럼 정작
    /// 조용히 틀리는 자리가 통째로 검사 밖</b>이었다(<c>!471</c> AI 리뷰).
    /// </summary>
    public sealed class PlayEventsTests
    {
        string sandbox = string.Empty;

        /// <summary>임시 폴더로 바꿔 끼운다. 저장소 밖이라 리포를 더럽히지 않는다.</summary>
        void UseSandbox()
        {
            sandbox = Path.Combine(Path.GetTempPath(), "metagame-evt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandbox);
            PlayEvents.FolderResolver = () => sandbox;
        }

        [TearDown]
        public void Cleanup()
        {
            PlayEvents.End();
            PlayEvents.FolderResolver = PlayEvents.DefaultFolder;
            if (sandbox.Length > 0)
            {
                try
                {
                    Directory.Delete(sandbox, true);
                }
                catch (Exception)
                {
                    // 못 지워도 임시 폴더다 — 검사를 실패시킬 이유가 없다.
                }
                sandbox = string.Empty;
            }
        }

        [Test]
        public void TheEditorWritesNothing()
        {
            // 에디터의 Application.dataPath 는 저장소 안이다. 켜 두면 리포에
            // `METAGAME/events/` 가 생긴다.
            //
            // **폴더를 정하는 쪽을 직접 본다.** 전에는 `Begin` 이 에디터에서 무조건
            // 돌아섰기 때문에 이 검사가 늘 통과했다 — 아무것도 안 지켰다(`!471` 리뷰).
            Assert.That(PlayEvents.DefaultFolder(), Is.Empty,
                "에디터에서 폴더를 정한다 — 저장소 안에 events/ 가 생긴다");

            PlayEvents.Begin(null);

            Assert.That(PlayEvents.Open, Is.False, "에디터에서 이벤트 파일이 열렸다");
            Assert.That(PlayEvents.Path, Is.Empty);
        }

        [Test]
        public void TheLastLineSurvivesTheCap()
        {
            // **상한에 닿은 판이 가장 알고 싶은 판이다.** 그런데 닫는 쪽이 `Active` 를
            // 보면(상한에서 거짓) 일찍 돌아서서, `PlayEvents.End` 가 일부러 만들어 둔
            // "상한과 무관하게 마지막 한 줄" 이 사라진다(`!471` AI 리뷰).
            UseSandbox();
            PlayEvents.Begin(null);
            Assert.That(PlayEvents.Open, Is.True, "임시 폴더에서도 안 열렸다");
            string path = PlayEvents.Path;

            // 한 줄로 상한을 넘긴다.
            PlayEvents.Write("pad", new Dictionary<string, object>
            {
                ["x"] = new string('x', (int)PlayEvents.MaxBytes + 16),
            });

            Assert.That(PlayEvents.Active, Is.False, "상한에 닿았는데 계속 쓴다");
            Assert.That(PlayEvents.Open, Is.True, "상한에 닿았다고 파일을 닫으면 마지막 줄을 못 남긴다");

            var go = new GameObject("~검사");
            try
            {
                go.AddComponent<PlayObserver>().End("test");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }

            string[] lines = File.ReadAllLines(path);
            Assert.That(lines[lines.Length - 1], Does.Contain("\"type\":\"session_end\""),
                "상한에 닿은 판에서 마지막 줄이 사라졌다 — 그 판이야말로 답이 거기 있다");
            Assert.That(lines[lines.Length - 1], Does.Contain("\"result\":\"test\""));
        }

        [Test]
        public void LeavingTheSceneClosesTheRun()
        {
            // **가장 흔한 끝은 종료가 아니라 씬 교체다** — 클리어하고 로비로 돌아가는
            // 길. 그때 안 닫으면 다음 판의 `Begin` 이 그냥 덮어, 결과도 걸린 시간도
            // 없이 끊긴 파일만 남는다(`!471` AI 리뷰).
            UseSandbox();
            PlayEvents.Begin(null);
            string path = PlayEvents.Path;

            var go = new GameObject("~검사");
            go.AddComponent<PlayObserver>();
            UnityEngine.Object.DestroyImmediate(go);   // 씬이 내려가는 것과 같은 자리

            Assert.That(PlayEvents.Open, Is.False, "씬이 내려갔는데 판이 안 닫혔다");

            string[] lines = File.ReadAllLines(path);
            Assert.That(lines[lines.Length - 1], Does.Contain("\"type\":\"session_end\""),
                "씬 교체로 끝난 판에 마지막 줄이 없다");
            Assert.That(lines[lines.Length - 1], Does.Contain("\"result\":\"destroy\""));
        }

        [Test]
        public void TheNumbersNeverSkip()
        {
            // 빠진 번호가 곧 유실 신호다. 그러려면 번호가 애초에 안 빠져야 한다.
            UseSandbox();
            PlayEvents.Begin(null);
            string path = PlayEvents.Path;

            PlayEvents.Write("a");
            PlayEvents.Write("b");
            PlayEvents.End(new Dictionary<string, object> { ["result"] = "test" });

            string[] lines = File.ReadAllLines(path);
            Assert.That(lines.Length, Is.EqualTo(4), "session_start · a · b · session_end 네 줄이어야 한다");
            for (int i = 0; i < lines.Length; i++)
                Assert.That(lines[i], Does.StartWith("{\"seq\":" + (i + 1) + ","),
                    "번호가 빠졌다 — 유실을 세는 근거가 없어진다: " + lines[i]);
        }

        [Test]
        public void ALineNeverBreaksTheFile()
        {
            // NDJSON 은 **한 줄이 한 이벤트**다. 줄바꿈이 값에 섞이면 그 파일 전체를
            // 못 읽는다 — 자유 문자열을 안 싣는 것이 규칙이지만 규칙에 기대지 않는다.
            string got = PlayEvents.Escape("a\nb\r\nc\td\"e\\f");

            Assert.That(got, Does.Not.Contain("\n"));
            Assert.That(got, Does.Not.Contain("\r"));
            Assert.That(got, Does.Not.Contain("\t"));
            Assert.That(got, Is.EqualTo("a\\nb\\r\\nc\\td\\\"e\\\\f"));
        }

        [Test]
        public void ControlCharactersBecomeEscapes()
        {
            // 제어문자가 그대로 들어가면 JSON 파서가 줄 하나를 통째로 버린다.
            Assert.That(PlayEvents.Escape("ab"), Is.EqualTo("a\\u0001b"));
        }

        [Test]
        public void TheFileNameSortsByTime()
        {
            // 이름이 곧 정렬 순서여야 오래된 것부터 지울 수 있다 — 평문 로그와 같은 규칙.
            string a = PlayEvents.FileNameFor(new System.DateTime(2026, 9, 15, 4, 3, 9));
            string b = PlayEvents.FileNameFor(new System.DateTime(2026, 9, 15, 9, 33, 0));

            Assert.That(a, Is.EqualTo("20260915-040309.ndjson"));
            Assert.That(string.CompareOrdinal(a, b), Is.LessThan(0), "문자열 정렬이 곧 시각 정렬이어야 한다");
        }

        [Test]
        public void TheBlockerBecomesAName()
        {
            // 한글 문구를 그대로 실으면 문구가 바뀔 때마다 집계가 갈라진다.
            //
            // **이 검사만으로는 부족하다** — 여기 적힌 문구도 이 파일 안의 리터럴이라
            // `ShipAssembly` 가 바뀌어도 그대로 통과한다. 두 파일을 맞대 보는 것은
            // `EveryBlockerMessageHasExactlyOneName` 이다.
            Assert.That(PlayObserver.BlockerCode("주 추진 엔진 모듈이 조립대에 없다"), Is.EqualTo("no_engine"));
            Assert.That(PlayObserver.BlockerCode("파츠가 2/4 층만 쌓였다"), Is.EqualTo("incomplete"));
            Assert.That(PlayObserver.BlockerCode("3층이 승무원 거주 모듈 이 아니다"), Is.EqualTo("wrong_order"));
            Assert.That(PlayObserver.BlockerCode("우주선이 발사 구역 밖에 있다"), Is.EqualTo("off_pad"));
            Assert.That(PlayObserver.BlockerCode("Part_2 이 12도 기울어 있다"), Is.EqualTo("tilted"));
        }

        [Test]
        public void AnUnknownBlockerIsNotSilentlyDropped()
        {
            // 모르는 사유를 빈 값으로 두면 **막혔는데 안 막힌 것처럼** 보인다.
            // `other` 로 남겨야 "새 사유가 생겼는데 이름을 안 붙였다" 를 알 수 있다.
            Assert.That(PlayObserver.BlockerCode("연료가 없다"), Is.EqualTo("other"));

            // 막히지 않은 상태는 빈 값이 맞다.
            Assert.That(PlayObserver.BlockerCode(""), Is.Empty);
            Assert.That(PlayObserver.BlockerCode(null), Is.Empty);
        }

        [Test]
        public void EveryBlockerMessageHasExactlyOneName()
        {
            // **이 검사가 없으면 위의 검사는 자기 자신을 확인할 뿐이다.** 위는
            // `PlayObserver` 안의 리터럴끼리 맞춰 볼 뿐이라, `ShipAssembly` 의 문구가 한
            // 글자만 바뀌면 실제 집계는 조용히 `other` 로 떨어지는데 통과한다
            // (`!471` AI 리뷰). 여기서 두 파일을 맞대 본다.
            //
            // 값으로 못 하는 이유는 문구가 보간이라서다 — `$"파츠가 {n}/{m} 층만 쌓였다"`
            // 는 조립대와 파츠가 실제로 선 뒤라야 나온다. 그래서 소스를 읽는다
            // (`TestSource` 의 "값으로 되면 값으로" 기준에 따른 판단).
            var messages = Literals(TestSource.Read("Runtime/Gameplay/Parts/ShipAssembly.cs"),
                                    "Blocker\\s*=\\s*\\$?\"([^\"]*)\"");
            Assert.That(messages.Count, Is.GreaterThan(0), "ShipAssembly 에서 막는 문구를 못 읽었다");

            var names = Literals(TestSource.Read("Runtime/Bootstrap/PlayObserver.cs"),
                                 "reason\\.Contains\\(\"([^\"]+)\"\\)");
            Assert.That(names.Count, Is.GreaterThan(0), "PlayObserver 에서 이름 조각을 못 읽었다");

            foreach (string message in messages)
            {
                int hits = 0;
                foreach (string piece in names)
                    if (message.Contains(piece)) hits++;

                Assert.That(hits, Is.EqualTo(1),
                    "막는 문구 하나에 이름이 " + hits + "개 걸린다 — 0이면 조용히 other 로 떨어지고, "
                    + "2 이상이면 어느 이름이 붙을지 순서가 정한다: " + message);
            }

            foreach (string piece in names)
            {
                bool used = false;
                foreach (string message in messages)
                    if (message.Contains(piece)) used = true;

                Assert.That(used, Is.True,
                    "이 조각에 걸리는 문구가 이제 없다 — 문구가 바뀌었는데 이름이 안 따라갔다: " + piece);
            }
        }

        /// <summary>주석 처리된 줄은 뺀 채 정규식의 첫 묶음을 모은다. 빈 값도 뺀다.</summary>
        static List<string> Literals(string source, string pattern)
        {
            var found = new List<string>();
            foreach (string line in source.Split('\n'))
            {
                var m = Regex.Match(line, pattern);
                if (!m.Success || m.Groups[1].Value.Length == 0) continue;

                int comment = line.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0 && comment < m.Index) continue;

                found.Add(m.Groups[1].Value);
            }
            return found;
        }

        [Test]
        public void TheObserverLooksRarely()
        {
            // 프레임에 얹히는 유일한 자리다. 보는 것은 이미 계산된 값 몇 개뿐이고,
            // 파트를 옮기고 용접하는 일은 초 단위라 자주 볼 값어치가 없다(`-374`).
            Assert.That(PlayObserver.Interval, Is.GreaterThanOrEqualTo(0.25f),
                "관찰 주기가 너무 촘촘하다 — 프레임 저하와 구분이 안 된다");
        }

        [Test]
        public void TheEventFolderSitsBesideTheLogFolder()
        {
            // 두 폴더가 같은 규칙으로 정해져야 **로그는 남는데 이벤트는 안 남는** 상태가
            // 안 생긴다. 그 상태의 원인을 찾는 것이 가장 어렵다.
            var src = TestSource.Read("Runtime/Core/PlayEvents.cs");

            Assert.That(src, Does.Contain("RunLog.FolderFor("),
                "이벤트가 폴더 규칙을 따로 갖고 있다 — 한쪽만 고쳐진다");
        }
    }
}
