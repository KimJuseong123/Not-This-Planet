# S15P21B202-247 운석 충격을 세 단계로 나눈다

## 무엇이 문제였나

기획 §20.1 은 충격을 세 단계로 나눈다. **셋이 모두 같았다.**

| 단계 | 기획 | 예전 |
| --- | --- | --- |
| 약한 충격파 | 밀려나지만 바로 행동 가능 | 25 피해 + 900N |
| 강한 충격 | 크게 날아가고 **장비를 떨어뜨림** | 25 피해 + 900N |
| 직접 충돌 | 날아간 뒤 **기절** | 25 피해 + 900N |

`DamageSource` 의 "닿으면 25" 하나뿐이고 `Tuning.MaxHealth` 가 100이라 **네 번**을 맞아야
기절했다. 낙하 예고(S15P21B202-241)·방망이(233·243·245)·공세 구조(234)를 다 만들어 놓고
**맞아도 별일이 없었다.**

## 피해를 Meteor 로 옮겼다

`DamageSource` 를 운석에서 뗐다. 두 이유가 있다.

**균일하다.** 컴포넌트 하나에 값 하나뿐이라 단계를 표현할 자리가 없다.

**착탄 후에도 때렸다.** `repeatCooldown` 0.5초, `MeteorDespawnSeconds` 6초 —
**바닥에 박힌 운석 옆에 서 있으면 열두 번 맞았다.** §19 는 "충돌 **순간에만** 넉백과
기절 판정이 있다" 고 정한다.

`Meteor.ApplyImpact` 가 `HasImpacted` 로 이미 한 번만 돌게 막혀 있으므로, 여기로
옮기는 것만으로 §19 의 그 줄이 지켜진다.

체력을 안 거치는 것이 오히려 기획에 맞다 — §20.1 의 제목이 **"체력 없는 상태 기반 피해"**
이고 "체력, 사망과 영구 탈락은 없다" 고 못 박는다. 체력은 낙하(§20.2)에만 남는다.

## 경계는 반경의 절반

§20.1 은 약함·강함을 나누면서 **경계를 정하지 않았다.** 반경의 절반으로 잡았다.

새 기준을 만들지 않고 `AddExplosionForce` 가 이미 쓰는 거리 감쇠와 **같은 축**을 쓴다.
규칙이 둘이면 "밀리기만 했는데 장비를 놓쳤다" 가 생긴다.

직접 충돌은 **거리로 알 수 없다.** 운석 본체가 몸에 닿았는지는 충돌 콜백만 안다. 그래서
`MeteorImpact.TierFor` 는 `Direct` 를 돌려주지 않고, `OnCollisionEnter` 가
`GetComponentInParent<PlayerRig>()` 로 따로 잡는다.

`MeteorImpact` 는 상태가 없는 순수 계산이다 — `MeteorWarningLook`(S15P21B202-241)과 같은
방식이고, 검증이 거리를 직접 넣어 경계를 잠근다.

## 중형이 이제 아프다

`SetSize` 가 **반경만** 바꾸고 힘은 900 고정이었다. 크기를 나눠 놓고
(S15P21B202-234) 중형은 **맞는 범위만 넓고 아프지는 않았다.**

```csharp
public static float ForceFor(MeteorSize size)
{
    var scale = Meteor.RadiusFor(size) / PhysicsScale.MeteorSmallImpactRadius;
    return PhysicsScale.MeteorImpactForce * scale;
}
```

상수를 새로 두지 않았다. 반경(§17.7 소형 2m · 중형 3.5m)에서 끌어내므로 **반경만 바꿔도
힘이 따라온다.**

## 장비 떨어뜨리기를 기절에서 떼냈다

`ReleaseHand` · `ClampInteractor.Cancel` · `ResetClampGrab` · `ResetToolState` 다섯 줄이
`PlayerRig.GoDown` **안에만** 있었다. §20.1 의 "강한 충격: 장비를 떨어뜨림" 은 기절이
아니므로 **기절하지 않으면 장비를 떨어뜨릴 수가 없었다.**

`public void DropEquipment()` 로 빼고 `GoDown` 이 그것을 부른다.

고정못은 손만 뗀다. `ResetClampGrab` 이 진행분을 남기므로 §12.1 "운석에 맞아도 빠진
정도는 유지된다" 가 지켜진다.

## 검증에서 걸린 것

**`PlayerRig` 의 transform 은 원점에 있다.**

```csharp
var go = new GameObject("플레이어 " + nickname);   // 원점
rig.Doll = RagdollBuilder.Build(nickname, footPos, ...);   // 발 위치
rig.Doll.Root.SetParent(go.transform, true);
```

`transform.position` 으로 거리를 재니 **모든 플레이어가 원점 기준**이 됐다. 1.6m 에 세운
플레이어가 강한 충격을 받아 검증이 실패했다. `FootPosition`(`Doll.Hips.position` 에서
엉덩이 높이를 뺀 값)으로 바꿨다 — 착탄 지점이 지면이므로 발에서 재는 것이 맞다.

**인게임에서도 틀렸을 버그다.** 래그돌만 움직이고 부모 transform 은 영원히 원점에 있으니,
검증이 없었으면 "원점 근처에서만 강한 충격" 이 됐다.

**순서.** `GoDown` 을 먼저 부르면 `KnockDown` 이 `Downed` 를 보고 즉시 돌아나가
**날아가지 않는다.** §20.1 은 "날아간 뒤 기절" 이다. 검증 하나가 이 순서만 본다.

**래그돌은 뼈마다 Rigidbody 다.** `OverlapSphere` 가 한 사람을 열 번 돌려주므로 강한
충격이 겹쳐 들어가 기절로 넘어갔다. `Struck` 목록으로 사람 단위로 모은다.

**기존 검증 하나의 전제가 뒤집혔다.** `SpawnedMeteor_CarriesDamageSource` 가 잠그던
것이 바로 문제였다 — `SpawnedMeteor_DoesNotCarryDamageSource` 로 바꿨다.

## 검증 17개

| 묶음 | 무엇 |
| --- | --- |
| 경계 (순수 계산) | 반경 밖 · 안쪽 절반/바깥쪽 절반 · 중형의 넓은 강한 충격 범위 |
| 크기 | 중형이 더 세게 밀침 · 반경 비율과 일치 · `SetSize` 가 둘 다 바꿈 |
| 직접 충돌 | **한 번에 기절** · **날아간 뒤** 기절 · 반경 밖에서 맞아도 기절 |
| 강한 충격 | 날아가지만 기절 아님 · 장비를 놓음 |
| 약한 충격파 | 밀리지만 조종 유지 · 반경 밖은 무사 |
| **착탄 순간에만** | 6초 버텨도 두 번째 판정 없음 · 두 번째 `ApplyImpact` 무시 |
| 쳐낸 운석 | 같은 규칙 (§20.1) |
| 기절한 사람 | 다시 밀림 (기존 동작 방어선) · 래그돌 한 번만 처리 |

전체 494개 중 481 통과. 실패 13개는 develop 에 이미 있던 것이다(S15P21B202-232 건).
477 + 17 = 494, 464 + 17 = 481 — **늘어난 실패가 없다.**

## 이미 되던 것

`GameLayers.PlayerMask` 에 `PlayerDowned`(7번) 가 포함돼 있어서 §20.1 의 "기절한
플레이어도 다른 물체의 충격으로 다시 밀릴 수 있다" 는 **이미 동작했다.** 건드리지 않고
검증만 걸어 방어선으로 삼았다.

## 남긴 것

- **분화구와 박힌 운석은 §19 의 몫이다.** 운석은 여전히 6초 뒤에 사라진다.
  "착탄 후 접촉 피해 없음" 은 두 이슈가 겹치는 부분이라 여기서 처리했다.
- 낙하 판정(§20.2 휘청임 · 머리부터 충돌하면 기절)은 별도다.
- `DamageSource` 클래스는 남겨 뒀다. 운석이 안 쓰게 됐을 뿐이고, 다른 위험물이 쓸 수 있다.
- 소리·VFX 는 재생 경로가 없다([Sound] · [Art]).

## 인게임 확인은 못 했다

빌드가 develop 에서 멈춘다(지형 자산 체크아웃 손상 + 로비 씬 Volume Profile 누락).

EditMode 로 잠근 것은 **단계 경계와 각 단계의 결과**다. "강한 충격이 강하게 느껴지는지"
와 `MeteorHeavyKnockSpeed = 8` 이 적당한지는 인게임에서 봐야 한다.
