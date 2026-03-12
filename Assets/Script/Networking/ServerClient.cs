using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using NativeWebSocket;
using System.Linq;
using System.Threading.Tasks;

namespace NodeXR
{
    // -------------------------
    // DTOs (기존 유지)
    // -------------------------
    [Serializable]
    public class NodeSelectReq
    {
        public string room_id;
        public string node_id;
    }

    [Serializable]
    public class CategoryItem
    {
        public string category_id;
        public string category_name;
    }

    [Serializable]
    public class CategoryData
    {
        public List<CategoryItem> categories;
    }

    [Serializable]
    public class CategoryRootResponse
    {
        public bool isSuccess;
        public string code;
        public string message;
        public CategoryData result;
    }

    [Serializable]
    public class CategorySelectReqDto
    {
        public string room_id;
        public string category_id;
    }

    [Serializable]
    public class CategorySelectResult
    {
        public string category_name;
    }

    [Serializable]
    public class CategorySelectResponse
    {
        public bool isSuccess;
        public string code;
        public string message;
        public CategorySelectResult result;
    }

    [Serializable]
    public class UtteranceReq
    {
        public string room_id, user_id, phase, text;
    }

    [Serializable]
    public class Generate3DReq
    {
        public string room_id;
        public string asset_id;
    }

    [Serializable]
    public class ThreeDResponse
    {
        public bool isSuccess;
        public string code;
        public string message;
        public ThreeDResult result;
    }

    [Serializable]
    public class ThreeDResult
    {
        public string asset_id;
        public string glb_url;
    }

    // ============================================================
    // ✅ /api/graph 전용 DTO (기존 GraphStateDto와 충돌 방지)
    // 스펙:
    // POST /api/graph
    // { session_id: UUID }
    // ============================================================
    [Serializable]
    public class GraphApiReqDto
    {
        public string session_id;
    }

    [Serializable]
    public class GraphApiNodeDto
    {
        public string node_id;
        public string node_type;

        // CATEGORY
        public string label;
        public string category_id;
        public int order;

        // ASSET
        public string img_url;
        public string parent_category_id;
    }

    [Serializable]
    public class GraphApiEdgeDto
    {
        public string edge_id;
        public string from_node_id;
        public string to_node_id;
    }

    [Serializable]
    public class GraphApiStateDto
    {
        public string graph_snapshot_id;
        public List<GraphApiNodeDto> nodes;
        public List<GraphApiEdgeDto> edges;
    }

    [Serializable]
    public class GraphApiResultDto
    {
        public GraphApiStateDto graph_state;
    }

    [Serializable]
    public class GraphApiRootResponse
    {
        public bool isSuccess;
        public string code;
        public string message;
        public GraphApiResultDto result;
    }

    // ============================================================
    // ServerClient
    // ============================================================
    public class ServerClient : MonoBehaviour
    {
        [Header("Endpoints")]
        public string httpBaseUrl = "http://192.168.0.236:8000";
        public string wsBaseUrl = "ws://192.168.0.236:8000";

        private WebSocket _ws;
        private string _lastRoomId;                      // 마지막 연결 room
        private string _lastWsPath = "/ws/graph_event/"; // 마지막 연결 wsPath (Focus 재연결용)

        public event Action<GraphEventDto> OnGraphEvent;
        public GraphEventDto LastDto { get; private set; }

        private static ServerClient _instance;

        private bool _isConnecting = false;
        private float _lastReconnectTime = -999f;
        private string _connectedUrl = ""; // 현재 _ws가 붙은 url 기록 (정확성 체크용)

        // ✅ 외부에서 WS 상태 확인용
        public WebSocketState WsState => _ws != null ? _ws.State : WebSocketState.Closed;
        public string WsConnectedUrl => _connectedUrl;

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Update()
        {
            if (_ws != null)
            {
#if !UNITY_WEBGL || UNITY_EDITOR
                _ws.DispatchMessageQueue();
#endif
            }
        }

        // 퀘스트 슬립(Focus Lost) 후 복귀 시 자동 재연결
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) return;
            if (string.IsNullOrEmpty(_lastRoomId)) return;

            if (_ws == null || _ws.State != WebSocketState.Open)
            {
                Debug.Log("<color=yellow>[WS]</color> Focus regained. Reconnecting...");
                ConnectRoomWS(_lastRoomId, _lastWsPath);
            }
        }

        public void EnsureWSConnected(string roomId, string wsPath)
        {
            if (string.IsNullOrWhiteSpace(roomId)) return;

            string cleanRoomId = roomId.Trim();
            string cleanWsPath = string.IsNullOrEmpty(wsPath) ? "/ws/graph_event/" : wsPath;
            string expectedUrl = $"{wsBaseUrl.TrimEnd('/')}{cleanWsPath}{cleanRoomId}";

            if (_isConnecting) return;
            if (Time.realtimeSinceStartup - _lastReconnectTime < 2.0f) return;

            if (_ws == null || _ws.State != WebSocketState.Open)
            {
                Debug.Log($"[WS] Ensure reconnect (state={_ws?.State}) -> {expectedUrl}");
                _lastReconnectTime = Time.realtimeSinceStartup;
                _ = ConnectRoomWS_Internal(cleanRoomId, cleanWsPath, expectedUrl);
                return;
            }

            if (!string.IsNullOrEmpty(_connectedUrl) && _connectedUrl != expectedUrl)
            {
                Debug.LogWarning($"[WS] Ensure reconnect (wrong url)\ncur={_connectedUrl}\nexp={expectedUrl}");
                _lastReconnectTime = Time.realtimeSinceStartup;
                _ = ConnectRoomWS_Internal(cleanRoomId, cleanWsPath, expectedUrl);
                return;
            }
        }

        /// <summary>
        /// ✅ 그래프 이벤트 수신용 WS 연결 (기본: /ws/graph_event/{room_id})
        /// </summary>
        public async void ConnectRoomWS(string roomId, string wsPath = "/ws/graph_event/")
        {
            if (string.IsNullOrWhiteSpace(roomId))
            {
                Debug.LogError("[WS] roomId is null/empty");
                return;
            }

            string cleanRoomId = roomId.Trim();
            string cleanWsPath = string.IsNullOrEmpty(wsPath) ? "/ws/graph_event/" : wsPath;
            string expectedUrl = $"{wsBaseUrl.TrimEnd('/')}{cleanWsPath}{cleanRoomId}";

            await ConnectRoomWS_Internal(cleanRoomId, cleanWsPath, expectedUrl);
        }

        private async Task ConnectRoomWS_Internal(string cleanRoomId, string cleanWsPath, string expectedUrl)
        {
            if (_isConnecting) return;
            _isConnecting = true;

            _lastRoomId = cleanRoomId;
            _lastWsPath = cleanWsPath;

            try
            {
                if (_ws != null && _ws.State == WebSocketState.Open && _connectedUrl == expectedUrl)
                {
                    Debug.Log($"<color=yellow>[WS]</color> Already connected. url={expectedUrl}");
                    return;
                }

                Debug.Log($"<color=white>[WS Attempt]</color> url={expectedUrl}");

                if (_ws != null)
                    await DisconnectWS();

                _ws = new WebSocket(expectedUrl);
                _connectedUrl = expectedUrl;

                _ws.OnOpen += async () =>
                {
                    Debug.Log("<color=green>[WS] Connected</color>");
                    if (_ws != null && _ws.State == WebSocketState.Open)
                        await _ws.SendText("{\"type\":\"ping\"}");
                };

                _ws.OnMessage += (bytes) =>
                {
                    var json = Encoding.UTF8.GetString(bytes);
                    var dto = SafeParseGraphEvent(json);
                    if (dto != null)
                    {
                        LastDto = dto;
                        OnGraphEvent?.Invoke(dto);
                    }
                };

                _ws.OnClose += (e) =>
                {
                    Debug.Log($"<color=red>[WS] Closed:</color> {e}");
                    _connectedUrl = "";
                };

                _ws.OnError += (e) =>
                {
                    Debug.LogError($"<color=red>[WS] Error:</color> {e}");
                    _connectedUrl = "";
                };

                await _ws.Connect();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WS Exception] {ex}");
                _connectedUrl = "";
            }
            finally
            {
                _isConnecting = false;
            }
        }

        public async Task DisconnectWS()
        {
            if (_ws != null)
            {
                try { await _ws.Close(); }
                catch { }
                _ws = null;
            }
            _connectedUrl = "";
        }

        // -------------------------
        // Categories
        // -------------------------
        public IEnumerator GetCategories(string roomId, Action<List<CategoryItem>> onSuccess)
        {
            var url = $"{httpBaseUrl.TrimEnd('/')}/api/categories?room_id={roomId}";

            using var req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var response = JsonUtility.FromJson<CategoryRootResponse>(req.downloadHandler.text);
                    var items = response?.result?.categories;

                    if (items != null)
                    {
                        var filtered = items
                            .Where(c => !string.IsNullOrEmpty(c.category_name) &&
                                        c.category_name.Trim().ToUpper() != "ROOT")
                            .Take(4)
                            .ToList();

                        onSuccess?.Invoke(filtered);
                        yield break;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[GetCategories Parse Fail] {e}");
                }
            }
            else
            {
                Debug.LogError($"[GetCategories Fail] {req.error} / {req.downloadHandler?.text}");
            }

            onSuccess?.Invoke(null);
        }

        /// <summary>
        /// ✅ /api/graph (디버깅/재로딩용)
        /// 스펙대로 POST + { session_id } 로 호출
        /// </summary>
        public IEnumerator GetGraphStateForDebug(string sessionId, Action<GraphApiStateDto> onSuccess)
        {
            var url = $"{httpBaseUrl.TrimEnd('/')}/api/graph";
            var body = new GraphApiReqDto { session_id = sessionId };
            string json = JsonUtility.ToJson(body);

            using var req = CreatePostRequest(url, json);
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var res = JsonUtility.FromJson<GraphApiRootResponse>(req.downloadHandler.text);
                    var state = res?.result?.graph_state;

                    if (res != null && res.code == "GRAPH200" && state != null)
                    {
                        // img_url 상대경로 보정
                        string baseUrl = httpBaseUrl.TrimEnd('/');
                        if (state.nodes != null)
                        {
                            foreach (var n in state.nodes)
                                n.img_url = FixUrl(n.img_url, baseUrl);
                        }

                        onSuccess?.Invoke(state);
                        yield break;
                    }

                    Debug.LogError($"[GetGraphStateForDebug] bad response code={res?.code} body={req.downloadHandler.text}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[GetGraphStateForDebug Parse Fail] {e}\nbody={req.downloadHandler.text}");
                }
            }
            else
            {
                Debug.LogError($"[GetGraphStateForDebug Fail] {req.error} / {req.downloadHandler?.text}");
            }

            onSuccess?.Invoke(null);
        }

        public IEnumerator SelectCategory(string roomId, string categoryId, Action<bool> onOk)
        {
            var url = $"{httpBaseUrl.TrimEnd('/')}/api/categories/select";
            var body = new CategorySelectReqDto { room_id = roomId, category_id = categoryId };
            string json = JsonUtility.ToJson(body);

            using var req = CreatePostRequest(url, json);
            yield return req.SendWebRequest();

            bool ok = (req.result == UnityWebRequest.Result.Success);
            if (!ok)
                Debug.LogError($"[SelectCategory Fail] {req.error} / {req.downloadHandler?.text}");

            onOk?.Invoke(ok);
        }

        // -------------------------
        // Utterances / Select2D / 3D
        // -------------------------
        public IEnumerator PostUtterance(string roomId, string userId, string serverPhase, string text, Action<bool> onOk)
        {
            var url = $"{httpBaseUrl.TrimEnd('/')}/api/utterances";
            var body = new UtteranceReq { room_id = roomId, user_id = userId, phase = serverPhase, text = text };
            string json = JsonUtility.ToJson(body);

            Debug.Log($"<color=yellow>[Utterance Send]</color> Phase: {serverPhase}");

            using var req = CreatePostRequest(url, json);
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("<color=green>[Utterance Success]</color>");
                onOk?.Invoke(true);
            }
            else
            {
                Debug.LogError($"<color=red>[Utterance Failed]</color> {req.error} / {req.downloadHandler?.text}");
                onOk?.Invoke(false);
            }
        }

        public IEnumerator Select2D(string roomId, string nodeId, Action<bool> onOk)
        {
            var url = $"{httpBaseUrl.TrimEnd('/')}/api/2d/select";
            var body = new NodeSelectReq { room_id = roomId, node_id = nodeId };
            string json = JsonUtility.ToJson(body);

            using var req = CreatePostRequest(url, json);
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("<color=cyan>[Select2D Success]</color> 서버 DB 반영 완료");
                onOk?.Invoke(true);
            }
            else
            {
                Debug.LogError($"<color=red>[Select2D Failed]</color> {req.error} / {req.downloadHandler?.text}");
                onOk?.Invoke(false);
            }
        }

        public IEnumerator Generate3D(string roomId, string assetId, Action<bool, string> callback)
        {
            var url = $"{httpBaseUrl.TrimEnd('/')}/api/3d/generate";
            var body = new Generate3DReq { room_id = roomId, asset_id = assetId };
            string json = JsonUtility.ToJson(body);

            using UnityWebRequest request = CreatePostRequest(url, json);
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var response = JsonUtility.FromJson<ThreeDResponse>(request.downloadHandler.text);
                    if (response != null && response.code == "3D200")
                        callback?.Invoke(true, response.result.glb_url);
                    else
                        callback?.Invoke(false, null);
                }
                catch
                {
                    callback?.Invoke(false, null);
                }
            }
            else
            {
                callback?.Invoke(false, null);
            }
        }

        private UnityWebRequest CreatePostRequest(string url, string json)
        {
            var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            return req;
        }

        // -------------------------
        // WS payload parse + URL fix
        // -------------------------
        private GraphEventDto SafeParseGraphEvent(string json)
        {
            try
            {
                var dto = JsonUtility.FromJson<GraphEventDto>(json);
                if (dto == null) return null;

                string baseUrl = httpBaseUrl.TrimEnd('/');

                dto.core_img_url = FixUrl(dto.core_img_url, baseUrl);

                if (dto.graph_state?.nodes != null)
                {
                    foreach (var node in dto.graph_state.nodes)
                        node.img_url = FixUrl(node.img_url, baseUrl);
                }

                return dto;
            }
            catch
            {
                return null;
            }
        }

        private string FixUrl(string original, string baseUrl)
        {
            if (string.IsNullOrEmpty(original)) return original;
            if (original.StartsWith("http")) return original;

            string path = original.StartsWith("/") ? original : "/" + original;
            return baseUrl + path;
        }
    }
}
