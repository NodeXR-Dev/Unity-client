using System;
using System.Collections.Concurrent;
using System.Text;
using Meta.Net.NativeWebSocket;   // Meta XR Voice SDK 번들 (autoReferenced). 별도 패키지 추가 시 GUID 충돌.
using UnityEngine;

// GraphManager 뮤테이션 이벤트 → 서버 WS 동기화 레이어.
// GraphManager는 서버를 모르고 이벤트만 발행한다. 이 클래스가 유일한 연결 지점이다.
//
// 엔드포인트(서버 ws_room_event.py 기준):
//   ws://{host}/ws/rooms/event
//   room_id/user_id 는 URL이 아니라 매 메시지 본문(WSEvent)에서 읽는다.
// 이벤트 봉투: { "event_type": "...", "room_id": "<uuid>", "user_id": "<uuid>", "job_id": "<uuid>|null", "payload": {...} }
//   user_id 는 서버 WSEvent 필수 키 → 모든 송신 메시지에 포함한다(비우면 WS400).
//   job_id 는 서버 aa81878 에서 봉투 최상위에 추가됐다(수신 전용, 송신 시엔 payload 안에 넣는다 — 아래 참고).
//
// [송신] NODE_CREATE / EDGE_CREATE / EDGE_DELETE / NODE_TEXT_UPDATE / NODE_DELETE / NODE_MOVE
//   - NODE_CREATE / EDGE_CREATE: 로컬 임시 id 를 job_id 로 실어 보낸다(서버가 node_id/edge_id 발급).
//     서버 ACK(요청자에게만, { payload.result: { job_id, node_id|edge_id } })를 받아
//     GraphManager.ApplyServerNodeId / ApplyServerEdgeId 로 로컬 id 를 서버 발급 id 로 rekey 한다.
//   - GRAPH_UPDATED: 서버가 semantic 업데이트 결과로 push 하는 전체 그래프 스냅샷 → LoadGraph+RenderGraph 로 전체 갱신.
//   - 2D_GENERATED / 2D_COLOR_CHANGED: (img_url, 봉투 job_id) 를 구독자에게 전달. 그 외 수신은 로그만(뮤테이션 이벤트를 GraphManager 로 재적용하지 않음 — echo loop 방지).
//   - EDGE_CREATE 는 교차 엣지(포트↔서브그래프)만 송신. 트리 엣지는 서버가 NODE_CREATE 시 자동 생성한다.
//
// [노드 생성 경로 2종 — 의도적 분리]
//   - 키보드 직접 생성: WS NODE_CREATE(job_id 송신 → 서버 node_id 발급 → ACK 로 rekey, LLM 없음).
//   - 음성 발화(LLM 확장): REST /api/utterances(UtteranceApiClient, 서버가 UUID 발급 후 병합).
//   - 둘은 서로 다른 입력(키보드 라벨칸 vs 음성)에서 발생하므로 이중 생성되지 않는다.
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

    // 서버 2D 생성 완료(2D_GENERATED / 2D_COLOR_CHANGED) 통보 — img_url 만 전달하는 호환 이벤트.
    //
    // [이 이벤트를 유지하는 이유] 개발자1의 MvpGenerated2DSync 가 Action<string> 으로 구독한다.
    //   3D 생성에 asset_id 가 필요해 아래 OnImage2DResult 를 새로 만들었는데, 그때 이 이벤트의
    //   시그니처까지 바꿔버리면 그쪽 코드가 컴파일되지 않는다(파일이 달라 git 은 충돌을 잡지 못한다).
    //   내가 만든 변경이므로 여기서 흡수한다. 새 코드는 OnImage2DResult 를 쓸 것.
    public event Action<string> OnImage2DGenerated;

    // 2D 생성 결과 전체(img_url + job_id + asset_id). Generate2DController 가 구독한다.
    // job_id 는 봉투 최상위 필드(payload 안이 아니다). 서버가 안 실어 보내면 "" 로 온다.
    // asset_id 는 3D 생성 요청의 입력(source asset)이라 반드시 함께 전달해야 한다.
    public event Action<Image2DResult> OnImage2DResult;

    // 서버 3D 생성 완료(3D_GENERATED) 통보. (Generate3DController 가 구독)
    public event Action<Model3DResult> OnModel3DGenerated;

    // 2D 생성 결과. 인자가 늘어 Action<string,string,...> 로는 호출부에서 순서를 헷갈리기 쉬워 묶었다.
    public readonly struct Image2DResult
    {
        public readonly string ImgUrl;
        public readonly string JobId;    // 봉투 job_id. 없으면 "".
        public readonly string AssetId;  // 서버 asset_id. 3D 생성의 source_asset_id 로 쓴다.

        public Image2DResult(string imgUrl, string jobId, string assetId)
        {
            ImgUrl  = imgUrl;
            JobId   = jobId;
            AssetId = assetId;
        }
    }

    // 3D 생성 결과. model_url 은 GLB(.glb) 주소다.
    public readonly struct Model3DResult
    {
        public readonly string ModelUrl;
        public readonly string JobId;
        public readonly string AssetId;   // 생성된 3D asset_id (source 가 아니다)
        public readonly string MimeType;

        public Model3DResult(string modelUrl, string jobId, string assetId, string mimeType)
        {
            ModelUrl = modelUrl;
            JobId    = jobId;
            AssetId  = assetId;
            MimeType = mimeType;
        }
    }

    // HTTP(2D generate / part_node / utterances POST) 등에서 재사용하도록 연결 정보 노출.
    public string Host   => _host;
    public string RoomId => _roomId;
    public string UserId => _userId;
    public bool IsConnected =>
        _socket != null && _socket.State == WebSocketState.Open;

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

    // ⚠️ Meta 포크의 NativeWebSocket 은 OnMessage 를 **스레드풀(백그라운드) 스레드**에서 호출한다
    //    (스택: _ThreadPoolWaitCallback). 거기서 Unity 오브젝트(gameObject/Instantiate/Destroy/StartCoroutine)를
    //    건드리면 "can only be called from the main thread" 오류가 난다. 따라서 HandleIncoming 은 문자열만
    //    스레드-세이프 큐에 넣고, 실제 처리(ProcessMessage)는 메인스레드 Update 에서 뽑아 실행한다.
    private readonly ConcurrentQueue<string> _incomingQueue = new ConcurrentQueue<string>();

    private void Update()
    {
        while (_incomingQueue.TryDequeue(out string raw))
            ProcessMessage(raw);
    }

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

        // 서버는 room_id/user_id 를 URL이 아닌 메시지 본문(WSEvent)에서 읽는다.
        string url = $"{ServerAddress.Ws(_host)}/ws/rooms/event";

        if (string.IsNullOrEmpty(_userId))
            Debug.LogWarning("[GraphSyncClient] user_id 가 비어 있습니다. 서버가 user_id 를 필수로 요구하므로 송신이 WS400으로 거부됩니다. (seed 예: 11111111-1111-1111-1111-111111111111)");

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

    // 백그라운드(스레드풀) 스레드에서 호출됨 → 문자열만 큐에 넣는다(Unity 오브젝트 접근 금지).
    // Meta 포크 델리게이트 시그니처: (byte[] data, int offset, int length)
    private void HandleIncoming(byte[] data, int offset, int length)
    {
        _incomingQueue.Enqueue(Encoding.UTF8.GetString(data, offset, length));
    }

    // 메인스레드(Update)에서 실행. 수신 이벤트를 파싱해 rekey/전체갱신/이미지 등에 반영한다.
    private void ProcessMessage(string raw)
    {
        string eventType = null;
        try { eventType = JsonUtility.FromJson<IncomingHeader>(raw)?.event_type; }
        catch { /* 파싱 실패는 raw만 남긴다 */ }

        if (eventType == "ERROR")
        {
            Debug.LogWarning($"[GraphSyncClient] 서버 ERROR 수신: {raw}");
            return;
        }

        Debug.Log($"[GraphSyncClient] 수신(event_type={eventType}): {raw}");

        // NODE_CREATE / EDGE_CREATE ACK(요청자 대상) → 서버 발급 id 로 로컬 rekey.
        if (eventType == "NODE_CREATE" || eventType == "EDGE_CREATE")
        {
            HandleCreateAck(eventType, raw);
            return;
        }

        // GRAPH_UPDATED: 서버가 semantic 업데이트 결과로 push 하는 전체 그래프 스냅샷 → 전체 갱신.
        if (eventType == "GRAPH_UPDATED")
        {
            HandleGraphUpdated(raw);
            return;
        }

        // 2D 생성/색상변경 완료 통보 → img_url 을 구독자에게 전달. (그래프 뮤테이션 아님 → echo loop 무관)
        // 색상 변경은 event_type 만 2D_COLOR_CHANGED 로 다르고 payload 구조(asset_id/mime_type/img_url)는 같다.
        // 이 분기가 없으면 색상 변경 결과가 영영 반영되지 않고 컨트롤러가 타임아웃난다.
        if (eventType == "2D_GENERATED" || eventType == "2D_COLOR_CHANGED")
        {
            try
            {
                var evt = JsonUtility.FromJson<Image2DEvent>(raw);
                string url = evt?.payload?.img_url;
                if (!string.IsNullOrEmpty(url))
                {
                    // 두 이벤트를 모두 발행한다. 호환 이벤트(img_url 만)는 개발자1의
                    // MvpGenerated2DSync 가 구독하고, 전체 결과는 Generate2DController 가 쓴다.
                    OnImage2DResult?.Invoke(new Image2DResult(
                        url,
                        evt.job_id ?? "",
                        evt.payload.asset_id ?? ""));
                    OnImage2DGenerated?.Invoke(url);
                }
                else
                    Debug.LogWarning($"[GraphSyncClient] {eventType} 수신했으나 img_url 이 비어 있습니다.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GraphSyncClient] {eventType} 파싱 실패: {e.Message}");
            }
            return;
        }

        // 3D 생성 완료 통보 → model_url(GLB) 을 구독자에게 전달.
        if (eventType == "3D_GENERATED")
        {
            try
            {
                var evt = JsonUtility.FromJson<Model3DEvent>(raw);
                string url = evt?.payload?.model_url;
                if (!string.IsNullOrEmpty(url))
                    OnModel3DGenerated?.Invoke(new Model3DResult(
                        url,
                        evt.job_id ?? "",
                        evt.payload.asset_id ?? "",
                        evt.payload.mime_type ?? ""));
                else
                    Debug.LogWarning("[GraphSyncClient] 3D_GENERATED 수신했으나 model_url 이 비어 있습니다.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GraphSyncClient] 3D_GENERATED 파싱 실패: {e.Message}");
            }
        }
    }

    // NODE_CREATE / EDGE_CREATE ACK 파싱 → GraphManager rekey.
    //   payload = { isSuccess, code, message, result: { job_id, node_id|edge_id, graph_snapshot_id } }
    private void HandleCreateAck(string eventType, string raw)
    {
        AckEnvelope ack = null;
        try { ack = JsonUtility.FromJson<AckEnvelope>(raw); }
        catch (Exception e)
        {
            Debug.LogWarning($"[GraphSyncClient] {eventType} ACK 파싱 실패: {e.Message}");
            return;
        }

        AckResult result = ack?.payload?.result;
        if (result == null)
        {
            Debug.LogWarning($"[GraphSyncClient] {eventType} ACK 에 payload.result 가 없습니다: {raw}");
            return;
        }
        if (string.IsNullOrEmpty(result.job_id))
        {
            Debug.LogWarning($"[GraphSyncClient] {eventType} ACK 에 job_id 가 없습니다: {raw}");
            return;
        }
        if (_graphManager == null) return;

        // 성공 판정은 발급 id 유무로 한다(isSuccess 키에 의존하지 않음 — 명세 원문 키가 "isSuccess "(뒤 공백)라
        // 서버가 그대로 내보내면 JsonUtility 가 못 읽어 false 로 오판할 수 있음).
        if (eventType == "NODE_CREATE")
        {
            if (string.IsNullOrEmpty(result.node_id))
            {
                Debug.LogWarning($"[GraphSyncClient] NODE_CREATE ACK 에 node_id 가 없습니다(code={ack.payload.code}): {raw}");
                return;
            }
            _graphManager.ApplyServerNodeId(result.job_id, result.node_id);
        }
        else
        {
            if (string.IsNullOrEmpty(result.edge_id))
            {
                Debug.LogWarning($"[GraphSyncClient] EDGE_CREATE ACK 에 edge_id 가 없습니다(code={ack.payload.code}): {raw}");
                return;
            }
            _graphManager.ApplyServerEdgeId(result.job_id, result.edge_id);
        }
    }

    // GRAPH_UPDATED 파싱 → 전체 그래프 갱신(LoadGraph + RenderGraph).
    //   payload = { graph: { graph_version, core_2d_image, sub_graphs:[...] } } — 콜드로드(GET /api/graph)와 동일 구조.
    //   서버가 진실의 원천이므로 전체 교체한다(⚠️ 아직 서버 미확정인 로컬 placeholder 는 이때 사라질 수 있음).
    private void HandleGraphUpdated(string raw)
    {
        if (_graphManager == null) return;

        GraphUpdatedEnvelope env = null;
        try { env = JsonUtility.FromJson<GraphUpdatedEnvelope>(raw); }
        catch (Exception e)
        {
            Debug.LogWarning($"[GraphSyncClient] GRAPH_UPDATED 파싱 실패: {e.Message}");
            return;
        }

        GraphSnapshotDto snapshot = env?.payload?.graph;
        if (snapshot == null)
        {
            Debug.LogWarning($"[GraphSyncClient] GRAPH_UPDATED 에 payload.graph 가 없습니다: {raw}");
            return;
        }

        GraphData graph = snapshot.ToGraphData();
        _graphManager.LoadGraph(graph);
        _graphManager.RenderGraph();
        Debug.Log($"[GraphSyncClient] GRAPH_UPDATED 반영: nodes={graph.nodes.Count}, edges={graph.edges.Count}, graph_version={graph.graph_version}");
    }

    // ─────────────────────────────────────────────
    // GraphManager 이벤트 구독
    // ─────────────────────────────────────────────

    private void Subscribe()
    {
        if (_graphManager == null || _isSubscribed) return;
        _graphManager.OnNodeCreated     += HandleNodeCreated;
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
        _graphManager.OnNodeCreated     -= HandleNodeCreated;
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

    // 키보드 직접 생성 → NODE_CREATE(PROPERTY 전용, node_type 없음 — API 명세 2026-07-10).
    // 서버가 node_id 를 발급하므로 로컬 노드 id 를 job_id 로 실어 보낸다. ACK(job_id+node_id) 시 ApplyServerNodeId 로 rekey.
    // position 은 배열 [x,y,z]. 루트: parent_node_id="" + sub_graph_id(서버 발급). 자식: parent_node_id 채우고 sub_graph_id=""(서버가 부모에서 유도).
    private void HandleNodeCreated(string jobId, string nodeText, string parentNodeId, string subGraphId, Vector3 position)
    {
        var env = new NodeCreateEnvelope
        {
            room_id = _roomId,
            user_id = _userId,
            payload = new NodeCreatePayload
            {
                job_id         = jobId,
                sub_graph_id   = subGraphId ?? "",
                node_text      = nodeText,
                parent_node_id = parentNodeId ?? "",
                position       = new[] { position.x, position.y, position.z },
            },
        };
        Send("NODE_CREATE", JsonUtility.ToJson(env));
    }

    private void HandleNodeTextUpdated(string nodeId, string newText)
    {
        // [2026-08-01] 서버 미등록 노드에 보내면 [NODE404] 로 거부되고 에러 로그만 쌓인다.
        //   (생성 ACK 전 구간. 텍스트는 NODE_CREATE 로 함께 올라가므로 여기서 건너뛰어도 유실 없다.)
        if (_graphManager != null && !_graphManager.IsServerKnown(nodeId)) return;

        // 서버 NodeUpdatePayload = { node_id, text } (text는 non-blank 요구)
        var env = new NodeTextEnvelope
        {
            room_id = _roomId,
            user_id = _userId,
            payload = new NodeTextPayload { node_id = nodeId, text = newText },
        };
        Send("NODE_TEXT_UPDATE", JsonUtility.ToJson(env));
    }

    private void HandleNodeDeleted(string nodeId)
    {
        var env = new NodeDeleteEnvelope
        {
            room_id = _roomId,
            user_id = _userId,
            payload = new NodeIdPayload { node_id = nodeId },
        };
        Send("NODE_DELETE", JsonUtility.ToJson(env));
    }

    private void HandleNodeMoved(string nodeId, Vector3 position)
    {
        // [2026-08-01] 서버 미등록 노드에 보내면 [NODE404] 로 거부된다(생성 ACK 전 구간).
        //   보류된 위치는 ApplyServerNodeId 가 rekey 직후 한 번 재발행해 서버와 맞춘다.
        if (_graphManager != null && !_graphManager.IsServerKnown(nodeId)) return;

        // API 명세: NODE_MOVE position 은 배열 [x, y, z] (NODE_CREATE 와 동일 표준).
        // ⚠️ 서버 _handle_node_move 가 배열을 읽도록 함께 바뀌어야 함(현재 dict 로 읽으면 실패).
        var env = new NodeMoveEnvelope
        {
            room_id = _roomId,
            user_id = _userId,
            payload = new NodeMovePayload
            {
                node_id  = nodeId,
                position = new[] { position.x, position.y, position.z },
            },
        };
        Send("NODE_MOVE", JsonUtility.ToJson(env));
    }

    // 교차 엣지(포트↔서브그래프) → EDGE_CREATE. 로컬 edge_id 를 job_id 로 실어 보내고
    // 서버 ACK(job_id+edge_id) 로 rekey(ApplyServerEdgeId)한다. label 은 선택(빈 문자열 허용).
    // 로컬 GraphData 표준은 PROPERTY/REFERENCE → PART다.
    // 현재 서버 저장 표준은 반대인 PART → PROPERTY/REFERENCE이므로 WS 경계에서만 방향을 뒤집는다.
    // 서버 snapshot 수신 시에는 GraphSnapshotDto가 다시 로컬 표준으로 정규화한다.
    // 양 끝 노드가 서버-known 이 아니면 서버가 NODE404 → 송신 skip(로그).
    private void HandleEdgeCreated(EdgeData edge)
    {
        if (edge == null) return;

        if (_graphManager != null &&
            (!_graphManager.IsServerKnown(edge.from_node_id) || !_graphManager.IsServerKnown(edge.to_node_id)))
        {
            Debug.LogWarning($"[GraphSyncClient] EDGE_CREATE 송신 skip: 양 끝 노드가 서버-known 이 아닙니다. from={edge.from_node_id} to={edge.to_node_id}");
            return;
        }

        string serverFromNodeId = edge.from_node_id;
        string serverToNodeId   = edge.to_node_id;

        var fromNode = _graphManager?.GetNode(edge.from_node_id);
        var toNode   = _graphManager?.GetNode(edge.to_node_id);
        bool isLocalPartApplication =
            (fromNode?.NodeType == NodeType.PROPERTY || fromNode?.NodeType == NodeType.REFERENCE) &&
            toNode?.NodeType == NodeType.PART;

        if (isLocalPartApplication)
        {
            serverFromNodeId = edge.to_node_id;
            serverToNodeId   = edge.from_node_id;
        }

        var env = new EdgeCreateEnvelope
        {
            room_id = _roomId,
            user_id = _userId,
            payload = new EdgeCreatePayload
            {
                job_id       = edge.edge_id,
                from_node_id = serverFromNodeId,
                to_node_id   = serverToNodeId,
                label        = "",
            },
        };
        Send("EDGE_CREATE", JsonUtility.ToJson(env));
    }

    // AllPort X → EDGE_DELETE. edge_id 는 EDGE_CREATE ACK 로 서버 edge_id 로 rekey 된 값이어야 서버가 찾는다
    // (로컬 GUID 인 채로 보내면 EDGE404). 서버가 진실의 원천이므로 삭제 성공 여부는 로컬에서 이미 반영됨.
    private void HandleEdgeDeleted(string edgeId)
    {
        var env = new EdgeDeleteEnvelope
        {
            room_id = _roomId,
            user_id = _userId,
            payload = new EdgeIdPayload { edge_id = edgeId },
        };
        Send("EDGE_DELETE", JsonUtility.ToJson(env));
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

    // 서버 2D_GENERATED 이벤트 파싱용. job_id 는 payload 가 아니라 봉투 최상위에 온다(서버 ws_response.ws_success_event).
    [Serializable] private class Image2DEvent { public string event_type; public string job_id; public Image2DPayload payload; }
    [Serializable] private class Image2DPayload { public string asset_id; public string mime_type; public string img_url; }

    // 서버 3D_GENERATED 이벤트 파싱용(서버 Model3DGeneratedWSEvent / Model3DAssetPayload).
    // 이 이벤트에는 user_id 가 없다(서버 스키마가 room_id/job_id/payload 만 가진다).
    [Serializable] private class Model3DEvent { public string event_type; public string job_id; public Model3DPayload payload; }
    [Serializable] private class Model3DPayload { public string asset_id; public string mime_type; public string model_url; }

    [Serializable] private class NodeTextPayload { public string node_id; public string text; }
    [Serializable] private class NodeIdPayload   { public string node_id; }
    [Serializable] private class NodeMovePayload { public string node_id; public float[] position; }   // 배열 [x,y,z]
    // NODE_CREATE 는 position 을 배열 [x,y,z] 로 받는다(NODE_MOVE 의 dict 와 다름). parent_node_id 는 루트면 "".
    // node_id 는 서버가 발급하므로 요청에는 없다. 대신 로컬 노드 id 를 job_id 로 실어 보낸다(ACK 로 rekey).
    // sub_graph_id 는 루트 노드만 서버 발급값을 싣는다(자식은 "" → 서버가 부모에서 유도). node_type 은 보내지 않는다(PROPERTY 전용).
    [Serializable] private class NodeCreatePayload { public string job_id; public string sub_graph_id; public string node_text; public string parent_node_id; public float[] position; }
    // EDGE_CREATE 요청: 로컬 edge_id 를 job_id 로 실어 보낸다(ACK 로 rekey).
    [Serializable] private class EdgeCreatePayload { public string job_id; public string from_node_id; public string to_node_id; public string label; }

    [Serializable] private class NodeTextEnvelope { public string event_type = "NODE_TEXT_UPDATE"; public string room_id; public string user_id; public NodeTextPayload payload; }
    [Serializable] private class NodeDeleteEnvelope { public string event_type = "NODE_DELETE"; public string room_id; public string user_id; public NodeIdPayload payload; }
    [Serializable] private class NodeMoveEnvelope { public string event_type = "NODE_MOVE"; public string room_id; public string user_id; public NodeMovePayload payload; }
    [Serializable] private class NodeCreateEnvelope { public string event_type = "NODE_CREATE"; public string room_id; public string user_id; public NodeCreatePayload payload; }
    [Serializable] private class EdgeCreateEnvelope { public string event_type = "EDGE_CREATE"; public string room_id; public string user_id; public EdgeCreatePayload payload; }
    [Serializable] private class EdgeIdPayload      { public string edge_id; }
    [Serializable] private class EdgeDeleteEnvelope { public string event_type = "EDGE_DELETE"; public string room_id; public string user_id; public EdgeIdPayload payload; }

    // NODE_CREATE / EDGE_CREATE ACK 파싱용. result 는 두 이벤트를 겸용한다(JsonUtility 는 없는 키를 무시).
    [Serializable] private class AckEnvelope { public string event_type; public AckPayload payload; }
    [Serializable] private class AckPayload  { public bool isSuccess; public string code; public string message; public AckResult result; }
    [Serializable] private class AckResult   { public string job_id; public string node_id; public string edge_id; public string graph_snapshot_id; }

    // GRAPH_UPDATED 파싱용. graph 는 콜드로드(GET /api/graph)와 공유하는 GraphSnapshotDto(Data/GraphQueryDto.cs).
    [Serializable] private class GraphUpdatedEnvelope { public string event_type; public GraphUpdatedPayload payload; }
    [Serializable] private class GraphUpdatedPayload  { public GraphSnapshotDto graph; }
}
