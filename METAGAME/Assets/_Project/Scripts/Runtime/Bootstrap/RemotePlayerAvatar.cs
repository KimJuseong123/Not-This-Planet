using UnityEngine;
using Metagame.Core;

namespace Metagame.Bootstrap
{
    /// <summary>
    /// 다른 사람을 보여주는 표시 전용 아바타. 물리를 돌리지 않는다.
    ///
    /// 서버는 위치를 중계만 하고 물리는 각 PC 가 따로 돌리므로, 원격 캐릭터를 래그돌로
    /// 세우면 화면마다 팔다리가 다르게 보인다. 그래서 몸통 위치와 바라보는 방향만 따라간다.
    /// 사람 모델을 그대로 쓰는 것은 소유권·보간이 정리된 뒤의 일이다.
    /// </summary>
    public sealed class RemotePlayerAvatar : MonoBehaviour
    {
        private const float BodyDiameter = 0.55f;
        private const float BodyHeight = 1.30f;
        private const float HeadDiameter = 0.42f;

        /// 위치는 초당 몇 번만 온다. 그 사이를 메우지 않으면 뚝뚝 끊겨 보인다.
        private const float FollowPerSecond = 12f;

        /// 이보다 멀리 튀면 이어붙이지 않고 바로 옮긴다 (재접속·부활 등).
        private const float TeleportDistance = 6f;

        public long UserId { get; private set; }
        public string Nickname { get; private set; }

        /// 닉네임을 띄울 머리 위 지점.
        public Vector3 LabelAnchor
        {
            get { return transform.position + Vector3.up * (BodyHeight + HeadDiameter + 0.25f); }
        }

        private Vector3 targetPosition;
        private float targetYaw;

        public static RemotePlayerAvatar Create(long userId, string nickname, Transform parent)
        {
            var go = new GameObject("~원격 " + userId);
            if (parent != null) go.transform.SetParent(parent, true);

            Color color = ColorFor(userId);

            GameObject body = Prim.Decor(Prim.Capsule(
                "몸통", Vector3.zero, BodyDiameter, BodyHeight, color, 0.9f, go.transform));
            body.transform.localPosition = Vector3.up * (BodyHeight * 0.5f);

            GameObject head = Prim.Decor(Prim.Ball(
                "머리", Vector3.zero, HeadDiameter, color, 0.9f, go.transform));
            head.transform.localPosition = Vector3.up * (BodyHeight + HeadDiameter * 0.4f);

            // 어느 쪽을 보고 있는지 알 수 있게 코를 하나 붙인다.
            GameObject nose = Prim.Decor(Prim.Ball(
                "시선", Vector3.zero, HeadDiameter * 0.4f, Color.white, 0.9f, go.transform));
            nose.transform.localPosition =
                new Vector3(0f, BodyHeight + HeadDiameter * 0.4f, HeadDiameter * 0.5f);

            var avatar = go.AddComponent<RemotePlayerAvatar>();
            avatar.UserId = userId;
            avatar.Rename(nickname);
            return avatar;
        }

        public void Rename(string nickname)
        {
            Nickname = string.IsNullOrWhiteSpace(nickname) ? UserId.ToString() : nickname;
        }

        public void MoveTo(Vector3 position, float yaw)
        {
            targetPosition = position;
            targetYaw = yaw;

            if ((transform.position - position).sqrMagnitude > TeleportDistance * TeleportDistance)
                Snap();
        }

        /// <summary>보간 없이 목표 지점으로 옮긴다. 처음 등장하거나 크게 튀었을 때 쓴다.</summary>
        public void Snap()
        {
            transform.position = targetPosition;
            transform.rotation = Quaternion.Euler(0f, targetYaw, 0f);
        }

        void Update()
        {
            // 프레임 길이에 상관없이 같은 속도로 따라붙는다.
            float t = 1f - Mathf.Exp(-FollowPerSecond * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, targetPosition, t);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, Quaternion.Euler(0f, targetYaw, 0f), t);
        }

        /// <summary>
        /// userId 로 색을 정한다 — 같은 사람은 누구 화면에서나 같은 색으로 보인다.
        /// 황금비로 색상환을 돌아 인접한 userId 끼리도 색이 뚜렷이 갈린다.
        /// </summary>
        public static Color ColorFor(long userId)
        {
            float hue = Mathf.Repeat(userId * 0.6180339f, 1f);
            return Color.HSVToRGB(hue, 0.55f, 0.95f);
        }
    }
}
