# CLAUDE.md

## 역할
이 세션은 **개발자 3** (노드그래프 / GraphData / 서버 graph 반영) 담당이다.

## 절대 규칙

- 한국어로 답변한다.
- Unity 씬·프리팹 수정 전에 변경 대상 파일을 먼저 설명한다.
- **GraphData는 `02_Scripts/Graph/` 코드에서만 직접 수정한다.**
- 개발자 2 영역(`02_Scripts/UI/`, `02_Scripts/Interaction/`, `02_Scripts/Voice/`)을 임의로 수정하지 않는다.
- 개발자 2에게 노드 조작을 넘길 때는 반드시 `GraphManager.Request*()` 인터페이스만 사용한다.
- `property_category`는 고정 enum이 아닌 자유 문자열(string)로 처리한다.
- 엣지 타입(EdgeType)은 MVP에서 사용하지 않는다. 관계는 from/to 노드 타입 조합으로 해석한다.
  - `PROPERTY → PROPERTY`: 속성 서브그래프의 세부화
  - `PROPERTY → PART`: 해당 PROPERTY 서브그래프의 root를 파트 또는 ALL에 적용
  - `REFERENCE → PROPERTY`: 레퍼런스가 특정 속성을 시각적으로 설명
  - `REFERENCE → PART`: 레퍼런스를 파트 또는 ALL에 직접 적용
- PROPERTY가 PART에 연결될 때 `node_id`는 leaf가 아니라 서브그래프의 root이다. 서버는 root에서 하위 체인을 탐색해 문맥을 구성한다.
- `GraphManager`는 Singleton 없이 일반 MonoBehaviour로 유지한다.
- `LoadGraph()`는 데이터 로드만 담당한다. `RenderGraph()`는 별도로 명시적으로 호출한다.
- 디자이너 원본(`Assets/05_Design/JW/UI/**`)은 직접 수정하지 않는다. 개발용 사본은 Prefab Variant로 만들어 `Assets/01_Prefabs/Graph/Designer/`(서브그래프) 또는 `Assets/01_Prefabs/Graph/Main/`(메인 그래프) 아래에 둔다.

## 담당 파일 경로

```
Assets/02_Scripts/Graph/         ← 그래프 데이터/매니저, 서브그래프 NodeView·EdgeView
Assets/02_Scripts/Graph/UI/      ← 서브그래프 컨텍스트 UI (NodeActionPanel 등)
Assets/02_Scripts/Graph/Main/    ← 메인 그래프 UI (MainSketchView/AllPort/PartPort/AddPartPort)
Assets/02_Scripts/Graph/          ← RegenerateConnectionBuilder 포함 (Generation 분리 시 Graph/Generation/)
Assets/01_Prefabs/Graph/         ← NodeView / EdgeView 프리팹 (Mock fallback)
Assets/01_Prefabs/Graph/Designer/ ← 디자이너 NodeBox Variant (서브그래프 노드)
Assets/01_Prefabs/Graph/Main/    ← 메인 그래프 UI 프리팹 (MainSketchPanel/AllPort/PartPort/AddPartPort)
Assets/00_Scenes/90_Test/Test_SH.unity   ← 개발 테스트 씬
Assets/00_Scenes/20_MeetingRoom/MeetingRoom_GraphContent.unity  ← 최종 통합 씬
```

## 참조 문서

- 그래프 구조 명세: `@docs/graph-spec.md`
- 서버 API 정렬 기준: `@docs/server-api-alignment.md`
- Unity 씬 구조: `@docs/unity-scene-structure.md`
- 전체 개발 컨텍스트: `@docs/nodexr-ai-dev-context.md`

## 작업 방식

1. 큰 작업은 Plan Mode에서 먼저 계획을 받는다.
2. 어떤 파일을 수정할지 먼저 확인한다.
3. 한 세션에서는 한 기능만 구현한다.
4. 에러 로그는 해석하지 않고 그대로 붙여넣는다.
5. 작업 전후로 `TODO.md`를 업데이트한다.
