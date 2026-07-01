# NodeXR TODO — 개발자 3 (노드그래프 / GraphData / 서버 반영)

마지막 업데이트: 2026-07-02

---

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

> 핵심 주의: `+`로 만든 로컬 노드는 GUID다. 발화(텍스트 입력) 시 utterance로 서버에 전송되어 서버가 하위 그래프를 발급/병합한다. 발화 전 로컬 노드는 서버가 모른다.
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
- [ ] [사용자] 컴파일 확인 + 서버 배포 시 왕복 검증(생성/수정/삭제). 미배포면 404(또는 `_offlineFallback=true`로 로컬 UI 검증).
- [ ] 서버팀 확인: delete 응답 봉투 스키마, `isSuccess` 키 공백 오타.
- [ ] `Dev/SeedGraphLoader.TestPartRename`는 이제 WS 미발행(PART=REST) → Dev 테스트 의미 갱신 필요.

### E-3. 노드 생성(발화) — 서버 REST /api/utterances (2026-07-02)
- [x] **구현**: `Data/UtteranceDto` + `UtteranceApiClient`(`POST /api/utterances`). GraphManager는 `OnUtteranceNodeRequested` 이벤트만 발행, 경계 클래스가 REST 담당 → 성공 시 `MergeServerGraph`(node_id upsert, **서버 position 미적용·reflow 배치**).
- [x] `NodeData` 확장: `parent_node_id` + `NodeAssetData data`(REFERENCE 자산 저장만, 표시 추후).
- [x] `NodeActionPanel` "+" → 인라인 발화 입력(`_utteranceInput`) → `RequestNodeByUtterance`. 입력필드 없으면 기존 즉시생성 fallback. `GraphSyncClient.UserId` 게터 추가.
- [x] (프리팹) NodeView_Sub에 발화 전용 `UtteranceInputField` 추가 + `NodeActionPanel._utteranceInput` 연결 (LabelInputField와 분리).
- [x] **빈 상태 root 생성 인터페이스 (2026-07-02)**: `RequestCreateRootPropertyNode()` 추가 — 부모 없는 서브그래프 첫 PROPERTY root 생성(sub_graph_id=자기 자신). 이후 텍스트 입력=발화 흐름은 자식과 동일. **두 `+` 버튼 구분**: 키보드 `+`=root / 노드 `+`=자식(`RequestCreatePropertyNode`).
- [x] (Unity Inspector) `UtteranceApiClient` 컴포넌트 추가 + `_graphManager`/`_syncClient` 연결(구독). — Test_SH `GraphSyncClient` 오브젝트에 배선 완료.
- [x] graph-api-for-interaction.md — 두 `+` 버튼 + 발화 흐름 + `RequestCreateRootPropertyNode`/`RequestNodeByUtterance` 문서화(개발자 2 핸드오프).
- [ ] (개발자 2) **키보드 `+` 버튼 UI** 제작 → `RequestCreateRootPropertyNode()` 연결 (빈 상태 첫 노드 생성).
- [ ] [사용자] 컴파일 확인 + 서버 배포 시 검증(발화→응답 graph 병합, reflow 위치). 미배포면 404(또는 `_offlineFallback=true`).
- [ ] 서버팀 확인: `user_id` 필수/형식, `isSuccess` 키 공백 오타, 응답이 노드 삭제 포함 가능성.

### E. 서버 연동 — WebSocket 기준
- [x] **WS 연결 토대 (2026-07-01)**: `GraphSyncClient` 실제 WS 연결 + 송신 3종(NODE_TEXT_UPDATE/NODE_DELETE/NODE_MOVE). 라이브러리는 Meta XR Voice 번들 `Meta.Net.NativeWebSocket` 사용(별도 패키지 X, GUID 충돌 회피). 수신은 로그만.
- [x] `GraphManager.RequestUpdateNodeText` + NodeView 인라인편집 배선(PROPERTY 텍스트 동기화 갭 해소).
- [x] `Dev/SeedGraphLoader` — seed 실제 UUID 그래프 로드 + ContextMenu 왕복 검증.
- [ ] **[사용자] Unity 컴파일 확인** — `Meta.Net.NativeWebSocket` 사용이 실제로 컴파일되는지. 실패 시 System.Net.WebSockets로 대체.
- [ ] 수신 이벤트 GraphManager 반영(echo loop 방지 설계 후).
- [ ] EDGE_CREATE/DELETE 송신(서버 sub_graph_id 제약 대응).
- [ ] NODE_CREATE: 서버 이벤트 부재 → 로컬 생성 노드 서버 반영 불가(대책 논의).
- [x] **2D 기본 생성 흐름 — Unity 선구현 (2026-07-01)**: `Generate2DController` + `Data/Generate2DDto`(`Generate2DRequestDto={room_id}`). `RequestGenerate()`→`POST /api/2d/generate`(발화 기반 기본 생성, 노드/connection 미반영), WS `2D_GENERATED`{img_url} 수신→중앙 RawImage 다운로드 표시. **단 서버 생성이 스텁+주석**이라 서버팀 구현 후 실동작.
- [ ] 서버팀 확인 후 `Generate2DRequestDto.utterance` 추가(현재 스키마 `{room_id}`만 → 발화는 room_id 문맥으로 서버가 보유한다는 가정, 협의 대기).
- [x] (씬 배선 2026-07-01) Test_SH `GraphSyncClient` 오브젝트에 `Generate2DController` 컴포넌트 추가 + `_syncClient` 연결.
- [ ] (Unity Inspector) `Generate2DController._centerImage` → `MainSketchPanel/SketchImage`(RawImage) 드래그 연결. (프리팹 인스턴스 참조라 인스펙터에서 수동)
- [x] **전체 그래프(서브그래프 강체) 이동 — Unity 선구현 (2026-07-01)**: `NodeData.sub_graph_id` 추가 + 백필, `RequestMoveSubgraph`/`RequestMoveSubgraphByMember`. 서버 전체이동 op 미구현이라 개별 NODE_MOVE로 전송.
- [x] **Reflow 동기화 방향 정정 (2026-07-01)**: Reflow는 **클라 로컬 레이아웃 전용** → reflow 좌표를 서버 NODE_MOVE로 보내지 않는다(이전 "모델 A" 폐기). `ReflowAllSubtrees()`에 OnNodeMoved 자동 발행 넣지 않음. 서버 동기화는 생성/삭제/텍스트 중심. 트리 전체 이동 허용 시 root/sub_graph 단일 좌표만 전송하도록 나중에 설계.
- [ ] 서버팀 확인: 전체이동 op 추가 시 `sub_graph_id` 키 이벤트 스키마
- [ ] 서버팀 확인: NODE_CREATE 이벤트 존재 여부
- [ ] 서버팀 확인: NODE_TEXT_UPDATE payload 필드명 ("text" vs "node_text")
- [ ] 서버팀 확인: NODE_DELETE 캐스케이드 정책 (서버가 자식까지 삭제하는지)
- [ ] 서버팀 확인: EDGE_CREATE label 필수 여부
- [ ] GraphSyncClient Send() 구현 (서버팀 확인 완료 후)
- [ ] WS 연결 라이브러리 결정 및 IGraphTransport 구현체 작성
- [ ] GET /api/graph 또는 WS GRAPH_UPDATED 수신 → LoadGraph() + RenderGraph()

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
