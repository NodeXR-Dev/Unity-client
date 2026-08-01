using System;

// 속성(PROPERTY) 노드 → 파트(PART/ALL) 포트 연결의 "무장" 상태를 들고 있는 전역 홀더.
//
// 흐름:
//   1) 노드의 연결 버튼(NodeActionPanel._linkButton) 탭 → Arm(nodeId)
//   2) 메인 그래프의 PartPort / AllPort 탭 → 무장 상태면 확장 대신
//      GraphManager.RequestConnectFromPort(portNodeId, armedNodeId) 실행 후 Clear()
//   3) 같은 노드의 연결 버튼을 다시 탭하거나 Clear() 로 취소
//
// 왜 static 인가: 노드(서브그래프)와 포트(메인 그래프)는 서로 다른 프리팹 계층에 있어
//   씬에서 참조를 이어주려면 배선이 늘어난다. 상태는 "현재 무장된 노드 id" 하나뿐이라
//   전역 홀더가 배선 없이 가장 단순하다. (씬 전환 시 Clear 로 초기화.)
//
// [주의] 데이터 변경은 하지 않는다. 실제 연결은 항상 GraphManager.RequestConnectFromPort 가 담당한다.
public static class GraphLinkSelection
{
    // 현재 연결 대기 중인 속성 노드 id. 비어 있으면 무장 해제 상태.
    public static string ArmedNodeId { get; private set; }

    public static bool IsArmed => !string.IsNullOrEmpty(ArmedNodeId);

    // 무장 상태가 바뀔 때 발행. 인자는 무장된 nodeId (해제 시 null).
    // 버튼 라벨/색 갱신용으로 NodeActionPanel 이 구독한다.
    public static event Action<string> OnArmedChanged;

    // 같은 노드를 다시 무장하면 토글(취소)로 동작한다.
    public static void Arm(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return;

        if (ArmedNodeId == nodeId)
        {
            Clear();
            return;
        }

        ArmedNodeId = nodeId;
        OnArmedChanged?.Invoke(ArmedNodeId);
    }

    public static void Clear()
    {
        if (ArmedNodeId == null) return;

        ArmedNodeId = null;
        OnArmedChanged?.Invoke(null);
    }
}
