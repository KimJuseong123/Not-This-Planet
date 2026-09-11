using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotThisPlanet.Launcher;

/// <summary>
/// JSON 을 **리플렉션 없이** 읽는다 (S15P21B202-406).
///
/// <para><b>왜.</b> 트리밍(<c>PublishTrimmed</c>)을 켜려면 리플렉션으로만 닿는
/// 코드가 없어야 한다. <c>JsonSerializer</c> 의 기본 경로는 실행 시점에 타입을
/// 들여다보므로, 트리머가 그 타입의 프로퍼티를 지워도 <b>빌드는 통과하고 실행만
/// 조용히 깨진다.</b> 소스 생성기를 쓰면 읽는 코드가 컴파일 시점에 만들어져
/// 트리밍이 안전해진다.</para>
///
/// <para>그래서 런처가 70.2 MB → 더 작아진다. 사용자가 받는 첫 파일이고,
/// 자체 갱신마다 다시 받는 파일이다.</para>
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Manifest))]
[JsonSerializable(typeof(LatestPointer))]
[JsonSerializable(typeof(LauncherInfo))]
public partial class LauncherJson : JsonSerializerContext
{
}
