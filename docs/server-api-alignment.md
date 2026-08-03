# 서버 API 정렬 기준 (개발자 3 기준)

마지막 업데이트: 2026-08-02  
서버 코드 기준: 로컬 클론 `nodexr-server` commit `44a3db1` (실서버 기동 + `/openapi.json` 대조 + REST/WS 왕복 검증, Quest 3S 실기 포함)

> 아래 **0. 현행 정합성** 이 가장 최신이며, 그 안에서도 날짜가 늦은 절이 우선한다.
> 하단 2026-06-24 이후 섹션은 과거 가정이 섞여 있다.

---

## 0. 현행 정합성 (2026-07-15 실서버 검증)

**정책 결정: 클라만 정렬.** 서버(별도 repo)는 건드리지 않고, 서버가 실제 제공하는 표면에 클라를 맞추며, 서버에 없는 기능은 안전 폴백/비활성으로 둔다.

### 검증된 서버 기준값
- REST 베이스: `http://{host}:8000/api/...`, WS: `ws://{host}:8000/ws/rooms/event` (쿼리 없음, 매 메시지 body `{event_type, room_id, user_id, payload}`로 lazy-register).
- 결과 이미지: WS `2D_GENERATED{asset_id, mime_type, width, height, img_url}` → **img_url(MinIO :9000) 다운로드**. `send_to_user`(요청자 1인)만, broadcast는 주석 처리.
- 인증/CORS 없음. 공통 봉투 `{isSuccess, code, message, result}`.

### ✅ 이미 정합 (그대로 동작)
- WS 연결 URL·6종 뮤테이션(NODE_CREATE/MOVE/TEXT_UPDATE/DELETE, EDGE_CREATE/DELETE)·NODE_CREATE·EDGE_CREATE ACK(job_id→서버 id rekey).
- `POST /api/rooms/generate`, `/enter`, `GET /api/rooms/list`, `/{id}/info`.
- `POST /api/2d/generate/graph`(connections `[{part_node_id, node_id}]`)·`/feature`, `GET /api/history/{room_id}`.

### ❌ 불일치 (클라가 옛 스펙 대상으로 선구현 → 서버에 없어 404)
`/api/node/generate/utterance`, `/api/sub_graph/generate`, `/api/references/*`, `GET /api/graph`, `/api/2d/color_change`, `/api/part_node/generate/keyboard`.
- 클라는 각각 실패 시 **로컬 폴백**(placeholder/self-GUID/seed)으로 방어되어 오프라인·체험 흐름은 완주된다.

### ✅ 2026-07-30 갱신 — PART 는 정합 완료 (라이브 검증)

위 목록에서 `/api/part_node/*` 를 **제거**했다. 서버가 commit `c687675`로 PART CRUD를 추가했고(`app/api/part_node.py`), 클라를 그 표면에 맞췄다.

- 서버 실표면: `POST /api/part_node/generate` `{room_id, text, position:[x,y,z]}` → `PartNodeResponse{room_id, part_node_id, part_node_text}`. `PATCH /modify`, `DELETE /delete` 는 원래 필드명까지 일치했다.
- **옛 명세의 발화/키보드 2분기는 서버에 없다.** `generate` 하나만 있고 `text` 만 받는다(LLM 분기 없음). → 클라 요청 DTO 를 `PartNodeCreateRequest` 하나로 통합하고 `CreatePart`/`CreatePartKeyboard` 둘 다 `generate` 로 보낸다.
- `AddPartPort.InvokeAdd` 를 서버 우선으로 복원. 직전 MVP 우회가 로컬 우선이라 서버 호출이 도달하지 못하는 죽은 코드였다.
- 검증: `text="팔걸이"` → 200 PART_NODE200 → DB에 PART 행 생성 → 로컬 노드가 서버 UUID(`d583e3b1…`)를 그대로 사용. 서버 UUID 확보가 2D 생성의 `part_node_id` 전제조건이라 이 왕복이 필수다.

### 🚫 2026-07-30 블로커 — 방 생성 500 이 WS 검증을 막는다

`POST /api/rooms/generate` 가 항상 500(`COMMON500`). `passlib 1.7.4` + `bcrypt 5.0.0` 비호환으로 `app/core/security.py::hash_password()` 가 모든 입력에 `ValueError: password cannot be longer than 72 bytes` 를 던진다(입력이 4바이트여도). `requirements.txt:28` 이 bcrypt 미핀이라 신규 설치 환경에서만 재현된다. `/api/rooms/enter` 도 동일.

영향: MVP 는 방 생성 실패 시 체험 모드로 빠지고(`MvpClassroomFlow.cs:2827-2832`), `if (_session.online)` 가 false 라 `ConfigureGraphSocket()` 이 호출되지 않아 **GraphSyncClient 가 끝까지 비활성** → WS 뮤테이션 경로 전체가 미검증 상태다. 클라 문제가 아니다.

서버팀 전달 완료(2026-07-30). **로컬에서는 `bcrypt==4.0.1` 로 내려 해소**했고 이후 전 구간을 검증했다(아래 2026-08-02 절). 리포 차원 수정(`requirements.txt` 핀)은 서버팀 몫으로 남아 있다.

### ✅ 2026-08-02 — 전 구간 실기 검증 완료 (Quest 3S)

```
방 생성 → /enter → WS 연결 → NODE_CREATE(ACK rekey) → part_node/generate
→ EDGE_CREATE → 2d/generate/graph → WS 2D_GENERATED → MinIO 이미지 다운로드·표시
```

- `POST /api/2d/generate/graph` 는 `connections=[{part_node_id, node_id}]` 만 보면 되고 **엣지 방향을 서버가 해석하지 않는다**(`Connection2D` 스키마). 서버 생성 코드에 `from_node_id`/`to_node_id` 참조 없음.
- **엣지 방향 규약은 이미 양방향 번역돼 있다.** 로컬 표준 `PROPERTY/REFERENCE → PART`, 서버 저장 표준 `PART → PROPERTY/REFERENCE`, 변환은 `GraphSyncClient.HandleEdgeCreated`(송신)와 `GraphSnapshotDto.ToGraphData()`(수신)가 전담한다. DB 에서 `PART→PROPERTY` 로 보이는 것은 정상이며, 이를 버그로 오인해 로컬 규약을 뒤집으면 번역이 이중으로 걸려 깨진다.
- 2D 결과 이미지 URL 은 `.env` 의 `MINIO_PUBLIC_BASE_URL` 을 그대로 쓴다. `localhost` 로 두면 헤드셋에서 받을 수 없다.

### 🚫 2026-08-02 남은 서버측 제약

- **2D/3D 결과가 요청자 1인에게만 간다.** `image_2d_generation_task_service.py:102` 가 `send_to_user` 로 보내고, `connection_manager.py:172` 의 `broadcast_to_room` 은 주석 처리 상태. 멀티에서 다른 참가자는 이미지를 못 받는다. **job_id 유무 문제가 아니라 전달 범위 문제**이므로, 서버가 방 전체로 브로드캐스트하는 것이 정답이다(클라 Fusion 전파는 요청자 이탈·URL 도달성 문제가 남는 우회책).
- `GRAPH_UPDATED` 브로드캐스트도 같은 이유로 미emit → 전체 그래프 동기화·타 유저 전파 경로가 죽어 있다. 그래서 초기 그래프는 콜드로드(`GET /api/graph` 미구현)도, push 도 없다.
- `3D_GENERATED`/3D 생성 스텁(`Model3DGenerationService` 가 print 만) → 3D 는 클라 mock(`MvpRocket3DStage`)로 처리.
- **MinIO(9000)가 LAN 의 다른 기기에서 안 닿는다.** Docker Desktop 퍼블리시 포트 특성으로, 네이티브 프로세스인 uvicorn(8000)은 정상인데 MinIO 만 즉시 연결 거부된다. 맥에서 자기 LAN IP 로 curl 하면 루프백이라 통과해 오진하기 쉽다. 현재는 네이티브 TCP 포워더(9100→9000)로 우회 중이며, 근본 해결은 서버 쪽 배포 방식 변경이 필요하다.

### 멀티플레이 반영
- 서버 그래프 동기화가 죽어 있으므로, MVP 협업의 실채널은 **Photon Fusion**이다.
  `MvpNetworkSession`(room_id 기반 Shared 세션) + `MvpGraphNetworkBridge`(GraphManager 이벤트→`GraphNetworkManager` RPC, 에코 가드)로 배선.
- 서버 WS(GraphSyncClient)는 병행 유지(서버 DB 기록용). 원격 Fusion 적용은 `AddNode` 등 저수준 경로라 서버로 재전송되지 않음.

---

## 아키텍처 개요

실시간 그래프 조작(이동, 텍스트 수정, 삭제, 엣지 추가/삭제)은 **WebSocket 이벤트**로만 처리한다.  
REST API는 발화(utterance) 기반 그래프 생성, 2D 이미지 생성/재생성, 룸 관리에 사용한다.

```
Unity GraphManager
     │  뮤테이션 이벤트 (C# Action<>)
     ▼
GraphSyncClient (Assets/02_Scripts/Graph/GraphSyncClient.cs)
     │  WS 이벤트 JSON 송신
     ▼
WS /ws/rooms/{room_id}/event?user_id={user_id}
     │
FastAPI GraphInteractionService
     │  DB 저장 (soft delete) + 그래프 스냅샷 생성
```

Unity가 서버로 보내는 데이터는 **GraphSyncClient만 담당**한다.  
GraphManager는 서버를 모르고 이벤트만 발행한다.

---

## 1. WebSocket 이벤트 (그래프 실시간 조작)

### 엔드포인트

```
WS /ws/rooms/{room_id}/event?user_id={user_id}
```

### 메시지 공통 구조 (추정 — 서버팀 확인 필요)

```json
{
  "event_type": "NODE_MOVE",
  "payload": { ... }
}
```

---

### 1-1. NODE_MOVE

노드 위치를 서버에 저장한다. 드래그 중 자주 발생하므로 서버에서 그래프 스냅샷을 생성하지 않는다.

```json
{
  "event_type": "NODE_MOVE",
  "payload": {
    "node_id": "uuid",
    "position": { "x": 1.5, "y": 0.0, "z": 0.0 }
  }
}
```

서버 처리: `position_x/y/z` DB 업데이트 + 이벤트 로그 저장. 스냅샷 없음.

**Reflow 재배치 동기화 (모델 A, 2026-07-01 결정):**
노드 추가/삭제 시 `ReflowAllSubtrees()`가 루트를 고정하고 하위 서브트리 전체 위치를 결정론적으로 재계산한다. 이때 **위치가 바뀐 노드 전부**를 서버로 보내 다른 클라이언트와 좌표를 맞춘다(서버 snapshot이 좌표의 단일 진실).
- 클라이언트: reflow 후 변경된 `(node_id, position)`을 **개별 `NODE_MOVE` 여러 개로 전송**(클라에서 묶어 발사하되 각각은 표준 NODE_MOVE). reflow는 "전체 그래프 통째 이동"이 아니라 개별 노드 N개의 좌표 변경이므로 기존 NODE_MOVE로 충분하다.
- 대안(모델 B: 구조 이벤트만 보내고 각 클라 로컬 reflow)은 채택하지 않음 — 웹 등 렌더러가 레이아웃을 공유해야 하는 위험 때문.
- 전제: 편집 잠금(locking, 개발자1)으로 동시에 한 명만 수정 → 위치 이벤트 충돌 없음. 노드 최초 위치는 Unity가 서버에 전달. 신규 접속자는 서버 snapshot을 받아 `LoadGraph()`+`RenderGraph()`로 렌더링.

**전체 그래프(서브그래프 강체) 이동 (2026-07-01):**
서버에는 서브그래프 통째 이동 op가 **미구현**. 서버팀 합의: 그 op를 넣게 되면 Unity가 `sub_graph_id`를 저장/전달해야 함. → **Unity 쪽은 선제적으로 구현**해 둠(서버 붙이면 바로 연결).
- `NodeData.sub_graph_id` 추가. 서버 값 저장, 없으면 GraphManager가 레이아웃 루트 node_id로 백필.
- `GraphManager.RequestMoveSubgraph(subGraphId, delta)` / `RequestMoveSubgraphByMember(memberNodeId, delta)`: 같은 서브그래프 노드 전체를 delta만큼 강체 이동.
- 서버 전송: 현재는 노드별 **개별 NODE_MOVE**로 나감(전체이동 op 미구현). 서버가 전체이동 이벤트를 추가하면 그때 `sub_graph_id` 키 이벤트로 교체.

---

### 1-2. NODE_TEXT_UPDATE

노드 텍스트를 업데이트한다.

> **확정 (서버 코드 확인됨)**: payload 필드명은 `"text"` (`"node_text"` 아님)

```json
{
  "event_type": "NODE_TEXT_UPDATE",
  "payload": {
    "node_id": "uuid",
    "text": "새 텍스트"
  }
}
```

서버 처리: `node_text` DB 컬럼 업데이트 + 스냅샷 생성.  
제약: 빈 문자열 불가 (서버가 `[GRAPH400] node text must not be blank` 에러 반환).

Unity 클라이언트: `GraphManager.OnNodeTextUpdated` 이벤트에서 받은 텍스트를 그대로 `"text"` 키로 전송.

---

### 1-3. NODE_DELETE

노드를 soft delete한다.

> **확정 (서버 코드 확인됨)**: 서버는 **해당 노드와 연결된 엣지만** soft delete한다. **자식 노드는 cascade 삭제하지 않는다.**

```json
{
  "event_type": "NODE_DELETE",
  "payload": {
    "node_id": "uuid"
  }
}
```

서버 처리:
1. 지정 노드 soft delete (`deleted_at` 설정)
2. 해당 노드와 연결된 엣지(from 또는 to) soft delete
3. 스냅샷 생성

**Unity 클라이언트 대응 전략**:  
`GraphManager.RequestDeleteNode()` 내부에서 자식 노드들을 재귀 수집 후 각각 개별 삭제하고 있음.  
따라서 GraphSyncClient에서 `OnNodeDeleted` 이벤트가 여러 번 발생 → 각각 NODE_DELETE 이벤트를 개별 전송해야 함.

```
부모 삭제 요청
  → 자식A 삭제 → OnNodeDeleted("자식A") → NODE_DELETE 전송
  → 자식B 삭제 → OnNodeDeleted("자식B") → NODE_DELETE 전송
  → 부모 삭제  → OnNodeDeleted("부모")  → NODE_DELETE 전송
```

---

### 1-4. EDGE_CREATE

엣지를 생성한다.

> **확정 (서버 코드 확인됨)**: `label` 필드는 선택사항 (없으면 빈 문자열로 처리).

```json
{
  "event_type": "EDGE_CREATE",
  "payload": {
    "from_node_id": "uuid",
    "to_node_id": "uuid",
    "label": ""
  }
}
```

서버 처리: 엣지 생성 + 스냅샷 생성.  
제약:
- `from_node_id == to_node_id` 금지
- 두 노드가 같은 `sub_graph_id`에 속해야 함 (서버가 `[GRAPH409]` 반환)
- 이미 존재하는 엣지 재생성 시 `[GRAPH409]` 반환

---

### 1-5. EDGE_DELETE

엣지를 soft delete한다.

```json
{
  "event_type": "EDGE_DELETE",
  "payload": {
    "edge_id": "uuid"
  }
}
```

서버 처리: 엣지 soft delete + 스냅샷 생성.

---

### 미지원 이벤트 (서버 확인 필요)

| 이벤트 | 상태 | 비고 |
|--------|------|------|
| `NODE_CREATE` | **서버에 없음** | PROPERTY 노드 수동 생성 동기화 방법 미정 |

---

## 2. REST API (그래프 생성/조회)

### 2-1. 그래프 조회

서버 연동 시 초기 그래프 로드에 사용할 REST API.  
(실제 엔드포인트 경로는 서버팀 확인 필요)

```
GET /api/graph?room_id={room_id}
```

응답:

```json
{
  "room_id": "room_001",
  "graph_version": 1,
  "nodes": [
    {
      "node_id": "uuid",
      "type": "PROPERTY",
      "node_text": "미래지향 스타일",
      "position": [0.0, 0.0, 0.0],
      "parent_node_id": null,
      "sub_graph_id": "uuid"
    }
  ],
  "edges": [
    {
      "edge_id": "uuid",
      "from_node_id": "uuid",
      "to_node_id": "uuid",
      "label": ""
    }
  ]
}
```

Unity 처리: 응답 받으면 `GraphManager.LoadGraph(graphData)` → `GraphManager.RenderGraph()` 순서로 호출.

---

### 2-2. 2D Graph Generate

PROPERTY 또는 REFERENCE → PART 적용 연결을 `connections`로 전송해 그래프 기반 2D 이미지를 생성한다.

```
POST /api/2d/generate/graph
```

요청:

```json
{
  "room_id": "room_001",
  "user_id": "user_001",
  "connections": [
    {
      "part_node_id": "uuid-part-all",
      "node_id": "uuid-property-root"
    }
  ]
}
```

필드 의미:

| 필드 | 의미 |
|------|------|
| `room_id` | 대상 회의실 UUID |
| `user_id` | 생성 요청자 UUID |
| `part_node_id` | 적용 대상 PART 또는 ALL node_id |
| `node_id` | 적용할 PROPERTY 서브그래프의 root node_id 또는 REFERENCE node_id |

서버는 PROPERTY root에서 하위 체인을 탐색해 문맥을 구성한다. Unity는 사용자가 leaf에 드롭하더라도 `RequestConnectFromPort`와 `RegenerateConnectionBuilder`에서 root로 정규화한다.

Unity 처리: `Generate2DController.RequestGenerateGraph([selectedPartNodeIds])`.
결과 이미지는 WS `2D_GENERATED`로 수신한다.

---

## 3. Unity ↔ 서버 필드명 매핑

| Unity (C#) | 서버 DB / JSON | 비고 |
|------------|---------------|------|
| `NodeData.node_id` | `node_id` (UUID) | Unity는 string으로 처리 |
| `NodeData.type` | `type` ("PROPERTY" 등) | NodeType enum → 문자열 직렬화 |
| `NodeData.label` | — | 서버에 없는 필드. DisplayText 제공용 |
| `NodeData.node_text` | `node_text` | 서버 기준 텍스트 필드. WS 이벤트도 이 값 사용 |
| `NodeData.property_category` | `property_category` | 자유 문자열 |
| `NodeData.is_global` | `is_global` | ALL 여부 |
| `NodeData.position` | `position: [x,y,z]` | WS에서는 `{ x, y, z }` 딕셔너리 형태 |
| `EdgeData.edge_id` | `edge_id` (UUID) | |
| `EdgeData.from_node_id` | `from_node_id` | |
| `EdgeData.to_node_id` | `to_node_id` | |

> **label vs node_text**: 서버는 `node_text`만 사용한다. `label` 필드는 클라이언트 내부 displayText 제공용으로만 남긴다. 텍스트 변경 이벤트(OnNodeTextUpdated)와 WS 송신은 `node_text` 방향으로 통일한다.

---

## 4. 서버팀 확인 대기 항목

| # | 항목 | 현재 가정 |
|---|------|----------|
| 1 | NODE_CREATE 이벤트 유무 | 없음 (서버 코드에 없음). 수동 PROPERTY 생성 동기화 방법 미정 |
| 2 | NODE_TEXT_UPDATE payload 필드명 | **`"text"` 확정** (서버 코드 확인) |
| 3 | NODE_DELETE 자식 노드 cascade | **없음 확정** (서버는 해당 노드+연결엣지만 삭제) |
| 4 | EDGE_CREATE label 필수 여부 | **선택사항 확정** (빈 문자열 허용) |
| 5 | sub_graph_id 클라이언트 전달 방법 | **해결**: `NodeData.sub_graph_id` 추가(2026-07-01). 서버 값 저장, 없으면 레이아웃 루트 node_id로 백필. 전체 그래프 이동 대비 선구현 |
| 6 | WS 메시지 래핑 포맷 | **확정**: `{ "event_type": "...", "room_id": "<uuid>", "payload": {...} }`. `room_id` 필수(서버가 path와 일치 검증, 불일치 시 WS409). 경로 `/ws/rooms/{room_id}/event?user_id=` |
| 7 | 그래프 조회 REST 엔드포인트 경로 | **없음 확인**: `get_room_info`는 그래프 미포함, 접속 시 스냅샷도 안 내려옴. seed UUID 하드코딩으로 테스트 |
| 8 | 그래프 이벤트 broadcast/응답 | **없음 확인**: 그래프 이벤트는 DB 저장만, 되쏘지 않음. 성공 응답 없음(실패 시 ERROR만) |
| 9 | 2D 이미지 생성 | **배포 확인(2026-07-11)**: `/api/2d/generate/graph`·`/feature`. 결과는 WS `2D_GENERATED`{img_url}. PROPERTY root 하위 탐색은 서버팀 수정 필요 |

---

## 5. GraphSyncClient 구현 상태

`Assets/02_Scripts/Graph/GraphSyncClient.cs`

- **구현됨(2026-07-01)**: 실제 WS 연결 + 송신 3종(NODE_TEXT_UPDATE / NODE_DELETE / NODE_MOVE). 수신은 로그만(echo loop 방지).
- **WS 라이브러리**: 별도 패키지 추가 안 함. **Meta XR Voice SDK 번들** `Meta.Net.NativeWebSocket` 사용
  (`com.endel.nativewebsocket` 추가 시 GUID 충돌). 주의: Meta 포크는 `DispatchMessageQueue` 없음(async 연속으로 자동 dispatch),
  `OnMessage` 시그니처 `(byte[] data, int offset, int length)`.
- **테스트**: `Assets/02_Scripts/Graph/Dev/SeedGraphLoader.cs` 가 seed 실제 UUID 노드를 로드. ContextMenu로 왕복 검증.
- **미구현(TODO)**: 수신 이벤트의 GraphManager 반영, EDGE_CREATE/DELETE 송신(sub_graph_id 제약), NODE_CREATE(서버 부재),
  2D 이미지 수신(2D_GENERATED)→중앙 이미지, 그래프 로드 엔드포인트.
- node_id는 서버가 UUID로 기대 → 로컬 생성 노드(GUID)는 서버 DB에 없어 NODE404. seed UUID 노드만 서버 반영됨.
