# NodeXR TODO — 개발자 3 (노드그래프 / GraphData / 서버 반영)

마지막 업데이트: 2026-07-10

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
