// 다운로드 버튼이 가리키는 곳. 비우면 버튼이 꺼집니다.
// 인증 토큰이 들어간 URL, 만료되는 개인 다운로드 URL은 넣지 마세요.
//
// **여기를 비워 두면 안 됩니다** (S15P21B202-452). 서버에는 값이 들어 있는데 저장소가
// 비어 있으면, 다음 사이트 배포가 서버를 덮어 **다운로드 버튼이 조용히 꺼집니다.**
// 2026-09-13 에 실제로 그 상태였습니다. `test_site_release_config.py` 가 이제 막습니다.
//
// 주는 것은 **런처**입니다(S15P21B202-406). 사용자는 이것 하나만 받고, 다음부터는
// 런처가 바뀐 파일만 받아 맞춥니다 — 릴리스마다 156 MB 를 다시 받지 않습니다.
// 이름에 버전이 없는 것은 일부러입니다: 릴리스마다 이 파일을 고치지 않아야 합니다.
window.ReleaseConfig = {
  downloadUrl: "/builds/NotThisPlanetLauncher.exe",
  version: "v1.0.0",
  fileSize: "15 MB",
  // 공개 응답용 링크만 사용하세요. 설문 편집 주소는 넣지 마세요.
  feedbackUrl: "https://docs.google.com/forms/d/e/1FAIpQLSeUK83EHeX9n6cuMBYJWsuoGVNIfnNwRiW2XKEIjVoV950Z3w/viewform"
};
