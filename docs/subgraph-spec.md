# NodeXR 서브그래프 구조 명세 (개발자 3 기준)

최초 작성: 2026-06-10  
마지막 업데이트: 2026-06-10

---

## 1. 개념 정의

서브그래프는 **별도 데이터 구조가 아니다.**  
기존 `GraphData(NodeData, EdgeData)` 안에서 PROPERTY/REFERENCE 노드들이 이루는 부분 그래프이다.  
"서브그래프"는 논리적 호칭이며 클래스로 분리하지 않는다.

---

## 2. 구조 규칙 (7개 확정)

| # | 규칙 |
|---|------|
| 1 | PROPERTY 노드는 부모 PROPERTY를 **최대 1개**만 가진다 |
| 2 | PROPERTY 그래프는 **Tree 구조**를 유지한다 |
| 3 | **Cycle은 허용하지 않는다** |
| 4 | **레이아웃 루트** = 들어오는 PROPERTY 엣지가 없는 PROPERTY 노드 |
| 5 | **메인그래프 루트** = PART와 직접 연결된 PROPERTY 노드 |
| 6 | REFERENCE는 UI 액션(R 버튼) 전용이며 레이아웃·View 생성 대상이 **아니다** |
| 7 | R 버튼은 현재 노드 → 상위 PROPERTY 체인 → 연결된 PART 정보를 수집하여 Reference Search Panel에 전달한다 |

### 규칙 4와 5의 차이

```
경우 A — 레이아웃 루트 = 메인그래프 루트 (일반적)
    [미래지향]  ← 들어오는 PROPERTY 없음  (레이아웃 루트)
       └─ → [PART: ALL]                   (메인그래프 루트)

경우 B — 레이아웃 루트이지만 아직 PART 미연결
    [새 노드]   ← 들어오는 PROPERTY 없음  (레이아웃 루트)
                  PART와 연결 안 됨        (메인그래프 루트 아님)
```

---

## 3. CanConnect 추가 조건

기존 규칙 유지 + PROPERTY→PROPERTY 연결 시 아래 조건 추가:

```
to 노드에 이미 들어오는 PROPERTY 엣지가 존재하면 → 연결 거부
이유: Tree 구조 보장 (부모 PROPERTY 최대 1개)
```

코드 위치: `GraphManager.CanConnect()`

---

## 4. 삭제 캐스케이드 규칙

PROPERTY 노드 P를 삭제하면 아래 대상을 **자식 먼저(post-order)** 순서로 함께 삭제한다.

| 삭제 대상 | 조건 |
|----------|------|
| P의 모든 PROPERTY 자손 | 재귀적으로 전부 |
| 각 자손에 종속된 REFERENCE | `REFERENCE → PROPERTY` 엣지에서 to == 자손 |
| P 자신에 종속된 REFERENCE | `REFERENCE → PROPERTY` 엣지에서 to == P |
| 위 노드들과 연결된 EdgeData 전체 | 데이터 + EdgeView 모두 |

삭제 순서: `CollectCascadeTargets()` 로 목록 선확보 → 순서대로 View 제거 → 데이터 제거 → Reflow

```
CollectCascadeTargets("prop_future") 반환 예시:
    ["prop_plant", "prop_utopia", "ref_img1", "prop_modern", "prop_future"]
    (자식 먼저, 자신 마지막)
```

---

## 5. RenderGraph 처리 범위

```
NodeData 순회 시:
    PROPERTY  → SpawnNodeView 대상 ✓
    PART      → 메인 그래프 UI 담당, 스킵
    REFERENCE → 레이아웃/View 제외, 스킵

EdgeData 순회 시 (SpawnEdgeView):
    from, to 양쪽 NodeView 가 _nodeViewMap 에 존재할 때만 생성
    → PROPERTY→PART, REFERENCE 관련 엣지는 자동으로 스킵됨
    → PROPERTY→PART EdgeView 는 서브↔메인 연결 단계(Phase 2)에서 처리
```

---

## 6. 레이아웃 알고리즘

### 상수 (Inspector 조절 가능 — GraphManager SerializeField)

```
_layoutHSpacing = 0.5f   // depth 단계별 오른쪽 간격 (X축)
_layoutVSpacing = 0.6f   // 형제 노드 세로 간격 (Y축)
_layoutTreeGap  = 0.2f   // 독립 서브트리 간 추가 여백
```

### 핵심 규칙: 루트 노드 위치 고정

`ReflowAllSubtrees()` 호출 시 **레이아웃 루트의 위치는 변경하지 않는다.**  
자식 노드들만 루트의 현재 X/Y를 기준으로 재배치된다.

### 구조

- X축: depth 증가 → 오른쪽 (`depth 0 = 루트 X`, `depth 1 = 루트 X + H_SPACING`, ...)
- Y축: 같은 depth의 형제 노드는 서브트리 높이 기반으로 세로 정렬
- 각 노드의 Y 중심 = 자신의 서브트리 전체 높이의 중앙

### Step 1 — 서브트리 높이 계산 (bottom-up)

```
CalculateSubtreeWidth(nodeId):  ← 함수명 유지 (높이 개념으로 사용)
    children = GetPropertyChildren(nodeId)
    if children 없음: return 1.0f
    return sum(CalculateSubtreeWidth(child) for child in children)
```

### Step 2 — 위치 할당 (top-down)

```
AssignPositions(nodeId, x, centerY):
    node.SetPosition(x, centerY, 0)          ← 이 노드 이동
    AssignChildPositions(nodeId, x, centerY) ← 자식 재귀

AssignChildPositions(nodeId, x, centerY):
    children = GetPropertyChildren(nodeId)
    if 없음: return

    totalHeight = sum(CalculateSubtreeWidth(child))
    cursor = centerY - (totalHeight / 2) × _layoutVSpacing

    for each child:
        childHeight = CalculateSubtreeWidth(child)
        childCenterY = cursor + (childHeight / 2) × _layoutVSpacing
        AssignPositions(child, x + _layoutHSpacing, childCenterY)
        cursor += childHeight × _layoutVSpacing
```

### Step 3 — ReflowAllSubtrees (진입점)

```
ReflowAllSubtrees():
    roots = FindLayoutRoots()

    for each root:
        rootNode = _nodeRegistry.Get(rootId)
        // 루트 위치는 현재 NodeData.Position 그대로 유지
        AssignChildPositions(root, rootNode.Position.x, rootNode.Position.y)

    depthMap = CalculateDepthMap()
    for each (nodeId, nodeView) in _nodeViewMap:
        nodeView.transform.position = node.Position
        nodeView.SetDepth(depthMap[nodeId])
```

### Reflow 호출 시점

| 이벤트 | 호출 여부 |
|--------|----------|
| `RenderGraph()` 완료 후 | ✓ |
| `RequestCreatePropertyNode()` 완료 후 | ✓ |
| `RequestDeleteNode()` 완료 후 | ✓ |
| `RequestDeleteEdge()` | ✗ (PROPERTY→PART 엣지만 대상, 레이아웃 변화 없음) |

---

## 7. 깊이별 머티리얼

레이아웃 루트 = depth 0 = Box_00 (가장 진한 파랑)  
자식으로 내려갈수록 depth 증가, 색 옅어짐.

| depth | 머티리얼 |
|-------|---------|
| 0 | `Box_00.mat` |
| 1 | `Box_01.mat` |
| 2 | `Box_02.mat` |
| 3 | `Box_03.mat` |
| 4+ | `Box_04.mat` (clamp) |

REFERENCE: 레이아웃 제외, View 없음, 머티리얼 적용 없음.  
머티리얼 미연결 시 fallback: `_propertyMaterial` → 단색 → 기본 색상.

---

## 8. NodeView 프리팹 구조

```
NodeView_Sub (GameObject)  — NodeView + NodeActionPanel 컴포넌트
├── NodeBox2                  ← FBX 메시. 깊이별 머티리얼 적용 (_meshRenderer)
├── Port_In  (-1.96, 0, 0)   ← InputPort 앵커 (_inputPort). ConnectorSphere 없을 때 fallback
├── Port_Out (+1.99, 0, 0)   ← OutputPort 앵커 (_outputPort). 동일 fallback
└── Canvas (World Space)
      ├── DeleteButton         ← Button 컴포넌트. NodeActionPanel._deleteButton
      ├── AddButton            ← Button 컴포넌트. NodeActionPanel._addButton
      ├── ReferenceButton      ← Button 컴포넌트. NodeActionPanel._referenceButton (stub)
      └── LabelInputField      ← TMP_InputField. NodeView._labelInput
            └── Placeholder    ← TMP_Text. 빈 label 일 때 표시
```

> **ConnectorSphere**: 현재 노드 프리팹에 없음. 엣지 프리팹 자식으로 관리됨 (섹션 9 참조).

---

## 9. EdgeView (Bezier + ConnectorSphere)

### 프리팹 구조

```
EdgePrefab (GameObject)  — EdgeView 컴포넌트
├── LineRenderer              ← _lineRenderer 연결
├── FromSphere                ← ConnectorSphereView (_fromConnector) — 출발 끝점 구
└── ToSphere                  ← ConnectorSphereView (_toConnector)   — 도착 끝점 구
```

### 베지어 계산

```
P0 = fromView.OutputPort.position  (Port_Out 또는 ConnectorSphere transform)
P3 = toView.InputPort.position     (Port_In  또는 ConnectorSphere transform)

tangentLen = max(distance(P0, P3) × 0.5, 0.2)
P1 = P0 + Vector3.down × tangentLen   ← 수직 탄젠트 (부모→아래)
P2 = P3 + Vector3.up   × tangentLen   ← 수직 탄젠트 (자식→위)

positionCount = 21 (20 segment)
색상: 흰색 (Color.white)
선 두께: _lineWidth = 0.004f  (Inspector 조절 가능)
LateUpdate에서 매 프레임 갱신
```

### ConnectorSphere 동작

- `UpdateLine()` 에서 `FromSphere.position = P0`, `ToSphere.position = P3` 매 프레임 세팅
- `localScale = Vector3.one × _connectorScale` (기본 0.06f, Inspector 조절)
- 머티리얼: `Sphere_Default.mat` (기본) / `Sphere_Grabbed.mat` (Pressed)
- 외부 호출: `EdgeView.SetFromConnectorPressed(bool)` / `SetToConnectorPressed(bool)`
- 엣지 삭제 시 구도 함께 삭제 (GameObject 자식이므로 자동)

---

## 10. GraphManager API

### 신규

```csharp
// Add 버튼 → PROPERTY 자식 노드 생성 + 엣지 연결 + Spawn + Reflow
public string RequestCreatePropertyNode(string parentId)

// R 버튼 → 상위 체인 + 연결 PART 수집 (ReferenceSearchPanel stub)
public ReferenceContext CollectReferenceContext(string nodeId)

// 레이아웃 재계산 (루트 고정, 자식만 재배치)
public void ReflowAllSubtrees()
```

### 변경

```csharp
// 기존 stub → 캐스케이드 삭제 + EdgeView 정리 + Reflow
public bool RequestDeleteNode(string nodeId)

// EdgeView _edgeViewMap 정리 추가
public bool RequestDeleteEdge(string edgeId)
```

### Deprecated

```csharp
// RequestCreateNode(parentId, text) → RequestCreatePropertyNode(parentId) 로 대체
[Obsolete] public void RequestCreateNode(string parentNodeId, string text)
```

---

## 11. ReferenceContext

```csharp
public class ReferenceContext
{
    public List<string> chainLabels;  // 레이아웃 루트 → 현재 노드 PROPERTY 라벨 순서
    public string partNodeId;         // 연결된 PART node_id (없으면 null)
    public string partLabel;          // 연결된 PART 라벨   (없으면 null)
}
```

수집 로직:
1. 현재 노드에서 `GetPropertyParent()` 를 반복해 레이아웃 루트까지 올라감
2. 루트의 나가는 엣지 중 `to.type == PART` 탐색
3. `ReferenceSearchPanel.Open(context)` 호출 (현재 stub — 개발자 2 구현 예정)

---

## 12. View 추적 구조 (GraphManager)

```csharp
Dictionary<string, NodeView> _nodeViewMap   // 기존 유지
Dictionary<string, EdgeView> _edgeViewMap   // 신규 추가
```

EdgeView 등록: `SpawnEdgeView()` 에서 `_edgeViewMap[edge.edge_id] = view`  
EdgeView 제거: `RequestDeleteNode()` 및 `RequestDeleteEdge()` 에서 `Destroy + Remove`

---

## 13. 인라인 라벨 편집 (NodeView)

- `TMP_InputField _labelInput` — 클릭 시 편집 활성화
- `Bind()` 에서 `_labelInput.text = data.label ?? ""` 세팅
  - `label = ""` 이면 placeholder 표시 ("NodeName")
  - `label` 에 값 있으면 해당 텍스트 표시
- `onEndEdit` → `OnLabelSubmit(string)` → `data.label = newText.Trim()`
  - 빈 문자열 허용 (다시 placeholder로 돌아옴)
- 신규 노드 기본 label: `""` (빈 문자열 — placeholder 표시)

World Space Canvas 클릭 동작 조건:
- EventSystem이 씬에 있어야 함
- Canvas의 Event Camera가 설정되어야 함
- XR 환경에서는 `TrackedDeviceGraphicRaycaster` 필요 (개발자 2 협의 필요)

---

## 14. 서브↔메인 연결 (Phase 2, 미구현)

PROPERTY→PART EdgeView 렌더링은 이 단계에서 다루지 않는다.  
Phase 2에서 PROPERTY 노드의 OutputPort 와 PartPort/AllPort Transform 을 연결하는 EdgeView 를 추가한다.  
`GraphManager.GetEdgesIncomingToNode(partNodeId)` 조회 구조는 그대로 유지된다.
