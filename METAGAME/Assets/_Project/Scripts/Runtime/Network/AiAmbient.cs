using Metagame.Core;
using Metagame.Gameplay;
using UnityEngine;

namespace Metagame.Network
{
    /// <summary>
    /// 명령이 없을 때의 AI 동료 — <b>가만히 서 있지 않는다</b> (S15P21B202-746).
    ///
    /// <para>라이브 시연에서 명령을 기다리는 동료가 선 채로 굳어 있으면 사람이 아니라 기계로 보인다.
    /// 가장 가까운 <b>사람</b> 동료를 따라가기 · 제자리에서 기다리기 · 감정 표현 · 춤을 <b>무작위로,
    /// 무작위 간격으로</b> 바꿔 가며 한다. 모두 다른 것과 부딪히지 않는 간단한 일이다 — 파츠나 도구를
    /// 건드리면 사람의 판이 흐트러진다.</para>
    ///
    /// <para>명령이 오면 <see cref="AiCompanionController"/> 가 이것을 멈추고, 명령이 끝나면 다시
    /// 돈다. 명령 쪽 상태와 섞이지 않게 따라가기 조종기를 <b>따로</b> 쥔다.</para>
    /// </summary>
    public sealed class AiAmbient
    {
        public enum Mode { None, Follow, Wait, Emote, Dance }

        // 한 가지를 얼마나 오래 하나(초). 범위 안에서 무작위다.
        public const float FollowMin = 8f, FollowMax = 18f;
        public const float WaitMin = 3f, WaitMax = 8f;
        public const float DanceMin = 6f, DanceMax = 11f;
        public const float EmoteSeconds = 2.5f;
        /// <summary>감정 표현을 하려고 사람 쪽을 돌아보는 데 줄 시간. 넘기면 그냥 다음으로.</summary>
        public const float FaceTimeout = 3f;

        // 무엇을 고르나 — 합이 1 이 아니어도 된다(비율로 쓴다).
        public const float WeightFollow = .45f, WeightWait = .30f, WeightEmote = .15f, WeightDance = .10f;

        /// <summary>사람과 이보다 멀어졌으면 쉬지 않고 따라간다(m). 판에서 혼자 떨어진 동료는 이상해 보인다.</summary>
        public const float CatchUpDistance = 14f;

        /// <summary>따라갈 때 사람 곁에 서는 거리(m). 셋이 한 점에 몰리지 않게 방향도 무작위다.</summary>
        public const float FormationMin = 2.2f, FormationMax = 3.4f;

        static readonly string[] Emotes = { "greet", "heart", "cheer" };

        readonly System.Random random;
        readonly AiFollowController follow = new AiFollowController();
        Mode mode, last;
        float until, faceStarted;
        bool performing, lookAtHuman;
        string emote;
        PlayerRig human;

        public AiAmbient(int seed) { random = new System.Random(seed); }

        public Mode Current => mode;

        /// <summary>지금 보여 주는 감정 표현·춤. <see cref="AiCompanionController.SocialPerforming"/> 이 읽는다.</summary>
        public string Performing => performing ? (mode == Mode.Dance ? "dance" : emote) : null;

        public string Status =>
            mode == Mode.Follow ? "쉬는 중 · 따라다니기" :
            mode == Mode.Wait ? "쉬는 중 · 기다리기" :
            mode == Mode.Emote ? "쉬는 중 · 감정 표현" :
            mode == Mode.Dance ? "쉬는 중 · 춤" : "쉬는 중";

        /// <summary>
        /// 다음에 할 일. <b>순수 함수</b>다 — 검사가 값으로 부른다.
        ///
        /// <para><paramref name="roll"/> 은 [0,1). 손이 차 있으면 감정 표현·춤을 빼고, 사람과 멀면
        /// 따라가기만 한다. 춤과 감정 표현은 <b>두 번 잇지 않는다</b> — 연달아 춤추는 동료는 고장 난
        /// 것처럼 보인다.</para>
        /// </summary>
        public static Mode Pick(double roll, bool canEmote, bool canDance, bool farFromHuman, bool hasHuman, Mode last)
        {
            if (!hasHuman) return canDance && last != Mode.Dance && roll < .15 ? Mode.Dance : Mode.Wait;
            if (farFromHuman) return Mode.Follow;
            float follow = WeightFollow, wait = WeightWait;
            float emote = canEmote && last != Mode.Emote ? WeightEmote : 0f;
            float dance = canDance && last != Mode.Dance ? WeightDance : 0f;
            double total = follow + wait + emote + dance, at = roll * total;
            if ((at -= follow) < 0) return Mode.Follow;
            if ((at -= wait) < 0) return Mode.Wait;
            if ((at -= emote) < 0) return Mode.Emote;
            return dance > 0f ? Mode.Dance : Mode.Wait;
        }

        /// <summary>그 일을 얼마나 오래 하나(초). <paramref name="roll"/> 은 [0,1).</summary>
        public static float Duration(Mode mode, double roll)
        {
            float t = (float)roll;
            switch (mode)
            {
                case Mode.Follow: return Mathf.Lerp(FollowMin, FollowMax, t);
                case Mode.Wait: return Mathf.Lerp(WaitMin, WaitMax, t);
                case Mode.Dance: return Mathf.Lerp(DanceMin, DanceMax, t);
                case Mode.Emote: return FaceTimeout + EmoteSeconds;
                default: return WaitMin;
            }
        }

        public void Stop()
        {
            follow.Stop(); mode = Mode.None; performing = false; emote = null; human = null;
        }

        /// <param name="canDance">지금 춤을 출 수 있나(쿨다운·동작). 게임 쪽이 안다.</param>
        public InputFrame Tick(PlayerRig self, bool allowed, bool canDance, float now)
        {
            if (!allowed || self == null || self.Downed || self.IsRiding || self.Ragdolled || self.GrabbedByOthers)
            { if (mode != Mode.None) Stop(); return default; }
            bool handsFull = self.HeldTool != null || self.Carrying || self.LeftHand.Gripping || self.RightHand.Gripping;
            var nearest = NearestHuman(self);
            if (nearest != human && mode == Mode.Follow) { follow.Stop(); mode = Mode.None; }
            human = nearest;

            if (mode == Mode.None || now >= until) Next(self, handsFull, canDance, now);
            switch (mode)
            {
                case Mode.Follow:
                {
                    var input = follow.Tick(self, true);
                    if (!follow.Active) mode = Mode.None;   // 길이 없거나 사람이 쓰러졌다 — 다음으로
                    return input;
                }
                case Mode.Wait:
                    return lookAtHuman && human != null ? AiCompanionController.Face(self, human) : default;
                case Mode.Emote:
                {
                    if (handsFull || human == null) { mode = Mode.None; performing = false; return default; }
                    var face = AiCompanionController.Face(self, human);
                    if (!performing)
                    {
                        var direction = Quaternion.Euler(0, face.AimYaw, 0) * Vector3.forward;
                        if (now - faceStarted > .15f && Vector3.Angle(self.FacingDirection, direction) < 12f)
                        { performing = true; until = now + EmoteSeconds; }
                        else if (now - faceStarted > FaceTimeout) mode = Mode.None;
                    }
                    return face;
                }
                case Mode.Dance:
                    if (handsFull) { mode = Mode.None; performing = false; }
                    return default;
                default:
                    return default;
            }
        }

        void Next(PlayerRig self, bool handsFull, bool canDance, float now)
        {
            follow.Stop(); performing = false; emote = null;
            bool far = human != null && (human.FootPosition - self.FootPosition).magnitude > CatchUpDistance;
            last = mode != Mode.None ? mode : last;
            mode = Pick(random.NextDouble(), !handsFull, canDance && !handsFull, far, human != null, last);
            until = now + Duration(mode, random.NextDouble());
            switch (mode)
            {
                case Mode.Follow:
                    if (!follow.Apply("follow", human)) { mode = Mode.Wait; break; }
                    float angle = (float)(random.NextDouble() * 360.0);
                    float radius = Mathf.Lerp(FormationMin, FormationMax, (float)random.NextDouble());
                    follow.SetFormation(Quaternion.Euler(0, angle, 0) * Vector3.forward * radius, false);
                    break;
                case Mode.Wait:
                    lookAtHuman = random.NextDouble() < .6;
                    break;
                case Mode.Emote:
                    emote = Emotes[random.Next(Emotes.Length)]; faceStarted = now;
                    break;
                case Mode.Dance:
                    performing = true;
                    break;
            }
        }

        /// <summary>가장 가까운 사람 동료(AI 동료가 아닌). 쓰러진 사람은 따라가지 않는다.</summary>
        static PlayerRig NearestHuman(PlayerRig self)
        {
            PlayerRig best = null; float bestDistance = float.PositiveInfinity;
            foreach (var player in NetPlayer.AllServerPlayers)
            {
                if (player == null || player.IsAi) continue;
                var rig = player.VisualRig;
                if (rig == null || rig == self || rig.Downed || rig.gameObject.scene != self.gameObject.scene) continue;
                float d = (rig.FootPosition - self.FootPosition).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = rig; }
            }
            return best;
        }
    }
}
