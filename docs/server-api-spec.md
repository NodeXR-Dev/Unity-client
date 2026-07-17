# 서버 API 명세 (개발자 3 그래프 도메인 발췌)

출처: 서버팀(정소은) 전체 API 명세, 수령 2026-07-10.
이 문서는 **노드그래프/GraphData/서버 반영** 담당(개발자 3)에 직접 관련된 엔드포인트만 발췌·정리한다.
룸/기능(feature)/3D/색상변경/가이드 등 타 담당 영역은 원본 명세 참조.

공통 응답 봉투: `{ isSuccess, code, message, result }`. WS ACK는 요청자에게만 전송.

---

## WebSocket `/ws/rooms/event`

봉투: `{ event_type, room_id, user_id, payload }`

### NODE_CREATE (키보드 직접 생성 — PROPERTY 전용)
```
payload: { job_id, sub_graph_id(루트 노드일 때만, 아니면 null), node_text, parent_node_id, position:[x,y,z] }
ACK    : result: { job_id, node_id }
```
- **node_type 없음** — 키보드 NODE_CREATE 는 PROPERTY 전용(PART 는 `/api/part_node/generate/keyboard`).
- **루트**: `parent_node_id=null` + `sub_graph_id`(먼저 `/api/sub_graph/generate` 로 발급). 자식: `parent_node_id` 채우고 `sub_graph_id=null`(서버가 부모에서 유도).

### EDGE_CREATE
```
payload: { job_id, from_node_id, to_node_id }   # label 없음
ACK    : result: { job_id, edge_id }
```
- 로컬 GraphData는 `PROPERTY(root)/REFERENCE → PART`. 현재 서버 저장은 반대 방향이므로 Unity WS 경계에서 `PART → PROPERTY/REFERENCE`로 변환한다.

### EDGE_DELETE   `payload: { edge_id }`
### NODE_MOVE     `payload: { node_id, position:[x,y,z] }`
### NODE_DELETE   `payload: { node_id }`  (서버가 연결 엣지·자식까지 삭제)
### NODE_TEXT_UPDATE `payload: { node_id, text }`
### UTTERANCE_CREATE `payload: { utterance }`  (지속 발화 스트림)

### 수신 전용(서버→클라)
- **GRAPH_UPDATED** `payload: { graph: {graph_version, core_2d_image, sub_graphs:[...]} }` — semantic update 시 전체 그래프 push.
- **2D_GENERATED** `payload: { asset_id, mime_type, width, height, img_url }`
- **3D_GENERATED** `payload: { asset_id, mime_type, model_url }`
- **AGENT_GUIDE** `payload: { guide_id, guide_type, message, evidence{...} }` — 발화 가이드.

---

## REST — 서브그래프 / 노드

### 서브그래프 생성   `POST /api/sub_graph/generate`
```
req: { room_id }
res: SUB_GRAPH200  result: { room_id, sub_graph_id }
```
→ 키보드 "+"(루트 PROPERTY) 생성 시 먼저 호출해 sub_graph_id 확보 후 WS NODE_CREATE.

### 노드 생성(발화)   `POST /api/node/generate/utterance`   ⚠️ URL 변경(구 `/api/utterances`)
```
req: { room_id, user_id, node_type, parent_node_id, utterance, position:[x,y,z] }
res: NODE200  result: { node_id, node_text }
```
→ 응답이 **전체 그래프가 아니라 { node_id, node_text }** 로 바뀜(구 MergeServerGraph 방식 재검토 필요).

### 그래프 조회   `GET /api/graph`
```
res: GRAPH200  result: { room_id, graph_version, core_2d_image,
        sub_graphs:[ { sub_graph_id, root_node_id, nodes:[...], edges:[...] } ] }
node: { node_id, type, node_text, position:[x,y,z], parent_node_id, used_in_generation, data }
edge: { edge_id, from_node_id, to_node_id, label, used_in_generation }
```
→ 콜드로드 가능해짐. **sub_graphs 중첩 구조** → 클라 GraphData(flat nodes/edges)로 평탄화 필요.

### 그래프 히스토리   `GET /api/history/{room_id}` — result.history[]에 위 그래프 구조 배열.

---

## REST — PART 노드 (WS 아님, REST 동기화)

- 생성(키보드) `POST /api/part_node/generate/keyboard`  req `{ room_id, text, position }`  res `{ room_id, part_node_id, part_node_text }`
- 생성(발화)   `POST /api/part_node/generate`           req `{ room_id, utterance, position }`  res 동일
- 수정         `PATCH /api/part_node/modify`            req `{ room_id, part_node_id, part_node_text }`
- 삭제         `DELETE /api/part_node/delete`           req `{ room_id, part_node_id }`

---

## REST — 레퍼런스 (R 버튼 / REFERENCE 노드)

- 연결   `POST /api/references/generate` (multipart) req `{ room_id, file(BIN), node_id, metadata:{mime_type,width,height} }` res `{ room_id, node_id, reference_url }`
- 키워드 `POST /api/references/keyword`  req `{ room_id, node_id }`  res `{ room_id, node_id, reference_keyword }` (부모 노드 체인 반영)

---

## REST — 2D 스케치

- 그래프 기반 `POST /api/2d/generate/graph`   req `{ room_id, user_id, connections:[{part_node_id, node_id(root)}] }`
- 요구사항 기반 `POST /api/2d/generate/feature` req `{ room_id, user_id }`
- 색상 변경   `POST /api/2d/color_change` (multipart)
- 완료 알림은 WS `2D_GENERATED`.

---

## 주요 델타 (현재 클라 코드 대비) — 클라 반영 완료(2026-07-10)

> 클라 코드는 전부 반영됨. 남은 것은 **서버 엔드포인트 배포**(sub_graph/generate, /api/graph, node/generate/utterance는 현재 미배포 확인). 배포되면 클라 변경 없이 동작.

| 항목 | 명세 | 클라 반영 | 서버 |
|------|------|-----------|------|
| NODE_CREATE payload | node_type 없음, 루트에 sub_graph_id | ✅ node_type 제거, sub_graph_id(루트) | — |
| 루트 생성 | sub_graph/generate → NODE_CREATE(sub_graph_id) | ✅ `SubGraphApiClient` 신설 | ⏳ 미배포 |
| EDGE_CREATE payload | label 없음 | (유지, label 무해) | — |
| 발화 노드 생성 | `/api/node/generate/utterance`, 응답 {node_id,node_text} | ✅ `UtteranceApiClient` 개편(rekey) | ⏳ 미배포 |
| 그래프 콜드로드 | `GET /api/graph`(sub_graphs 중첩) | ✅ `GraphLoadApiClient`+`GraphQueryDto` 평탄화 | ⏳ 미배포 |
| GRAPH_UPDATED 수신 | 전체 그래프 push | ✅ `GraphSyncClient.HandleGraphUpdated`(GraphSnapshotDto 재사용) | 수신 전용 |
| used_in_generation | 노드/엣지에 존재 | ✅ `NodeData`/`EdgeData` 필드 추가 | — |
