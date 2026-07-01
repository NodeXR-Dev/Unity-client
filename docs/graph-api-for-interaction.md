# GraphManager API — 인터랙션 담당자용

> 작성: 개발자 3 | 마지막 수정: 2026-07-02

---

## 기본 약속

| 규칙 | 이유 |
|------|------|
| `GraphData` / `NodeData` / `EdgeData` 직접 수정 금지 | Registry 동기화가 깨짐 |
| `NodeRegistry` / `EdgeRegistry` 직접 접근 금지 | 내부 구현 세부사항 |
| `AddNode()` / `RemoveNode()` / `AddEdge()` / `RemoveEdge()` 직접 호출 금지 | 저수준 내부 메서드 |
| **데이터 변경은 `Request*()` 메서드만 호출** | 레이아웃 Reflow, 서버 동기화가 자동 처리됨 |
| 선택 상태(`SelectedNodeId`)는 Interaction 쪽에서 관리 | GraphManager는 데이터/구조만 담당 |
| UI 알림(Toast, 패널 등)은 Interaction/UI 쪽에서 처리 | GraphManager는 성공/실패 값만 반환 |

---

## 노드 생성 — 두 개의 `+` 버튼과 발화 흐름

서브그래프 노드는 **두 가지 `+` 버튼**으로 만든다. 둘 다 **빈 노드를 먼저 생성**하고, 이후 노드 텍스트칸 입력이 **발화(서버 전송)** 트리거다.

| `+` 버튼 | 위치 | 호출 메서드 | 결과 |
|----------|------|-------------|------|
| **키보드 `+`** | 화면 키보드 (빈 상태, 그래프에 노드 0개) | `RequestCreateRootPropertyNode()` | 부모 없는 **서브그래프 첫 root** PROPERTY 노드 |
| **노드 `+`** | 각 노드에 붙은 `+` (X/R과 함께) | `RequestCreatePropertyNode(parentId)` | 그 노드의 **연결된 자식** PROPERTY 노드 |

생성 이후 흐름은 root/자식 **동일**하다:

```
+ 버튼 클릭 → 빈 PROPERTY 노드 생성 (라벨 없음)
    ↓
노드 텍스트칸(LabelInput)에 키보드로 입력 → 제출(Enter)
    ↓
NodeView → GraphManager.RequestNodeByUtterance(nodeId, text)
    ├─ 텍스트를 노드에 로컬 반영 (오프라인·즉시)
    └─ OnUtteranceNodeRequested 이벤트 발행
         ↓
    UtteranceApiClient → POST /api/utterances (parent=이 노드)
         ↓
    서버 응답 그래프 → GraphManager.MergeServerGraph()로 하위 노드 병합
```

> **개발자 2 담당:** 키보드 `+` 버튼 UI를 만들어 `RequestCreateRootPropertyNode()`에 연결. (노드 `+`/X/R은 NodeActionPanel에 이미 배선됨)
> **로컬 node_id 주의:** `+`로 만든 노드는 로컬 GUID다. 텍스트 입력(발화) 시 utterance로 서버에 전송되어 서버가 하위 그래프를 발급/병합한다. 발화 전 로컬 노드는 서버가 아직 모른다.

---

## 선택 상태 관리 패턴

GraphManager는 "현재 선택된 노드"를 관리하지 않는다.  
Interaction 쪽에서 `SelectedNodeId`를 직접 들고 있다가, 사용자 행동이 발생하면 GraphManager를 호출한다.

```csharp
// Interaction 쪽 예시
string selectedNodeId = null;

void OnNodeRaySelected(string nodeId)
{
    selectedNodeId = nodeId;
    // 하이라이트 등 비주얼은 Interaction에서 직접 처리
}

void OnAddButtonPressed()
{
    if (selectedNodeId == null) return;
    string newId = graphManager.RequestCreatePropertyNode(selectedNodeId);
    if (newId == null)
        ShowToast("자식 노드를 추가할 수 없습니다.");
}
```

---

## Request* API — 데이터 변경용

### `RequestCreateRootPropertyNode()` → `string`

빈 상태에서 부모 없는 **서브그래프 첫 root PROPERTY 노드**를 생성한다.  
`RequestCreatePropertyNode`와 대칭이지만 부모/엣지가 없고, `sub_graph_id`는 자기 자신이 된다.  
ReflowAllSubtrees()가 자동 호출된다. 생성 후 텍스트 입력 흐름은 자식 노드와 동일(→ `RequestNodeByUtterance`).

| | |
|--|--|
| **호출 시점** | **키보드 `+` 버튼**(그래프에 노드 0개일 때 첫 노드 생성) |
| **반환값** | 생성된 `node_id` (string) / 실패 시 `null` |

```csharp
string rootId = graphManager.RequestCreateRootPropertyNode();
if (rootId == null)
    ShowToast("노드 생성 실패");
// 이후: 새 노드 텍스트칸 입력 → RequestNodeByUtterance 로 발화 전송
```

---

### `RequestCreatePropertyNode(parentId)` → `string`

PROPERTY 노드 아래 자식 PROPERTY 노드를 생성하고 엣지로 연결한다.  
ReflowAllSubtrees()가 자동으로 호출되어 오른쪽 방향으로 재배치된다.

| | |
|--|--|
| **호출 시점** | **노드에 붙은 `+` 버튼**(NodeActionPanel) — 기존 노드에 연결된 자식 추가 |
| **반환값** | 생성된 `node_id` (string) / 실패 시 `null` |

```csharp
string newNodeId = graphManager.RequestCreatePropertyNode(selectedNodeId);
if (newNodeId == null)
    ShowToast("자식 노드 생성 실패");
```

---

### `RequestCreatePartNode(label, isGlobal)` → `string`

메인 그래프에 PART 노드를 생성한다.  
`isGlobal = true`이면 ALL 파트로 취급한다.

| | |
|--|--|
| **호출 시점** | AddPartPort의 + 버튼 완료 시 |
| **반환값** | 생성된 `node_id` / 실패 시 `null` |

```csharp
string partId = graphManager.RequestCreatePartNode("다리", isGlobal: false);
string allId  = graphManager.RequestCreatePartNode("ALL",  isGlobal: true);
```

---

### `RequestNodeByUtterance(nodeId, utterance)` → `void`

노드 텍스트칸(LabelInput) 제출 처리. **발화 서버 전송의 진입점.**  
입력 텍스트를 노드에 로컬 반영(오프라인·즉시)하고, `OnUtteranceNodeRequested` 이벤트를 발행한다.  
이 이벤트를 `UtteranceApiClient`가 구독해 `POST /api/utterances`(parent=이 노드)로 서버에 보내고, 응답 그래프를 `MergeServerGraph`로 병합한다. WS는 발행하지 않는다(생성은 REST 경로).

| | |
|--|--|
| **호출 시점** | `+`로 만든 노드의 텍스트칸에 입력 후 제출(Enter). root/자식 공통. **NodeView가 이미 호출** — 개발자 2가 직접 부를 일은 없음 |
| **반환값** | 없음(void). 로컬 반영은 항상 성공, 서버 전송 결과는 `UtteranceApiClient` 로그로 확인 |

```csharp
// NodeView 내부에서 자동 호출됨 (참고용)
_manager.RequestNodeByUtterance(_data.node_id, newText);
```

> **서버 미배포 시:** `/api/utterances` 404여도 로컬 텍스트 반영은 유지된다(오프라인 동작). 하위 그래프 자동 확장만 서버 배포 후 동작.

---

### `RequestConnectNodes(fromNodeId, toNodeId)` → `bool`

두 노드를 엣지로 연결한다. 연결 가능 여부는 내부에서 `CanConnect()`로 검증한다.

| 조합 | 의미 |
|------|------|
| PROPERTY → PROPERTY | 속성 서브그래프 세부화 |
| PROPERTY → PART | 서브그래프를 메인 파트에 적용 |
| REFERENCE → PROPERTY | 레퍼런스가 속성을 시각적으로 설명 |
| REFERENCE → PART | 레퍼런스를 파트에 직접 적용 |

| | |
|--|--|
| **호출 시점** | 저수준 API. 메인↔서브 드래그 연결에는 아래 `RequestConnectFromPort` 사용 권장 |
| **반환값** | `true` 성공 / `false` 실패 (이유는 `CanConnect()`로 사전 확인 가능) |

```csharp
// 연결 가능 여부를 먼저 확인하고 싶을 때
if (!graphManager.CanConnect(fromId, toId, out string reason))
{
    ShowToast(reason);
    return;
}
graphManager.RequestConnectNodes(fromId, toId);
```

---

### `RequestConnectFromPort(portNodeId, subgraphNodeId)` → `bool`

**메인 그래프 포트(ALL/PART) → 서브그래프 노드** 드래그 연결 전용 어댑터.
`RequestConnectNodes`를 감싸 방향을 자동 정규화하므로, 개발자 2는 인자 순서만 지키면 된다.

**확정된 연결 UX (2026-07-01 회의):**
- 제스처 방향은 **항상 메인 포트에서 시작** → 서브그래프로 드롭. (반대 방향 없음)
- 시작 트리거: 메인 그래프 엣지 연결부(ALL/PART 포트)를 **더블클릭**. Meta Quest 빌드이므로 핸드트래킹/컨트롤러의 더블 select로 감지.
- 드롭 대상: 연결하려는 서브그래프의 **가장 하위 노드(leaf)**. (네모박스 양옆 연결부)
- 데이터는 제스처와 반대로 항상 `서브그래프(PROPERTY/REFERENCE) → PART`로 저장된다. 이 헬퍼가 순서를 뒤집어 준다.

| 인자 | 의미 |
|------|------|
| `portNodeId` | 더블클릭으로 **시작**한 메인 포트의 node_id (`AllPort.NodeId` / `PartPort.NodeId`) |
| `subgraphNodeId` | 커서를 따라간 임시 엣지를 **드롭**한 서브그래프 노드의 node_id (권장: leaf) |

| | |
|--|--|
| **반환값** | `true` 성공 / `false` 실패 (인자를 뒤바꿔 넘겨도 `CanConnect`가 막아 안전하게 실패) |

```csharp
// 드롭 성공 시 (제스처 방향 그대로 넘긴다)
string portId = startPort.NodeId;          // 더블클릭으로 시작한 ALL/PART 포트
string leafId = droppedSubgraphNode.NodeId; // 임시 엣지를 놓은 서브그래프 leaf

if (!graphManager.RequestConnectFromPort(portId, leafId))
    ShowToast("연결할 수 없는 조합입니다.");

// hover 미리보기(연결 가능 여부 색 표시)는 순서 주의: 데이터 방향으로 확인
bool canDrop = graphManager.CanConnect(leafId, portId, out string reason);
```

**담당 경계:** 더블클릭 감지, 임시 엣지의 커서 추적, 드롭 대상 hit-test, hover 미리보기는 **개발자 2(Interaction)**. 이 헬퍼 호출 이후의 엣지 데이터/View/서버 반영은 **개발자 3**.

---

### `RequestDeleteNode(nodeId)` → `bool`

노드와 모든 하위 PROPERTY, 종속된 REFERENCE를 캐스케이드 삭제한다.  
Reflow가 자동으로 호출된다.

| | |
|--|--|
| **호출 시점** | X 버튼 클릭 |
| **반환값** | `true` 성공 / `false` 존재하지 않는 노드 |

```csharp
bool ok = graphManager.RequestDeleteNode(selectedNodeId);
if (!ok)
    ShowToast("노드를 찾을 수 없습니다.");
```

---

### `RequestDeleteEdge(edgeId)` → `bool`

엣지만 삭제한다. 양쪽 노드는 유지된다.

| | |
|--|--|
| **호출 시점** | AllPort Pressed 상태에서 X 버튼으로 연결 해제 |
| **반환값** | `true` 성공 / `false` 존재하지 않는 엣지 |

```csharp
bool ok = graphManager.RequestDeleteEdge(edgeId);
```

---

### `RequestRenamePartNode(nodeId, newLabel)` → `bool`

PART 노드의 라벨을 변경한다. 빈 문자열은 거부된다.

| | |
|--|--|
| **호출 시점** | PartPort 인라인 편집 완료 시 |
| **반환값** | `true` 성공 / `false` 빈 문자열 또는 존재하지 않는 노드 |

```csharp
bool ok = graphManager.RequestRenamePartNode(nodeId, inputField.text);
if (!ok)
    ShowToast("이름을 입력해주세요.");
```

---

### `RequestSelectNode(nodeId)` → `bool`

해당 node_id가 존재하는지 검증만 한다.  
실제 선택 상태 저장은 Interaction 쪽 책임이다.

| | |
|--|--|
| **호출 시점** | 노드 선택 시 존재 여부 확인용 (옵션) |
| **반환값** | `true` 유효한 노드 / `false` 존재하지 않음 |

```csharp
if (graphManager.RequestSelectNode(nodeId))
    selectedNodeId = nodeId;
```

---

### `RequestMoveNode(nodeId, position)` → `bool`

노드 위치를 수동으로 변경한다.  
ReflowAllSubtrees()는 호출하지 않으므로, 이후 Reflow를 원하면 별도로 호출한다.

| | |
|--|--|
| **호출 시점** | XR Grab으로 노드를 드래그해서 위치 변경 시 (Phase 2) |
| **반환값** | `true` 성공 / `false` 존재하지 않는 노드 |

```csharp
graphManager.RequestMoveNode(nodeId, grabTransform.position);
```

---

### `RequestMoveSubgraphByMember(memberNodeId, delta)` → `bool`

**전체 그래프(서브그래프 통째) 이동.** 멤버 노드 하나로 소속 서브그래프(같은 `sub_graph_id`) 전체를 `delta`만큼 강체 이동한다. 루트 포함 통째로 옮기므로 상대 배치는 유지된다.

| | |
|--|--|
| **호출 시점** | XR Grab으로 서브그래프를 통째로 드래그할 때. 개발자 2가 드래그 **이동량(delta)**을 매 프레임 또는 종료 시 넘긴다 |
| **반환값** | `true` 성공 / `false` sub_graph_id 없음 / 멤버 없음 |

```csharp
// 이번 프레임 이동량만큼 서브그래프 전체 이동
Vector3 delta = grabTransform.position - _lastGrabPos;
graphManager.RequestMoveSubgraphByMember(grabbedNodeId, delta);
_lastGrabPos = grabTransform.position;
```

> `sub_graph_id`를 이미 알고 있으면 `RequestMoveSubgraph(subGraphId, delta)`를 직접 호출해도 된다.
> 서버 반영: 현재는 이동된 노드마다 개별 `NODE_MOVE`로 나간다(서버 전체이동 op 미구현). 담당: 드래그 감지=개발자 2, 데이터/이벤트=개발자 3.

---

## 조회 API — 읽기 전용

반환된 객체를 **직접 수정하지 말 것.** 읽기 전용으로만 사용한다.

```csharp
// 특정 노드 조회
NodeData node = graphManager.GetNode(nodeId);
string label  = node?.DisplayText;     // label 우선, 없으면 node_text

// 전체 노드 목록
List<NodeData> all = graphManager.GetAllNodes();

// 특정 노드에 들어오는 엣지 (예: AllPort Pressed 상태에서 연결 목록 표시)
List<EdgeData> incoming = graphManager.GetEdgesIncomingToNode(nodeId);

// 특정 노드에 연결된 모든 엣지 (들어오는 + 나가는)
List<EdgeData> connected = graphManager.GetEdgesConnectedToNode(nodeId);

// 연결 가능 여부 사전 확인
bool ok = graphManager.CanConnect(fromId, toId, out string reason);
```

---

## R 버튼 — 레퍼런스 컨텍스트 수집

```csharp
// NodeActionPanel R 버튼 클릭 시
ReferenceContext ctx = graphManager.CollectReferenceContext(nodeId);

// ctx.chainLabels  : 루트 → 현재 노드 순서의 PROPERTY 라벨 목록
// ctx.partNodeId   : 루트 PROPERTY와 연결된 PART node_id (없으면 null)
// ctx.partLabel    : 연결된 PART 라벨 (없으면 null)

// 예: ReferenceSearchPanel.Open(ctx) 전달 (Phase 2 구현 예정)
```

---

## 호출하지 않는 메서드 (개발자 3 전용)

인터랙션 담당자는 아래 메서드를 호출하지 않는다.

| 메서드 | 이유 |
|--------|------|
| `LoadGraph(GraphData)` | 서버 응답 또는 SeedGraphLoader(개발용)에서만 호출 |
| `RenderGraph()` | LoadGraph 이후 개발자 3이 명시적으로 호출 |
| `ClearGraphView()` | 씬 전환 시 개발자 3이 관리 |
| `AddNode()` / `RemoveNode()` | 저수준 내부 메서드, Request* 를 사용할 것 |
| `AddEdge()` / `RemoveEdge()` | 저수준 내부 메서드, Request* 를 사용할 것 |
| `ReflowAllSubtrees()` | Request* 메서드 내부에서 자동 호출됨 |
| `RequestCreateNode()` | Deprecated, `RequestCreatePropertyNode()` 사용 |

---

## 흐름 요약

```
사용자 입력 (XR Ray / 버튼 클릭 / 텍스트 입력)
    ↓
InteractionManager
    ├─ selectedNodeId 관리
    ├─ GraphManager.Request*() 호출
    │    ├─ 키보드 + → RequestCreateRootPropertyNode()   (첫 root)
    │    ├─ 노드 +   → RequestCreatePropertyNode(parentId) (자식)
    │    └─ 텍스트칸 제출 → RequestNodeByUtterance()       (발화 전송, NodeView 자동)
    └─ 반환값(bool / string)으로 UI 알림 처리
         ↓
    GraphManager
         ├─ 데이터 변경 (Registry 갱신)
         ├─ View 생성/삭제 (NodeView / EdgeView)
         ├─ ReflowAllSubtrees() 자동 호출
         └─ 서버 동기화 (utterance=REST, part_node=REST, 2D=REST, 이동/삭제=WS)
```
