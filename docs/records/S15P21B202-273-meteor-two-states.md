# S15P21B202-273 운석을 쳐낸 소품과 박힌 흔적으로 나누고 착지 후 피해를 끝낸다

[문서 안내](../README.md) → [작업 기록](README.md) → 운석·환경

착탄한 운석에 계속 피해를 받던 문제를 수정하고 쳐낸 소품과 지면 흔적을 구분했습니다. 접점 법선·정착 판정과 기획 확인이 필요한 변경을 기록합니다.

본문의 “현재”, 검증 수치, 미완료 항목은 **기록 당시 기준**입니다. 후속 변경은 관련 이슈와 [주제별 색인](README.md)을 함께 확인합니다.

<a id="왜-했는가"></a>

## 변경 배경

> "이미 바닥에 다 떨어져서 가만히 놓여있는 운석에 닿아도 딜이 박힘"

기획 §19 는 "충돌 **순간에만** 넉백과 기절 판정이 있다" 고 정한다. 그런데 인게임에서는
바닥에 누운 운석에 스치기만 해도 피가 닳았다.

## 원인 — 접점 법선의 부호

`Meteor.OnCollisionEnter`는 지면 접점을 찾아 착탄을 처리하고, 그 뒤로는 `HasImpacted`
로 피해를 막는다. 지면 판정은 접점 법선의 y가 `MinimumSupportNormalY`(0.35) 를 넘는지
본다.

Unity의 `ContactPoint.normal`은 **`thisCollider` 기준**이고, 어느 쪽이
`thisCollider`가 되는지는 **어느 오브젝트가 콜백을 받았는지에 따라 달라진다.**

예전 코드는 그 모호함을 콜라이더에 대해서만 처리했다.

```csharp
static Collider OtherCollider(ContactPoint contact)
{
    if (contact.thisCollider != null
        && contact.thisCollider.GetComponentInParent<Meteor>() != null)
        return contact.otherCollider;
    return contact.thisCollider != null ? contact.thisCollider : contact.otherCollider;
}
```

콜라이더는 뒤집었지만 **법선은 `contact.normal`을 그대로 썼다.** 지면 쪽이
`thisCollider`로 잡히면 법선이 운석 반대쪽을 향해 y가 −1 에 가까워지고, 0.35 문턱을
넘지 못한다. 그러면 운석은 **영영 정착하지 않고** 살아 있는 동적 물체로 바닥에 누워,
사람이 닿을 때마다 직접 충돌 경로로 기절시켰다.

두 값이 같은 판단을 쓰므로 한 곳에서 함께 돌려주게 고쳤다.

```csharp
static (Collider Collider, Vector3 Normal) Other(ContactPoint contact)
{
    var meteorIsThis = contact.thisCollider != null
                       && contact.thisCollider.GetComponentInParent<Meteor>() != null;

    if (meteorIsThis)
        return (contact.otherCollider, contact.normal);

    // 지면 쪽이 콜백을 받았다. 법선을 뒤집어 운석 기준으로 맞춘다.
    var collider = contact.thisCollider != null
        ? contact.thisCollider
        : contact.otherCollider;

    return (collider, -contact.normal);
}
```

<a id="부호만으로는-부족했다--정착-안전망"></a>

## 접점 법선 보정과 정착 판정

부호를 고친 뒤에도 `Meteor_StaticSupportSettlesAndRaisesGlobalImpactOnce`가 계속
실패했다. 검증 쪽 문제였는데(아래 참조), 그 과정에서 **접점 판정 자체를 믿을 수 없다**
는 것을 알았다.

지면 `ENV_Ground_Arena_50m`은 MeshCollider 다. 급한 삼각형에 먼저 닿으면 법선의 y가
문턱을 못 넘고, 부호를 고쳐도 같은 증상이 난다. 그래서 "거의 멈췄고 아래에 정적
지지면이 있으면 정착" 이라는 안전망을 뒀다.

```csharp
void SettleIfAtRest(float deltaTime)
{
    if (_body == null || _body.isKinematic) return;

    if (_body.linearVelocity.sqrMagnitude
        > PhysicsScale.MeteorRestSpeed * PhysicsScale.MeteorRestSpeed)
    {
        _atRestSeconds = 0f;
        return;
    }

    _atRestSeconds += deltaTime;
    if (_atRestSeconds < PhysicsScale.MeteorRestSeconds) return;

    if (!TryFindSupportBelow(out var point, out var normal)) return;

    ApplyImpact(point, null, normal);
}
```

`PartAssemblyLink`가 `OnJointBreak` 콜백을 믿지 않고 `Tick`에서 조인트 생존을
확인하는 것과 같은 방식이다 — **콜백은 즉시성일 뿐이고 정확성은 안전망이 갖는다.**

`TryFindSupportBelow`는 아래로 `GameLayers.SurfaceMask` 레이캐스트를 쏘고, 자기 자신
· 동적 강체 · `TransportVehicle`을 제외한다. 그냥 "멈췄으면 정착" 으로 두면 운반체나
동적 파츠 위에 잠깐 얹혀 멈춘 것도 박혀 버리기 때문이다 — `TryGetSupportContact`가
막으려던 것과 같은 규칙을 지킨다.

## 두 상태로 나눴다

> "떨어질 때 방망이로 쳐내는 경우와 못쳐내서 바닥에 박히는 경우"

```
방망이로 쳐냄  →  동적 유지 · Prop 레이어 · 방망이로 밀린다 · 굴러다닌다 · 12초
못 쳐냄       →  키네마틱 · 반쯤 박힘 · 못 건드림 · 위에 올라갈 수만 · 30초
```

`Prop` 레이어로 옮기는 것이 핵심이다. `GameLayers.BatStrikeMask`가
`GameLayers.Meteor`를 빼고 `GameLayers.Prop`은 포함하므로(S15P21B202-257), **레이어만
옮기면 때릴 수 있는 소품이 된다** — 새 규칙을 만들지 않았다.

```csharp
void BecomeRolling()
{
    if (Rolling) return;

    Rolling = true;
    _sinceRolling = 0f;

    SetLayerDeep(gameObject, GameLayers.Prop);
}
```

피해 차단은 `Harmless` 하나로 모았다.

```csharp
public bool Harmless => HasImpacted || Rolling;
```

`Rolling` 가드는 `OnCollisionEnter`와 `ApplyImpact` **양쪽에** 넣어야 했다. 처음엔
콜백에만 넣었고, `ARollingPropDoesNotBecomeAScar`가 그것을 잡았다 — 굴러다니는 소품이
지면에 닿아 흔적으로 굳어 버렸다.

## 기획과 어긋나는 결정 3건 — [Plan] 확인 필요

| 기획 | 지금 | 근거 |
|---|---|---|
| §19 "박힌 운석과 분화구는 … 스테이지 동안 사라지지 않는다" | 둘 다 사라진다 (12초 / 30초) | 2026-09-02 "둘다 사라지게 한다" |
| §18.2 "반사된 운석의 충돌과 폭발은 일반 운석과 같은 피해를 준다" | 쳐낸 순간부터 순해진다 | 쳐낸 운석이 날아가다 동료를 기절시키면 **잘한 플레이가 팀을 해친다** |
| §17.2 "안쪽이 채워진다" | 원이 줄어든다 | S15P21B202-268 에서 이미 뒤집음 |

`MeteorScarDespawnSeconds`(30초)는 `MeteorScarCoolSeconds`(5초, 분화구가 식는 시간)보다
**길어야 한다.** 식기 전에 사라지면 §19 의 "약 5초 동안 붉은 빛, 연기와 불꽃이
약해진다" 가 화면에 보이지 않는다.

## 검증

새로 만든 `MeteorAftermathTests` 16개 + 기존 `MeteorTests` · `MeteorScarTests` ·
`MeteorImpactTierTests` 전부 통과.

develop 기준선을 따로 재서 실패 목록을 집합으로 비교했다.

```
develop : 647개 중 630 통과 / 17 실패
273     : 663개 중 647 통과 / 16 실패

273 에서만 실패 : 없음
273 이 고친 것  : MeteorTests.Meteor_StaticSupportSettlesAndRaisesGlobalImpactOnce
```

**새로 깨진 것이 없고, develop에서 이미 실패 표시이던 운석 검증 하나가 초록불이 됐다.**
이 버그는 검증 스위트에 처음부터 드러나 있었다 — 다만 그 검증이 `Physics.Simulate`만
돌려 EditMode에서 통과할 수 없는 상태였기 때문에(아래) 원인이 가려져 있었다.

남은 16개는 모두 develop 것이다(`ClampPull` · `TractorBeamController` · `PartClamp` ·
`DownedRescue` · `LowGravityField` · `PartialLift` · `TransportAltitude` —
S15P21B202-232 건, 다른 팀원 담당).

### 기존 검증 2개를 고쳤다

**`MeteorTests.Simulate`는 이 이슈를 검증할 수 없는 상태였다.** `Physics.Simulate`만
돌렸는데 EditMode는 `OnCollisionEnter`를 전달하지 않는다. 그래서
`Meteor_StaticSupportSettlesAndRaisesGlobalImpactOnce`는 **작성된 대로는 통과할 수 없는
검증**이었다. `Meteor.Tick`을 같이 돌리게 고쳤다.

**`Meteor_StaysForeverOnceItSettles` → `Meteor_ScarDespawnsAfterItsTime`.**이
검증은 §19 를 근거로 S15P21B202-251 이 소멸을 지울 때 만든 것이다. 이번 결정으로
전제가 뒤집혔다.

**`ADeflectedMeteor_HurtsTheSameWay` → `ADeflectedMeteor_StopsHurting`.** 위와 같이
§18.2 의 전제가 뒤집혔다.

<a id="남은-것"></a>

## 남은 작업

이 이슈는 "착지 후 피해가 끝난다" 까지다. 운석 낙하 연출 재설계의 나머지는 별건이다.

- **B** 대각선 고속 낙하 — 지금은 공중에서 수직으로 중력대로 떨어진다
- **C** 착지 지점의 빨간 원 + 시간 표현
- **D** 대각선 낙하에 맞춘 방망이 타격 판정 재확인
