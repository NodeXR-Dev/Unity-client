# NodeXR TODO — 개발자 3 (노드그래프 / GraphData / 서버 반영)

마지막 업데이트: 2026-08-12

---

## 2026-08-12 팀원 원격 접속용 서버 주소 확보 (씬 3곳 교체)

팀원들이 각자 집에서 Quest 로 붙어야 하는데, 씬에 박혀 있던 서버 주소
(`scores-texts-pierre-origin.trycloudflare.com`)가 죽어 있었다. 임시 터널이라
껐다 켜면 주소가 바뀌는 종류였다. **Unity 는 서버 주소를 앱에 구워서 빌드하므로
주소가 바뀌면 팀원 헤드셋마다 재설치가 필요하다** — 고정 주소가 요구사항의 핵심.

- [x] **씬 3곳 주소 교체** → `https://yippee-connector-ritzy.ngrok-free.dev`
  - `MvpLobby` : `NetworkManager.backendHost`, `LobbyCreateRequirementFlow.apiHost`
  - `MVP_SH` : `MvpClassroomFlow._backendHost`
  - 유니티가 켜져 있어 파일 직접 편집 대신 에디터(MCP)로 수정 후 저장. diff 는 주소 3줄뿐
- [x] **터널 구성**(서버 repo `docker-compose.tunnel.yml`) — 로컬 서버를 고정 주소로 노출.
  개발용 `docker-compose.yml` 은 안 건드려서 백엔드만 로컬로 돌리는 팀원에겐 영향 없음
  ```
  docker compose -f docker-compose.yml -f docker-compose.tunnel.yml up -d
  ```
- [x] **서버 이미지 8.89GB → 3.2GB**(서버 repo `Dockerfile`) — torch 를 기본 PyPI 에서 받아
  CUDA 빌드가 딸려오고 있었다(nvidia 2.7G + triton 691M). GPU 를 쓰지 않으므로 CPU 전용
  휠로 교체. 임베딩 모델도 이미지에 미리 받아 둬서, 기동 때마다 HuggingFace 를 타지 않는다
  (다운로드 실패 시 lifespan 에서 서버가 아예 안 뜨던 경로 제거)
- [x] **검증**(터널 경유 실측): 방 목록 200 / 방 생성 `ROOM200` / 타 사용자 입장 `ROOM203`.
  Unity 와 같은 User-Agent 로도 ngrok 경고 페이지 없이 JSON 정상 수신

### 조건 / 남은 것

- 이 방식은 **형님 PC 가 켜져 있을 때만** 팀원이 접속 가능하다. 2달 임시 용도로 선택한 절충.
  상시 가동이 필요해지면 서버 repo `chore/cloud-deploy` 브랜치의 배포 구성(`DEPLOY.md`)을 쓰면 된다
- [ ] **`OPENAI_API_KEY` 재발급 필요** — 작업 중 명령 출력에 키가 노출됐다. 폐기 후 교체
- [ ] MinIO(생성된 2D 이미지)는 아직 터널 밖으로 안 열려 있다. 아래 키가 들어와 2D 단계까지
  도달할 때 같이 뚫는다
- [ ] 팀원 배포용 APK 재빌드 — 씬 주소가 바뀌었으므로 기존 설치본은 옛 주소를 본다

---

## 2026-08-08 로비 STT 연결 + 유저플로우 완주 (로비 → MVP_SH)

- [x] **로비 STT 신규** `Assets/00_Scenes/MVP/Runtime/Lobby/MvpLobbyDictation.cs`
  - `LobbyCreateRequirementFlow.StartRequirementSpeechToText()` 가 빈 스텁이고 `featureTextInput` 도 미연결이라 요구사항 텍스트가 **항상 비어 세션 시작이 차단**돼 있었다. 인식 문장을 공개 API `SetRequirementFeatureText()` 로 넣어 해소.
  - 요구사항 패널이 열리면 자동으로 듣기 시작 → 부분 자막(흐리게) → 확정(진하게) → Flow 로 전달. 마이크 버튼으로 재녹음, 입력 레벨 바 제공. 폰트는 패널의 TMP 폰트를 재사용.
  - 원본 게이트(`requireSpeechBeforeCreation`, 소리 크기 2초)는 `Microphone` 을 직접 열어 STT 와 장치를 다투므로 **MvpLobby 에서 끄고 STT 가 대체**한다. 잡음이 아니라 실제 문장을 확인하므로 더 정확하다. 남의 파일(`02_Scripts/Lobby`)은 수정하지 않았다.
- [x] **로비에 회의실 부품이 붙던 회귀 수정**(오늘 만든 것): XR 부트스트랩 게이트를 MVP 폴더로 넓히면서 `MvpLobby` 도 걸려 `MvpMeetingRoomPlayerController` 가 로비에서 좌석 배치를 시도했다. 실측 결과 리그가 **y=-743 으로 발산**. → `MvpClassroomFlow` 가 있는 씬에서만 실행하도록 게이트 추가. 발산 방지 클램프(보정 50m 초과 시 목표 지점 직접 설정)도 넣었다.
- [x] **아바타·GraphNetworkManager 가 스폰되지 않던 문제**: 로비에서 씬을 갈아탄 직후 프리팹 로드가 끝나지 않아 `NetworkObjectSpawnException` 으로 조용히 실패했다(= 멀티플레이에서 서로 안 보임). `Prefabs.Load(동기)` 만으로는 부족해 로드 완료까지 프레임을 넘기며 재시도하는 `SpawnWhenPrefabReady()` 추가.
- [x] **발화 API 경로 불일치 수정**: 클라가 초안 명세로 선구현한 `POST /api/node/generate/utterance` 는 서버에 없어 **항상 404** 였다. 그러면 속성 노드가 서버에 등록되지 않아 스냅샷에서 누락되고, 2D 생성이 `Input Snapshot에서 생성 Connection의 Node를 찾을 수 없습니다` 로 실패한다.
  - 경로 → `POST /api/utterances`, 요청 → `{room_id, user_id, parent_node_id?, parent_node_position?, utterance}` (`node_type` 제거, `position` → `parent_node_position` = **부모** 위치)
  - 응답이 단건 `{node_id, node_text}` 가 아니라 **서브그래프 전체**(`sub_graphs[].root_node_id/nodes/edges`)다. root 를 로컬 placeholder 에 매핑한다 — PROPERTY→PART 연결에 쓰는 node_id 가 root 여야 서버가 하위 체인을 탐색할 수 있다(CLAUDE.md 규약).
  - 검증: 404 → 500 으로 바뀜(경로·스키마 통과, OpenAI 단계에서만 실패)
- [x] **서버 `password` optional**(별도 repo, 사용자 결정): 스키마만 바꾸면 공개 방에 아무도 못 들어오므로 4곳을 함께 고쳤다 — 요청 스키마(Create/Enter), 생성 시 해시 NULL 저장, **입장 시 해시가 NULL 이면 통과**, 응답 nullable. `room_password_hash` 가 이미 nullable 이라 마이그레이션 불필요. 비번 방의 오답/생략 거부는 그대로 유지됨을 실서버로 확인.

### 완주 검증 (플레이 모드 실측)

로비 → 요구사항 음성 → 방 생성(비밀번호 없이) → MVP_SH 진입까지 통과.

| 항목 | 결과 |
|---|---|
| 로비 STT | `[MVP 로비 STT] 요구사항 인식: …` |
| 요구사항 서버 전송 | `FEATURE200` |
| 세션 인계 | `로비 세션 이어받음 — room_id=…` |
| 회의실 상태 | `Briefing` (Welcome 재시작 안 함) |
| Fusion 러너 | 1개 (중복 방 없음) |
| 로컬 아바타 / GNM | 스폰됨 + 브리지 활성화 |
| WebSocket | `wss://…/ws/rooms/event` 연결 |
| 파트 노드 서버 등록 | `PART_NODE200` |

### 남은 블로커 — 코드가 아니라 서버 환경 설정

서버 `.env` 의 키 3개가 플레이스홀더(`REPLACE_..._KEY`)라 AI 생성 체인 전체가 막혀 있다.

- [ ] `OPENAI_API_KEY` — 발화 → 속성 서브그래프 추출. 없으면 `BTUTT500`, 속성 노드가 서버에 안 생겨 **2D 생성 불가**
- [ ] `GEMINI_API_KEY` — 2D 이미지 생성
- [ ] `MESHY_API_KEY` — 3D 모델 생성

### 무해한 에러 (제 변경과 무관, 참고)

- `카메라 리그를 찾을 수 없습니다` — `XRPlayerBinder` 가 `GameObject.Find` 를 쓰는데 에디터엔 HMD 가 없어 리그가 비활성. 실기기에선 정상
- 스크립트 누락 2건 — `Assets/01_Prefabs/Lobby/Player 4.prefab` 의 기존 손상(`VOICE LEVEL`, `Text (TMP)`)
- Photon Voice `opus_egpv` DllNotFound + AppId 미설정 — 음성 **채팅** 쪽. STT 와 무관

---

## 2026-08-08 XRMeetingWorld 도입 (로비·회의실 배경 교체 + Quest 최적화)

- [x] **FBX 임포트**: `Assets/04_Models/XRMeetingWorld/XRMeetingWorld.fbx` (Scale Factor 1 / Convert Units 해제 / 카메라·라이트 미임포트 / 머티리얼 External). 420×64×420m, 렌더러 1,678개, 삼각형 222,766, **고유 메시 20개**(같은 메시가 수백 번 반복되는 구조).
- [x] **투명 머티리얼 3종 수동 설정**(FBX가 알파를 안 옮김): `M_Glass_Curtainwall` 0.10/0.95 Q3010, `M_Glass_Skylight` 0.20→**0.35**/0.96 Q3000, `M_Water` 0.85/0.97 Q3020. Skylight 는 Blender 프레넬 알파가 URP 에 안 넘어와 지붕이 판으로 분해돼 보이던 것 → 알파 0.35 로 상향해 해소.
- [x] **작업용 프리팹** `Assets/01_Prefabs/World/XRMeetingWorld.prefab` — 자연물을 `WorldNature`(레이어 8) / `WorldGrass`(레이어 9)로 분리, 프로브 조회 Off, 모션벡터 Off, 지형 외 자연물 그림자 캐스팅 Off, 지붕·천창·유리벽 그림자 Off(내부가 새까매지고 60k 삼각형이 그림자 패스에 두 번 그려지던 문제), FBX 기본 콜라이더 제거. **Static Batching 은 일부러 끔**(메시 복제로 메모리가 늘고 인스턴싱이 무효화됨).
- [x] **회의실(MVP_SH)**: 월드를 y=**-2.32** 에 배치해 파빌리온 바닥(y=0.22)이 기존 회의실 바닥(y=-2.1)과 정확히 일치. `Meeting_Room` 껍데기 12개(벽/천장/바닥/파노라마/문/책장/조명기구 등) 비활성화. 옛 `Floor` 콜라이더 대체용 `MvpWorldFloor` BoxCollider(46×34m) 추가.
- [x] **책상·의자 제거**(`Table_01`/`Chairs` 삭제): 참조하던 4곳을 조사한 결과 —
  - `MvpWorkspaceLayout.PositionMainSketchPanel` / `MvpXrCanvasAnchor.TryPlaceInMeetingRoom` → **이미 카메라 기준 폴백이 있어** 책상이 없으면 유저 앞에 배치됨. 수정 불필요.
  - `MvpWorkspaceLayout.TryGetDeskTop` / `MvpClassroomFlow.TryGetDeskTop` → **호출자 0 인 죽은 코드**(구 '책상에 붙이기'의 잔재). 삭제.
  - `MvpMeetingRoomPlayerController.PlaceAtAssignedSeat` → 유일하게 폴백이 없어 배치가 통째로 실패했음. **`PlaceAtOpenFloorSpot()` 신규**: 의자가 없으면 참가자를 좌우로 나란히 세우고(중심 0,-2.1,0 / +z / 간격 1.2m / 4자리 / 눈높이 1.55m) 전원이 같은 방향을 보게 함. 마주 보게 하면 각자 앞에 뜨는 작업판이 상대 시야를 가림.
  - 카메라 리그 기본 위치를 방 밖 (38.9, 0, 34.3) → **(0, -2.1, -1.2)** 로(배치 코드 실행 전 1프레임 방어). 데스크톱 폴백 카메라도 서 있는 눈높이 (0, -0.55, -1.2).
  - 검증: 플레이 모드 에러 0, 카메라 바닥높이 1.55m, Welcome 패널이 파빌리온 안 눈높이에 정상 렌더.
- [x] **로비(MvpLobby)**: 파빌리온 **동쪽 55m 평지**(지형 높이차 0.00m, 파빌리온이 시야 38도)에서 바라보는 구도. 월드 회전 Y=90도, 위치 (-1.4, 0.125, 55) — y 는 텔레포트 발판 윗면과 지면을 맞춘 값. 야외와 안 어울리는 파란 콘크리트 발판은 렌더러만 끄고 콜라이더는 유지(텔레포트 정상).
- [x] **패스스루 → 스카이박스**(MvpLobby): `OVRManager.isInsightPassthroughEnabled` 가 켜져 있어 `CenterEyeAnchor` 가 투명 검정으로 클리어 → 하늘이 검게 나왔음. 완전한 가상 월드가 배경이 됐으므로 패스스루 Off + Skybox 클리어로 전환. (MVP_SH 는 이미 Off 상태였음)
- [x] **조명 통일**: 앰비언트 Trilight(하늘 0.62/0.70/0.82, 수평 0.48, 지면 0.26), 태양 세기 1.25 / 그림자 강도 0.55 / 각도 (46, 330).
- [x] **거리 컬링** 신규 `Assets/00_Scenes/MVP/Runtime/World/MvpWorldCulling.cs` — `Camera.layerCullDistances` 로 풀 55m / 나무·바위 220m, far plane 400m, 경계를 가리는 선형 포그(90~260m). 두 씬 모두 배치.
  - 주의: `Camera.layerCullSpherical` 은 **빌트인 렌더러 전용**이라 URP 에서 에러 → 사용하지 않음.
- [x] **실측 검증**(플레이 모드, 로비): 드로우콜 **1,118 → 401**(-64%), SetPass 27(SRP Batcher 정상), 삼각형 318k. 렌더러 기준 1,678 → 약 200개. 콘솔 에러 0.
- [ ] **Quest 실기기 확인**: 눈높이 대비 지면 높이, 파빌리온 내부 밝기, 풀 컬링 경계(55m) 팝인, 실제 프레임.
- [ ] (선택) 드로우콜을 더 줄이려면 `Mobile_RPAsset` 의 **GPU Resident Drawer**(현재 Disabled)를 Instanced Drawing 으로. 고유 메시가 20개뿐이라 효과가 크지만 렌더링 경로(Forward+) 요구사항이 있어 팀 공용 에셋 변경 필요 — 별도 판단.

---

## 2026-08-01 디자인 에셋 적용 + 씬 정리 + 미작동 기능 감사

- [x] **Design_0527 디자인 에셋 반영**: `Assets/05_Design/JW/**` 를 디자이너 브랜치와 동기화(새 NodeBox `New_base.fbx`/`NewNodebox.prefab`, ShaderGraph 7종, `TextPannel.prefab`, `RotatingGradientBorder` 셰이더). 구 `Box_00~04.mat` → `Materials/old/`, `NodeBox2.fbx` → `NodeBox(Old).fbx` (GUID 보존 → 참조 무손상 확인). `ProjectSettings/GraphicsSettings.asset` 도 반영(Always Included Shaders 에 새 셰이더 1개 추가).
- [x] **노드 외형 교체**: `NodeView_Sub.prefab` 루트의 옛 MeshFilter/MeshRenderer 제거 → 자식 `NodeBoxModel`(New_base 메시, localScale 102.3)로 대체. 크기 실측 0.397×0.184×0.205m 로 기존(0.397×0.2×0.2)과 사실상 동일 → 콜라이더/UI 무영향. `NodeView._meshRenderer` 재배선.
- [x] **깊이·타입별 머티리얼 신규**: `01_Prefabs/Graph/Designer/Materials/` 에 `NodeDepth_00~04`(Shader00~04) + `NodeGrey`/`NodeRed`(ShaderGrey/Red) 생성. `_depthMaterials` 와 타입별 머티리얼(`_all/_part/_property/_reference/_unknown`) 재배선.
- [x] **버튼 4상태 스프라이트**: 노드 X/R/+ 에 hover/pressed/disabled 연결(`m_Transition`=SpriteSwap). 기존엔 default 만 있어 눌러도 시각 반응 없었음.
- [x] **PartPort/AddPartPort 스프라이트 오연결 수정**: `joint_all_*` → `joint_part_*`. (그림 자체는 all/part 가 동일해 시각 차이는 없음 — 정합성만 교정)
- [x] **연결 버튼 톤 정합**(`MvpXrGraphLinkController`): 새 노드가 반투명(ShaderGraph Transparent, ZWrite off)이라 불투명 진청록 '연결' 패널이 노드 앞에 뜬 것처럼 보였음 → 배경 `(0.72,0.80,0.92,0.42)`, 라벨 어두운 톤으로 변경.
- [x] **회귀 수정**: `RotatingGradientBorderUI` 를 붙였다 뗀 뒤 `LabelInputField` Image 가 투명(a=0, Sliced)에서 불투명 흰색으로 남는 문제 → 프리팹에서 원복.
- [x] **개발용 찌꺼기 제거**(MVP.unity): `SeedGraphLoader`(청소로봇 더미 시드), `GraphConnectDragger`(레거시 마우스 연결) 삭제. 둘 다 런타임에 코드로 강제 비활성화되던 죽은 오브젝트. 플레이 검증 완료(노드 생성 정상, MVP 에러/경고 0).
- [x] **불용 항목 정리**: `CableMat`/`CableRenderer.cs` 는 미채택 — CableMat 은 URP Lit 계열이라 LineRenderer vertex color 를 무시(활성 엣지 초록선이 깨짐), CableRenderer 는 EdgeView 의 베지어와 기능 중복.
- [x] **TODO 정합**: 아래 "A. EdgeView ConnectorSphere", "B. ConnectedNodeButton.prefab" 은 실제로는 이미 배선 완료(프리팹 실측 확인). 미완료 표기가 낡았음.

### 미작동 기능 감사 결과 (구현됐으나 실행 경로 없음)

- [ ] **⚠️ 루트 노드 서버 등록 불가**: `SubGraphApiClient` 가 MVP.unity 에 없음 → `GraphManager.OnSubGraphRequested` 구독자 0 → 루트 노드가 `_pendingCreateNodeIds` 에 갇혀 영구 "서버 미등록". 재제출도 중복 가드에 막힘. (서버 `/api/sub_graph/generate` 도 미배포라 배선해도 404 — 최소한 pending 해제 폴백 필요)
- [ ] **히스토리 완전 미연결**: `HistoryApiClient` 는 호출자 0 + 씬에 없음. 클래스만 존재.
- [ ] **서버 그래프 콜드로드 미연결**: `GraphLoadApiClient` 씬에 없음(ContextMenu 로만 실행 가능).
- [ ] **R 버튼 레퍼런스 검색 도달 불가**: `MvpXrGraphLinkController` 가 `ReferenceButton` 을 `SetActive(false)` 로 숨김 → MVP 40 에서 만든 `MvpReferencePanel` 진입점 없음.
- [ ] **음성 모델 배포 문제**: `StreamingAssets/SherpaOnnx/ko-zipformer/` 121MB 가 gitignore → 다른 팀원 클론 시 음성 인식 미동작.

---

## 2026-07-15 MVP 17차 (MCP 실화면 기반 XR UI/UX 전수 점검)

- [x] **작업 전 점검**: Welcome/Create/Join/Briefing/Design/Review/Result/History/3D/Complete를 Unity MCP Game View로 순회하고, 활성 버튼·입력창의 월드 크기와 패널 간 겹침을 실측
- [x] **공통 시각 체계 개선**: 밝은 평면 패널을 고대비 딥블루 글래스 카드로 통일하고, 본문/보조/성공/주의/오류 색을 XR 거리에서 구분되도록 재정의. 버튼 hover/press/disabled 피드백과 외곽선 추가
- [x] **가독성/잘림 수정**: 일반 본문·입력 라벨 최소 크기 상향, 버튼 내부 여백 별도 규칙, 짧은 설명문/통계 라벨 별도 축소 규칙 적용. 전체 화면 재검사 결과 TMP overflow **0건**, 버튼·입력 UI 겹침 **0건**
- [x] **공간 배치 개선**: AI 부품 추천 패널을 머리 앞 중앙에서 공동 보드 왼쪽 날개로 이동하고 보드 회전/크기를 추종하도록 MvpWorkspaceSidePanelFollower 추가. 실측 간격 약 7.5cm로 중앙 보드와 비겹침
- [x] **설계 액션 정리**: 우측 액션을 AI 부품 추천 → 2D 스케치 → 3D 확인 → 설계 마치기로 단순화하고, 드물게 쓰는 자리/설정은 하단 보조 버튼과 테이블 도크로 분리
- [x] **XR 인체공학**: 월드 키보드 폭 약 0.77m·22.5도 기울기·최소 키 높이 약 4.5cm, 테이블 설정 패널 조작 높이 약 4.6~5.2cm로 확대
- [x] **노드 UX 회귀 검증**: 한글 키보드 입력("물"), 3축 이동(z축 포함), + 자식 생성, R 숨김/연결 표시를 실제 콜백으로 확인. 새 노드의 연결 컨트롤이 늦게 붙는 타이밍 버그를 즉시 갱신 방식으로 수정
- [x] **작업 후 검증**: 수정 스크립트 7개 표준 검사 오류 0, Play 순회 중 Console error/warning 0, Welcome/Briefing/Design/Review/Complete 실화면 재촬영
- [ ] **Quest 실기기 최종 확인**: 손 포크/핀치의 실제 체감 거리, 컨트롤러 Ray/Grip, 4인 동시 시야와 테이블 착석 위치는 헤드셋에서 최종 확인

## 2026-07-15 MVP 16차 (UI/UX 레이아웃 정리: 겹침 제거 + 버튼 그룹/순서)

- [x] **겹침 측정 기반 수정**(화면 AABB 실측): 추천 패널(_toolCanvas)이 유저 앞 0.86m에 떠서 보드 중앙(파트포트·스케치·우측 액션바)을 덮던 것 → 앵커 수직오프셋 0.01→0.24로 **위로 띄움**. 재측정: tool vs sketch/ports/actionbar 겹침 **전부 False**
- [x] **액션바 그룹·순서**: 우측 세로바를 논리 순서 + 3섹션 라벨로 재편 — ①부품·아이디어(AI 부품 추천) ②그림 만들기(2D/3D/3D 보기·숨기기) ③정리·자리(완성/책상/내 자리). 그룹 간격 + 색상 코딩(생성=블루/시안/민트, 완성=Primary, 자리=Amber/Glass). `WorkspaceSectionLabel` 헬퍼
- [x] 검증: 스크린샷으로 상단 추천패널·중앙 스케치(선명)·우측 그룹 액션바가 각 구역에 분리 확인. 컴파일 0 에러
- 남은 폴리시(선택): 중앙 스케치 이미지 확대, 파트포트 상단 잔여 근접 — 필요 시 후속

## 2026-07-15 MVP 15차 (재피드백 7건: 손 근본원인·액션바 세로·노드자세·+진동·파트키보드)

- [x] **손 2개 근본원인**: 에디터엔 핸드트래킹 없어 손이 아예 렌더 안 됨 → 렌더러 disable이 무의미했음. 두 개의 **독립 손 시스템**이 원인: `TrackingSpace/*HandAnchor/[BuildingBlock] Hand Tracking`(OVR SDK 손)과 `OVRInteractionComprehensive/OVR*HandVisual`(Interaction SDK 손). 앱은 Interaction SDK를 쓰므로 **OVR 빌딩블록 손의 비주얼 드라이버(OVRMesh/OVRMeshRenderer/SkinnedMeshRenderer) 비활성** → Interaction SDK 손 하나만. 씬 저장. (실기기 확인)
- [x] **액션바 우측 세로**: 하단 가로바 → x=810 세로 컬럼(6버튼 y 250~-250) + 책상토글 y=-350. 3D 보기/숨기기 토글도 여기서 잘 보임. 사진 갤러리는 좌측(x=-810)으로 이동
- [x] **노드 항상 같은 자세**: `MvpWorkspaceLayout.LateUpdate`가 노드 회전=패널 회전으로 두던 것 → 책상 모드일 땐 노드 전용 업라이트(카메라 향) 회전 사용. 패널 누워도 노드는 세워둠
- [x] **+버튼 앞뒤 진동**: `MvpPressFeedback`의 z-lift(깊이 이동)가 포크 손가락과 hover↔press 토글 루프 → z-lift 제거(스케일 피드백만)
- [x] **'다시 앞으로' 안 눌림**: 프래질한 hover-reveal 도크 대신 **안정적 액션바 버튼**(DeskToggle, 라벨 상태연동)으로 대체
- [x] **파트추가 키보드**: `AddPartPort.BeginInput`에 `MvpWorldKeyboard.Open(_inputField)` 추가(VR 시스템키보드 없음). 생성도 404 서버 대신 `GraphManager.RequestCreatePartNode` **로컬 즉시**(Fusion 전파됨)
- [x] **'전체 설계' 빈 원(원 두개/이상한 이미지)**: ALL 노드 없는데 `MainSketchView`가 AllPort를 항상 표시 → `allNode==null`이면 `_allPort.gameObject.SetActive(false)`. 검증: is_global 0개·Refresh 후 AllPort active=False
- [ ] **+버튼 클릭 시 깜빡임(별개)** + 파트추가 '+' 포크 잘 안눌림: RenderGraph 재빌드/포크 콜라이더 관련 — 실기기 확인·증분렌더 필요로 보류
- 컴파일 0 에러. 손·도크·키보드그랩·파트+포크감은 실기기 최종 확인 권장

## 2026-07-15 MVP 14차 (피드백 11건: UI 정리 + 키보드/노드 상호작용 + 손 중복)

- [x] **#1 책상엔 보드만**: `MvpWorkspaceLayout.PlaceSketchOnDesk`가 노드까지 책상 평면에 눕히던 `ArrangeWorkspace()` 호출 제거 → 보드만 책상에, 노드는 유저 앞 그대로
- [x] **#2 '다시 앞으로' 도크 레이**: 토글 후 도크가 새 자세로 정착한 뒤 `WireScene()` 재와이어링(`RewireInteractionNextFrame`)로 레이/포크 표면 재계산. (실기기 검증 필요)
- [x] **#3 손 2개(+포킹 시 분리)**: 리그 손 메시 전수조사 → 메인 `OVR*HandVisual` 외에 HandSphereMap(지난턴)·DistanceGrab **레티클/synthetic 고스트 손 8개**가 손 위치에 겹쳐 렌더. synthetic/reticle 손 SMR 전부 비활성 → **손당 메인 메시 1개만 남김**. 씬 저장. (실기기 확인)
- [x] **#4 3D 온/오프**: 액션바 '3D 보기' 토글(`ToggleRocketVisibility`)로 생성된 3D 모델 표시/숨김
- [x] **#5 우측 세로 사진 갤러리**: '2D 만들기' 때마다 설계 보드 오른쪽(x≈640)에 썸네일 세로 누적(최대 4, 최신 위). 텍스처 복제 소유. 검증: 2장 누적·화면 내·텍스처 정상
- [x] **#6 전체(ALL) 제거**: ALL 포트 노드 생성 제거(`InitializeWaterRocketGraph`) + `AllPortSlot` UI 숨김(`MvpWorkspacePolish`). 링크 컨트롤러는 `FindObjectsByType<AllPort>`(비활성 제외)라 안전
- [ ] **#7 +버튼 깜빡임**: 원인 = +누르면 `RequestCreatePropertyNode`→`RenderGraph` 전체 재빌드로 노드뷰(+버튼) 파괴/재생성. 근본 수정은 **공유 GraphManager 증분 렌더** 필요(위험) → 별도 작업으로 보류
- [x] **#8 키보드 핀치 그랩**: 그랩 콜라이더가 상단 작은 핸들(250×46)만 덮어 본체 핀치가 뒤 노드로 샘 → 콜라이더를 키보드 전체(780×600×64)로 확대·중앙 배치. 핀치=그랩, 포크=키. (실기기 검증)
- [x] **#9 키보드 앞 노드 투명화**: 키보드 열 때 카메라~키보드 시선을 가리는 노드(수직거리<0.45m)를 α0.2로 dim, `OnDisable`에서 원복(`DimObstructingNodes`)
- [x] **#10 '말로 아이디어' 삭제**: 액션바에서 제거
- [x] **#11 그림 후 placeholder 제거**: 생성 시 'SketchPlaceholder'('아직 그림이 없어요') 숨김(`HideSketchPlaceholder`)
- 컴파일 0 에러. #2·#3·#8·#9는 VR 상호작용이라 실기기 최종 확인 권장. #7만 미해결(render 파이프라인)

## 2026-07-15 MVP 13차 (피드백 5건: 활성화 배타 + 반복 생성 흐름 + 책상도크)

- [x] **#3 형제 노드 배타 활성화**: 같은 부모 아래 자식 중 하나를 켜면 나머지 형제는 자동으로 끔(라디오식). `MvpXrGraphLinkController.DeactivateSiblings` 추가, ToggleChildActive에서 켜기 전에 호출. GetPropertyParentId로 형제 판별
- [x] **#5 생성 흐름 재설계 — '그때그때 반복 생성'**(핵심): 기존 "그림으로 보기"(Review→Generating→Result 일회성 이동) 제거. 워크스페이스 액션바에 **2D 만들기 / 3D 만들기 / 완성** 추가
  - `BeginWorkspaceGenerate`: 누를 때마다 지금 연결·활성 노드로 **중앙 이미지 재생성**(오프라인 즉시 mock, 온라인이면 서버 AI 이미지도 요청). 다음 단계로 안 넘어감. busy 가드로 중복 방지
  - `BeginWorkspace3D`: 누를 때마다 앞 공간 3D 모델 재생성(SpawnRocketStage 재사용, 이전 것 파괴). Design 모드에서도 3D 유지(ShowState clear 조건에 Design 추가)
  - **완성** 버튼 → Complete 화면(마무리는 별도)
  - `GetPartRequirements`가 **활성화된 자식 후손 텍스트까지 반영**(AppendActiveDescendants) → 활성화가 이미지 형태에 영향. 검증: 요구사항 반영(finSpan 1.16/안전노즈 둥근/가벼운몸통 길게), 2D 재생성 changed=True, 3D 스폰 확인, 에러 0
- [x] **#2 책상 도크 '다시 앞으로' 포크 개선**: 패널이 책상에 누우면 도크도 납작하게 유저 쪽으로 밀려 누르기 어려웠음 → 누웠을 땐 도크를 유저 쪽 모서리 위로 세우고, **항상 카메라를 향해 빌보드**(어느 자세에서도 포크 쉽게). `MvpDeskDock.LateUpdate` flat 분기. (실기기 포크감 확인 필요)
- [x] **#1 손 두 개(중복 렌더) 수정**: 유저 재확인 "둘 다 움직인다=둘 다 렌더". 카메라 리그 손 메시 전수조사 → `TouchHandGrabInteractor/HandSphereMap` 아래 손 메시가 **OculusHand_R+OpenXRRightHand 두 변형 모두 active**(메인 OVR*HandVisual은 한 변형만)로 실 손 위치에 겹쳐 렌더되는 게 원인. **HandSphereMap 하위 SkinnedMeshRenderer 4개(양손×2변형) enabled=false**(그랩 로직 유지, 메시만 숨김). 씬 저장. 실기기에서 손 1개로 보이는지 확인 필요(아니면 DistanceGrab synthetic 후보 다음 차례)
- [i] **#4 '전체(ALL) 설계' 용도 질문**: ALL 포트(InitializeWaterRocketGraph의 "전체"/라벨 물로켓)는 특정 부품이 아니라 **로켓 전체에 적용되는 속성**(예: "가볍게")을 붙이는 곳. 용도 전달이 약하면 라벨 명확화 또는 제거 가능(유저 결정 대기)

## 2026-07-15 MVP 12차 (PR 전 정리: 데드코드 제거 + 오디오/햅틱 피드백)

- [x] **데드코드 제거**(워크플로 감사 19에이전트, 씬/프리팹/리플렉션 참조까지 적대적 검증한 것만):
  - `MvpClassroomFlow`의 고아 파트선택 UI 클러스터 통째 삭제 — `AddCurrentRequirement`/`AddExampleRequirements(private)`/`SelectPart`/`CreatePartChoiceButton` + 필드 `_partButtons`/`_requirementInput`/`_requirementDock`/`_selectedPart`/`_workspaceActionBar`(인라인). 진입점(호출자) 0, private라 인스펙터 배선 불가 확정
  - 연쇄 데드: `MvpWaterRocketGraphController.AddExampleRequirements()`(+전용 헬퍼 `AddRequirementIfMissing`), `MvpFallbackSketchGenerator.CreateWaterRocketSketch(int)`(11차에서 design 오버로드로 대체돼 미사용)
  - **보존**: `GraphNetworkManager` public Lock API(TryBeginNodeEdit 등)·`NodeData` NodeAssetData/used_in_generation — 개발자2 배선용/서버 계약 미러라 의도적 표면(삭제 안 함)
- [x] **스파게티 대형 항목은 별도 브랜치 권고**(PR 직전 회귀 위험 회피): God class 분해(MvpClassroomFlow 3000줄, 책임 8+), Table_01 책상경계 4중 복붙→헬퍼, PointableCanvas 리플렉션 와이어링을 MvpXrInteractionBridge로 통합, GraphSyncClient private필드 SetPrivateField→Configure API, REST 엔벨로프 3중 복붙→제네릭. (감사 리포트에 상세)
- [x] **오디오/햅틱 피드백 신규**(MVP 전체 비음성 오디오 0건이었음 → 핸드트래킹엔 햅틱 불가라 오디오가 사실상 햅틱 대체):
  - 신규 `MvpAudioCue` — **런타임 절차적 합성**(사인/글라이드/엔벨로프, 애셋 불필요)로 큐 11종(Connect/Disconnect/NodeSpawn/NodeDelete/KeyClick/GestureTick/Commit/Grab/Release/Error/Success), 4보이스 풀. 컨트롤러 연결 시 `OVRInput` 진동 병행(맨손이면 무동작, try/catch 가드)
  - 신규 `MvpFeedbackHooks` — GraphManager 시맨틱 이벤트(OnEdgeCreated/Deleted, OnNodeCreated/Deleted) 구독 → 연결/생성/삭제 큐+햅틱. 입력경로(제스처/음성/추천/원격) 무관하게 커버. 시작 1.3s 억제창(시드 로드 소음 방지)
  - 키보드 키 클릭음(모든 키 공통 지점 1곳), 2D·3D 완성 순간 Success 큐(MvpClassroomFlow)
  - 씬: `MvpFeedback` 오브젝트(MvpAudioCue+MvpFeedbackHooks) 배치
- [x] **검증**: 컴파일 0 에러. 플레이모드에서 Instance 설정·클립 11종 생성·`Play`시 AudioSource 실제 재생(isPlaying)·**노드 생성→훅→스폰 큐 발동** 확인, 런타임 예외 0(OVR 햅틱 미지원 환경도 안전). 오디오 '가청' 자체는 헤드리스로 확인 불가

## 2026-07-15 MVP 11차 (재점검 + 서버 정합성 분석 + MVP 멀티플레이 배선)

- [x] **재점검**: 10차 코드 워크플로 검수(적대적) → 실결함 1건만: `MvpRocket3DStage.Clear()`가 자식 GameObject만 파괴하고 Material/절차적 Mesh 누수 → **수정**(sharedMaterial 전부 파괴, "RocketCone" 메시만 파괴, 프리미티브 공유메시 보존)
- [x] **서버 정밀 분석**(FastAPI-server repo 클론): REST `/api` + WS `/ws/rooms/event`, 2D는 `POST /api/2d/generate/graph`→WS `2D_GENERATED{img_url}`(MinIO). **3D 생성 스텁**(print만), **GRAPH_UPDATED 브로드캐스트 미emit**(주석). 상세 `docs/server-api-alignment.md §0`
- [x] **정합성**: WS 계층·방 API·2D 생성·히스토리는 정합. 클라가 옛 스펙으로 선구현한 REST 6개(node/sub_graph/part_node/references/graph/color_change)는 서버에 없어 404 → 각기 로컬 폴백. **정책: 클라만 정렬**(서버 repo 불변), 문서화 완료
- [x] **멀티플레이 상태 확인**: `partial`. Fusion은 로비/회의실 씬 아바타(위치·머리/몸통·색)·음성·명단만 동기화. 그래프는 서버 WS 의존(그 서버 채널 죽음). **MVP.unity엔 Fusion 러너·스폰 없어 싱글**. `GraphNetworkManager`(완성도 높은 Fusion 그래프 협업)는 GraphManager 없는 로비 씬에만 있어 고아
- [x] **MVP 멀티플레이 배선**(자기완결형: 로비 경유 X, MVP가 room_id로 직접 Fusion Shared 세션):
  - 신규 `MvpNetworkSession` — 온라인 시 room_id로 Shared 러너 시작(현재 씬 유지), 로컬 아바타 스폰, `GraphNetworkManager` 네트워크 오브젝트 스폰
  - 신규 `MvpGraphNetworkBridge` — GraphManager 로컬 편집 이벤트 → `GraphNetworkManager.Request*` RPC 포워딩. **에코 가드**(GNM에 `IsApplyingRemote` 추가, 원격 위치적용의 OnNodeMoved 재발화 차단) + 온라인 게이트
  - `MvpClassroomFlow.ConfigureGraphSocket`에서 온라인 확정 시 `BeginSession(roomId)` 훅
  - 씬/프리팹: MVP.unity에 `MvpNetwork`(세션+브리지) 배치, `MvpGraphNetwork.prefab`(NetworkObject+GNM) 생성+Fusion 프리팹테이블 리베이크, `MvpClassroomFlow._networkSession`·프리팹 참조 배선
- [x] **검증(단일피어 스모크)**: 컴파일 0 에러. 플레이모드에서 러너 시작+**Photon 클라우드 연결**(cloudReady)+Shared 세션+**아바타 스폰(tester)**+**GNM 스폰**+브리지 바인드/활성화+로컬 노드생성 포워딩(예외·중복·에코 0) 확인. **2인 전파는 빌드+두 번째 클라 필요**(헤드리스 불가)
  - AppIdFusion 설정됨(연결 가능), AppIdVoice 비어있음(보이스 별도)

## 2026-07-14 MVP 10차 (유저플로우 완주: 설계 반영 2D + mock 3D 생성)

- [x] **2D 스케치를 설계 반영형으로**: 기존엔 변형번호만 받아 **항상 같은 로켓**. 이제 노드 그래프에서 부품·요구사항을 읽어 형태에 반영
  - 신규 `MvpRocketDesign`(순수 데이터: 몸통 길이/굵기·날개 폭/수·노즈 뾰족/둥근·물 높이·요구사항 라벨·색)
  - `MvpWaterRocketGraphController.GetRocketDesign(variant)` — GraphManager 공개 API 읽기 전용(한국어 키워드 해석: 가벼/얇→길고얇게, 넓/큰→날개넓게, 안전/둥→둥근노즈, 물많/적→물높이…)
  - `MvpFallbackSketchGenerator.CreateWaterRocketSketch(design)` 오버로드: 몸통·물·날개·노즈콘(뾰족=삼각/둥근=돔)·라벨밴드를 설계값으로, 오른쪽에 **연결된 요구사항 수만큼 색 배지**. 기존 `(int)` 진입점은 호환 유지
  - 검증: 대비 설계 A(길쭉·둥근노즈·넓은날개4·물많음)/B(짧고통통·뾰족노즈·작은날개2·물적음) PNG가 형태·색·물높이·배지수까지 확연히 다름
- [x] **3D 단계를 실제 생성 단계로**(기존 "현재 준비 중입니다" 막다른 길 제거)
  - 신규 `MvpRocket3DStage`: 프리미티브로 몸통(실린더)·노즈(뾰족=절차적 콘/둥근=구)·날개(finCount개 Y축 배치)·노즐·밴드·받침 조립. 설계값 반영. 콜라이더 제거(뒤 버튼 포킹 통과). 아래→위 팝인(EaseOutBack) 후 천천히 회전
  - `MvpClassroomFlow.BuildThreeDPage` → `ThreeDGenerateRoutine`: 유저 앞 0.72m·눈아래 0.34m에 스폰 → "3D 물로켓을 만드는 중…"+진행바 → 완성 시 결과화면("우리 팀 3D 물로켓 완성!" + 부품/아이디어/날개/노즈 요약 + 2D결과/수업마치기)
  - `ShowState`가 3D·완료 외 상태로 가면, `RestartFlow`에서도 3D 모델 정리(`ClearRocketStage`)
- [x] **완주 확인**: Welcome→…→Design→Review→Generating(설계반영 2D)→Result→**ThreeD(3D 생성)**→Complete 로 막다른 길 없이 진행. Review "그림 만들기"는 무조건 활성(준비도 지표는 안내용)
- [x] 플레이모드 검증: `ShowState(ThreeD)`→코루틴→스테이지 스폰(SPAWNED)→완성→결과화면·3D모델 스크린샷 확인. 컴파일 에러 0, 런타임 예외 0
  - 주의: 에디터 포커스 밖이면 프레임이 거의 안 틱해 애니메이션이 멈춰 보임(실기기/빌드는 정상). 검증 시 `_elapsed` 강제·`Update()` 직접 호출로 완성 상태 확인

## 2026-07-14 MVP XR 9차-fix (책상에 붙일 때 배경 사라짐)

- [x] 원인: 패널이 평평하게 눕으면 내부 깊이 레이어(배경이 뒤쪽, 스프레드 0.031m)가 수직이 돼 **배경이 책상 표면 아래로 묻혀 가려짐**(WorkspaceSurface worldY -1.383 < 책상 -1.37)
- [x] 수정: 책상 위 오프셋 0.02→**0.05m** (양쪽 패널). 실측: 배경이 책상 위 1.7cm로 올라와 안 묻힘. 스크린샷으로 청록 배경 복귀 확인

## 2026-07-14 MVP XR 9차 ('책상에 붙이기' 도크: 설계모드 + hover-reveal)

- [x] 인-UI 버튼 제거, **별도 hover-reveal 도크**(`MvpDeskDock`) 신규: 패널 아래 ~10cm 공간에 흐리게 있다가 **레이가 닿으면 커지며 나타남**(alpha 0.18→1, scale 0.72→1)
- [x] **인트로/설계 어디서든** 상시: 도크가 현재 중앙 패널(인트로=_flowCanvas, 설계=MainSketchPanel)을 따라 아래 배치. `MvpClassroomFlow.EnsureDeskDock/UpdateDeskDockTarget/ToggleDeskAttach`
- [x] 클릭 → 현재 패널 책상에 평평(forward=-Y)하게 붙임 / 다시 → 앞으로. 설계 모드는 `MvpWorkspaceLayout.SetDeskMode`(보드+노드 책상에 눕히고 유저-향 재정렬 중단)
- [x] 실측+스크린샷 검증(인트로·설계 양방향 토글, 도크 follow/hover/label), 컴파일 성공(C# 에러 0)

## 2026-07-14 MVP XR 8차 (가운데 패널 '책상에 붙이기')

- [x] `MvpClassroomFlow._flowCanvas`(가운데 패널) 하단에 **"책상에 붙이기" 토글 버튼** 추가
- [x] 누르면 **책상(Table_01) 윗면에 평평하게** 눕힘: 중앙 정렬, 윗면 y+0.02, 책상 크기에 맞게 축소(scale 0.0011), `LookRotation(-Vector3.up, awayFromUser)`로 위에서 바로 읽히게(글씨 위쪽=유저 반대편) + poke normal 위쪽. 앵커 비활성으로 고정
- [x] 다시 누르면("다시 앞으로") 앵커 재활성+Recenter로 유저 앞 복귀, scale 0.00135 복원
- [x] 실측+스크린샷 검증(평평·안뒤집힘·토글 복귀), Unity 컴파일 성공(C# 에러 0)

## 2026-07-14 MVP XR 7차 (키보드 위치 튜닝 + 자식 노드 활성화 기능)

- [x] **키보드 위치**: 유저 앞 0.4m, 눈 아래 0.35m, 틸트 반대 방향 +22.5°(순수). 실측 수평0.40·아래0.35·euler(22.5,0,0)
- [x] **자식 노드 활성화** (신규 기능): `NodeData.is_active` + `GraphManager.IsNodeActive/RequestSetNodeActive/GetPropertyParentId` + `EdgeView.SetLineColor/SetLineVisible/ToNodeId`
  - 자식 PROPERTY의 '연결' 버튼 → **'활성화' 토글**('활성화'↔'끄기'). 최상위는 그대로 부품 '연결'
  - **활성 자식만** 부모→자식 **초록선**(EdgeView), 비활성은 선 숨김 + 노드 흐리게(CanvasGroup α0.35 + 메시 톤다운)
  - 새 자식 = 비활성 시작. `MvpXrGraphLinkController.ToggleChildActive/RefreshChildActivation`
  - 실측: 비활성→선enabled=False·α0.35·"활성화" / 활성→선enabled=True(green)·α1.0·"끄기". 스크린샷으로 대비 확인
- [x] Unity 컴파일 성공(C# 에러 0)

## 2026-07-14 MVP XR 6차 (포크 normal 반전 + 키보드 배치 + 연결 애니메이션)

- [x] **포크 백워드 확정 수정**: Meta PokeInteractor는 손가락이 normal의 +쪽에서 -normal 방향으로 눌러야 발동. `WireCanvas`가 `NormalFacing.Forward`(+Z=유저 반대)라 먼 쪽에서 눌러야 했음 → **`Backward`(-Z=유저 쪽)** 로. 실측: `PlaneSurface.Normal` dot(유저방향)=0.99. (Meta 기본값도 Backward였음)
- [x] **키보드 배치**: 노드에 붙이지 않고 **항상 유저 정면**(카메라 앞 0.85m)에 생성(시스템 키보드처럼). 실측 dist 0.87m·정면 dot 0.98
- [x] **키보드 틸트**: 반대 방향 **+22.5°** (수평 기준으로 순수 22.5, 실측 euler=(22.5,0,0))
- [x] **연결 애니메이션**: 노드 '연결' 누르면 연결 가능한 부품 포트가 **맥동(scale+glow)** 하며 "여기 눌러" 안내, 연결/취소 시 꺼짐. 링크 버튼 텍스트 "연결됨"→"취소"로 명확화. (MvpPartDropTargetFeedback Update 맥동)
- [x] Unity 컴파일 성공(C# 에러 0)

## 2026-07-14 MVP XR 상호작용 5차 (포크면/키보드 방향 반전 — MCP 실측 검증)

- [x] **원인 확정**: 좌석은 테이블 +Z쪽인데 메인 패널이 `table.forward(+Z)`로 회전 → 패널 +Z가 유저를 정면으로 향해 **읽기 반전 + 포크가 뒷면**. 노드(패널 회전 상속)·키보드(노드 회전 상속)까지 전부 반전. (안내 패널은 카메라 기준이라 정상이었음)
- [x] **메인 패널을 유저(카메라) 방향으로** — `PositionMainSketchPanel` 테이블 분기를 카메라 기준(away-from-user)으로. 파트 포트(연결 포크)도 같이 정상화
- [x] **키보드를 노드 회전 대신 카메라 직접 향하게** — `MvpWorldKeyboard.PlaceNearTarget` (틸트 -22.5° 유지)
- [x] **노드 항상 유저 향함 안전장치** — `MvpWorkspaceLayout.LateUpdate`에서 배치 모드 무관하게 노드 회전=패널 회전(0.2s 스로틀)
- [x] 실측 검증: 카메라를 -Z↔+Z 양쪽으로 옮겨도 패널·노드·키보드 dot(fwd, awayFromCam)=1.00 (항상 유저 반대편=읽기/포크 정상, 카메라 따라 반전됨)
- [x] 4차분(텍스트-도망/키보드) 반영: `pixelDragThreshold=140`, 노드 글씨 poke=키보드 전용(드래그-이동 제거)
- [x] 진단 로그 `[MVP+]`/`[MVPconnect]`/`[MVPkbd]`/`[MVPgrab]` 추가 (헤드셋 테스트 후 '+'/연결 원인 확정용)
- [x] Unity 컴파일 성공(C# 에러 0)

## 2026-07-14 MVP XR 상호작용 4차 (플레이모드 MCP 실측 검증)

- [x] **포크/레이 z깊이**를 캔버스 스케일 무관 월드 고정(0.06m)으로 — 노드 캔버스에서 0.80m→0.06m 실측 확인. 스테일 호버(손 떨어져도 호버 유지)·표면 불일치 해결
- [x] **접촉 게이트(MvpNodeContactGate)를 직접 UI 포크에서 제거** — 콜라이더를 본체(0.4m)로 줄인 뒤 '+'(중심에서 0.28m)·노드 글씨·연결점을 게이트가 막던 문제. 노드 그랩(핀치)용 게이트는 유지
- [x] **연결 끊기(토글) 구현** — 연결된 부품을 다시 누르면 엣지 삭제 → 연결선도 다음 프레임 제거(플레이모드에서 연결선 생성→삭제 실측 확인)
- [x] **비활성 캔버스 배선 스킵** — 숨겨진 노드가 일시적으로 48m 볼륨 만드는 것 방지
- [x] 키보드 -22.5° 틸트 + 포크영역 크기 일치 실측 확인(0.66×0.41m)
- [x] 전체 PokeInteractable 캔버스 z깊이 감사 = 전부 0.06m 확인. 큰 콜라이더는 회의실 지오메트리(천장/테이블 등), 음수-스케일 경고는 거울반전 장식(방 에셋, 그래프 아님)
- [x] Unity 컴파일 성공(C# 에러 0)

## 2026-07-14 MVP XR 상호작용 버그 감사·수정 (3차)

멀티에이전트 감사(확정 19 / 반박 4)로 근본 원인 확정 후 수정.

- [x] **이슈1** 새 노드의 "연결" 포트가 `scale 1.0`(형제 0.02)로 ~50배 크게 뜸 → `MvpXrGraphLinkController.WireIdeaHandles`에서 형제 스케일에 맞춰 축소
- [x] **이슈2** 노드 콜라이더/포크 볼륨이 Canvas rect(481×493)로 산정돼 월드 ~48m가 됨(← 2차의 "Canvas 외곽" 방식이 원인) → 콜라이더는 본체 메시(`EnsureCollider`), 포크/레이 볼륨은 실제 raycastTarget 그래픽 경계(`WireCanvas`)로 재산정
- [x] **이슈3** 손바닥 법선이 온디바이스에서 반대 정렬 → `MvpSpatialNodeGestureController.IsPalmUp` 법선 반전 + `_invertPalmNormal` 인스펙터 토글
- [x] `+` 버튼 접촉 게이트가 `Button.onClick` 경로로 우회되던 버그 → 게이트를 `InvokeAdd` 공통 진입점으로 이동
- [x] "작업판 맞추기"가 자동 정렬을 sticky로 켜 이후 공간 노드가 arc로 스냅되던 버그 → 1회성으로 복구
- [x] 안내 문구가 없는 버튼명('부품 연결')을 지칭 → 실제 라벨('연결')로 정정
- [x] 손 스캔/영구선 갱신의 매 프레임 전체 씬 스캔 완화(VR 프레임 히칭)
- [x] Unity 컴파일 성공 확인(C# 에러 0)
- [ ] **후속 결정 필요**: RequirementCount가 미연결 노드까지 세어 빈 설계로 Review/Generate 도달(수업 흐름 판단), 로컬 노드에 NODE_TEXT_UPDATE 발행(서버 404), 손 스캔 per-node 공유화, WaitForPalm 타임아웃
- [ ] 실기기(Quest)에서 이슈3 손바닥 방향·이슈1/2 크기 육안 검증

## 2026-07-14 MVP XR 노드 입력 신뢰성 재수정

- [x] 핀치 그랩을 손끝 접촉 때만 허용하고 게이즈·Poke 입력과 분리
- [x] 노드 Poke 시 월드 키보드가 열리도록 클릭·짧은 드래그 처리를 분리
- [x] + 버튼의 원본 서버 전용 리스너를 분리하고 로컬 자식 생성과 즉시 이름 입력 복구
- [x] R 버튼을 노드 연결점으로 전환하고 노드 연결점 → 부품 포트의 두 단계 연결 적용
- [x] Unity 컴파일 및 Play에서 노드 생성·자식 생성·키보드·부품 연결 경로 검증

## 2026-07-14 MVP XR 조작 보정 2차
- [x] 컨트롤러 Grip과 손 Pinch로 노드를 앞뒤를 포함한 3축에서 이동하도록 변경
- [x] 노드의 실제 Canvas 외곽에 맞춰 충돌 영역을 다시 계산하고, 빈 공간 Pinch 오작동을 차단
- [x] `+` 버튼의 실제 Button onClick 경로를 로컬 자식 PROPERTY 생성과 즉시 이름 입력에 연결
- [x] 키보드에 전용 Pinch/Grip 이동 핸들과 22.5도 기울기를 적용하고, 모든 키의 XR Raycast 대상을 보장
- [x] 기존 `R` 버튼을 숨기고 `연결` 버튼 하나로 통일
- [x] Unity Play 검증: 자식 생성 1→2, 한글 입력·적용, 키보드 핸들/각도, 연결 버튼 정리를 확인
## 완료

### 기반 데이터 구조
- [x] NodeType enum (PART / PROPERTY / REFERENCE / UNKNOWN)
- [x] NodeData / EdgeData / GraphData 클래스
- [x] NodeRegistry / EdgeRegistry 구현
- [x] GraphManager 기본 구조 (Singleton 없음, 일반 MonoBehaviour)
- [x] CanConnect() 연결 규칙 구현 (PROPERTY Tree 보장 포함)
- [x] RenderGraph() / LoadGraph() 분리

### GraphManager 인터페이스
- [x] RequestCreatePartNode(label, isGlobal)
- [x] RequestConnectNodes(fromNodeId, toNodeId)
- [x] RequestDeleteNode(nodeId) — 캐스케이드 삭제 + Reflow
- [x] RequestDeleteEdge(edgeId) — EdgeView 정리 포함
- [x] RequestRenamePartNode(nodeId, newLabel)
- [x] RequestCreatePropertyNode(parentId) — PROPERTY 자식 즉시 생성 + Reflow
- [x] CollectReferenceContext(nodeId) — R 버튼 체인 수집 (stub)
- [x] ReflowAllSubtrees() — 루트 고정, 자식만 재배치
- [x] GetEdgesIncomingToNode(nodeId) — AllPort Pressed 조회용
- [x] 서버 동기화 이벤트 5개 추가 (OnNodeDeleted / OnEdgeCreated / OnEdgeDeleted / OnNodeMoved / OnNodeTextUpdated)

### 메인 그래프 UI 스크립트
- [x] AllPort.cs — 4상태 스프라이트, Pressed 토글 + 연결 엣지 X 삭제 + 반환값 처리
- [x] PartPort.cs — 4상태 스프라이트, Pressed 토글 + rename/delete
- [x] AddPartPort.cs — 3상태 스프라이트 (Add/Hover/Inputting)
- [x] MainSketchView.cs — Refresh() 부분 갱신

### 서브그래프 스크립트
- [x] NodeView.cs — TMP_InputField 인라인 편집, 빈 label → 플레이스홀더 표시
- [x] NodeActionPanel.cs — X(삭제) / +(추가) / R(레퍼런스 stub) 버튼 + 반환값 처리
- [x] EdgeView.cs — Cubic Bezier, 흰색/얇은 선, 수직 탄젠트(트리 레이아웃), ConnectorSphere 엔드포인트 관리
- [x] ConnectorSphereView.cs — Default/Grabbed 머티리얼 상태 전환
- [x] ReferenceContext.cs — R 버튼 컨텍스트 데이터 클래스
- [x] ~~MockGraphLoader.cs~~ → **Dev/SeedGraphLoader로 대체 후 삭제(2026-07-02)** (seed 실제 UUID 그래프 로드)

### Unity Prefab / 씬 Inspector 연결
- [x] AllPort.prefab — 스프라이트 4종, _dotImage, _connectedListContainer
- [x] PartPort.prefab — 스프라이트 4종, _dotImage, _labelText, _deleteButton, _renameField
- [x] AddPartPort.prefab — 스프라이트 3종
- [x] NodeView_Sub.prefab — NodeActionPanel 추가, 버튼 3종 연결, _labelInput 연결
- [x] NodeView_Sub.prefab — _depthMaterials Box_00~04 연결 (깊이별 머티리얼)
- [x] Canvas Event Camera → Main Camera (World Space UI 클릭 활성화)

### 스크립트 폴더 정리 (2026-07-02 재구성)
- [x] Data/ — DTO·레지스트리 (NodeType, NodeData, EdgeData, GraphData, NodeRegistry, EdgeRegistry, ReferenceContext, UtteranceDto, PartNodeDto, Generate2DDto, RegenerateDto)
- [x] View/ — NodeView, EdgeView, ConnectorSphereView
- [x] Main/ — AllPort, PartPort, AddPartPort, MainSketchView
- [x] UI/  — NodeActionPanel
- [x] Server/ (신설) — GraphSyncClient, Generate2DController, PartNodeApiClient, UtteranceApiClient, RegenerateConnectionBuilder
- [x] Dev/ — SeedGraphLoader
- [x] 루트 — GraphManager (core만)
- [x] **MockGraphLoader 삭제 (2026-07-02)** — SeedGraphLoader로 대체(레거시). Test_SH 씬 오브젝트/참조도 제거.

### 팀원 인터페이스 문서
- [x] docs/graph-api-for-interaction.md — 인터랙션 담당자용 GraphManager API 문서
- [x] GraphSyncClient.cs skeleton — 서버 동기화 레이어 뼈대 (이벤트 구독 + Debug.Log)

### 서버 코드 검토
- [x] nodexr-server 구조 파악 — FastAPI + WebSocket 기반 확인
- [x] 실제 WS 이벤트 스키마 확인 (NODE_MOVE / NODE_TEXT_UPDATE / NODE_DELETE / EDGE_CREATE / EDGE_DELETE)
- [x] server-api-alignment.md 전면 재작성 (REST → WebSocket 기준)

---

## 개발자 2 핸드오프 체크포인트 (2026-07-02)

개발자 2의 5개 과제별 인터페이스 준비 상태. 모든 노드 조작은 `GraphManager.Request*()` 경유.

| 개발자 2 과제 | 개발자 3 인터페이스 | 상태 |
|----------------|----------------------|------|
| **1. 발화 그래프 생성/수정/추가/삭제** | `RequestCreateRootPropertyNode()`(키보드 +), `RequestCreatePropertyNode(parentId)`(노드 +), `RequestNodeByUtterance()`(발화 전송, NodeView 자동), `RequestDeleteNode()`(X) | ✅ 코드·문서·씬(UtteranceApiClient) 완료. **남은 것: 개발자 2가 키보드 `+` UI 제작** |
| **2. 서브↔메인 연결** | `RequestConnectFromPort(portNodeId, subgraphNodeId)` + `CanConnect()` 미리보기 | ✅ 인터페이스·문서 완료. 개발자 2가 더블클릭/드롭 제스처 |
| **3. 서브그래프 전체이동** | `RequestMoveSubgraphByMember(memberNodeId, delta)` | ✅ 인터페이스 완료. 개발자 2가 XR Grab delta 전달 |
| **4. 기능/니즈 논의창** | (순수 UI — 개발자 3 의존 없음) | 🟡 개발자 2 자체 구현 |
| **5. 2D 완료 알림** | `GraphSyncClient.OnImage2DGenerated` 이벤트 구독 | ✅ 인터페이스 완료. 서버 2D generate 배포 후 실동작 |

> 핵심 주의: `+`로 만든 로컬 노드는 임시 placeholder(GUID)다. 발화(텍스트 입력) 시 서버에는 **그 placeholder 가 아니라 기존 부모**를 parent_node_id 로 보낸다(루트면 parent 없음). 서버가 UUID를 발급한 서브그래프를 응답 → 병합하고 placeholder 는 제거된다.
> 서버 미배포 API(`/api/utterances`, `/api/part_node/*`, `/api/2d/generate`)는 현재 404 → 오프라인 로컬 흐름으로만 검증 가능.

---

## 남은 작업

### A. EdgeView 프리팹 — ConnectorSphere 세팅 (Unity Editor)
- [ ] EdgeView 프리팹에 ConnectorSphere FBX 자식 2개 추가 (FromSphere / ToSphere)
- [ ] 각각에 ConnectorSphereView 컴포넌트 추가
- [ ] Sphere_Default.mat / Sphere_Grabbed.mat 연결 (`Assets/03_UI/FBX/Materials/`)
- [ ] EdgeView `_fromConnector` / `_toConnector` Inspector 연결
- [ ] `_connectorScale` 값 씬에서 시각적으로 조정 (기본값 0.06f)

### B. ConnectedNodeButton.prefab 제작
- [ ] AllPort Pressed 목록용 — Button + TMP_Text 구조
- [ ] `AllPort._connectedNodeButtonPrefab` 슬롯에 연결

### C. RegenerateConnectionBuilder 구현 ✅ (2026-07-01, Unity 선구현)
- [x] `Assets/02_Scripts/Graph/RegenerateConnectionBuilder.cs` + `Data/RegenerateDto.cs`(ConnectionDto/RegenerateRequestDto)
- [x] PROPERTY/REFERENCE → PART 엣지를 `connection[{part_node_id, node_id(leaf)}]`로 변환. 부분 재생성(selectedPartNodeIds) 지원.
- [x] `GraphManager.BuildRegenerateRequestJson(assetId[, selected])` 래퍼.
- [ ] **서버 2D generate 엔드포인트 구현 대기**(현재 주석/스텁) → 붙으면 POST 전송 코드 추가.

### D. Phase 2 — 서브↔메인 연결 (UX 확정 2026-07-01)
확정: 메인 포트(ALL/PART) 더블클릭으로 시작 → 서브그래프 **가장 하위 leaf**로 드롭. 데이터는 leaf(PROPERTY/REFERENCE)→PART로 저장. 서버는 leaf에서 상위 체인을 거슬러 탐색.
- [x] `RequestConnectFromPort(portNodeId, subgraphNodeId)` 헬퍼 추가 (방향 정규화)
- [x] graph-spec.md / server-api-alignment.md — root→leaf, 탐색 방향 반대로 수정
- [x] graph-api-for-interaction.md — 확정 UX + 헬퍼 문서화 (개발자 2 핸드오프)
- [ ] PROPERTY→PART 엣지를 씬에서 시각화 (OutputSphere → PartPort/AllPort Transform)
- [ ] GraphManager에서 Phase 2 EdgeView 별도 추적
- [ ] (개발자 2) 메인 포트 더블클릭 감지 + 임시 엣지 커서 추적 + 드롭 hit-test (Meta Quest 핸드트래킹/컨트롤러)

### E-2. 파트 노드 CRUD — 서버 REST (2026-07-01)
- [x] **REST 일원화 구현**: `Data/PartNodeDto` + `PartNodeApiClient`(생성 `POST /api/part_node/generate`, 수정 `PATCH /api/part_node/modify`, 삭제 `DELETE /api/part_node/delete`). 성공 시에만 GraphManager 로컬 변경.
- [x] GraphManager 조정: `RequestCreatePartNode(label,isGlobal,nodeId)` 오버로드(서버 UUID 수용), `RequestRenamePartNode` 로컬 전용화(WS 미발행), `RequestDeleteNode(nodeId,emitSync)`.
- [x] Main UI 배선: MainSketchView/AddPartPort/PartPort → `PartNodeApiClient` 경유. 생성 utterance=입력텍스트. **PART은 WS NODE_* 미사용**.
- [ ] (Unity Inspector) `PartNodeApiClient` 컴포넌트 추가 + `_graphManager`/`_syncClient` 연결, `MainSketchView._apiClient` 연결.
- [x] **명세 대조 + position 추가 (2026-07-04)**: 서버팀 PART API 명세 수령. 유니티 기존 `PartNodeApiClient`/`PartNodeDto`가 엔드포인트·메서드·필드·응답봉투 전부 일치(코드 PART_NODE200/201/202). **유일 차이였던 generate 요청 `position:[x,y,z]` 추가** — `PartNodeGenerateRequest.position`, `CreatePart(utterance,isGlobal,position,onDone)` 오버로드, `AddPartPort`가 `transform.position` 전달. **서버는 여전히 미구현**(part_node 라우터 부재 확인 — `utterances/generation/features/rooms/ws`만) → 배포 전까지 `_offlineFallback`로 UI 검증.
- [ ] [사용자] 컴파일 확인 + 서버 배포 시 왕복 검증(생성/수정/삭제). 미배포면 404(또는 `_offlineFallback=true`로 로컬 UI 검증).
- [ ] 서버팀 확인: delete 응답 봉투 스키마, `isSuccess` 키 공백 오타, generate `position` 사용처(레이아웃/2D 문맥).
- [ ] `Dev/SeedGraphLoader.TestPartRename`는 이제 WS 미발행(PART=REST) → Dev 테스트 의미 갱신 필요.

### E-4. 노드 생성 2경로 분리 — 키보드=WS NODE_CREATE / 음성=REST utterance (2026-07-05)
서버팀이 WS `NODE_CREATE`(직접 생성, LLM 없음) 구현 → 키보드 직접 생성은 이걸로, 음성 발화(LLM 확장)만 `/api/utterances` 유지. 합의로 확정.
- [x] **유니티 구현**: `GraphManager.RequestSubmitNodeText(nodeId,text)` 추가 — 서버 미등록 노드면 `OnNodeCreated`(→WS NODE_CREATE), 이미 등록됐으면 `OnNodeTextUpdated`. `NodeView.OnLabelSubmit` 이 이걸 호출(기존 RequestNodeByUtterance 대신). `RequestNodeByUtterance`는 음성용으로 유지.
- [x] `GraphSyncClient`: `OnNodeCreated` 구독 + `NODE_CREATE` 송신. payload=`{node_id(클라 발급 UUID), node_text, parent_node_id(루트면 ""), node_type, position[x,y,z]}`. **position은 배열**(NODE_MOVE의 dict와 다름). 생성 후 노드 "+" 재활성화(server-known 반영).
- [x] **정합 핵심**: WS는 응답 없음 → **클라가 node_id(UUID) 발급해 전송** → 서버가 같은 UUID로 저장 → placeholder 스왑 불필요, 삭제/이동 정합.
- [ ] **[서버팀] 대기**: (1) NODE_CREATE payload에 `node_id` 수용(현재 미수용→서버가 자동발급하면 NODE404), (2) 부모 없는 NODE_CREATE 타입 — 현재 무조건 PART, 유니티 루트는 PROPERTY → `node_type` payload 수용 필요. **이 둘 배포 전엔 키보드 생성이 완전 동작 안 함.**
- [x] **position 배열 통일 (2026-07-05)**: API 명세가 NODE_MOVE도 position=배열 `[x,y,z]`로 확정 → 유니티 `NodeMovePayload.position`을 `WsVec3` dict → `float[]` 배열로 변경(`WsVec3` 제거). NODE_CREATE와 동일 표준.
  - ⚠️ **[서버팀] 대기**: 현재 로컬 서버 `_handle_node_move`(296행)는 아직 `_get_required_dict`로 **dict를 읽음** → 서버도 배열로 바꿔야 함. **유니티(배열)만 앞서가면 그 사이 NODE_MOVE 실패**(둘 lockstep 배포 필요).
- [ ] [사용자] 서버 배포 후 왕복 검증: 키보드 "+"→텍스트→NODE_CREATE 200, 이후 삭제/이동 NODE404 없는지.

### E-5. NODE_CREATE/EDGE_CREATE job_id 매칭 — 서버 발급 id 채택 (2026-07-10)
서버팀(소은) 방향 전환: **서버가 node_id/edge_id 발급**. 요청 시 유니티가 로컬 임시 id(`job_id`)를 실어 보내면 서버 ACK(요청자 대상)에 그대로 담아 되돌려줌 → 클라가 로컬 id를 서버 발급 id로 **rekey**. E-4의 "클라 UUID 발급→서버 동일 저장" 모델을 폐기하고 이 모델로 교체.
- [x] **서버 계약 확인(코드 직독)**: `graph_interaction_service._handle_node_save`/`_handle_edge_create` + `ws_response`. 요청 payload NODE_CREATE=`{job_id, parent_node_id(루트 ""), node_text, node_type, position[x,y,z]}`(node_id 없음), EDGE_CREATE=`{job_id, from_node_id, to_node_id, label}`. ACK 봉투=`{event_type, room_id, user_id, payload:{isSuccess, code, result:{job_id, node_id|edge_id, graph_snapshot_id}}}`. 서버는 NODE_CREATE에서 parent 있으면 **트리 엣지 자동 생성**(ACK에 그 edge_id 없음).
- [x] **GraphManager**: `ApplyServerNodeId(jobId, serverNodeId)`/`ApplyServerEdgeId(jobId, serverEdgeId)` 추가 — 레지스트리·graphData·연결 엣지 from/to·NodeView/EdgeView 맵·ActionPanel·sub_graph_id·`_serverKnownNodeIds`를 일괄 rekey. `RequestSubmitNodeText`의 **낙관적 서버-known 선반영 제거**(ACK 시점으로 이동 → 자식 "+"도 ACK 후 활성).
- [x] **GraphSyncClient**: NODE_CREATE payload `node_id`→`job_id`. `HandleEdgeCreated` no-op→**실제 EDGE_CREATE 송신**(job_id=로컬 edge_id, 양 끝 서버-known 아니면 skip). `HandleIncoming`에 NODE_CREATE/EDGE_CREATE ACK 분기 추가 → `Apply*` 호출. ACK 파싱 DTO(`AckEnvelope/AckPayload/AckResult`) + `EdgeCreateEnvelope/Payload` 추가.
- [ ] [사용자] Unity 컴파일 확인 + 서버 왕복 검증: 자식 노드 "+"→NODE_CREATE→ACK rekey(GameObject명 서버 UUID), 교차 엣지 연결→EDGE_CREATE→ACK rekey→AllPort X(EDGE_DELETE) 404 없는지.
- [ ] 서버팀 확인: broadcast 없음(요청자 ACK만) → 멀티플레이 반영은 별도 과제.

### E-6. 루트 생성 = 서브그래프 API + NODE_CREATE(sub_graph_id) (2026-07-10)
전체 API 명세 수령(`docs/server-api-spec.md`). 루트 생성 방식 확정: **`POST /api/sub_graph/generate {room_id}` → sub_graph_id 발급 → WS NODE_CREATE(sub_graph_id, parent="")**. NODE_CREATE payload에서 **node_type 제거**(키보드=PROPERTY 전용). 지난 세션 "parentless NODE_CREATE 대기" 항목 대체.
- [x] **GraphManager**: `OnNodeCreated` 4번째 인자 nodeType→**subGraphId**. `OnSubGraphRequested(rootNodeId)` 이벤트 추가. `RequestSubmitNodeText` 루트/자식 분기(루트=OnSubGraphRequested 발행 후 대기, 자식=OnNodeCreated with sub_graph_id=""). `SubmitRootNodeWithSubGraph(rootNodeId, subGraphId)` 신규(서버 sub_graph_id 반영 후 NODE_CREATE 발행).
- [x] **GraphSyncClient**: `NodeCreatePayload` node_type 제거 + `sub_graph_id` 추가. `HandleNodeCreated` 인자 정합(subGraphId).
- [x] **SubGraphApiClient.cs 신설**(Server/): `OnSubGraphRequested` 구독 → `POST /api/sub_graph/generate` → 성공 시 `SubmitRootNodeWithSubGraph`. 실패 시 로컬 루트 유지(취소해도 서버에 빈 sub_graph 안 생기도록 **텍스트 제출 시점**에 POST).
- [x] (Unity Inspector) `SubGraphApiClient` 컴포넌트 추가 + `_graphManager`/`_syncClient` 연결(GraphSyncClient 오브젝트에 배선).
- [x] Dev 검증 훅: `SeedGraphLoader` ContextMenu "Test/루트 노드 생성 (키보드+ 대체)" — 키보드 UI 없이 루트 흐름 트리거.
- [ ] **⚠️ [서버팀] `/api/sub_graph/generate` 미배포 확인(2026-07-10, 404)**: 서버 `app/api/`에 sub_graph 라우터 없음(SUB_GRAPH200 코드 부재). 배포되면 클라 변경 없이 동작. 그 전까진 루트 404→로컬 유지(graceful degrade).
- [ ] [사용자] 서버 배포 후 왕복 검증: 키보드 "+"(또는 Dev 훅)→sub_graph/generate 200→NODE_CREATE(sub_graph_id)→ACK rekey. 이후 그 루트에서 자식 "+" 활성화·자식 NODE_CREATE.
- 후속(별도 세션): 없음(그래프 도메인 델타 #1~#5 반영 완료). (`docs/server-api-spec.md` 델타표)

### E-7. 그래프 콜드로드 GET /api/graph + used_in_generation (2026-07-10, 델타 #3+#5)
API 명세의 `GET /api/graph`(sub_graphs 중첩)로 서버 그래프를 받아 렌더. seed 하드코딩 대체.
- [x] **#5 used_in_generation**: `NodeData`/`EdgeData`에 `bool used_in_generation` 추가(파싱·저장만).
- [x] **GraphQueryDto.cs 신설**(Data/): `GraphQueryResponse`/`GraphSnapshotDto`/`CoreImageDto`/`SubGraphDto`/`GraphNodeDto`/`GraphEdgeDto` + `GraphSnapshotDto.ToGraphData()`(sub_graphs 중첩→flat 평탄화, 각 노드에 sub_graph_id 채움). GRAPH_UPDATED(#4)도 GraphSnapshotDto 재사용 예정.
- [x] **GraphLoadApiClient.cs 신설**(Server/): `GET /api/graph?room_id=` → 파싱 → `LoadGraph`+`RenderGraph`. `_loadOnStart=false` 기본(배포 후 켬) + ContextMenu "Load Graph From Server". 실패 시 기존 그래프 유지(seed 안 덮음). core_2d_image url은 로그만(중앙 이미지 연결은 후속).
- [ ] **⚠️ [서버팀] `GET /api/graph` 미배포 확인(2026-07-10)**: 서버에 graph 라우터/GRAPH200 없음(주석 참조만, history만 존재). 배포되면 클라 변경 없이 동작.
- [ ] (Unity Inspector) `GraphLoadApiClient` 컴포넌트 추가 + `_graphManager`/`_syncClient` 연결. 서버 배포 후 `_loadOnStart` 켜고 `SeedGraphLoader` 비활성화.
- [ ] [사용자] 컴파일 확인 + 배포 후 검증: ContextMenu로 GET 200→노드/엣지 수·서브그래프 렌더 확인.

### E-8. GRAPH_UPDATED WS 수신 (2026-07-10, 델타 #4)
서버가 semantic 업데이트 결과로 push 하는 전체 그래프 스냅샷 수신 → 전체 갱신.
- [x] **GraphSyncClient**: `HandleIncoming`에 `GRAPH_UPDATED` 분기 + `HandleGraphUpdated` — `payload.graph`(GraphSnapshotDto, E-7 재사용) → `ToGraphData()` → `LoadGraph`+`RenderGraph`. 봉투 DTO(`GraphUpdatedEnvelope/Payload`) 추가.
- ⚠️ 전체 교체 방식이라 서버 미확정 로컬 placeholder 는 push 시 사라질 수 있음(semantic push는 5분/topic drift 주기라 허용). 필요 시 merge 방식으로 후속 조정.
- [ ] [사용자] 컴파일 확인 + 서버 GRAPH_UPDATED push 시 그래프 재렌더 확인.

### E-10. 잔여 명세 갭 일괄 반영 (2026-07-10)
전체 API 명세 대조 후 남은 내 도메인 갭 6종 처리.
- [x] **① EDGE_DELETE 송신**: `GraphSyncClient.HandleEdgeDeleted` no-op → WS `EDGE_DELETE { edge_id }` 송신(+`EdgeDeleteEnvelope`). EDGE_CREATE ACK로 rekey된 서버 edge_id로 나감.
- [x] **② PART 노드 생성(키보드)**: `/api/part_node/generate/keyboard { room_id, text, position }` → `PartNodeApiClient.CreatePartKeyboard`(+`PartNodeKeyboardRequest`). `AddPartPort`가 `CreatePart`→`CreatePartKeyboard`로 전환(타이핑 입력=키보드).
- [x] **③④ 레퍼런스**: `ReferenceApiClient`+`ReferenceDto` 신설. 키워드 `/api/references/keyword`(JSON), 연결 `/api/references/generate`(multipart, 이미지 바이트는 호출부 제공). REFERENCE 노드는 그래프 동기화로 반영. R버튼 UI(ReferenceSearchPanel) 미구현이라 클라 메서드만 준비.
- [x] **⑤ 히스토리**: `/api/history/{room_id}` → `HistoryApiClient`+`HistoryDto`(GraphSnapshotDto 재사용). 조회 + `LoadSnapshot/LoadSnapshotAt`로 과거 버전 복원. 스크러버 UI는 별도.
- [x] **⑥ 2D URL 재정렬**: `/api/2d/generate/feature`(요구사항), `/api/2d/generate/graph`(connections), `/api/2d/color_change`(multipart)로 `Generate2DController` 재작성. 구 `/api/2d/generate`·`/regenerate` 제거. `RegenerateConnectionBuilder`→`BuildGraphRequest`(room_id/user_id/connections), `GraphManager.BuildGraphSketchRequestJson`/`BuildGraphConnections`. `Generate2DRequestDto`/`RegenerateRequestDto` 제거.
- [ ] (Unity Inspector) 신규 컴포넌트 배선: `ReferenceApiClient`, `HistoryApiClient`(+`_graphManager`/`_syncClient`), `Generate2DController._graphManager` 추가 연결.
- [x] **[서버 배포됨 확인 2026-07-11] 2D 생성**: `/api/2d/generate/graph`·`/feature` 서버 구현 완료(`generation.py`, `/api` prefix). 클라 요청 스키마 정확히 일치(`Generate2DGraphRequest {room_id,user_id,connections:[{part_node_id,node_id}]}`, feature `{room_id,user_id}`). → **end-to-end 테스트 가능**(feature=배선 불필요, graph=`Generate2DController._graphManager` 연결 필요). 응답 code=IMG202(클라는 2xx만 확인).
- [ ] **⚠️ [서버팀] 미배포**: references/keyword·generate, history, 2d/color_change, part_node/generate·keyboard·modify·delete, sub_graph/generate, GET /api/graph — 배포 후 왕복 검증. (WS 그래프 CRUD·2D generate는 배포됨.)
- [ ] [사용자] 컴파일 확인.

### E-9. 발화 노드 생성 개편 (2026-07-10, 델타 #2)
구 `POST /api/utterances`(응답=전체 그래프, MergeServerGraph) → 명세 `POST /api/node/generate/utterance`(응답 단건 `{node_id, node_text}`).
- [x] **UtteranceDto**: 요청에 `node_type`+`position` 추가(루트/자식 DTO 분리 유지 — parent UUID|None 422 회피). `UtteranceResult`를 `{node_id, node_text}`로 변경(구 graph 제거).
- [x] **UtteranceApiClient**: URL 변경, 요청 값 placeholder 기준(node_type/position). 응답 처리 = `MergeServerGraph` 대신 **`ApplyServerNodeId(placeholderNodeId, node_id, node_text)`**로 rekey + 라벨(LLM node_text) 갱신.
- [x] **GraphManager.ApplyServerNodeId**: `serverNodeText` 선택 인자 추가(발화 경로에서 라벨/텍스트 갱신). NODE_CREATE ACK 경로는 기본 null로 그대로.
- 참고: `MergeServerGraph`는 이제 미호출(정의만 남김 — 향후 필요 시 재사용). 발화 UI(음성)는 개발자2 담당·미배선이라 경로는 준비 상태.
- [ ] **⚠️ [서버팀] `/api/node/generate/utterance` 미배포 확인**: 배포되면 클라 변경 없이 동작(루트는 parent 필드 없는 요청).
- [ ] [사용자] 컴파일 확인 + 배포 후 검증: placeholder→발화→node_id rekey + 라벨=서버 node_text.

### E-3. 노드 생성(발화) — 서버 REST /api/utterances (2026-07-02)
- [x] **구현**: `Data/UtteranceDto` + `UtteranceApiClient`(`POST /api/utterances`). GraphManager는 `OnUtteranceNodeRequested` 이벤트만 발행, 경계 클래스가 REST 담당 → 성공 시 `MergeServerGraph`(node_id upsert, **서버 position 미적용·reflow 배치**).
- [x] `NodeData` 확장: `parent_node_id` + `NodeAssetData data`(REFERENCE 자산 저장만, 표시 추후).
- [x] `NodeActionPanel` "+" → 인라인 발화 입력(`_utteranceInput`) → `RequestNodeByUtterance`. 입력필드 없으면 기존 즉시생성 fallback. `GraphSyncClient.UserId` 게터 추가.
- [x] (프리팹) NodeView_Sub에 발화 전용 `UtteranceInputField` 추가 + `NodeActionPanel._utteranceInput` 연결 (LabelInputField와 분리).
- [x] **빈 상태 root 생성 인터페이스 (2026-07-02)**: `RequestCreateRootPropertyNode()` 추가 — 부모 없는 서브그래프 첫 PROPERTY root 생성(sub_graph_id=자기 자신). 이후 텍스트 입력=발화 흐름은 자식과 동일. **두 `+` 버튼 구분**: 키보드 `+`=root / 노드 `+`=자식(`RequestCreatePropertyNode`).
- [x] (Unity Inspector) `UtteranceApiClient` 컴포넌트 추가 + `_graphManager`/`_syncClient` 연결(구독). — Test_SH `GraphSyncClient` 오브젝트에 배선 완료.
- [x] graph-api-for-interaction.md — 두 `+` 버튼 + 발화 흐름 + `RequestCreateRootPropertyNode`/`RequestNodeByUtterance` 문서화(개발자 2 핸드오프).
- [x] **발화 parent 정렬 (2026-07-04, 서버 모델 확정)**: 서버 `/api/utterances`는 `parent_node_id`가 **서버 DB에 존재해야** 하고(없으면 NODE404) 발화로 **새 서브그래프를 생성해 UUID를 발급**한다(`parent_node_id`는 Optional=None → 루트). 기존엔 "방금 만든 로컬 placeholder 자신"을 parent 로 보내 404였음. 수정:
  - `RequestNodeByUtterance`: parent = `GetPropertyParent(nodeId)`(= "+" 눌렀던 기존 부모). 부모가 서버 미등록이면 전송 보류(경고). 이벤트 `OnUtteranceNodeRequested(serverParentId, text, placeholderId)`로 확장(3-인자).
  - `MergeServerGraph(incoming, placeholderIdToRemove)` 오버로드: 병합 후 로컬 placeholder 제거(`RemoveLocalNodeWithView`, WS 미발행). 서버-known 노드는 절대 제거 안 함.
  - `UtteranceApiClient`: parent 유무로 `UtteranceRequest`(parent+position) / `UtteranceRootRequest`(루트, parent 필드 없음 — JsonUtility 가 null UUID 를 못 생략 → 별도 DTO) 분기. 성공 시 `MergeServerGraph(graph, placeholderId)`. UX: `+ → 빈칸 → 발화 입력 → 서버 서브그래프로 대체`.
  - **검증 완료(2026-07-04 17:28)**: seed 부모/자식/루트 발화 모두 `POST /api/utterances 200 OK`, 노드 생성·캐스케이드 삭제 정상.
  - **후속 수정 1**: 서버가 전체 그래프(PART 포함)를 응답 → `MergeServerGraph`가 `AddEdge`(CanConnect)로 `PART→PROPERTY`/`PART→PART` 서버 엣지를 거부하던 문제. 병합 전용 `AddServerEdge`(CanConnect 미적용, 필수필드·중복만 방어)로 교체. 서버가 진실의 원천.
  - **후속 수정 2 (빈 노드 "+" 비활성)**: 빈 placeholder 밑에 또 자식을 만들면 발화 parent 가 서버 미등록이라 실패 → 아예 **서버-known 노드에서만 "+" 활성화**. `GraphManager.IsServerKnown(nodeId)` 추가, `NodeActionPanel.Bind` 에서 `_addButton.interactable = IsServerKnown(nodeId)`, `InvokeAdd` 에도 방어 가드. placeholder 가 발화로 서버 노드로 대체되면 그 패널은 server-known → "+" 활성.
- [x] **발화 중복/빈/제거후 재제출 가드 (2026-07-04)**: `NodeView.OnLabelSubmit`이 `onEndEdit`(Enter+포커스이탈로 여러 번 발화)에 걸려 있어 ①같은 텍스트 중복 발화→서버 서브그래프 이중 생성, ②placeholder 제거 후 재제출→`RequestNodeByUtterance 실패: 존재하지 않는 node_id` 경고, ③빈 입력→서버 422 위험. → `_lastSubmittedText` 로 dedupe + 빈 문자열 skip. `Bind` 에서 바인딩 라벨로 초기화(변경 없는 재제출 무시).
- [ ] (개발자 2) **키보드 `+` 버튼 UI** 제작 → `RequestCreateRootPropertyNode()` 연결 (빈 상태 첫 노드 생성).
- [ ] [사용자] 컴파일 확인 + 서버 연결 재검증(발화→응답 graph 병합, placeholder 제거, reflow 위치). seed 부모 또는 루트(parent=null)로 왕복.
- [ ] 서버팀 확인: `isSuccess` 키 공백 오타, 응답이 노드 삭제 포함 가능성. (`user_id` 필수/형식은 확인 완료)

### E. 서버 연동 — WebSocket 기준
- [x] **WS 서버 정렬 (2026-07-04)**: 서버 리팩터에 맞춰 `GraphSyncClient` 수정. (1) 연결 URL `/ws/rooms/{room_id}/event?user_id=` → **`/ws/rooms/event`** (room_id·user_id는 URL이 아닌 매 메시지 본문에서 읽음). (2) 송신 봉투 3종(NODE_TEXT_UPDATE/NODE_DELETE/NODE_MOVE)에 **`user_id` 필드 추가**(서버 `WSEvent` 필수 키, 비면 WS400 → 빈 값 경고 로그). NODE_MOVE position은 서버 서비스가 `{x,y,z}` dict로 읽어 기존과 일치. seed user 예: `11111111-1111-1111-1111-111111111111`.
  - 서버팀 확인 완료: NODE_DELETE 자식 캐스케이드는 **서버가 수행**. 노드 생성은 REST `/api/utterances`가 담당(WS NODE_CREATE 존재하나 이중생성 방지로 WS 미송신).
  - ⚠️ 미결: 서버 코드 utterance 요청 필드는 `parent_node_position`인데 **API 명세는 `position`**. Unity는 현 서버 코드에 맞춰 `parent_node_position` 유지. 서버가 명세대로 `position`으로 리네임하면 `UtteranceApiClient` 동시 변경 필요.
- [x] **NODE_DELETE root-only 발행 (2026-07-04, 서버팀 확정)**: `RequestDeleteNode`가 자손마다 `OnNodeDeleted`를 쏘던 것 → **삭제 root(nodeId) 하나만** 발행하도록 변경. 로컬은 자손 전체 즉시 제거, 서버가 자식 노드·엣지 캐스케이드 삭제. (`GraphManager.cs:622~`)
  - ⚠️ 확인 필요: Unity 로컬 캐스케이드는 "종속 REFERENCE"까지 포함하는데, 서버 캐스케이드는 `parent_node_id` 재귀 기준. 부모-자식 아닌 종속 REFERENCE가 서버에 남을 수 있는지 서버팀과 대조 필요.
- [x] **WS 연결 토대 (2026-07-01)**: `GraphSyncClient` 실제 WS 연결 + 송신 3종(NODE_TEXT_UPDATE/NODE_DELETE/NODE_MOVE). 라이브러리는 Meta XR Voice 번들 `Meta.Net.NativeWebSocket` 사용(별도 패키지 X, GUID 충돌 회피). 수신은 로그만.
- [x] `GraphManager.RequestUpdateNodeText` + NodeView 인라인편집 배선(PROPERTY 텍스트 동기화 갭 해소).
- [x] `Dev/SeedGraphLoader` — seed 실제 UUID 그래프 로드 + ContextMenu 왕복 검증.
- [ ] **[사용자] Unity 컴파일 확인** — `Meta.Net.NativeWebSocket` 사용이 실제로 컴파일되는지. 실패 시 System.Net.WebSockets로 대체.
- [ ] 수신 이벤트 GraphManager 반영(echo loop 방지 설계 후).
- [ ] EDGE_CREATE/DELETE 송신(서버 sub_graph_id 제약 대응).
- [x] NODE_CREATE 대책 확정(2026-07-04): 서버에 노드 생성 이벤트 없음 → **발화(/api/utterances)로만 생성**. 로컬 placeholder 는 서버로 안 보내고, parent(기존 노드) 기준으로 서버가 UUID 발급 후 병합. 로컬 GUID 노드를 서버에 직접 등록하는 경로는 두지 않음. (E-3 참조)
- [x] **2D 기본 생성 흐름 — Unity 선구현 (2026-07-01)**: `Generate2DController` + `Data/Generate2DDto`(`Generate2DRequestDto={room_id}`). `RequestGenerate()`→`POST /api/2d/generate`(발화 기반 기본 생성, 노드/connection 미반영), WS `2D_GENERATED`{img_url} 수신→중앙 RawImage 다운로드 표시. **단 서버 생성이 스텁+주석**이라 서버팀 구현 후 실동작.
- [ ] 서버팀 확인 후 `Generate2DRequestDto.utterance` 추가(현재 스키마 `{room_id}`만 → 발화는 room_id 문맥으로 서버가 보유한다는 가정, 협의 대기).
- [x] (씬 배선 2026-07-01) Test_SH `GraphSyncClient` 오브젝트에 `Generate2DController` 컴포넌트 추가 + `_syncClient` 연결.
- [ ] (Unity Inspector) `Generate2DController._centerImage` → `MainSketchPanel/SketchImage`(RawImage) 드래그 연결. (프리팹 인스턴스 참조라 인스펙터에서 수동)
- [x] **전체 그래프(서브그래프 강체) 이동 — Unity 선구현 (2026-07-01)**: `NodeData.sub_graph_id` 추가 + 백필, `RequestMoveSubgraph`/`RequestMoveSubgraphByMember`. 서버 전체이동 op 미구현이라 개별 NODE_MOVE로 전송.
- [x] **Reflow 동기화 방향 정정 (2026-07-01)**: Reflow는 **클라 로컬 레이아웃 전용** → reflow 좌표를 서버 NODE_MOVE로 보내지 않는다(이전 "모델 A" 폐기). `ReflowAllSubtrees()`에 OnNodeMoved 자동 발행 넣지 않음. 서버 동기화는 생성/삭제/텍스트 중심. 트리 전체 이동 허용 시 root/sub_graph 단일 좌표만 전송하도록 나중에 설계.
- [x] **⟳ Reflow 위치 서버 영속화 — Unity push 구현 (2026-07-04)**: 실시간 동기화는 **Photon**, 서버는 **콜드로드/영속** 담당(07-01 "로컬 전용" 재역전). `ReflowAllSubtrees()`가 재배치 전후 위치를 비교해 **실제 이동한 "서버 등록 노드"만** 개별 WS `NODE_MOVE`(OnNodeMoved) 발행. 서버 등록 판별용 `_serverKnownNodeIds`(LoadGraph/MergeServerGraph 유입 노드) 추가 → 로컬 전용 노드는 NODE404 방지 위해 제외. 임계값 `ReflowMoveEpsilonSqr`로 미세이동 억제. (`GraphManager.cs`)
  - ⚠️ 서버팀 일감 (Unity 무관): **입장 시 그래프+위치 로드 API 없음**(`get_room_info`는 room/users만) → 없으면 새 유저가 저장된 위치를 못 받음. 배치 위치동기 엔드포인트는 미채택(기존 개별 NODE_MOVE 사용).
- [ ] 서버팀 확인: 전체이동 op 추가 시 `sub_graph_id` 키 이벤트 스키마
- [ ] 서버팀 확인: NODE_CREATE 이벤트 존재 여부
- [ ] 서버팀 확인: NODE_TEXT_UPDATE payload 필드명 ("text" vs "node_text")
- [x] 서버팀 확인: NODE_DELETE 캐스케이드 정책 → **서버가 자식까지 삭제**(2026-07-04). Unity는 root만 NODE_DELETE 발행.
- [ ] 서버팀 확인: EDGE_CREATE label 필수 여부
- [ ] GraphSyncClient Send() 구현 (서버팀 확인 완료 후)
- [ ] WS 연결 라이브러리 결정 및 IGraphTransport 구현체 작성
- [ ] GET /api/graph 또는 WS GRAPH_UPDATED 수신 → LoadGraph() + RenderGraph()
- [x] **서버 그래프 → 메인그래프 PART 반영 배선 (2026-07-04)**: 서버엔 그래프 조회 경로 없음 확인(`/rooms/{id}/info`=users만, `enter`=user만, `WS_CONNECT`=user만) → 서버 그래프의 유일 통로는 **발화 200 응답**. 그 응답의 PART는 `MergeServerGraph`로 GraphManager엔 들어오나 메인 UI 갱신이 끊겨 안 보였음. `GraphManager.OnGraphChanged` 이벤트 추가(LoadGraph/RenderGraph·MergeServerGraph 완료 후 발행) → `MainSketchView`가 구독해 `Refresh()`. 이제 **PART=파트텍스트가 PartPort에, PROPERTY=서브그래프 NodeView**로 자동 분리 렌더.
- [x] **PART/ALL 정리 — 루트 PART = ALL 확정 (2026-07-04)**: 서버 Node 에 `is_global` 컬럼 없음 확인. 서버 PART 는 계층(루트=전체대상 → 하위=영역). 합의: **루트 PART(`parent_node_id==null`) = ALL, 하위 PART = PartPort**. `GraphManager.BackfillIsGlobal()`(서버-known PART 만, 로컬 생성 PART 는 유지) 추가 → LoadGraph/MergeServerGraph 에서 호출. `MainSketchView` 는 기존 `is_global` 판정 그대로 재사용. seed: 0001 청소로봇→ALL, 0002/0004/0006→PartPort. graph-spec.md 갱신.

### G. SeedGraphLoader 서버 정합 (2026-07-04)
- [x] **원인 규명**: 기존 SeedGraphLoader가 낡은 "의자" 테마 더미(0002/0004를 PROPERTY로)인데 **node_id(UUID)는 서버 실데이터와 동일** → 발화 응답 병합 시 같은 UUID의 로컬 라벨이 서버 진실("집게 팔" 등)로 덮여 뒤죽박죽 보임. + 서버 DB에 테스트 노드 누적 → 발화마다 전체 그래프 쏟아짐.
- [x] **SeedGraphLoader 재작성**: seed_all_dummy.sql 과 정확히 일치(0001 청소로봇 PART=ALL, 0002/0004/0006 PART, 0003/0005/0007/0008 PROPERTY, 0009 REFERENCE, 0010 PROPERTY + 엣지 9개 + parent_node_id). 로컬 시작상태=서버진실 → 병합 충돌 제거. ContextMenu 테스트도 로봇 노드로 갱신.
- [ ] **[사용자] 서버 DB 리셋**: 누적 테스트 노드 제거 위해 seed_all_dummy.sql 재적용(psql). docker-compose 접속정보 확인 필요.
- [ ] [사용자] 리셋+재컴파일 후 재검증: 시작 시 메인그래프 ALL=청소로봇+파트3, 서브그래프 PROPERTY 5개. 발화 시 해당 노드만 추가되는지.
- ⚠️ 근본 해결은 서버 그래프 로드 API(서버팀). SeedGraphLoader는 그때까지의 스탠드인.

### F. 씬 이전
- [ ] Test_SH → MeetingRoom_GraphContent 씬으로 이전

---

## 다른 팀원과 논의 필요

### 개발자 2 (UI/Interaction/Voice)
1. **XR에서 TMP_InputField 포커스 방식**  
   현재 World Space Canvas + 마우스 클릭으로만 테스트됨. XR Ray Interactor 환경에서 텍스트 입력 방식 확인 필요.
2. **ConnectorSphere pressed 상태 트리거 시점**  
   `EdgeView.SetFromConnectorPressed(bool)` / `SetToConnectorPressed(bool)` API 준비됨.  
   XR Grab/Select 이벤트를 언제 호출할지 협의 필요.
3. **NodeActionPanel 버튼 XR 인터랙션 방식**  
   현재 UnityEngine.UI Button (onClick). XR에서 Ray Interactor가 World Space Canvas를 클릭할 때 TrackedDeviceGraphicRaycaster 필요 여부 확인.

### 서버팀
0. **서버↔클라 그래프 모델 정합 — 클라측 해소 완료 (2026-07-04)**  
   서버는 PART/PROPERTY/REFERENCE 섞인 **단일 트리**(시드: `0001 PART → 0002 PART → 0003 PROPERTY`). 클라는 **서브그래프=PROPERTY 전용, PART/ALL=메인, 적용만 PROPERTY→PART**. → **서술 vs 적용 방향 구분으로 해소**:
   - **서술**(서버 소유): `PROPERTY→PROPERTY` / `PART→PROPERTY`(파트가 속성 가짐) / `PART→PART`(분해). `AddServerEdge`로 병합, PART는 메인 UI로·PROPERTY는 서브그래프로 렌더.
   - **적용**(클라 로컬): `PROPERTY→PART` / `REFERENCE→PART`. 포트 더블클릭→leaf 드롭 제스처. 서버 미전송, `RegenerateConnectionBuilder`가 `to==PART`만 골라 connection 배열로. → 서버 서술 엣지와 자동 분리(충돌 없음).
   - **ALL 유도**: 루트 PART(parent=null)=ALL(`BackfillIsGlobal`).
   - **적용 엣지 서버 영속은 MVP 비채택**(업그레이드 시 connection 배열 재사용). graph-spec.md 4장 반영.
   - **서버팀 남은 확인**: PART에 발화 시 서버가 PART 직속 PROPERTY(서술)를 만드는데, 클라는 그 PROPERTY를 서브그래프 루트로 렌더한다 — 이 표현이 서버 의도와 맞는지 대조 필요.
4. **NODE_CREATE WS 이벤트 존재 여부**  
   현재 서버에 NODE_CREATE 이벤트 없음. PROPERTY 노드 수동 생성 시 동기화 방법 확인 필요.
5. **NODE_TEXT_UPDATE payload 필드명**  
   서버 schema는 "text", 클라이언트는 label/node_text 혼재. 정확한 필드명 확인.
6. **NODE_DELETE 캐스케이드 정책**  
   클라이언트는 자식 노드를 모두 개별 삭제. 서버가 자동 cascade delete하는지, 아니면 클라이언트가 각 node_id를 보내야 하는지 확인.
7. **PROPERTY 노드 생성/삭제 API 유무**  
   현재 utterance 기반 생성만 있음. 수동 생성/삭제 API 준비 일정 확인.
8. **sub_graph_id 클라이언트 전달 방식**  
   서버 DB에 sub_graph_id 있음. Unity NodeData에 해당 필드 없음. 연동 시 처리 방식 확인.

### 디자인팀 (개발자 1)
9. **ConnectorSphere 최종 크기**  
   현재 코드에서 `_connectorScale = 0.06f` 로 Inspector 조절 중. 확정 수치 전달 필요.
10. **서브그래프 최초 생성 UX — 확정 (2026-07-02)**  
    두 개의 `+` 버튼: **키보드 `+`**(빈 상태 → 서브그래프 첫 root 노드) / **노드 `+`**(연결 자식). 둘 다 빈 노드 생성 후 텍스트 입력=발화 서버 전송. 인터페이스(`RequestCreateRootPropertyNode`/`RequestCreatePropertyNode`) 준비 완료 → 개발자 2가 키보드 `+` UI만 배선.

## 2026-07-15 MVP 18차 (추천 트레이·핀치 지속성·중복 손 비주얼 점검)

- [x] AI 추천 패널을 보드 왼쪽 바깥 배치에서 보드 근접 추천 트레이로 교체 (보드 전면 5.5cm, 카메라 거리 1.32m 실측)
- [x] 중복 책상 배치 버튼을 하나의 작업 보드 버튼으로 정리 (MvpDeskDock 미생성, DeskToggle 1개 실측)
- [x] 핀치 선택 중 접촉 판정이 끊기는 문제와 복수 손 입력 소스 스캔 점검 (잡는 중 유지 + 손별 단일 소스)
- [x] 손 시각화 경로를 한 세트로 한정 (독립 Hand Tracking 및 OpenXR 복제 메시 차단)
## 2026-07-15 MVP 19차 (범용 협업 설계 랜딩)

- [x] 랜딩 브랜드·문구를 물로켓 전용에서 범용 협업 설계로 전환
- [x] 물로켓 일러스트 대신 아이디어·노드·스케치 흐름을 보여주는 네이티브 UI 비주얼 적용
- [x] 새 설계방의 예시 문구를 주제 독립적으로 변경하고 화면 가독성 재검증 (Welcome/Create 0 overflow)
## 2026-07-15 MVP 20 (Landing brand mark and hero layout)

- [x] Replace the landing illustration with the NodeXR brand mark and a single calm spatial hero composition.
- [x] Refine the welcome copy and CTA hierarchy, then verify compile and XR UI bounds.


## 2026-07-15 MVP 21 (XR input and shared-workspace ergonomics)

- [x] Make every MVP text field open the world keyboard and submit a custom part on Enter.
- [x] Reduce and restyle the movable world keyboard as a physical key layout.
- [x] Move design actions to an in-board shared tool shelf; replace table settings with a left-wrist menu.
- [x] Correct duplicate hand-mesh filtering and provide a true 3D show/hide control.


## 2026-07-15 MVP 22 (Shared board simplification and private wrist history)

- [x] Keep only image generation and 3D generation in the shared bottom toolbar; move Finish to the lower-right corner.
- [x] Remove persistent gesture/wrist tutorial copy from the shared workspace.
- [x] Make AddPartPort world-keyboard Enter commit and render a real PartPort.
- [x] Place generated 3D visibly in the shared table workspace.
- [x] Require palm-up plus wrist gaze, and show generated-image history only in the local wrist menu.

## 2026-07-15 MVP 23 (Product-level shared board UI)

- [x] Rebuild the shared board with a clear XR-first visual hierarchy and restrained product palette.
- [x] Restyle part cards, sketch viewport, toolbar, and finish action as one consistent component system.
- [x] Remove decorative noise and prevent text/control overlap at the current meeting-room viewing distance.
- [x] Verify interaction target size, layout bounds, and runtime errors in Unity Play Mode.
## 2026-07-15 MVP 24 (Hand visual ownership and 3D visibility)

- [x] Keep one tracked hand visual per side while preserving synthetic interaction data.
- [x] Prevent Meta HandVisual from re-enabling synthetic poke/grab ghost meshes before render.
- [x] Turn the shared 3D action into Create, Hide, and Show states.
- [x] Verify duplicate renderer suppression and 3D state transitions in Play Mode.
## 2026-07-15 MVP 25 (Wrist button toggle and guidance cleanup)

- [x] Remove the shared workspace status chip completely.
- [x] Show only a round wrist button while palm-up wrist gaze is valid.
- [x] Open and close the private wrist menu only through that button.
- [x] Hide button and menu on gaze loss and simplify fist guidance copy.
## 2026-07-15 MVP 26 (Node pinch bounds)

- [x] Measure each runtime node visual bounds against its grab collider.
- [x] Match the pinch/grab collider to the visible node body without outside padding.
- [x] Verify new and existing nodes keep the same bounds after runtime rewiring.
## 2026-07-15 MVP 27 (Keyboard placement)

- [x] Place the world keyboard 0.40m in front of the user's eyes.
- [x] Place the world keyboard 0.35m below eye level.
- [x] Preserve the 22.5-degree tilt and runtime grab interaction.
## 2026-07-15 MVP 28 (Dead code and asset cleanup before GitHub push)

- [x] Verify MVP.unity scene integrity (all script GUIDs resolve, no null script references).
- [x] Remove 81 dead members (~1,290 lines): unused methods, write-only fields, zombie desk-dock chain.
- [x] Delete orphaned MvpDeskDock.cs (only caller was dead code).
- [x] Replace obsolete TMP enableWordWrapping with textWrappingMode (10 sites).
- [x] Move 21MB Captures screenshots out of Assets to gitignored LocalCaptures/.
- [x] Confirm zero compile errors and zero MVP warnings in Unity console.

## 2026-07-16 MVP 29 (UX gaps: room code, multiplayer signals, confirmations, voice)

- [x] Show invite code (room_id) with copy button on the briefing page (online rooms only).
- [x] Propagate server-ACK node/edge id rekey over Fusion (fixes peer id divergence after WS rekey).
- [x] Sync child-node active toggle over Fusion (green line / dim state now shared).
- [x] Wire node locks: acquire on grab/keyboard edit, release on end; blocked nodes show a hand badge and refuse movement.
- [x] Release a leaver's node locks on OnPlayerLeft; join/leave notices with participant count.
- [x] Attribute remote edits ("OO님이 아이디어를 추가했어요") via GraphNetworkManager events.
- [x] Confirm dialogs before cascade node delete (NodeActionPanel hook) and before finishing the design.
- [x] Restore voice dictation entry point and add a "말로 추가" toggle button to the workspace action bar.
- [x] Fix silently-dropped workspace messages (SetWorkspaceMessage now falls back to the student guide chip).
- [x] Animate the generating-page progress bar; add "방 나가기" to the table settings dock; one-time gesture coachmark.

## 2026-07-16 MVP 30 (Play-mode QA of MVP 29 + dock reachability fixes)

- [x] Verify in play mode with screenshots: welcome, create-room, briefing invite code + copy, part recommendations, gesture coachmark, finish-design confirm, result fallback sketch, voice error path, leave-room flow.
- [x] Fix: MvpTableSettingsDock was never instantiated at runtime (its only creator was dead code) — now created and shown on design start.
- [x] Fix: dock canvas started inactive with no activation path — added ShowCollapsed()/HideDock(), hidden again on leave/restart.
- [x] Fix: dock expanded panel sank under the tabletop (center pivot at table height) — lift by half panel height in PlaceOnTable.

## 2026-07-16 MVP 31 (Button contrast, action-bar hierarchy, voice plan)

- [x] Add CyanDeep/MintDeep factory colors; swap 6 washed-out pastel buttons on dark glass panels (join/demo/enter/copy-code/history/restart) — verified in play mode.
- [x] Action-bar visual hierarchy: only "그림 생성하기" keeps the accent blue; "3D 생성하기"/"말로 추가" use the muted glass tone (listening state stays red) — verified in play mode.
- [x] Write docs/voice-input-plan.md — 3-phase voice input plan (PC Link language pack → Quest RECORD_AUDIO/permission/ko-dictation gate → server STT as mainline, tied to server issue #40).

## 2026-07-16 MVP 32 (On-device Korean STT prototype — sherpa-onnx)

- [x] Add RECORD_AUDIO permission to AndroidManifest.
- [x] Integrate official sherpa-onnx 1.13.4 C# bindings (managed dll + win-x64 natives in Assets/Plugins/SherpaOnnx) — no third-party Unity packages.
- [x] Bundle Korean streaming zipformer int8 model (~130MB) in StreamingAssets; models gitignored (encoder 121MB exceeds GitHub limit) with download instructions in docs/voice-input-plan.md.
- [x] MvpOnDeviceDictation: streaming mic recognition (partials, silence auto-finalize, 0.66s tail padding), WAV decode test helper.
- [x] Wire as first-priority backend in MvpVoiceRequirementController (Windows/Meta as fallback).
- [x] Verified in editor against bundled Korean test wavs — near-exact transcripts, RTF ~0.03.
- [x] Quest native: arm64 .so libs, StreamingAssets→persistentDataPath extraction, runtime mic permission. (on-device Quest verification still pending)

## 2026-07-16 MVP 33 (Voice UI placement + live verification)

- [x] Android arm64 natives (libsherpa-onnx-c-api.so, libonnxruntime.so) placed with PluginImporter set to Android/ARM64; win-x64 dlls restricted to Editor+Windows.
- [x] MvpOnDeviceDictation.Prepare(): mic runtime permission (Quest) → APK model extraction to persistentDataPath → recognizer init; graceful "model not in build" fallback.
- [x] Voice controller: prepare-status messages, fallback to platform dictation on failure.
- [x] World keyboard "말하기" key — dictation into ANY input field (node rename, custom part, room forms); gray partial captions in preview; auto/manual finalize appends to committed text.
- [x] Live editor verification: real mic starts listening (button → "듣기 멈추기"), stop path clean, injected recognition result creates idea node (label verified), keyboard voice key prepare→listen→stop cycle works.

## 2026-07-16 MVP 34 (Voice UI polish)

- [x] MvpOnDeviceDictation.Level — smoothed mic RMS exposed for UI feedback.
- [x] MvpVoiceIndicator — state dot: hidden(idle) / amber slow-blink(preparing) / red heartbeat pulse scaled by voice level(listening).
- [x] Attached to action-bar "말로 추가" button and keyboard "말하기" key; button label gains 준비 중… state.
- [x] Guide-chip partial captions prefixed with red dot; verified pulsing dot + red states in play mode on both surfaces.

## 2026-07-16 MVP 35 (UX debt: modal confirm, message queue, missing status chip)

- [x] CRITICAL FIX: guide chip (SpatialStatus) no longer existed — action-bar renewal removed it, silently discarding ALL guidance/voice captions/notices. Rebuilt as a chip above the action bar; guide also accepts renewed Generate2D button name.
- [x] Confirm dialogs are now modal: dark blocker behind the panel blocks clicks, tapping outside cancels; MvpGentleFollow keeps the popup in view when the user turns away (VR).
- [x] Guide chip message queue: unrelated messages wait 1.3s minimum instead of clobbering (same-prefix streams like voice captions update in place); auto-returns to contextual guidance.
- [x] Align Generate2D interactable rule between guide and workspace dock (parts+ideas+connections) to stop 0.25s flicker fights.
- [x] Invite-code copy button label reverts after 1.6s.
- [x] Play-mode sweep of Complete/3D/Result pages; verified modal open→blocker-cancel→confirm→cleanup lifecycle (earlier "leak" was editor pause deferring Destroy).
- [ ] Observation: 3D generation page panel blocks the "look ahead" view of the assembling rocket — consider shrinking/fading the panel during assembly.

## 2026-07-16 MVP 36 (Overlap/occlusion UX — front UI must yield, not block)

- [x] World keyboard broadcasts OnOpenedGlobal/OnClosedGlobal; overlapping panels yield instead of stacking.
- [x] Recommendation panel auto-hides while keyboard edits a node label and restores on close; stays if the keyboard target is its own custom-part input.
- [x] Table settings dock auto-collapses when the keyboard opens into the same space.
- [x] Touch highlight on nodes: the node your fingertip actually contacts grows 6% (scale-only, sync-safe) so you can tell which of several overlapping nodes a pinch will grab; leaves external scale changes untouched.
- [x] Verified in play mode: hide→restore cycle, inner-input exception, dock collapse. (Touch highlight needs hand-tracking device check.)

## 2026-07-16 MVP 37 (Occlusion follow-ups: X-ray reach-through, 3D fade, stray-node hint)

- [x] MvpPanelXray: reach a fingertip past the recommendation panel → panel fades to 22% and releases raycasts, letting you grab nodes/board behind it; restores when the hand returns. (Device check pending — no hand tracking in editor.)
- [x] 3D assembly: flow canvas fades to 16% after 0.9s so the assembling rocket is visible ("앞쪽 공간을 바라보세요" no longer contradicts the screen); restores on completion/state change. Verified restore path.
- [x] Guide chip suggests "작업판 맞추기" when any node sits >1.6m from the board for 2.5s (45s cooldown) — verified live with a node moved 3.7m away.
- [x] BUGFIX: rocket-part colliders were deferred-destroyed while pop-in set scale to zero → "BoxCollider does not support negative scale" errors that froze the editor via Error Pause. StripCollider now uses DestroyImmediate.

## 2026-07-17 MVP 38 (Correctness audit — intent-vs-behavior review, 8 fixes)

- [x] CRITICAL: action bar + keyboard each created their own sherpa-onnx recognizer → model loaded twice (hundreds of MB, fatal on Quest). Recognizer is now a shared static; mic ownership arbitrated (starting one listener finalizes the other).
- [x] Edit-lock leak: opening the keyboard on node B while editing node A overwrote the tracked lock id without unlocking A — previous edit lock is now released on switch.
- [x] Live voice captions could get stuck behind the message queue (stale caption shown later) — LiveMarker-prefixed messages now bypass the queue.
- [x] Leaving the room / restarting while dictating left the mic running — RestartFlow stops listening first.
- [x] Fast state round-trips could double-run the 3D assembly routine (double fade/progress) — coroutine handle now stopped before restart.
- [x] Gesture coachmark burned its once-per-device flag before being seen — flag now set only when "알겠어요" is pressed.
- [x] X-ray reach-through only released GraphicRaycaster; VR rays hit the PointableCanvas BoxCollider — colliders now toggle with panel solidity.
- [x] Late joiners never received the existing graph (RequestBroadcastCurrentGraph had no caller) — master now rebroadcasts 2s after a player joins; attribution notices suppressed during each client's initial 8s sync window.
- [x] Verified: all edits compiled clean (assembly rebuilt after edits, zero CS errors in editor log).
- Noted, not fixed: preparing-voice callback can start the mic after a RestartFlow (re-tap stops it); session notices during Briefing are dropped (chip exists only in Design); keyboard Enter mid-dictation discards the in-flight fragment (matches user intent).

## 2026-07-17 MVP 39 (Pre-push sweep #2 — dead code fixpoint + repo hygiene)

- [x] Member-level dead-code rescan at fixpoint: removed leftover `_expanded` declaration (MvpTableSettingsDock) and restored-but-dead `SubmitTranscriptionForTest`. Kept `DecodeWavForTest` (QA tool for Quest verification) and all MenuItem/engine entry points.
- [x] Verified every new public API added this cycle (rekey/active-sync events, lock helpers, session notices, keyboard globals, LiveMarker, dock show/hide) has live references.
- [x] Repo hygiene: captures + 130MB models gitignored, no >90MB file staged for commit, every new asset (Voice scripts, SherpaOnnx dlls/.so, StreamingAssets) has its .meta pair.
- Deferred refactors (need a compile-verifiable session; behavior-safe but multi-file): IHand scanning duplicated in 5 files → extract shared hand registry; MvpClassroomFlow at 3,769 lines → split state builders; glass-tone color literal (0.13,0.17,0.27) ×4 → factory constant.

## 2026-07-17 MVP 40 (Node R button → reference design search via Vuplex WebView)

- [x] NodeActionPanel.ReferenceHook (static, scene-injected like ConfirmDeleteHook) — R button delegates to MVP, other scenes keep the log stub.
- [x] MvpReferencePanel: world canvas beside the board with CanvasWebViewPrefab loading Bing image search, SafeSearch forced (adlt=strict — 초등 대상). Top bar: keyword input (world keyboard via MvpXrKeyboardInput) + close. PointableCanvas + MvpPanelXray attached; webview destroyed on close (Quest memory).
- [x] Keyword strategy: instant local fallback "물로켓 {part} {deepest 2 chain labels} 디자인" from CollectReferenceContext; if online, ReferenceApiClient.RequestKeyword (auto-added, _syncClient injected) upgrades the query unless the user already edited it.
- [ ] Verify: needs editor compile + play test (editor wasn't accepting remote refresh; focus Unity → Ctrl+R). Then Quest: internet access + webview input check.
- Phase 2 (later): tap an image in results → GenerateReference upload → REFERENCE node in graph (server API exists).

## 2026-07-17 MVP 41 (Pre-push formatting sweep — whole MVP folder)

- [x] Normalizer pass 1: re-indented 175 column-0 member declarations (incl. multi-line signatures), stripped 2,407 trailing-whitespace lines, collapsed 3+ blank-line runs (19 files).
- [x] Normalizer pass 2: promoted 565 under-indented body lines to 4×brace-depth minimum (deeper alignment respected; comment/string/preprocessor-safe scanner, no verbatim strings in folder).
- [x] Split 2 joined declarations ("}    private void …"); glass-tone literal ×4 → MvpStudentUiFactory.GlassAction.
- [x] Verified zero residue: col-0 members inside classes 0, joins 0, trailing whitespace 0, brace imbalance 0 across all MVP .cs.
- [ ] Editor recompile pending (indent/whitespace-only + same-value constant swap — focus Unity once to confirm green).

## 2026-07-30 서버 연동 1 (개발자3 — MVP 씬 온라인 전환 + PART REST 정합)

배경: 실통합 씬이 `MeetingRoom_Base`가 아니라 `MVP.unity`임을 확인. Base는 그래프 에셋을 전혀 참조하지 않고(`[Mount] GraphContent`는 빈 껍데기), MVP.unity가 GraphManager/GraphSyncClient/MainSketchPanel 일체를 들고 있다. 그런데 MVP는 `_autoConnect:0 / _sendToServer:0 / _offlineFallback:1` — 서버를 한 번도 켜본 적이 없는 상태였다.

- [x] 서버 실표면 재조사(`nodexr-server`, 로컬 클론). `docs/server-api-alignment.md`(2026-07-15)가 stale함을 확인:
  - 있음: `POST /api/utterances`, `/api/part_node/{generate,modify,delete}`, `/api/2d/generate/{graph,feature}`, `GET /api/history/{room_id}`, `GET /api/rooms/*`, WS `/ws/rooms/event`(NODE_CREATE/MOVE/TEXT_UPDATE/DELETE, EDGE_CREATE/DELETE + ACK)
  - 없음: `GET /api/graph`(콜드로드), `POST /api/sub_graph/generate`, `/api/references/*`, `/api/part_node/generate/keyboard`, `/api/node/generate/utterance`, `/api/2d/color_change`
  - 죽은 채널: `GRAPH_UPDATED` 미emit(`connection_manager.py:172` broadcast_to_room 주석) → 서버→클라는 ACK와 `2D_GENERATED`만 유효.
- [x] PART REST 경로 정합. 서버는 `generate` 하나에 `{room_id, text, position}`만 받는다(발화/키보드 분기 없음).
  - `PartNodeDto.cs`: `PartNodeGenerateRequest`(utterance) + `PartNodeKeyboardRequest`(text) → `PartNodeCreateRequest`(text) 통합.
  - `PartNodeApiClient.cs`: `CoCreate`/`CoCreateKeyboard` 둘 다 `POST part_node/generate` + body `text`. modify/delete는 원래 일치해 무변경.
- [x] `AddPartPort.InvokeAdd` 서버 우선으로 복원. 직전 MVP 우회가 로컬 우선이라 **서버 호출이 도달하지 못하는 죽은 코드**였다(주석의 사유 "generate/keyboard 404"가 해소됨). 서버 실패 시 `CreateLocalPart` 폴백 유지. 두 오버로드 모두 `GraphManager.AddNode`를 타므로 Fusion 전파는 동일 — 차이는 node_id 발급 주체이고, 2D 생성의 `part_node_id`로 쓰려면 서버 UUID여야 한다.
- [x] `Assets/00_Scenes/MVP/MVP_SH.unity` 생성(MVP.unity 사본, 새 meta GUID). 원본 무변경.
  - `_autoConnect 0→1`, `_sendToServer 0→1`, `PartNodeApiClient._offlineFallback 1→0`.
  - 누락된 `SubGraphApiClient`/`HistoryApiClient`/`ReferenceApiClient` 배치(fileID 1650281635~637). 앞 둘은 서버 엔드포인트가 없어 현재 404 — 배선 선반영. `ReferenceApiClient`는 `MvpClassroomFlow.EnsureReferenceApi()`가 `FindFirstObjectByType`으로 먼저 찾으므로 런타임 AddComponent와 중복되지 않는다.
  - `SeedGraphLoader._loadOnStart`는 1로 유지 — 콜드로드 엔드포인트가 없어 초기 그래프 경로가 이것뿐이고, 시드 UUID가 `seed_all_dummy.sql`과 정확히 일치한다.
- [x] Unity 컴파일 통과(에디터 Play 진입 확인).
- [x] **PART 생성 REST 왕복 라이브 검증 완료.** `POST /api/part_node/generate` 200 → DB에 `d583e3b1…| PART | 팔걸이` 생성 → 로컬 노드가 서버 UUID를 그대로 사용(`[Connect] 무장: 포트 d583e3b1`). 폴백 경고 없음. PartPort 렌더링도 0→1→2개로 정상. `part_node/delete`도 curl로 200 확인.
- [ ] **WS 동기화 검증 불가 — 서버 블로커.** 원인 사슬: `POST /api/rooms/generate` 500 → `MvpClassroomFlow.cs:2827` created=false → `:2829-2831` 랜덤 roomId + online=false(체험 모드) → `:2834` `if (_session.online)` false → `ConfigureGraphSocket()`(`:3610`, enabled=true + Connect()) 미호출 → GraphSyncClient 비활성 유지. **개발자2 코드는 정상**이고 서버 500이 앞단을 막은 것.
  - 500 근본 원인: `passlib 1.7.4` + `bcrypt 5.0.0` 비호환 → `app/core/security.py` `hash_password()`가 모든 입력에 `ValueError: password cannot be longer than 72 bytes` (입력이 4바이트여도). `requirements.txt:28` `passlib[bcrypt]`가 bcrypt 미핀이라 신규 설치 환경만 발생. `/api/rooms/enter`도 동일 영향.
  - 서버팀 전달 완료(2026-07-30). 수정안: `requirements.txt`에 `bcrypt==4.0.1` 핀.
  - 남은 검증(서버 수정 후): WS 연결 → NODE_TEXT_UPDATE/NODE_MOVE DB 반영 → 2D 생성 왕복 → 히스토리 조회.
- [정정] `MVP_SH.unity`의 `_autoConnect`/`_sendToServer` 변경은 **MVP 흐름에서 무의미**하다. `PrepareExistingScene()`(`:385-388`)이 시작 시 끄고 `ConfigureGraphSocket()`(`:3620-3625`)이 켜므로 씬 값이 양방향으로 덮어써진다. 실효가 있는 건 `_offlineFallback: 0`(PART 실패를 드러내 이번 검증에 유용)과 추가한 API 클라 3개뿐.
- [정정] MVP에서 `SeedGraphLoader`는 설계상 비활성(`:382-383`)이며 로봇 시드가 아니라 물로켓 시나리오를 쓴다. 온라인 흐름에서는 `_roomId`가 서버 생성 방으로 교체된다(`:3617`) → 시드 room `aaaaaaaa…`은 체험 모드에서만 쓰인다. 계획서의 "시드 유지" 근거는 MVP에 해당되지 않았다.
- 남은 DB 흔적: 시드 room에 검증용 PART `d583e3b1-0fe4-4a6f-8bd8-a7cfdb561d1e`("팔걸이") 생존. 다음 검증 전에 소프트 삭제 여부 판단.
- [x] 로컬 DB 시드 복구 완료. `seed_all_dummy.sql` 재실행(멱등, `deleted_at=NULL` 복원)으로 `…0005` 텍스트 원복 + `…0007`·`…0008` 삭제 해제, 이어 잔여 테스트 노드 9개/엣지 9개 소프트 삭제. 결과: 시드 room 생존 노드 정확히 10개(원문 일치), 생존 엣지 9개 = 시드 엣지(`18181818%`) 전부, 비시드 엣지 0개. 스키마·서버 코드 무변경.
- [ ] 검증 후 `docs/server-api-alignment.md` 0절 갱신(위 조사 결과 반영).
- 이번 범위 제외: 노드 프리팹 `NewNodebox` 교체 + untracked 디자이너 에셋(NM00~04.mat, Shader00~04, New_base.fbx) 커밋. MVP는 계속 `NodeView_Sub.prefab` 사용.

## 2026-08-01 서버 연동 2 (개발자3 — Quest 3S 실기 검증 + PART 서버 등록 일원화)

- [x] **Quest 3S device 서버 연동 성공.** 헤드셋에서 방 생성 → 입장 → WS 연결 → 이벤트 송신까지 전 경로 확인. DB에 device 생성 방 `fe275a61-…` 기록됨. WS `NODE_TEXT_UPDATE`/`NODE_MOVE` 정상 송신, 서버 ERROR 봉투 파싱까지 동작.
- [x] 빌드 파이프라인 정리(전부 내 영역 밖 문제였음, 순차 해결):
  - 프리팹 누락 21개 → `UiTest.unity`/`backup/Lobby.unity`가 원인. **체크 해제로는 안 됨** — `AotPreBuilder.cs:153-168`이 `EditorBuildSettings.scenes`의 `.path`만 읽고 `.enabled`를 무시하므로 **목록에서 제거**해야 한다.
  - OVRCameraRig 충돌 → 같은 이유로 `MeetingRoom_Base` 제거(AOT가 additive로 동시 오픈).
  - `Microphone Usage Description` 공백 → Android 빌드 거부. Player Settings에 입력.
  - 유령 항목 `Assets/Scenes/MeetingRoom.unity` → macOS에선 경고, **Android에선 하드 에러**. 목록에서 제거.
  - 빌드 타깃이 macOS(`OSXUniversal`)였음 → Android 전환.
  - `insecureHttpOption: 0` → device에서 `InvalidOperationException: Insecure connection not allowed`. **Unity는 loopback(127.0.0.1)만 예외**라 에디터 테스트에선 안 드러났다. `Always allowed`(2)로 변경.
  - 퀘스트 Wi-Fi 미연결(wlan0 IP 없음) → 연결 후 `192.168.0.238`, 맥(`192.168.0.236`)까지 ping 정상.
- [x] 서버 로컬 환경(리포 파일 무변경): `bcrypt 5.0.0 → 4.0.1`(passlib 1.7.4 비호환 해소, `rooms/generate` 500 → 200), `.env`의 `MINIO_PUBLIC_BASE_URL`을 LAN IP로. `.env`는 `.gitignore:138` 대상.
- [x] **PART 서버 등록을 GraphManager 이벤트로 일원화.** 실기에서 모든 WS 뮤테이션이 `[NODE404] Node not found`로 거부됐고, 원인은 `MvpWaterRocketGraphController.cs:124`가 `RequestCreatePartNode(label, isGlobal)` **2-인자(로컬 전용) 오버로드**를 써서 서버에 노드가 없던 것. `AddPartPort`만 고쳤던 7-30 수정이 이 두 번째 호출부를 놓쳤다.
  - 개발자2 파일을 건드리지 않기 위해(충돌 회피) **내 파일 2개만 수정**:
    - `GraphManager.cs`: `OnLocalPartNodeCreated(localId, label, isGlobal, position)` 이벤트 추가. `nodeId`가 null일 때(로컬 발급)만 발행 — 3-인자 오버로드(서버 발급/폴백)는 미발행하여 이중 등록·재귀 차단.
    - `PartNodeApiClient.cs`: 이벤트 구독 → `POST /api/part_node/generate` → `ApplyServerNodeId(localId, serverId, serverText)`로 rekey. 오프라인 폴백 2곳은 `Guid.NewGuid()`를 명시 전달해 재귀 방지.
  - 효과: 호출부가 누구든(현재·미래 불문) 서버 등록이 자동으로 붙는다. CLAUDE.md의 "GraphManager는 서버를 모르고 이벤트만 발행, `*ApiClient`가 서버 경계" 구조와 일치.
- [x] **실기 재검증 성공(Quest 3S).** 주먹 제스처로 만든 루트 PROPERTY가 서버 DB에 생성됨:
  `fc521e53-… | PROPERTY | "아이디어" | sub_graph_id 있음 | (-0.39, -0.05, -0.41)`.
  `NODE_CREATE(job_id, parent="", sub_graph="")` 송신 → `NODE200 노드 생성 성공` ACK → rekey → 위치 동기화까지 전 체인 확인. `[NODE404]` 소멸.
- [x] 제스처 생성 경로 3건 추가 수정(전부 `GraphManager.cs`, 개발자2 파일 무변경):
  - `RequestSubmitNodeText` 루트 분기: 없는 `/api/sub_graph/generate` 대기 제거 → `NODE_CREATE` 직접 송신. 서버가 `parent_node_id=None && PROPERTY`면 서브그래프를 자동 생성한다(`graph_interaction_service.py:354-361`). 빈 문자열은 서버에서 `None`으로 파싱(`_parse_optional_uuid_payload`).
  - `RequestUpdateNodeText`: 서버 미등록 노드면 생성 경로를 함께 호출. 제스처 경로(`MvpSpatialNodeGestureController.cs:527-536` → `RequestCreateRootPropertyNode` → `RequestUpdateNodeText`)가 서버 등록을 한 번도 거치지 않던 문제.
  - `ApplyServerNodeId`: rekey 직후 현재 위치를 한 번 발행(ACK 전 이동분 보정).
- [x] **회귀 수정.** 위 작업 중 `OnNodeTextUpdated`/`OnNodeMoved` 발행 자체를 억제했다가 Fusion 전파(`MvpGraphNetworkBridge`)와 MVP 시나리오(`MvpWaterRocketGraphController`)까지 끊겼다(에디터에서 텍스트 수정 불가). **이벤트는 항상 발행하고, 서버 송신 억제는 서버 경계(`GraphSyncClient.HandleNodeTextUpdated`/`HandleNodeMoved`에서 `IsServerKnown` 확인)로 이동.** 엣지가 이미 쓰던 패턴(`GraphSyncClient:375`)과 일관.
- [x] **네이티브 크래시 해결됨(개발자1, `011a78b`).** `Capacity(128)` × `NetworkString<_128>` → `Capacity(32)` × `NetworkString<_64>` (2,048워드, 상한 32,768 대비 여유). Spawn 실패가 사라져 `Spawned()`가 정상 호출되고, 이후 `NodeLocks` 예외 폭주와 SIGSEGV도 함께 해소. 아래는 원인 규명 기록.
- [x] **(원인 기록) 네이티브 크래시**
  ```
  AssertException: 25500 >= NetworkObjectHeader.WORDS && 102000 <= 32768
    at Fusion.NetworkRunner.Spawn(...)
  ```
  `GraphNetworkManager`의 networked 상태가 Fusion NetworkObject 상한(32,768워드)을 3배 초과(102,000)해 **Spawn 자체가 실패**한다. 그 결과 `Spawned()`가 영영 호출되지 않아 `NodeLocks` 접근이 매 프레임 `InvalidOperationException`을 던지고(`TryGetNodeLockOwner` ← `MvpNodeInteractionController.RefreshLockBadge`, LateUpdate), 노드 조작 시 SIGSEGV로 이어진다.
  - 원인 선언(`GraphNetworkManager.cs:20-21`): `[Networked, Capacity(128)] NetworkDictionary<NetworkString<_128>, PlayerRef> NodeLocks`. 실제 키는 UUID 36자라 `_128`은 과대.
  - 수정안 A: 키 축소 `NetworkString<_64>` / B: `Capacity(32)` / C: 문자열 대신 해시 키.
  - 부차적으로 `:39` 가드 `IsReadyForRpc`가 Spawned 여부를 확인하지 않는 것도 함께 보완 필요.
  - 개발자1(`02_Scripts/Lobby/GraphNetworkManager.cs`) 사안. **이게 안 고쳐지면 노드 조작마다 앱이 죽어 실기 검증이 계속 끊긴다.**
- [ ] 서버팀 전달: `requirements.txt:28` `passlib[bcrypt]`에 `bcrypt==4.0.1` 핀 추가(미핀이라 신규 설치 환경마다 재발). 개발자1 로비(`NetworkManager.cs:170`)도 같은 엔드포인트라 동일 영향.
- 참고: LAN IP는 DHCP라 날마다 바뀐다(7-31 `192.168.219.49` → 8-01 `192.168.0.236`). 씬에 박히는 값이라 바뀌면 `MVP_SH.unity`의 `_host`/`_backendHost` 수정 후 재빌드 필요. 공유기에서 고정 IP 할당 권장.

## 2026-08-02 서버 연동 3 (개발자3 — 2D 생성 왕복 완주 + 초대 코드 단축)

**Quest 3S 실기에서 서버 연동 전 구간이 통했다.** 방 생성 → WS 연결 → 노드 생성(제스처) → 파트 생성 → 속성↔파트 연결 → 2D 생성 → 이미지 수신·표시.

- [x] **2D 생성이 아예 트리거되지 않던 문제 해결.** 버튼을 눌러도 `Generate2DController` 로그가 0건이었다. 원인은 버튼의 `interactable` 이 항상 false — `MvpClassroomFlow:3138` 의 활성 조건이 보는 `MvpWaterRocketGraphController.PartCount` 만 `GraphManager` 가 아닌 내부 맵 `_partIds` 를 세고 있었다. `AddPartPort → PartNodeApiClient → RequestCreatePartNode` 로 만든 파트는 `_partIds` 에 없어 항상 0. **`interactable=false` 인 Button 은 onClick 을 발생시키지 않으므로 어떤 핸들러도 불리지 않는다**(개발자2의 `BeginWorkspaceGenerate` 도 마찬가지였다).
  - `PartCount` 를 `GraphManager` 의 PART 노드 기준으로 변경(`is_global`=ALL 제외). 세 카운트가 모두 GraphManager 단일 출처가 됨.
  - `Generate2DController`: `_generateButton` 에 onClick 리스너 연결(그동안 interactable 제어에만 사용).
  - 진단 과정에서 `_workspaceGenBusy` 잠김 / VR 입력 문제 등 잘못된 가설을 여러 번 세웠다. **무로그 조기 반환이 많은 구간은 로그 부재를 근거로 추론하면 안 된다**는 교훈.
- [x] **2D 이미지 다운로드 실패 해결(로컬 우회).** 서버는 생성까지 정상인데 헤드셋이 `Cannot connect to destination host` 로 즉시 실패했다. 원인은 MinIO(9000)가 Docker Desktop 퍼블리시 포트라 **LAN 의 다른 기기에서 안 닿는 것**(uvicorn 8000 은 네이티브라 정상). 맥에서 자기 LAN IP 로 테스트하면 루프백이라 통과해 오진하기 쉽다.
  - 네이티브 파이썬 TCP 포워더(`0.0.0.0:9100 → 127.0.0.1:9000`)를 띄우고 `.env` 의 `MINIO_PUBLIC_BASE_URL` 을 9100 으로 변경 → 다운로드 성공.
  - **임시 조치다.** 근본 해결은 서버팀이 MinIO 를 네이티브로 띄우거나 Docker 네트워크를 조정하는 것.
- [x] **초대 코드 6자리 단축.** 36자 UUID 를 그대로 노출해 공유가 어려웠다. 서버 변경 없이 클라에서만 6자 코드를 만들고 `GET /api/rooms/list` 로 복원한다.
  - 처음 16진수(UUID 앞 6자)로 했다가 **공간 키보드(`MvpWorldKeyboard.BuildEnglishKeys`)에 숫자 행이 없어 입력 자체가 불가능**한 것을 실기에서 발견 → UUID 앞 7자(28비트)를 26진수 6자(A~Z)로 인코딩하도록 변경. 28비트(2.68억) < 26^6(3.09억) 이라 손실 없음.
  - 대소문자 무관, UUID 전체 붙여넣기 하위호환, 코드가 여러 방과 겹치면 입장 거부.
- [x] 속성↔파트 연결 UI 배선 완료. `GraphLinkSelection`(무장 상태) + `NodeActionPanel._linkButton/_linkLabel` + `PartPort`/`AllPort` 완료 처리. `NewNodebox.prefab` 에 LinkButton 추가(사용자가 에디터에서 배치).
- [x] 노드 프리팹을 새 디자인(`Designer/NewNodebox`)으로 전환하고 `Test_SH` 도 재지정. 누락돼 있던 디자인 에셋(프리팹·NM 머티리얼·셰이더그래프·FBX) 26 파일 커밋 — git 이력이 전혀 없어 "삭제"가 아니라 "애초에 미추가" 였다.
- [x] Quest 빌드를 막던 항목들 해소: `microphoneUsageDescription` 공백(Android 빌드 거부), `insecureHttpOption: 0`(**Unity 는 loopback 만 예외라 에디터에선 안 드러나고 헤드셋이 LAN IP 로 붙는 순간 모든 REST 차단**), 빌드 씬 목록의 깨진 씬(`AotPreBuilder` 가 `.enabled` 를 무시하고 목록의 모든 씬을 열어, 체크 해제가 아니라 **제거**해야 함), 빌드 타깃이 macOS 였던 것.

### 남은 것

- [ ] **엣지 선이 안 보인다.** `EdgePrefab` 의 `_lineWidth 0.004` / `_connectorScale 0.001`(1mm) 이 과소해 선도 연결구도 보이지 않는다. `EdgeView` 참조·포트 좌표·프리팹 구조는 모두 정상 확인. 값만 키우면 되고 적정값은 에디터에서 눈으로 맞추는 편이 빠르다.
- [x] ~~**2D/3D 결과가 요청자에게만 간다** → 서버가 방 전체 브로드캐스트 권장~~ **서버가 반대로 확정했다(`aa81878`).** `broadcast_to_room` 이 아예 삭제됐고(`connection_manager.py` -78줄) 요청자 전용 `send_personal_message` + `job_id` 매칭으로 갔다. 브로드캐스트 요청은 폐기한다. 멀티 참가자 공유가 필요하면 **클라가 Fusion 으로 전파**해야 한다(요청자 이탈 시 끊김·URL 도달성 문제는 그대로 남는다).
- [x] ~~**공간 키보드에 숫자 행이 없다**~~ 개발자2 가 해결(`c6bcdb1`). `BuildNumericKeys()` 숫자·기호 레이아웃 + `123` 토글 + `ShiftCase()` 대문자까지 들어갔다. 방 비밀번호 숫자 입력 가능.
- [x] ~~서버팀 전달: `bcrypt==4.0.1` 핀~~ 서버팀 반영 완료(`requirements.txt:31`).
- [ ] 백엔드 주소가 씬에 박혀 있어 IP 변경·사람마다 수정이 반복된다(하루에 3번 바뀐 날도 있음). **`_backendHost`(방/초대코드)와 `_host`(WS/그래프) 두 곳을 함께 바꿔야 하며**, 한쪽만 바꾸면 방은 A 서버, 그래프는 B 서버로 갈라져 "코드를 찾지 못함" 이 된다(실제 발생). 설정 파일이나 런타임 입력으로 빼는 것을 권장.

## 2026-08-03 서버 규약 재정합 (개발자3 — job_id 도입)

서버 `develop` 17커밋을 받으니 **2D 생성 요청 스키마가 깨져 있었다.** `aa81878` 이 생성 요청에 `job_id`(UUID, 필수)를 추가해, 지금까지의 클라 요청은 전부 422 로 거부되는 상태였다. 실기 테스트 전에 발견.

- [x] **요청 DTO 에 `job_id` 추가.** `Generate2DDto.cs` — feature/graph 양쪽. 클라가 `Guid.NewGuid()` 로 발급한다.
- [x] **결과 대조.** `Generate2DController._pendingJobId` 에 보관하고 WS 수신 job_id 와 대조해 불일치면 무시. 서버가 요청자에게만 보내도록 바뀌었으므로 **연속 요청 시 늦게 온 이전 결과가 최신 화면을 덮는 것**을 막는 용도다. 양쪽 다 값이 있을 때만 판정해 구버전 서버와 호환된다.
- [x] **WS 봉투 최상위 `job_id` 파싱.** `GraphSyncClient.Image2DEvent` 에 필드 추가, `OnImage2DGenerated` 를 `Action<string>` → `Action<string,string>` 으로 변경(구독자는 `Generate2DController` 하나뿐이라 안전).
- [x] **`2D_COLOR_CHANGED` 분기 추가.** payload 구조는 `2D_GENERATED` 와 같은데 event_type 만 달라 클라가 통째로 무시하고 있었다. **색상 변경 결과가 영영 안 오고 타임아웃나는 상태였다.**
- [x] **`color_change` 폼 필드 보강.** `user_id`/`job_id`/`metadata` 가 빠져 있었다(`metadata` 누락은 이번 변경과 무관한 기존 결함 — 이 경로는 한 번도 통과한 적이 없었던 것으로 보인다). `metadata` 는 `{mime_type,width,height}` JSON 문자열이고 서버가 width/height 를 `gt=0` 으로 검증하므로, 호출부가 크기를 안 주면 `ResolveImageSize()` 가 이미지를 디코드해 채운다.
- [x] 검증: 서버 `/openapi.json` 과 대조해 4개 엔드포인트의 필수 필드가 정확히 일치함을 확인(추측 아님).

### 남은 것

- [ ] **[사용자] Unity 컴파일 확인 + 2D 생성 왕복 재검증.** 위 수정은 아직 에디터에서 컴파일되지 않았다.
- [ ] **3D 생성 경로가 클라에 아예 없다.** 서버는 `POST /api/3d/generate {room_id,user_id,job_id,asset_id}` + WS `3D_GENERATED{asset_id,mime_type,model_url}` 로 준비됨(`03199d0`). 클라는 `MvpClassroomFlow.cs:1350` 의 `"Generate3D"` 문자열 하나뿐 — 요청·수신·모델 표시 전부 신규 구현 필요.
- [ ] **레퍼런스 노드 제약.** 서버 `846680f` 커밋 메시지: "Unity 상에서 레퍼런스 노드 자식은 생성하지 못하도록 해야 함". REFERENCE 노드에 자식 생성 UI 를 막아야 한다.
- [ ] **`GET /api/graph` 전체 조회 스펙 확인**(`af0cd6a`). 콜드로드 경로가 새 응답 형식과 맞는지 미확인.
- [ ] **`9fc276e` 의 asset + graph_snapshot 저장 형식 변경**이 클라에 영향 있는지 미확인.
- [ ] **[사용자] MinIO 공개 주소.** `.env:18` 이 `http://localhost:9000` 으로 되돌아가 있다. 헤드셋 테스트 시 `http://<맥 LAN IP>:9100` + `tools/minio_forward.py` 필요(8/2 와 동일한 함정).
