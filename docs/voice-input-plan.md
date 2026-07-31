# MVP 음성 입력(발화→노드) 지원 계획

작성: 2026-07-16 · 담당: 개발자 3 (그래프/서버 반영)

## 현황

인식 **이후** 파이프라인은 이미 완성되어 있다. 부족한 것은 "말소리 → 텍스트" 앞단뿐이다.

```
[음성 인식]                      ← 이 문서의 범위
   ↓ transcription(string)
MvpVoiceRequirementController.SubmitTranscription()
   ↓ GraphManager.RequestCreateRootPropertyNode()      (로컬 노드 즉시 생성 — 오프라인에서도 동작)
   ↓ GraphManager.RequestNodeByUtterance()
   ↓ (온라인) UtteranceApiClient → POST /api/node/generate/utterance
   ↓ 서버 LLM 확장 → 응답 {node_id, node_text}
   ↓ ApplyServerNodeId(rekey) → Fusion RPC로 전 참가자 동기화   (2026-07-16 구현 완료)
```

클라이언트에 이미 있는 인식 백엔드 2종:

| 백엔드 | 플랫폼 | 조건 | 상태 |
|---|---|---|---|
| `DictationRecognizer` (Windows) | 에디터 / PC Link | OS에 한국어 음성 인식 언어팩 설치 | 코드 완성. 언어팩 없으면 시작 실패(친절한 안내 처리 확인됨) |
| `AppDictationExperience` (Meta Voice SDK) | Quest 네이티브 | `META_VSDK_PLATFORM_INTEGRATION` 심볼 + 마이크 권한 | 코드 완성. **미검증 리스크 다수(아래)** |
| (없음) | 그 외 | — | "이 기기의 음성 인식 설정이 아직 필요해요" 안내 |

## 리스크 (해결 순서대로)

1. **Quest 한국어 인식 품질/지원 여부 미확인** — Meta 온디바이스 dictation의 한국어 지원은 제한적일 수 있다. 실기기 검증 전까지 Quest 네이티브 경로를 본선으로 삼으면 안 된다.
2. **`RECORD_AUDIO` 권한 부재** — `Assets/Plugins/Android/AndroidManifest.xml`에 마이크 권한이 없고, 런타임 권한 요청(`Permission.RequestUserPermission`)도 없다. 현재 상태로는 Quest에서 무조건 실패한다.
3. **`META_VSDK_PLATFORM_INTEGRATION` 심볼** — Player Settings에 켜져 있는지 미확인. 꺼져 있으면 Quest 경로가 컴파일 자체에서 제외된다.
4. **시연 PC 언어팩** — 오늘 확인한 개발 PC에는 Windows 음성 인식이 미설치("Speech recognition is not supported on this machine").

## 단계별 계획

### Phase 1 — 시연 최소선: PC Link 경로 확보 (0.5일)
- [ ] 시연 PC에 Windows 한국어 음성 인식 설정 (설정 → 시간 및 언어 → 음성 → 한국어 음성 팩 설치). 절차를 README에 문서화.
- [ ] 미지원 기기에서 '말로 추가' 버튼을 비활성화하고 라벨을 "음성 미지원 기기"로 — 반복 클릭/혼란 방지. (버튼 첫 표시 시 1회 지원 여부 프로브)
- 결과: **Quest를 PC Link로 시연하는 시나리오에서 음성 입력이 실제로 동작.**

### Phase 2 — Quest 네이티브 사전 작업 + 검증 (1일)
- [ ] `AndroidManifest.xml`에 `<uses-permission android:name="android.permission.RECORD_AUDIO" />` 추가.
- [ ] '말로 추가' 첫 탭 시 `Permission.HasUserAuthorizedPermission` 확인 → 없으면 `RequestUserPermission` + 안내 칩("마이크 사용을 허용해 주세요").
- [ ] Player Settings에 `META_VSDK_PLATFORM_INTEGRATION` 심볼 확인/추가.
- [ ] **실기기 한국어 dictation 테스트** — 여기서 한국어가 되면 Quest 네이티브를 본선으로 승격, 안 되면 Phase 3이 본선.
- 참고: Quest에는 Google 서비스가 없어 Android `SpeechRecognizer` 폴백은 불가. Meta가 안 되면 곧장 서버 STT로 간다.

### Phase 2.5 — 온디바이스 한국어 STT ✅ 에디터 프로토타입 완료 (2026-07-16)

**구현됨** — 공식 sherpa-onnx C# 바인딩 직접 통합(서드파티 Unity 패키지 없음):

- `Assets/Plugins/SherpaOnnx/` — `sherpa-onnx.dll`(관리, netstandard2.0) + `x86_64/`(sherpa-onnx-c-api.dll, onnxruntime.dll) — NuGet 1.13.4에서 추출.
- `Assets/StreamingAssets/SherpaOnnx/ko-zipformer/` — 한국어 스트리밍 zipformer int8 모델(≈130MB) + tokens + 검증용 test_wavs.
- `MvpOnDeviceDictation.cs` — 마이크 스트리밍 인식(부분 자막 OnPartial, 무음 자동 확정 OnFinal, 종료 시 0.66s 무음 패딩으로 끝어절 잘림 방지), WAV 디코드 검증 헬퍼.
- `MvpVoiceRequirementController`가 이 백엔드를 **1순위**로 사용(모델 있으면), Windows/Meta 경로는 폴백.

**검증 결과(에디터, 모델 동봉 테스트 음성):**

| 정답 | 인식 |
|---|---|
| 그는 괜찮은 척하려고 애쓰는 것 같았다. | 걔는괜찮은척하려구애쓰는거같았다 |
| 지하철에서 다리를 벌리고 앉지 마라. | 지하철에서다리를벌리고하진마라. |

3.4초 음성 처리 93ms(RTF≈0.03, 데스크톱). 출력에 공백이 없는 점(후처리 여지)과 구어 변형 수준 오차 — 아이디어 캡처 용도로 충분.

**⚠ 모델은 git에 올라가지 않는다** — encoder(121MB)가 GitHub 100MB 제한 초과라 `.gitignore` 처리. 팀원은 아래로 받는다:

```bash
curl -L -o ko-model.tar.bz2 "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-streaming-zipformer-korean-2024-06-16.tar.bz2"
tar -xjf ko-model.tar.bz2
# *.int8.onnx 3개 + tokens.txt 를 Assets/StreamingAssets/SherpaOnnx/ko-zipformer/ 에 복사
```

**Quest 네이티브 준비 완료(2026-07-16):**
- [x] arm64 네이티브(`libsherpa-onnx-c-api.so`, `libonnxruntime.so`) → `Assets/Plugins/SherpaOnnx/Android/arm64-v8a/` (PluginImporter: Android/ARM64 전용, win dll 은 에디터+Windows 전용).
- [x] `Prepare()` 비동기 준비 체인: 마이크 런타임 권한 → APK→persistentDataPath 모델 추출(최초 1회 ≈130MB) → 인식기 초기화. 빌드에 모델이 없으면 "키보드로 입력해 주세요" 안내 후 재시도 차단.
- [x] `IsSupported()` Android 분기 해제.
- [ ] **Quest 실기기 검증만 남음**: 인식률(아이 발음·소음)/프레임/발열. ※ 모델이 gitignore 라 빌드 머신에 모델을 받아둬야 APK 에 포함된다.

**음성 UI 배치(2026-07-16):**
- 액션바 **'말로 추가'** — 루트 아이디어 생성 (부분 자막은 안내 칩, 무음 시 자동 확정 → 노드 생성).
- 월드 키보드 **'말하기' 키** — 노드 이름 변경·직접 부품 추가·방 만들기 폼 등 **모든 입력칸**에서 받아쓰기. 미확정 부분 자막은 프리뷰에 회색으로 표시, 확정 시 커밋 텍스트에 이어 붙음.
- 에디터 라이브 검증 완료: 실마이크로 듣기 시작/정지, 인식 결과→노드 생성, 키보드 키 준비→듣는 중→정지 사이클.

#### (참고) 당초 조사 내용

Meta 온디바이스 dictation과 별개로, 서드파티 온디바이스 엔진 2종이 Quest에서 실증돼 있다.
**둘 다 에디터/Windows에서도 같은 코드로 돌아가므로, 채택 시 Windows 언어팩 의존(Phase 1)도 함께 사라진다.**

| 후보 | 방식 | 한국어 | 크기/성능 | Unity 통합 |
|---|---|---|---|---|
| **sherpa-onnx** 한국어 streaming zipformer (`sherpa-onnx-streaming-zipformer-korean-2024-06-16`) | 스트리밍(말하는 동안 부분 자막) | **한국어 전용 모델** | ~60MB, 모바일 지연 ~160ms | Ponyu-dev/Unity-Sherpa-ONNX 플러그인(Android 라이브러리 원클릭 설치, 마이크 캡처 포함) |
| **whisper.unity** (whisper.cpp) | 클립 일괄 변환(push-to-talk와 맞음) | 다국어(60개, ko 포함) — tiny는 한국어 약함, base/small 권장 | tiny 75MB~small 466MB, Quest 3에서 근실시간 실증(whisper-meta-quest) | Macoron/whisper.unity (MIT) |

- 1순위 프로토타입: **sherpa-onnx 한국어 zipformer** — 한국어 전용 + 스트리밍이라 "듣는 중 · {부분 자막}" UX(기존 Report 경로)와 정확히 맞는다.
- 2순위: whisper.unity base 모델 — 품질 비교용.
- 검증 항목: ① 아이 발음·교실 소음에서 인식률 ② VR 렌더링과 동시 실행 시 프레임/발열 ③ APK 크기 증가(+60~150MB) 수용 여부 ④ 커뮤니티 플러그인 유지보수 리스크.
- 통과 시 `IDictationBackend`의 `OnDeviceSTT` 백엔드로 편입 → **모든 기기에서 서버 없이 동작**하는 본선이 되고, Phase 3(서버 STT)은 품질 백업으로 강등.

### Phase 3 — 서버 STT (온디바이스 검증 실패 시 본선 · 서버 이슈 #40과 연계, 클라 1일 + 서버 협의)
기기 무관·한국어 품질 최고·서버 팀의 "실시간 발화 처리(#40)"와 자연스럽게 결합되는 경로.

- 클라이언트 (push-to-talk, VAD 불필요):
  - [ ] '말로 추가' 누름 → `Microphone.Start()` 녹음(레드 펄스 표시), 다시 누름 또는 8초에 자동 종료.
  - [ ] AudioClip → WAV(16kHz mono) 인코딩 → `POST /api/utterances/audio` (엔드포인트 스펙은 서버 팀과 협의) with room_id/user_id.
  - [ ] 응답 대기 동안 "생각을 정리하는 중…" 안내 → 응답 `{ text }` 또는 `{ node_id, node_text }`를 기존 `SubmitTranscription`/rekey 경로에 그대로 연결.
- 서버(협의 필요):
  - Whisper(또는 클라우드 STT)로 텍스트화 → 기존 발화 파이프라인 재사용.
  - 스펙 결정 사항: 엔드포인트 경로, 최대 녹음 길이, 오디오 포맷, 응답이 텍스트만인지 노드 생성까지인지.

### 구조 정리 (Phase 2~3과 병행)
- [ ] `MvpVoiceRequirementController`를 `IDictationBackend` 전략 3개(Windows / MetaQuest / ServerSTT)로 분리.
  - 기기에서 사용 가능한 백엔드를 우선순위(Meta → Windows → ServerSTT)로 자동 선택, 시작 실패 시 다음 백엔드로 폴백.
  - `OnStatusChanged` 이벤트·버튼 UX는 백엔드와 무관하게 동일 유지.

## 권장 순서 요약

**시연이 급하면 Phase 1만으로 충분**(PC Link + Windows 언어팩). 수업 실사용(학생 Quest 단독) 본선 후보는 **Phase 2.5(sherpa-onnx 한국어 온디바이스)** 이며, 프로토타입에서 아이 발음·소음·발열 검증을 통과하면 서버 없이 전 기기 동작이라 최선이다. 실패 시 Phase 3(서버 STT)이 본선. Phase 2(RECORD_AUDIO 권한 등)는 2.5와 3 공통의 전제 조건이다.
