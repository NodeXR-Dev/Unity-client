using System;
using System.Text;
using Meta.Net.NativeWebSocket;   // Meta XR Voice SDK 번들 (autoReferenced). 별도 패키지 추가 시 GUID 충돌.
using UnityEngine;

// GraphManager 뮤테이션 이벤트 → 서버 WS 동기화 레이어.
// GraphManager는 서버를 모르고 이벤트만 발행한다. 이 클래스가 유일한 연결 지점이다.
//
// 엔드포인트(서버 ws_room_event.py 기준):
//   ws://{host}/ws/rooms/{room_id}/event?user_id={user_id}
// 이벤트 봉투: { "event_type": "...", "room_id": "<uuid>", "payload": {...} }
//
// [이번 세션 범위]
//   - 송신: NODE_TEXT_UPDATE / NODE_DELETE / NODE_MOVE (3종)
//   - 수신: 로그만. 서버 이벤트를 GraphManager.Request* 로 재적용하지 않는다(echo loop 방지).
//   - EDGE_CREATE/EDGE_DELETE: 서버 sub_graph_id 제약으로 실패 가능 → 이번 세션 송신 제외(로그만).
//
// [서버 미지원 — TODO]
//   - NODE_CREATE 이벤트 없음. 로컬 노드 생성은 서버 반영 불가.
public class GraphSyncClient : MonoBehaviour
{
    [Header("연결 대상 GraphManager")]
    [SerializeField] private GraphManager _graphManager;

    [Header("서버 연결 정보")]
    [Tooltip("host:port. 예: 127.0.0.1:8000")]
    [SerializeField] private string _host = "127.0.0.1:8000";
    [Tooltip("서버 DB의 room_id (UUID). seed 사용 시 aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    [SerializeField] private string _roomId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    [Tooltip("user_id (UUID, 선택). 비우면 쿼리에서 생략")]
    [SerializeField] private string _userId = "";

    [Header("옵션")]
    [SerializeField] private bool _autoConnect = true;
    [Tooltip("false면 연결/수신만 하고 서버로 송신하지 않는다(관찰 모드)")]
    [SerializeField] private bool _sendToServer = true;

    private WebSocket _socket;
    private bool _isSubscribed;

    // 서버 2D 생성 완료(2D_GENERATED) 통보 시 img_url 을 전달한다. (Generate2DController 가 구독)
    public event Action<string> OnImage2DGenerated;

    // HTTP(2D generate / part_node / utterances POST) 등에서 재사용하도록 연결 정보 노출.
    public string Host   => _host;
    public string RoomId => _roomId;
    public string UserId => _userId;

    // ─────────────────────────────────────────────
    // 생명주기
    // ─────────────────────────────────────────────

    private void OnEnable()
    {
        Subscribe();
    }

    private async void Start()
    {
        if (_autoConnect)
            await Connect();
    }

    // Meta 포크의 NativeWebSocket 은 DispatchMessageQueue 가 없다.
    // Receive() 가 await 연속(메인스레드)에서 OnMessage 를 직접 호출하므로 별도 펌프가 필요 없다.

    private void OnDisable()
    {
        Unsubscribe();
    }

    private async void OnDestroy()
    {
        Unsubscribe();
        if (_socket != null)
            await _socket.Close();
    }

    // ─────────────────────────────────────────────
    // 연결
    // ─────────────────────────────────────────────

    public async System.Threading.Tasks.Task Connect()
    {
        if (string.IsNullOrEmpty(_host) || string.IsNullOrEmpty(_roomId))
        {
            Debug.LogWarning("[GraphSyncClient] Connect 실패: host 또는 room_id가 비어 있습니다.");
            return;
        }

        string url = $"ws://{_host}/ws/rooms/{_roomId}/event";
        if (!string.IsNullOrEmpty(_userId))
            url += $"?user_id={_userId}";

        _socket = new WebSocket(url);

        _socket.OnOpen += () =>
            Debug.Log($"[GraphSyncClient] WS 열림 → {url}");

        _socket.OnError += (err) =>
            Debug.LogError($"[GraphSyncClient] WS 오류: {err}");

        _socket.OnClose += (code) =>
            Debug.Log($"[GraphSyncClient] WS 닫힘: code={code}");

        _socket.OnMessage += HandleIncoming;

        Debug.Log($"[GraphSyncClient] 연결 시도… {url}");
        await _socket.Connect();
    }

    // 수신은 로그만. GraphManager 로 재적용하지 않는다(echo loop 방지).
    // Meta 포크 델리게이트 시그니처: (byte[] data, int offset, int length)
    private void HandleIncoming(byte[] data, int offset, int length)
    {
        string raw = Encoding.UTF8.GetString(data, offset, length);

        string eventType = null;
        try { eventType = JsonUtility.FromJson<IncomingHeader>(raw)?.event_type; }
        catch { /* 파싱 실패는 raw만 남긴다 */ }

        if (eventType == "ERROR")
        {
            Debug.LogWarning($"[GraphSyncClient] 서버 ERROR 수신: {raw}");
            return;
        }

        Debug.Log($"[GraphSyncClient] 수신(event_type={eventType}): {raw}");

        // 2D 생성 완료 통보 → img_url 을 구독자에게 전달. (그래프 뮤테이션 아님 → echo loop 무관)
        if (eventType == "2D_GENERATED")
        {
            try
            {
                var evt = JsonUtility.FromJson<Image2DEvent>(raw);
                string url = evt?.payload?.img_url;
                if (!string.IsNullOrEmpty(url))
                    OnImage2DGenerated?.Invoke(url);
                else
                    Debug.LogWarning("[GraphSyncClient] 2D_GENERATED 수신했으나 img_url 이 비어 있습니다.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GraphSyncClient] 2D_GENERATED 파싱 실패: {e.Message}");
            }
        }
    }

    // ─────────────────────────────────────────────
    // GraphManager 이벤트 구독
    // ─────────────────────────────────────────────

    private void Subscribe()
    {
        if (_graphManager == null || _isSubscribed) return;
        _graphManager.OnNodeDeleted     += HandleNodeDeleted;
        _graphManager.OnEdgeCreated     += HandleEdgeCreated;
        _graphManager.OnEdgeDeleted     += HandleEdgeDeleted;
        _graphManager.OnNodeMoved       += HandleNodeMoved;
        _graphManager.OnNodeTextUpdated += HandleNodeTextUpdated;
        _isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (_graphManager == null || !_isSubscribed) return;
        _graphManager.OnNodeDeleted     -= HandleNodeDeleted;
        _graphManager.OnEdgeCreated     -= HandleEdgeCreated;
        _graphManager.OnEdgeDeleted     -= HandleEdgeDeleted;
        _graphManager.OnNodeMoved       -= HandleNodeMoved;
        _graphManager.OnNodeTextUpdated -= HandleNodeTextUpdated;
        _isSubscribed = false;
    }

    // ─────────────────────────────────────────────
    // 송신 핸들러 (이번 세션: NODE_TEXT_UPDATE / NODE_DELETE / NODE_MOVE)
    // ─────────────────────────────────────────────

    private void HandleNodeTextUpdated(string nodeId, string newText)
    {
        // 서버 NodeUpdatePayload = { node_id, text } (text는 non-blank 요구)
        var env = new NodeTextEnvelope
        {
            room_id = _roomId,
            payload = new NodeTextPayload { node_id = nodeId, text = newText },
        };
        Send("NODE_TEXT_UPDATE", JsonUtility.ToJson(env));
    }

    private void HandleNodeDeleted(string nodeId)
    {
        var env = new NodeDeleteEnvelope
        {
            room_id = _roomId,
            payload = new NodeIdPayload { node_id = nodeId },
        };
        Send("NODE_DELETE", JsonUtility.ToJson(env));
    }

    private void HandleNodeMoved(string nodeId, Vector3 position)
    {
        // 서버 NodeMovePayload.position 은 배열이 아닌 { x, y, z } dict
        var env = new NodeMoveEnvelope
        {
            room_id = _roomId,
            payload = new NodeMovePayload
            {
                node_id  = nodeId,
                position = new WsVec3 { x = position.x, y = position.y, z = position.z },
            },
        };
        Send("NODE_MOVE", JsonUtility.ToJson(env));
    }

    // EDGE_CREATE는 서버가 같은 sub_graph_id만 허용(GRAPH409) → 이번 세션 송신 제외, 로그만.
    private void HandleEdgeCreated(EdgeData edge)
    {
        Debug.Log($"[GraphSyncClient] (미전송) EDGE_CREATE from={edge.from_node_id} → to={edge.to_node_id}");
        // TODO: sub_graph_id 제약 대응 후 EDGE_CREATE 송신 구현
    }

    private void HandleEdgeDeleted(string edgeId)
    {
        Debug.Log($"[GraphSyncClient] (미전송) EDGE_DELETE edge_id={edgeId}");
        // TODO: EDGE_DELETE 송신 구현
    }

    // ─────────────────────────────────────────────
    // 송신 공통
    // ─────────────────────────────────────────────

    private void Send(string eventType, string json)
    {
        if (!_sendToServer)
        {
            Debug.Log($"[GraphSyncClient] (관찰모드, 미전송) {eventType}: {json}");
            return;
        }
        if (_socket == null || _socket.State != WebSocketState.Open)
        {
            Debug.LogWarning($"[GraphSyncClient] 송신 skip({eventType}): 소켓이 열려있지 않음(State={_socket?.State}).");
            return;
        }
        Debug.Log($"[GraphSyncClient] 송신 {eventType}: {json}");
        _ = _socket.SendText(json);
    }

    // ─────────────────────────────────────────────
    // JsonUtility 직렬화용 봉투/페이로드
    // ─────────────────────────────────────────────

    [Serializable] private class IncomingHeader { public string event_type; }

    // 서버 2D_GENERATED 이벤트 파싱용
    [Serializable] private class Image2DEvent { public string event_type; public Image2DPayload payload; }
    [Serializable] private class Image2DPayload { public string asset_id; public string mime_type; public string img_url; }

    [Serializable] private class WsVec3 { public float x; public float y; public float z; }

    [Serializable] private class NodeTextPayload { public string node_id; public string text; }
    [Serializable] private class NodeIdPayload   { public string node_id; }
    [Serializable] private class NodeMovePayload { public string node_id; public WsVec3 position; }

    [Serializable] private class NodeTextEnvelope { public string event_type = "NODE_TEXT_UPDATE"; public string room_id; public NodeTextPayload payload; }
    [Serializable] private class NodeDeleteEnvelope { public string event_type = "NODE_DELETE"; public string room_id; public NodeIdPayload payload; }
    [Serializable] private class NodeMoveEnvelope { public string event_type = "NODE_MOVE"; public string room_id; public NodeMovePayload payload; }
}
