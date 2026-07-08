# 서버 API 정렬 기준 (개발자 3 기준)

마지막 업데이트: 2026-06-24  
서버 코드 기준: `/Users/imsohyun/Desktop/nodexr-server`

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

### 2-2. 2D Regenerate

PROPERTY 또는 REFERENCE → PART 적용 시 **부분 재생성** 요청. 선택된 연결/active 속성 기반.

> **상태(2026-07-01)**: 협의된 계약이나 **서버 미구현**. 실제 서버 `generation.py`의 엔드포인트는 전부 주석,
> 활성 스키마는 `Generate2DRequest = {room_id}` 뿐. 아래 connection 배열 계약은 서버 구현 대기 →
> Unity는 `RegenerateConnectionBuilder`로 **선구현**해 둠(서버 붙으면 바로 POST).

```
POST /api/2d/regenerate
```

요청:

```json
{
  "room_id": "room_001",
  "asset_id": "current_2d_asset_id",
  "connection": [
    {
      "part_node_id": "uuid-part-all",
      "node_id": "uuid-prop-plant"
    }
  ]
}
```

필드 의미:

| 필드 | 의미 |
|------|------|
| `asset_id` | 현재 중앙 이미지 ID |
| `part_node_id` | 적용 대상 PART 또는 ALL node_id |
| `node_id` | 적용할 PROPERTY 서브그래프의 기점(권장: 가장 하위 leaf) node_id 또는 REFERENCE node_id |

서버 동작: `node_id`가 PROPERTY이면 사용자가 PART에 연결한 서브그래프 기점(가장 하위 leaf)이다. 서버가 `node_id`에서 부모 PROPERTY를 따라 상위 체인을 거슬러 올라가며 탐색해 문맥을 구성한다. (각 PROPERTY의 부모는 최대 1개이므로 상위 경로는 유일하다.)

Unity 처리: `GraphManager.BuildRegenerateRequestJson(assetId[, selectedPartNodeIds])`
(내부적으로 `RegenerateConnectionBuilder.Build`) → JSON 생성 → (서버 구현 후) POST.
`selectedPartNodeIds` 지정 시 해당 PART로 향하는 연결만(부분 재생성).

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
| 9 | 2D 이미지 생성 | **서버 미구현 확인**: `/api/2d/generate` 주석 처리 + `Image2DGenerationService`는 print 스텁. `Generate2DRequest`={room_id}. 결과는 WS `2D_GENERATED`{img_url} |

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
