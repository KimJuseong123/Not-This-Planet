using System;
using UnityEngine;

namespace Metagame.Gameplay.Hazards
{
    /// <summary>
    /// 한 장면에서 사용하는 운석 물리·경고·방망이 판정값.
    ///
    /// 기본값은 <see cref="PhysicsScale"/> 에서 가져오지만, 실제 플레이 값은
    /// <see cref="MeteorSpawner"/> 에 직렬화되어 장면별로 조정할 수 있다.
    /// </summary>
    [Serializable]
    public sealed class MeteorTuning
    {
        [Header("운석 물리")]
        [SerializeField, Min(0f)] float _mass = PhysicsScale.MeteorMass;
        [SerializeField, Min(0f)] float _radius = PhysicsScale.MeteorRadius;
        [SerializeField, Min(0f)] float _smallImpactRadius = PhysicsScale.MeteorSmallImpactRadius;
        [SerializeField, Min(0f)] float _mediumImpactRadius = PhysicsScale.MeteorMediumImpactRadius;
        [SerializeField, Min(0f)] float _impactForce = PhysicsScale.MeteorImpactForce;
        [SerializeField, Range(0f, 1f)] float _heavyImpactFraction = PhysicsScale.MeteorHeavyImpactFraction;
        [SerializeField, Min(0f)] float _heavyKnockSpeed = PhysicsScale.MeteorHeavyKnockSpeed;

        [Header("정착·흔적")]
        [SerializeField, Min(0f)] float _restSpeed = PhysicsScale.MeteorRestSpeed;
        [SerializeField, Min(0f)] float _restSeconds = PhysicsScale.MeteorRestSeconds;
        [SerializeField, Min(0f)] float _deflectedDespawnSeconds = PhysicsScale.MeteorDeflectedDespawnSeconds;
        [SerializeField, Min(0f)] float _scarDespawnSeconds = PhysicsScale.MeteorScarDespawnSeconds;
        [SerializeField, Range(0f, 1f)] float _scarSinkRatio = PhysicsScale.MeteorScarSinkRatio;
        [SerializeField, Min(0f)] float _scarCoolSeconds = PhysicsScale.MeteorScarCoolSeconds;
        [SerializeField, Min(0f)] float _scarFadeSeconds = PhysicsScale.MeteorScarFadeSeconds;
        [SerializeField, Min(0)] int _scarLimit = PhysicsScale.MeteorScarLimit;
        [SerializeField, Min(0f)] float _maxLifeSeconds = PhysicsScale.MeteorMaxLifeSeconds;

        [Header("낙하 경고")]
        [SerializeField, Min(0f)] float _warningImminentSeconds = PhysicsScale.MeteorWarningImminentSeconds;
        [SerializeField, Min(0f)] float _warningStrikableBlinkHz = PhysicsScale.MeteorWarningStrikableBlinkHz;
        [SerializeField, Min(0f)] float _warningImminentBlinkHz = PhysicsScale.MeteorWarningImminentBlinkHz;
        [SerializeField, Min(0f)] float _warningLift = PhysicsScale.MeteorWarningLift;

        [Header("운석 방망이 판정")]
        [SerializeField, Min(0f)] float _batSwingWindowSeconds = PhysicsScale.BatSwingWindowSeconds;
        [SerializeField, Min(0f)] float _batAssistRadius = PhysicsScale.BatAssistRadius;

        public float Mass => _mass;
        public float Radius => _radius;
        public float SmallImpactRadius => _smallImpactRadius;
        public float MediumImpactRadius => _mediumImpactRadius;
        public float ImpactForce => _impactForce;
        public float HeavyImpactFraction => _heavyImpactFraction;
        public float HeavyKnockSpeed => _heavyKnockSpeed;
        public float RestSpeed => _restSpeed;
        public float RestSeconds => _restSeconds;
        public float DeflectedDespawnSeconds => _deflectedDespawnSeconds;
        public float ScarDespawnSeconds => _scarDespawnSeconds;
        public float ScarSinkRatio => _scarSinkRatio;
        public float ScarCoolSeconds => _scarCoolSeconds;

        /// <summary>다 식은 뒤 옅어져 사라지는 시간(초).</summary>
        public float ScarFadeSeconds => _scarFadeSeconds;
        public int ScarLimit => _scarLimit;
        public float MaxLifeSeconds => _maxLifeSeconds;
        public float WarningImminentSeconds => _warningImminentSeconds;
        public float WarningStrikableBlinkHz => _warningStrikableBlinkHz;
        public float WarningImminentBlinkHz => _warningImminentBlinkHz;
        public float WarningLift => _warningLift;
        public float BatSwingWindowSeconds => _batSwingWindowSeconds;
        public float BatAssistRadius => _batAssistRadius;

        // ── 시험용 즉시 조정 (CheatArenaConsole) ─────────────────────────────
        //
        // 이 객체는 스포너가 만들어 <b>모든 운석이 같은 것을 참조</b>하므로, 여기서 값을
        // 바꾸면 이미 떨어지고 있는 운석에도 그 자리에서 적용된다. 치트 패널이 충격파의
        // 세기를 손으로 맞춰 보기 위한 것이고, 판을 다시 시작하면 씬에 직렬화된 값으로
        // 돌아간다.

        /// <summary>강한 충격이 되는 안쪽 비율 0~1(§20.1). 기본 0.5.</summary>
        public void SetHeavyImpactFraction(float value) =>
            _heavyImpactFraction = Mathf.Clamp01(value);

        /// <summary>강한 충격·직접 충돌로 날아가는 속도(m/s). 기본 8.</summary>
        public void SetHeavyKnockSpeed(float value) =>
            _heavyKnockSpeed = Mathf.Max(0f, value);

        public float ImpactRadiusFor(MeteorSize size) =>
            size == MeteorSize.Medium ? _mediumImpactRadius : _smallImpactRadius;

        public float ImpactForceFor(MeteorSize size)
        {
            if (_smallImpactRadius <= 0f) return 0f;
            return _impactForce * ImpactRadiusFor(size) / _smallImpactRadius;
        }
    }
}
