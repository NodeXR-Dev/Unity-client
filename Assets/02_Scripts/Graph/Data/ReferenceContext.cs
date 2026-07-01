using System.Collections.Generic;

// R 버튼 클릭 시 GraphManager.CollectReferenceContext() 가 반환하는 컨텍스트.
// 현재 노드에서 레이아웃 루트까지의 PROPERTY 라벨 체인과
// 루트와 연결된 PART 정보를 담는다.
// ReferenceSearchPanel.Open(context) 에 전달 예정 (Phase 2).
[System.Serializable]
public class ReferenceContext
{
    // 레이아웃 루트 → 현재 노드 순서의 PROPERTY 라벨 목록
    public List<string> chainLabels;

    // 루트 PROPERTY 와 직접 연결된 PART node_id (없으면 null)
    public string partNodeId;

    // 루트 PROPERTY 와 직접 연결된 PART 라벨 (없으면 null)
    public string partLabel;
}
