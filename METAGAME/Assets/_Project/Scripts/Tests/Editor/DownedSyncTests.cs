using System.Reflection;
using Metagame.Network;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 기절 상태가 <b>참가자에게 전달되는 배선</b>을 검증한다 (S15P21B202-378).
    ///
    /// <para><b>왜 필요했나.</b> 2026-09-10 QA 에서 호스트가 기절했는데 참가자가
    /// 일으키려 해도 아무 반응이 없었다. 원인은 <c>PlayerRig.Downed</c> 가 복제되지
    /// 않아 참가자 기계에서 <b>언제나 false</b> 였던 것이다 —
    /// <c>FindDownedTeammate</c> 가 그 플래그로 후보를 걸러내므로 조준이 정확해도
    /// 대상이 없었다. 기획 §26-13 이 호스트에서만 성립하고 있었다.</para>
    ///
    /// <para>실제 복제는 Mirror 서버가 떠 있어야 하므로 여기서 돌리지 않는다. 대신
    /// <b>배선이 제자리에 있는지</b>를 리플렉션과 소스로 잡는다 — 이 배선은 조용히
    /// 빠지기 쉽고, 빠지면 증상이 "클릭이 안 먹는다" 로 보여 엉뚱한 곳을 파게 된다.</para>
    /// </summary>
    public sealed class DownedSyncTests
    {
        static FieldInfo Field(string name)
        {
            return typeof(NetPlayer).GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        /// <summary><c>downed</c> 필드가 있고 <b>SyncVar</b> 로 표시돼 있다.</summary>
        [Test]
        public void DownedIsASyncVar()
        {
            var field = Field("downed");
            Assert.That(field, Is.Not.Null, "NetPlayer.downed 가 없다 — 기절이 복제되지 않는다");
            Assert.That(field.FieldType, Is.EqualTo(typeof(bool)));

            // Mirror 의 weaver 가 SyncVar 를 다시 쓰지만 어트리뷰트는 남는다.
            var attrs = field.GetCustomAttributes(inherit: false);
            bool isSyncVar = false;
            foreach (var a in attrs)
                if (a.GetType().Name == "SyncVarAttribute") isSyncVar = true;

            Assert.That(isSyncVar, Is.True,
                "[SyncVar] 가 빠지면 값이 참가자에게 가지 않는다 — 증상이 조용히 되돌아온다");
        }

        /// <summary>
        /// <b>표시용 경로가 열려 있다.</b> 참가자 쪽에서 <c>GoDown()</c> 을 부르면 안 되므로
        /// (넉백·장비 해제·레이어·카메라까지 하고 그것들은 서버 권위다) 따로 있어야 한다.
        /// </summary>
        [Test]
        public void PlayerRigHasADisplayOnlyEntryPoint()
        {
            var method = typeof(PlayerRig).GetMethod(
                "SetRemoteDowned", BindingFlags.Instance | BindingFlags.Public);

            Assert.That(method, Is.Not.Null,
                "PlayerRig.SetRemoteDowned 가 없다 — 참가자 쪽에서 기절 표시를 맞출 길이 없다");
            Assert.That(method.GetParameters().Length, Is.EqualTo(1));
            Assert.That(method.GetParameters()[0].ParameterType, Is.EqualTo(typeof(bool)));
        }

        /// <summary>
        /// <b>후보 자격까지 맞춰야 한다.</b> 플래그만 세우면 후보가 되지 않는다 —
        /// <c>FindDownedTeammate</c> 는 잡을 지점(<c>CarryTarget</c>)도 요구하고, 그것을
        /// 만드는 <c>RefreshCarryTarget</c> 은 <c>GoDown</c>·<c>Revive</c> 안에서만 불려
        /// 클라이언트에서는 비어 있다.
        /// </summary>
        [Test]
        public void DisplayPathAlsoRebuildsTheGrabPoint()
        {
            var source = TestSource.Read("Runtime/Gameplay/Player/PlayerRig.cs");
            var body = TestSource.Method(source, "public void SetRemoteDowned(bool nowDowned)");

            Assert.That(body, Does.Contain("RefreshCarryTarget()"),
                "잡을 지점을 만들지 않으면 플래그만 맞고 후보가 되지 않는다");
            Assert.That(body, Does.Contain("RefreshRescueTarget()"));

            // 레이어는 건드리지 않는다 — RemoteAvatar 가 원격 몸을 RemotePlayer 로
            // 옮겨 두었고, 여기서 PlayerDowned 로 바꾸면 그 의도가 깨진다.
            Assert.That(body, Does.Not.Contain("SetDollLayer"),
                "원격 몸의 레이어는 RemoteAvatar 의 것이다 — 여기서 바꾸면 안 된다");
        }

        /// <summary>
        /// <b>서버에서는 SyncVar 값으로 되쓰지 않는다.</b> 서버의 몸이 진짜 상태이고,
        /// 한 틱 늦은 값이 방금 바뀐 상태를 지울 수 있다.
        /// </summary>
        [Test]
        public void ServerDoesNotOverwriteItsOwnState()
        {
            var source = TestSource.Read("Runtime/Network/NetPlayer.cs");
            var body = TestSource.Method(source, "void ApplyDowned(bool nowDowned)");

            Assert.That(body, Does.Contain("isServer"),
                "서버에서 되쓰면 권위가 뒤집힌다 — isServer 가드가 있어야 한다");
        }

        /// <summary>
        /// <b>서버가 값을 싣는 자리는 탑승 블록 밖이다.</b>
        ///
        /// <para>첫 판은 <c>riding = transport.IsControlling;</c> 옆에 두었는데 그 자리는
        /// <c>if (transport != null)</c> 안이다. 몸을 세운 두 경로가 모두 곧바로
        /// <c>AttachTransport()</c> 를 부르므로 지금은 non-null 이고 증상이 나지
        /// 않았지만, <b>기절은 탑승과 아무 상관이 없다</b> — 그 조건에 손대는 다음
        /// 사람이 복제를 조용히 끊을 수 있다. 이 경우가 그것을 막는다.</para>
        /// </summary>
        [Test]
        public void TheServerPushesDownedOutsideTheTransportBlock()
        {
            var source = TestSource.Read("Runtime/Network/NetPlayer.cs");
            var fixedUpdate = TestSource.Method(source, "void FixedUpdate()");

            Assert.That(fixedUpdate, Does.Contain("downed = body.Downed;"),
                "서버가 값을 싣지 않으면 SyncVar 가 있어도 아무것도 가지 않는다");

            // TestSource.Method 는 '이 글자부터 중괄호가 맞는 곳까지' 를 잘라 주므로
            // 메서드가 아닌 블록에도 그대로 쓸 수 있다.
            var transportBlock = TestSource.Method(fixedUpdate, "if (transport != null)");

            Assert.That(transportBlock, Does.Not.Contain("downed = body.Downed;"),
                "탑승 블록 안에 두면 그 조건이 바뀌는 날 기절 복제가 조용히 끊긴다");
        }

        /// <summary>
        /// <b>이미 기절한 채로 접속한 사람도 기절해 보여야 한다.</b> 훅은 값이 바뀔 때만
        /// 불리므로 몸을 만드는 자리에서도 한 번 적용해야 한다 — <c>ApplyRiding</c> 이
        /// 같은 이유로 그렇게 되어 있다("탄 채로 접속한 사람이 계속 보인다").
        ///
        /// <para><b>자리별로 본다.</b> 처음에는 파일 전체에서 두 호출의 개수가 같은지만
        /// 봤는데, 그러면 <c>riding</c> 쪽에 정당한 호출이 하나 늘기만 해도 기절과
        /// 무관하게 실패한다(<c>!287</c> AI 리뷰 지적). 몸을 세우는 메서드 안에서
        /// 짝이 맞는지 보고, 전체는 "적지 않다" 로만 확인한다.</para>
        /// </summary>
        [Test]
        public void LateJoinersSeeSomeoneAlreadyDowned()
        {
            var source = TestSource.Read("Runtime/Network/NetPlayer.cs");

            // 몸을 세우는 자리는 두 메서드다 — 서버가 세우거나 넘겨받는 쪽,
            // 그리고 남의 몸을 그리는 쪽.
            foreach (var maker in new[] { "bool BuildServerBody()", "void BuildRemoteBody()" })
            {
                var body = TestSource.Method(source, maker);

                Assert.That(TestSource.Count(body, "ApplyDowned(downed);"),
                    Is.EqualTo(TestSource.Count(body, "ApplyRiding(riding);")),
                    $"{maker} 안에서 riding 은 적용하고 downed 는 빼면, 이미 기절한 채로 "
                    + "접속한 사람이 남의 화면에 멀쩡해 보인다");
            }

            // 자리가 새로 생기는 것까지 잡는다. 동수가 아니라 "적지 않다" 인 이유는
            // 위 문단과 같다.
            Assert.That(TestSource.Count(source, "ApplyDowned(downed);"),
                Is.GreaterThanOrEqualTo(TestSource.Count(source, "ApplyRiding(riding);")),
                "몸을 세우는 자리가 새로 생겼는데 riding 만 적용했다");
        }

        /// <summary>
        /// <b>서버 루프는 서버에서만 돈다.</b> <c>downed = body.Downed;</c> 앞에 따로
        /// 가드를 두지 않는 근거가 이것이다 — <c>[ServerCallback]</c> 이 메서드 단위로
        /// 막는다. <c>riding</c> 도 같은 자리에서 같은 방식으로 실린다.
        ///
        /// <para><c>!287</c> AI 리뷰가 "대입에 권한 가드가 diff 범위 안에서 보이지
        /// 않는다" 고 물었다. 실제로는 있는데 diff 밖이었다. 이 단정이 그 근거를
        /// 코드 안에 남긴다 — 어트리뷰트가 사라지면 클라이언트가 자기 <c>downed</c> 를
        /// 덮어써서 <b>본인 화면에서만 기절이 풀린 것처럼</b> 남는다.</para>
        ///
        /// <para><b>첫 판은 원본에서 찾고 바로 앞이 어트리뷰트로 끝날 것을 요구했다.</b>
        /// 그러면 주석에 같은 글자가 먼저 나오거나 어트리뷰트가 하나 더 붙기만 해도
        /// 배선이 멀쩡한데 실패한다 — <b>바로 앞 커밋에서 고친 그 실수를 새 테스트에서
        /// 저지른 것이다</b>(<c>!287</c> 4회차 리뷰가 짚었다). 지금은 <see cref="TestSource"/>
        /// 를 거치고, 앞선 멤버가 끝난 자리부터 서명까지를 <b>어트리뷰트 구역</b>으로
        /// 보아 그 안에 있는지만 확인한다.</para>
        /// </summary>
        [Test]
        public void TheServerLoopIsServerOnly()
        {
            // 주석·문자열을 지운 뒤에 찾는다. 설명에 적어 둔 서명에 걸리면 안 된다.
            var code = TestSource.Blank(TestSource.Read("Runtime/Network/NetPlayer.cs"));

            int at = code.IndexOf("void FixedUpdate()", System.StringComparison.Ordinal);
            Assert.That(at, Is.GreaterThan(0), "서버 루프를 찾지 못했다");

            // 앞선 멤버가 끝나는 자리(} 또는 ;)부터 서명까지가 어트리뷰트 구역이다.
            // 독스트링은 이미 공백이므로 여기 남는 것은 어트리뷰트뿐이다.
            int prev = code.LastIndexOfAny(new[] { '}', ';' }, at);
            var attributes = prev >= 0 ? code.Substring(prev + 1, at - prev - 1)
                                       : code.Substring(0, at);

            Assert.That(attributes, Does.Contain("[ServerCallback]"),
                "[ServerCallback] 이 없으면 클라이언트도 이 루프를 돌려 SyncVar 를 덮어쓴다");
        }
    }
}
