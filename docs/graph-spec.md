# NodeXR 그래프 구조 명세 (개발자 3 기준)

## 1. 핵심 개념

중앙 2D 이미지는 노드가 아니다.

```
current_2d_asset_id = 현재 중앙에 표시 중인 2D 이미지 asset ID
```

그래프는 이 이미지를 수정하기 위한 **PART / PROPERTY / REFERENCE** 노드로만 구성된다.

---

## 2. 노드 타입

| 타입 | 설명 | 예시 |
|------|------|------|
| `PART` | 이미지에서 속성이 적용될 영역 | ALL, 몸통, 다리, 팔걸이 |
| `PROPERTY` | 디자인 속성 또는 속성의 하위 개념 | 미래지향 스타일, 식물, 베이지 |
| `REFERENCE` | 웹뷰/검색으로 가져온 참고 이미지 노드 | 금속 다리 참고 이미지 |

`ALL`은 전체 이미지에 적용되는 특수 PART이며, `is_global = true`로 표현한다.

> **ALL 유도 규칙 (2026-07-04 확정)**: 서버 Node 에는 `is_global` 컬럼이 **없다**. 서버는 PART 를
> 계층(루트 PART = 전체 대상 → 하위 PART = 영역)으로 저장한다. 클라는 **서버 루트 PART(`parent_node_id == null`)를 ALL 로 유도**하고, 하위 PART(`parent_node_id` 있음)는 일반 PartPort 로 표시한다.
> 백필은 서버-known PART 에만 적용하며(`GraphManager.BackfillIsGlobal`), 로컬 생성 PART 는 넘겨받은 `is_global` 값을 유지한다.
> 예) seed: `0001 청소 로봇(parent=NULL)` → ALL, `0002 집게 팔 / 0004 분리수거 통 / 0006 로봇 몸통` → PartPort.

```csharp
public enum NodeType { PART, PROPERTY, REFERENCE }
```

---

## 3. property_category

`PROPERTY` 노드는 `property_category` 필드를 가진다.

- **고정 enum이 아닌 자유 문자열(string)로 처리한다.**
- 서버 AI가 발화와 문맥을 보고 자동 생성한다.
- Unity에서는 그냥 `string`으로 저장하면 된다.

예시 값:

```
style, concept, motif, color, material, texture, function, mood, ...
```

---

## 4. 엣지 방향 규칙

엣지 타입(EdgeType)은 MVP에서 사용하지 않는다.  
`from_node_id → to_node_id` 방향과 노드 타입 조합으로 의미를 해석한다.

| from → to | 의미 | 소유·저장 |
|-----------|------|-----------|
| `PROPERTY → PROPERTY` | 속성 서브그래프의 세부화 | **서술** — 서버 소유(발화 생성) |
| `PART → PROPERTY` | 파트가 이 속성을 가짐(서버 계층). 파트에 발화 시 생성 | **서술** — 서버 소유 |
| `PART → PART` | 파트 분해(전체 대상 → 하위 파트) | **서술** — 서버 소유 |
| `PROPERTY → PART` | 연결된 PROPERTY(가장 하위 leaf 권장)를 기점으로 상위 체인을 파트 또는 ALL에 적용 | **적용** — 클라 소유(로컬 전용) |
| `REFERENCE → PROPERTY` | 레퍼런스가 특정 속성을 시각적으로 설명 | 서술 |
| `REFERENCE → PART` | 레퍼런스를 파트 또는 ALL에 직접 적용 | **적용** — 클라 소유(로컬 전용) |

> **서술(describe) vs 적용(apply) — 방향으로 구분 (2026-07-04 확정)**  
> - **서술** = 서버가 발화로 만드는 그래프 엣지(`to`가 PROPERTY/PART 자식). 서버 그래프에 영속되고 `MergeServerGraph`(`AddServerEdge`)로 병합.  
> - **적용** = 사용자가 만드는 연결. **제스처**: PART/ALL 포트에서 **더블클릭 → 엣지가 뻗어나와 → 서브그래프 맨 하위 leaf 에 드롭**(시작=PART, 끝=leaf). **데이터**: 저장 방향은 반대로 `from=PROPERTY(leaf) → to=PART`. `RequestConnectFromPort(port, subgraph)` 가 `RequestConnectNodes(subgraph, port)` 로 정규화해 저장.  
> - 두 관계는 **`to == PART` 인지로 갈린다** → `RegenerateConnectionBuilder` 는 적용 엣지만 골라 `connection[{part_node_id, node_id}]` 로 변환. 서버 서술 엣지(`PART→PROPERTY`/`PART→PART`)는 자동 제외(충돌 없음).  
> - **적용 엣지는 서버에 영속하지 않는다(MVP, 2026-07-04 결정)**. `GraphSyncClient` 는 `EDGE_CREATE` 미전송(서버 cross-subgraph 제약 GRAPH409). 실시간 공유는 Photon, 2D 결과물(asset)은 서버 영속. **업그레이드 경로**: 나중에 서버가 연결 영속 API 를 붙이면 동일한 `connection[{part_node_id, node_id}]` 배열을 그대로 영속 페이로드로 재사용.

예시:

```
미래지향 스타일 → 유토피아 → 식물   (서브그래프 체인)
식물 → ALL                         (가장 하위 leaf PROPERTY가 PART에 연결)

금속 → 무광                         (서브그래프 체인)
무광 → 다리                         (가장 하위 leaf PROPERTY가 PART에 연결)
```

의미: 전체 이미지에 미래지향 스타일 체인을 적용한다. 사용자는 서브그래프의 가장 하위 노드(leaf, 예: 식물)를 PART에 연결하고, 서버는 leaf인 식물에서 유토피아, 미래지향 스타일 순으로 상위 체인을 거슬러 올라가며 탐색해 문맥을 구성한다.

---

## 5. C# 데이터 구조

### NodeData

```csharp
[Serializable]
public class NodeData
{
    public string node_id;
    public NodeType type;
    public string label;
    public Vector3 position;

    // PROPERTY 전용
    public string property_category;  // 자유 문자열, null 가능

    // PART 전용
    public bool is_global;            // ALL이면 true

    // 서브그래프(PROPERTY 트리) 식별자. 서버 sub_graph_id 저장용.
    // 서버가 안 주면 GraphManager가 레이아웃 루트 node_id로 백필. 전체 그래프 이동에 사용.
    public string sub_graph_id;
}
```

### EdgeData

```csharp
[Serializable]
public class EdgeData
{
    public string edge_id;
    public string from_node_id;
    public string to_node_id;
}
```

### GraphData

```csharp
[Serializable]
public class GraphData
{
    public string room_id;
    public List<NodeData> nodes;
    public List<EdgeData> edges;
}
```

---

## 6. 연결 가능 규칙 (CanConnect)

```
허용:
  PROPERTY → PROPERTY  (O)
  PROPERTY → PART      (O)
  REFERENCE → PROPERTY (O)
  REFERENCE → PART     (O)

금지:
  PART → 어디든         (X)
  PROPERTY → REFERENCE  (X)
  같은 노드끼리          (X)
  이미 존재하는 엣지      (X)
```

---

## 7. RegenerateConnectionBuilder

2D **부분 재생성**용. 그래프에서 PROPERTY 또는 REFERENCE가 PART로 직접 연결된 엣지를 찾아 connection 배열로 변환한다.
협의(2026-07-01): 선택된 연결/active 속성 기반 부분 재생성 → `connection = [{ part_node_id, node_id }]` 전송.
**서버 2D generate 엔드포인트는 현재 미구현(주석)** → 이 빌더는 계약 대비 **Unity 선구현**.
구현: `RegenerateConnectionBuilder.Build(graph[, selectedPartNodeIds])` / `BuildRequest(graph, assetId[, selected])` / `BuildRequestJson(...)`,
또는 `GraphManager.BuildRegenerateRequestJson(assetId[, selected])`. `selectedPartNodeIds` 지정 시 해당 PART로 향하는 연결만(부분 재생성).

```csharp
public class ConnectionDto
{
    public string part_node_id;
    public string node_id;
}
```

변환 로직:

```
EdgeData를 순회하면서:
  - to_node_id가 PART 타입인 엣지를 찾는다.
  - from_node_id가 PROPERTY 또는 REFERENCE인지 확인한다.
  - 조건을 만족하면 ConnectionDto { part_node_id = to, node_id = from } 생성.

node_id 해석:
  - node_id가 PROPERTY이면 해당 PROPERTY는 연결된 서브그래프의 기점(권장: 가장 하위 leaf)이다.
    서버는 node_id에서 부모 PROPERTY를 따라 상위 체인을 거슬러 올라가며 탐색해 문맥을 구성한다.
    (각 PROPERTY의 부모는 최대 1개이므로 상위 경로는 유일하다.)
  - node_id가 REFERENCE이면 해당 레퍼런스를 바로 적용한다.
```

---

## 8. Mock GraphData 예시 (렌더링 테스트용)

```json
{
  "room_id": "room_001",
  "nodes": [
    { "node_id": "part_all",      "type": "PART",     "label": "ALL",       "is_global": true },
    { "node_id": "part_body",     "type": "PART",     "label": "몸통" },
    { "node_id": "prop_future",   "type": "PROPERTY", "label": "미래지향 스타일", "property_category": "style" },
    { "node_id": "prop_utopia",   "type": "PROPERTY", "label": "유토피아",    "property_category": "concept" },
    { "node_id": "prop_plant",    "type": "PROPERTY", "label": "식물",        "property_category": "motif" },
    { "node_id": "ref_metal_leg", "type": "REFERENCE","label": "금속 다리 참고 이미지" }
  ],
  "edges": [
    { "edge_id": "e1", "from_node_id": "prop_future",   "to_node_id": "prop_utopia" },
    { "edge_id": "e2", "from_node_id": "prop_utopia",   "to_node_id": "prop_plant" },
    { "edge_id": "e3", "from_node_id": "prop_plant",    "to_node_id": "part_all" },
    { "edge_id": "e4", "from_node_id": "ref_metal_leg", "to_node_id": "part_body" }
  ]
}
```

e3은 가장 하위 leaf인 `prop_plant`(식물)가 `part_all`에 연결된 적용 엣지입니다. 서버는 `prop_plant`에서 상위 체인인 `prop_utopia → prop_future`를 거슬러 올라가며 탐색해 문맥을 구성합니다.

---

## 9. GraphManager 인터페이스 (개발자 2 / UI 와의 경계)

개발자 2와 UI 계층은 아래 메서드만 호출한다. GraphData를 직접 건드리지 않는다.

```csharp
// 조작 (Request*)
GraphManager.RequestSelectNode(nodeId);
GraphManager.RequestMoveNode(nodeId, position);
GraphManager.RequestCreateNode(parentNodeId, text);
GraphManager.RequestCreatePartNode(label, isGlobal);        // 메인 그래프 UI 전용 PART 생성
GraphManager.RequestConnectNodes(fromNodeId, toNodeId);
GraphManager.RequestConnectFromPort(portNodeId, subgraphNodeId);  // 메인 포트(시작)→서브그래프(드롭) 드래그 연결. 데이터는 서브그래프→PART 로 저장
GraphManager.RequestMoveSubgraphByMember(memberNodeId, delta);    // 전체 그래프(서브그래프 강체) 이동. 멤버 노드 하나로 소속 서브그래프를 delta만큼 이동
GraphManager.RequestMoveSubgraph(subGraphId, delta);              // 같은 sub_graph_id 노드 전체를 delta만큼 이동
GraphManager.RequestDeleteNode(nodeId);                     // 노드 + 연결 엣지 모두 삭제
GraphManager.RequestDeleteEdge(edgeId);                     // 엣지만 삭제 (노드 유지)
GraphManager.RequestRenamePartNode(nodeId, newLabel);       // PART 노드 라벨 변경

// 조회 (read-only)
GraphManager.GetNode(nodeId);
GraphManager.GetAllNodes();
GraphManager.GetEdgesConnectedToNode(nodeId);               // from 또는 to가 nodeId인 전체 엣지
GraphManager.GetEdgesIncomingToNode(nodeId);                // to_node_id == nodeId인 엣지만 반환
```

GraphManager 내부에서:
- `Request*()` → GraphData 수정 → NodeView/EdgeView 갱신 → (필요시) 서버 동기화
- `Get*()` → Registry의 복사본 List를 반환한다. Registry 자체는 외부에 노출하지 않는다.
- `RenderGraph(GraphData)` → 서버 응답을 Unity에 반영
- `RequestCreatePartNode`는 현재 클라이언트에서 GUID를 생성해 즉시 `AddNode`로 등록한다.
  서버 연동 시 `POST /api/nodes` 응답 node_id로 교체 예정 (`@docs/server-api-alignment.md` 참고).
- `RequestDeleteEdge`는 AllPort Pressed 상태에서 연결 엣지만 제거할 때 사용한다. 노드는 유지.
- `RequestRenamePartNode`는 PartPort Pressed 상태에서 인라인 rename 시 사용한다.
  서버 연동 시 `PATCH /api/nodes/{node_id}` 예정 (`@docs/server-api-alignment.md` 참고).

---

## 10. 개발 순서

```
1. Test_SH 씬에서 GraphRoot 오브젝트 구성
2. PART / PROPERTY / REFERENCE 프리팹 제작 (01_Prefabs/Graph/)
3. Edge 프리팹 제작
4. NodeType enum 정의
5. NodeData / EdgeData / GraphData 클래스 작성
6. NodeView / EdgeView 데이터 바인딩
7. GraphManager.RenderGraph() 구현
8. Mock GraphData로 테스트 렌더링
9. CanConnect() 연결 규칙 구현
10. PROPERTY → PART 적용 엣지 처리
11. RegenerateConnectionBuilder 구현
12. 서버 GET /api/graph 연결
13. 서버 POST /edges, DELETE /edges, DELETE /nodes 연결
14. /2d/regenerate 요청 데이터 생성
15. MeetingRoom_GraphContent 씬으로 이전
```

---

## 11. 그래프 UI 영역 분리

같은 `GraphData`를 두 개의 별개 UI가 읽어 표시한다. 서로의 GameObject·컴포넌트를 직접 참조하지 않고, `GraphManager`의 조회 메서드(`GetAllNodes`, `GetEdgesConnectedToNode`)로만 상태를 가져온다.

### 11.1 메인 그래프 UI (`Assets/02_Scripts/Graph/Main/`)

- 형태: World Space Canvas 기반 2D 패널 (`MainSketchPanel.prefab`)
- 구성: 가운데 2D 스케치 영역(`SketchImage`) + 위쪽 `AllPort` + 아래쪽 `PartPortContainer`(채워진 `PartPort`들 + 끝의 `AddPartPort`) + 우측 `HistoryDots`
- 대상 노드: `PART`만 표시 — `is_global=true`인 ALL 1개 + `is_global=false`인 일반 PART들
- 책임: PROPERTY 서브그래프가 어떤 PART/ALL에 적용되는지를 시각화하는 진입점
- 부분 갱신: `MainSketchView.Refresh()` 시 기존 `PartPort`/`AddPartPort` 인스턴스를 재사용하고, 사라진 PART의 인스턴스만 Destroy한다(깜빡임 최소화).

#### AllPort 상태 명세 (스프라이트: `Assets/05_Design/JW/UI/Sprites/Joint/JointAll/`)

| 상태 | 스프라이트 | 조건 | 동작 |
|------|-----------|------|------|
| Empty | `joint_all_Empty-3.png` | 연결 없음 (default) | — |
| Hover | `joint_all_Hover.png` | 마우스 올림 | — |
| Connected | `joint_all_Connected.png` | incoming 엣지 존재 | — |
| Pressed | `joint_all_Pressed.png` | **누르는 동안만** (`_isHeldDown`) | 연결 엣지 목록 표시 (토글) |

- 스프라이트 우선순위: `_isHeldDown` > hover > connected > empty
- `_isExpanded`(토글)는 스프라이트와 무관 — 연결 목록 표시/숨김만 제어
- Pressed(클릭) 후 expanded 상태: 연결된 PROPERTY/REFERENCE 노드별 X 버튼 표시
  - X 클릭 → `RequestDeleteEdge(edgeId)` (노드 유지, 엣지만 삭제)
  - `GetEdgesIncomingToNode(allNodeId)`로 incoming 엣지만 조회

#### PartPort 상태 명세 (스프라이트: `Assets/05_Design/JW/UI/Sprites/Joint/JointPart/`)

| 상태 | 스프라이트 | 조건 | 동작 |
|------|-----------|------|------|
| Empty | `joint_part_Empty.png` | PART 생성됨, 연결 없음 | 라벨 표시 |
| Hover | `joint_part_Hover.png` | 마우스 올림 | — |
| Connected | `joint_part_Connected.png` | incoming 엣지 존재 | 라벨 표시 |
| Pressed | `joint_part_Pressed.png` | 클릭 토글(`_isExpanded`) 동안 유지 | 위 X(삭제) + 아래 rename field |

- 스프라이트 우선순위: `_isExpanded` > hover > connected > empty
- AllPort와 달리 `_isExpanded` 동안 pressedSprite를 계속 유지
- Pressed 상태: `_deleteButton` 활성 → `RequestDeleteNode(nodeId)`
- Pressed 상태: `_renameField` 활성 → Enter → `RequestRenamePartNode(nodeId, label)` + Collapse

#### AddPartPort 상태 명세 (스프라이트: `Assets/05_Design/JW/UI/Sprites/Joint/JointPart/`)

| 상태 | 스프라이트 | 조건 |
|------|-----------|------|
| Add (default) | `joint_part_Add.png` | 초기 상태 (+가 있는 원) |
| Hover | `joint_part_Hover.png` | 마우스 올림 |
| Inputting | `joint_part_Empty.png` | 클릭 후 이름 입력 중 |

- 클릭 → `_isInputting=true` → InputField 활성화 (placeholder: "(PartName)")
- Enter → `RequestCreatePartNode(label, false)` → `Refresh()`
- PART 생성: `AddPartPort` 클릭 → 이름 입력 후 Enter → `PartPort`가 즉시 라벨 표시와 함께 생성

### 11.2 서브그래프 UI (`Assets/02_Scripts/Graph/`, `Assets/02_Scripts/Graph/UI/`)

- 형태: 3D 월드 공간 (`NodeView`, `EdgeView`, `NodeActionPanel`)
- 구성: NodeBox 메시(PART/PROPERTY/REFERENCE 공용, 머티리얼로 구분) + 측면 ConnectorSphere 포트 앵커 + LineRenderer 엣지 + 컨텍스트 패널(Add/Delete)
- 대상 노드: 전체 (PART / PROPERTY / REFERENCE)
- 책임: PROPERTY 노드 간 세부화 체인과 PROPERTY → PART 적용 엣지를 3D로 보여준다
- 머티리얼 매핑: PART(is_global=true=ALL) → BOX_00, PART → BOX_01, PROPERTY → BOX_02, REFERENCE → BOX_03

두 UI는 같은 씬에 공존할 수도, 분리될 수도 있다. 메인 그래프 UI의 PART/ALL 포트가 PROPERTY 서브그래프 진입점 역할을 하지만, 실제 진입 인터랙션은 후속 작업으로 분리된다.
