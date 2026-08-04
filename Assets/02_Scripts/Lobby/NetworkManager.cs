using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Text;
using System.Threading.Tasks;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class NetworkManager : MonoBehaviour, INetworkRunnerCallbacks
{
    public static NetworkRunner runnerInsatance;

    public string lobbyName = "default";

    public Transform sessionListContentParent;
    public GameObject sessionListEntryPrefab;
    public Dictionary<string, GameObject> sessionListUiDictionary = new Dictionary<string, GameObject>();

#if UNITY_EDITOR
    public SceneAsset gameplaySceneAsset;
    public SceneAsset lobbySceneAsset;
#endif
    [SerializeField] private string gameplaySceneName;
    [SerializeField] private string lobbySceneName;

    public GameObject playerPrefab;

    [Header("Create Session UI")]
    public TMP_InputField roomNameInput;
    public TMP_InputField passwordInput;
    public TMP_InputField nicknameInput;

    [Header("Password Panel")]
    public GameObject passwordPanel;
    public TMP_InputField passwordPanelNicknameInput;
    public TMP_InputField passwordCheckInput;
    public GameObject passwordWarningImage;

    [Header("Network Status UI")]
    public TMP_Text networkStatusText;

    [Header("Reconnect / Session Recovery")]
    [SerializeField] private bool enableSessionRecovery = true;
    [SerializeField] private int maxReconnectAttempts = 3;
    [SerializeField] private float reconnectDelaySeconds = 2f;
    [SerializeField] private float reconnectBackoffSeconds = 1f;

    [Header("Date Navigation UI")]
    public TMP_Text meetingDateText; // 2026.01.30 (오늘) 이 적힐 텍스트
    private DateTime currentViewDate = DateTime.Now; // 현재 보고 있는 날짜 저장

    private SessionInfo selectedSession;
    private List<SessionInfo> cachedSessionList = new();

    private const string LastSessionNamePrefsKey = "Lobby.LastSessionName";
    private const string LastSessionNicknamePrefsKey = "Lobby.LastSessionNickname";
    private const string LastSessionUserIdPrefsKey = "Lobby.LastSessionUserId";

    private string lastSessionName;
    private string lastSessionNickname;
    private string lastSessionUserId;
    private bool intentionalShutdown;
    private bool suppressShutdownSceneLoad;
    private bool isRecoveringSession;
    private Coroutine reconnectRoutine;



    private void Awake()
    {
        runnerInsatance = gameObject.GetComponent<NetworkRunner>();

        if (runnerInsatance == null)
        {
            runnerInsatance = gameObject.AddComponent<NetworkRunner>();
        }

        runnerInsatance.RemoveCallbacks(this);
        runnerInsatance.AddCallbacks(this);

        LoadRecoverySession();

#if UNITY_EDITOR
        if (gameplaySceneAsset != null)
            gameplaySceneName = gameplaySceneAsset.name;

        if (lobbySceneAsset != null)
            lobbySceneName = lobbySceneAsset.name;
#endif
    }

    private void OnDestroy()
    {
        if (runnerInsatance != null)
        {
            runnerInsatance.RemoveCallbacks(this);
        }
    }

    private void Start()
    {
        // ✅ 시작 시 기존에 저장된 닉네임이 있다면 인풋필드에 표시
        string savedNickname = PlayerPrefs.GetString("PlayerNickname", "Actor_1");
        if (nicknameInput != null)
        {
            nicknameInput.text = savedNickname;
        }

        if (passwordPanelNicknameInput != null)
        {
            passwordPanelNicknameInput.text = savedNickname;
        }

        if (networkStatusText != null)
        {
            networkStatusText.text = "Connecting to network...";
            networkStatusText.color = Color.yellow;
        }

        runnerInsatance.JoinSessionLobby(SessionLobby.Shared, lobbyName);

        UpdateDateUIAndRefresh();
    }
        
    // ✅ 사용자가 이름을 입력하고 버튼을 누를 때 호출할 함수
    public void SetPlayerNickname()
    {
        if (nicknameInput == null || string.IsNullOrEmpty(nicknameInput.text))
        {
            Debug.LogWarning("닉네임을 입력해주세요!");
            return;
        }

        string newNickname = nicknameInput.text.Trim();
        
        // 1. 로컬 저장소에 저장 (방 생성/참가 시 이 값을 불러와 사용함)
        PlayerPrefs.SetString("PlayerNickname", newNickname);
        PlayerPrefs.Save();

        Debug.Log($"닉네임이 '{newNickname}'으로 등록되었습니다.");
        
    }

    // 기존 OnInput 부분을 이 코드로 덮어쓰세요.
    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        // 공유 모드(Shared Mode)에서는 NetworkManager에서 입력을 패킹하지 않으므로 비워둡니다.
        // 하지만 인터페이스 필수 멤버이므로 형태는 반드시 유지해야 합니다.
    }
    

    // -------------------------------
    // 세션 생성
    // -------------------------------
    public void CreateCustomSession()
    {
        StartCoroutine(CreateCustomSessionRoutine());
    }

   private IEnumerator CreateCustomSessionRoutine()
    {
        // 1. 입력값 준비 (기존 동일)
        string topic = string.IsNullOrEmpty(roomNameInput.text) ? "DefaultRoom" : roomNameInput.text;
        string pw = passwordInput.text;
        string nick = string.IsNullOrEmpty(nicknameInput.text) ? "Unknown" : nicknameInput.text;

        // 2. 서버 API 호출
        string serverUrl = "http://localhost:8000/api/rooms/generate";
        string jsonPayload = $"{{\"room_topic\":\"{topic}\", \"password\":\"{pw}\", \"nickname\":\"{nick}\"}}";
        
        using (UnityWebRequest request = new UnityWebRequest(serverUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                RoomResponse response = JsonUtility.FromJson<RoomResponse>(request.downloadHandler.text);
                string assignedRoomId = response.result.room_id;
                string currentTime = DateTime.Now.ToString("HH:mm");

                // ------------------------------------------------------------------
                // ✅ [최종 방어 로직] 상태에 따른 단계적 접속
                // ------------------------------------------------------------------
                if (runnerInsatance == null) { Debug.LogError("Runner가 없습니다!"); yield break; }

                // A. 만약 이미 로비에 있거나 접속 중이라면 JoinLobby를 호출하지 않고 대기만 합니다.
                if (runnerInsatance.LobbyInfo.IsValid) 
                {
                    Debug.Log("이미 로비에 연결되어 있습니다. 바로 세션 생성을 시도합니다.");
                }
                else 
                {
                    // B. 로비 접속 중(JoiningLobby)인지 체크해서, 아예 꺼져있을 때만 Join 호출
                    // IsRunning이 false거나 아예 초기 상태일 때만 실행
                    if (!runnerInsatance.IsRunning)
                    {
                        Debug.Log("러너가 정지 상태입니다. 로비 접속을 새로 시작합니다.");
                        var joinLobbyTask = runnerInsatance.JoinSessionLobby(SessionLobby.Shared, lobbyName);
                        // Task가 완료될 때까지 기다리지 않고 아래 while에서 상태 체크로 넘깁니다.
                    }
                    
                    // C. '이미 접속 중' 에러를 피하기 위해, 상태가 Ready가 될 때까지 안전하게 대기
                    Debug.Log("로비가 준비될 때까지 대기 중...");
                    float timeout = 0;
                    while (!runnerInsatance.LobbyInfo.IsValid && timeout < 10f) // 최대 10초 대기
                    {
                        timeout += Time.deltaTime;
                        yield return null; 
                    }

                    if (timeout >= 10f)
                    {
                        Debug.LogError("로비 접속 시간 초과!");
                        yield break;
                    }
                }

                // ------------------------------------------------------------------

                // 3. 세션 설정 및 StartGame
                var props = new Dictionary<string, SessionProperty>();
                if (!string.IsNullOrEmpty(pw)) props["password"] = pw;
                props["UserNames"] = nick;
                props["DisplayTopic"] = topic; 
                props["StartTime"] = currentTime; 

                int sceneIndex = GetSceneIndex(gameplaySceneName);

                Debug.Log($"세션 시작 시도: {assignedRoomId}");
                
                // StartGame도 이미 실행 중이면 에러가 날 수 있으니 체크
                if (runnerInsatance.IsCloudReady)
                {
                    intentionalShutdown = false;
                    var startTask = runnerInsatance.StartGame(new StartGameArgs()
                    {
                        Scene = SceneRef.FromIndex(sceneIndex),
                        SessionName = assignedRoomId,
                        GameMode = GameMode.Shared,
                        CustomLobbyName = lobbyName,
                        IsVisible = true,
                        SessionProperties = props
                    });

                    yield return WaitForTask(startTask);

                    if (!startTask.IsFaulted && !startTask.IsCanceled)
                    {
                        RememberSessionForRecovery(assignedRoomId, nick, null);
                    }
                }
            }
        }
    }

    // -------------------------------
    // 세션 참가
    // -------------------------------
    public void RequestJoinSession(SessionInfo session)
    {
        bool hasPwd = session.Properties.ContainsKey("password") &&
                      !string.IsNullOrEmpty(session.Properties["password"].PropertyValue?.ToString());

        if (!hasPwd)
        {
            StartCoroutine(JoinRoomRoutine(session.Name));
        }
        else
        {
            selectedSession = session;
            passwordCheckInput.text = "";
            if (passwordPanelNicknameInput != null)
            {
                passwordPanelNicknameInput.text = GetCurrentNickname();
            }

            passwordPanel.SetActive(true);
        }
    }

    public void OnConfirmPassword()
    {
        if (selectedSession == null) return;

        var prop = selectedSession.Properties["password"];
        string correctPwd = prop.PropertyValue?.ToString().Trim();
        string inputPwd = passwordCheckInput.text.Trim();

        if (inputPwd == correctPwd)
        {
            ApplyPasswordPanelNickname();

            if (passwordWarningImage != null) passwordWarningImage.SetActive(false);
            passwordPanel.SetActive(false);
            StartCoroutine(JoinRoomRoutine(selectedSession.Name));
        }
        else
        {
            StartCoroutine(ShowPasswordWarningRoutine());
        }
    }

    private IEnumerator ShowPasswordWarningRoutine()
    {
        if (passwordWarningImage != null) passwordWarningImage.SetActive(true);
        yield return new WaitForSeconds(3f);
        if (passwordWarningImage != null) passwordWarningImage.SetActive(false);
    }

    public void OnCancelPassword()
    {
        selectedSession = null;
        passwordPanel.SetActive(false);
    }

    private void ApplyPasswordPanelNickname()
    {
        if (passwordPanelNicknameInput == null)
            return;

        string newNickname = passwordPanelNicknameInput.text.Trim();
        if (string.IsNullOrEmpty(newNickname))
            return;

        PlayerPrefs.SetString("PlayerNickname", newNickname);
        PlayerPrefs.Save();

        if (nicknameInput != null)
            nicknameInput.text = newNickname;

        Debug.Log($"Nickname saved: {newNickname}");
    }

    private string GetCurrentNickname()
    {
        if (nicknameInput != null && !string.IsNullOrWhiteSpace(nicknameInput.text))
            return nicknameInput.text.Trim();

        return PlayerPrefs.GetString("PlayerNickname", "Actor_1");
    }

    private IEnumerator JoinRoomRoutine(string sessionName)
    {
        StopSessionRecovery();

        var runner = NetworkManager.runnerInsatance;

        // 1. 서버 API 호출 (방 입장 등록)
        string url = "http://localhost:8000/api/rooms/enter";
        string myNickname = PlayerPrefs.GetString("PlayerNickname", "Unknown");
        
        string jsonPayload = $"{{\"room_id\":\"{sessionName}\", \"nickname\":\"{myNickname}\"}}";

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            Debug.Log($"서버에 방 입장 요청 중... (ID: {sessionName})");
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                RoomEnterResponse response = JsonUtility.FromJson<RoomEnterResponse>(request.downloadHandler.text);
                Debug.Log($"서버 입장 처리 성공! 발급된 User ID: {response.result.user_id}");

                // ==========================================================
                // ✅ [데이터 연동 추가] 소현 님의 세션 정보 저장
                // ==========================================================
                // SceneRefs는 보통 싱글톤이거나 씬에 하나 있으므로 찾아옵니다.
                // var sceneRefs = FindFirstObjectByType<NodeXR.SceneRefs>();
                // if (sceneRefs != null && sceneRefs.session != null)
                // {
                //     sceneRefs.session.roomId = sessionName;          // 서버의 room_id (UUID)
                //     sceneRefs.session.userId = response.result.user_id; // 서버에서 준 내 ID
                //     Debug.Log("<color=cyan>[Session] 데이터 저장 완료! 소켓 연결 준비 끝.</color>");
                // }
                // else
                // {
                //     Debug.LogWarning("[Session] SceneRefs나 SessionData를 찾을 수 없어 ID를 저장하지 못했습니다.");
                // }
                // ==========================================================

                // 2. 실제 Photon Fusion 세션 접속 시작
                if (runner.IsRunning)
                {
                    suppressShutdownSceneLoad = true;
                    var shutdownTask = runner.Shutdown();
                    yield return WaitForTask(shutdownTask);
                    suppressShutdownSceneLoad = false;
                }

                int sceneIndex = GetSceneIndex(gameplaySceneName);
                if (sceneIndex < 0)
                {
                    Debug.LogError($"'{gameplaySceneName}' 씬을 찾을 수 없습니다!");
                    yield break;
                }

                var args = new StartGameArgs()
                {
                    SessionName = sessionName,
                    GameMode = GameMode.Shared,
                    Scene = SceneRef.FromIndex(sceneIndex),
                    CustomLobbyName = lobbyName,
                };

                intentionalShutdown = false;
                var startTask = runner.StartGame(args);
                yield return WaitForTask(startTask);

                if (!startTask.IsFaulted && !startTask.IsCanceled)
                {
                    RememberSessionForRecovery(sessionName, myNickname, response.result.user_id);
                }
            }
            else
            {
                Debug.LogError($"서버 방 입장 실패: {request.error}\n{request.downloadHandler.text}");
            }
        }
    }

    // 이 함수를 로비 화면이 켜질 때나 '새로고침' 버튼을 누를 때 호출하세요!
    public void RefreshRoomListFromServer() 
    {
        if (SceneManager.GetActiveScene().name != lobbySceneName) return;

        // 기존 UI 항목들 싹 비우기
        foreach (var entry in sessionListUiDictionary.Values) Destroy(entry);
        sessionListUiDictionary.Clear();

        StartCoroutine(GetRoomListRoutine());
    }

    // < 버튼에 연결할 함수
    public void OnClickPreviousDay()
    {
        currentViewDate = currentViewDate.AddDays(-1);
        UpdateDateUIAndRefresh();
    }

    // > 버튼에 연결할 함수
    public void OnClickNextDay()
    {
        currentViewDate = currentViewDate.AddDays(1);
        UpdateDateUIAndRefresh();
    }

    // 날짜 텍스트를 바꾸고 리스트를 새로고침하는 함수
    private void UpdateDateUIAndRefresh()
    {
        if (meetingDateText != null)
        {
            string dayTag = "";
            if (currentViewDate.Date == DateTime.Now.Date) dayTag = " (오늘)";
            else if (currentViewDate.Date == DateTime.Now.Date.AddDays(-1)) dayTag = " (어제)";
            else if (currentViewDate.Date == DateTime.Now.Date.AddDays(1)) dayTag = " (내일)";

            meetingDateText.text = currentViewDate.ToString("yyyy.MM.dd") + dayTag;
        }

        RefreshRoomListFromServer();
    }

    private IEnumerator GetRoomListRoutine() 
    {
        if (sessionListContentParent == null) yield break;

        // 기존 UI 싹 비우기 (날짜 바뀔 때 필수)
        foreach (var entry in sessionListUiDictionary.Values) Destroy(entry);
        sessionListUiDictionary.Clear();

        string url = "http://localhost:8000/api/rooms/list"; 
        using (UnityWebRequest request = UnityWebRequest.Get(url)) 
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success) 
            {
                RoomListResponse response = JsonUtility.FromJson<RoomListResponse>(request.downloadHandler.text);
                
                // 1. 시간순 정렬 (13시 -> 14시)
                response.result.rooms.Sort((a, b) => a.created_at.CompareTo(b.created_at));

                foreach (var room in response.result.rooms) 
                {
                    // ★ [날짜 필터링 추가]
                    // 서버 날짜(yyyy-MM-dd)와 현재 보고 있는 날짜(currentViewDate) 비교
                    DateTime roomDate;
                    if (DateTime.TryParse(room.created_at, out roomDate))
                    {
                        // 날짜(Year, Month, Day)가 같은 경우에만 UI 생성
                        if (roomDate.Date == currentViewDate.Date)
                        {
                            GameObject entry = Instantiate(sessionListEntryPrefab, sessionListContentParent, false);
                            SessionListEntry script = entry.GetComponent<SessionListEntry>();
                            
                            script.roomName.text = room.room_topic;
                            
                            // 시간 표시 (HH:mm)
                            if (room.created_at.Length >= 16) 
                                script.startTimeText.text = room.created_at.Substring(11, 5);
                            
                            // 활성화 여부 체크 및 등록
                            bool isActive = cachedSessionList.Exists(s => s.Name == room.room_id);
                            script.SetupFromDB(room, isActive); 

                            if(!sessionListUiDictionary.ContainsKey(room.room_id))
                                sessionListUiDictionary.Add(room.room_id, entry);
                        }
                    }
                }
            }
        }
    }

    // -------------------------------
    // 세션 리스트 갱신
    // -------------------------------
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        cachedSessionList = sessionList;
        // DeleteOldSessionsFromUI(sessionList);
        CompareList(sessionList);

        if (networkStatusText != null && networkStatusText.text != "Connected to network.")
        {
            networkStatusText.text = "Connected to network.";
            networkStatusText.color = Color.green;
        }
    }

    private void CompareList(List<SessionInfo> sessionList)
    {
        foreach (SessionInfo session in sessionList)
        {
            if (sessionListUiDictionary.ContainsKey(session.Name))
                UpdateEntryUI(session);
            else
                CreateEntryUI(session);
        }
    }

    private void CreateEntryUI(SessionInfo session)
    {
        GameObject newEntry = Instantiate(sessionListEntryPrefab, sessionListContentParent, false);
        SessionListEntry entryScript = newEntry.GetComponent<SessionListEntry>();
        entryScript.Setup(session);
        sessionListUiDictionary.Add(session.Name, newEntry);
        newEntry.SetActive(session.IsVisible);
    }

    private void UpdateEntryUI(SessionInfo session)
    {
        sessionListUiDictionary.TryGetValue(session.Name, out GameObject newEntry);
        SessionListEntry entryScript = newEntry.GetComponent<SessionListEntry>();
        entryScript.Setup(session);
        newEntry.SetActive(session.IsVisible);
    }

    private void DeleteOldSessionsFromUI(List<SessionInfo> sessionList)
    {
        List<string> existingKeys = new List<string>(sessionListUiDictionary.Keys);

        foreach (string key in existingKeys)
        {
            bool exists = sessionList.Exists(s => s.Name == key);
            if (!exists)
            {
                Destroy(sessionListUiDictionary[key]);
                sessionListUiDictionary.Remove(key);
            }
        }
    }

    private int GetSceneIndex(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
            string name = System.IO.Path.GetFileNameWithoutExtension(scenePath);
            if (name == sceneName)
                return i;
        }
        return -1;
    }

    private void LoadRecoverySession()
    {
        lastSessionName = PlayerPrefs.GetString(LastSessionNamePrefsKey, string.Empty);
        lastSessionNickname = PlayerPrefs.GetString(LastSessionNicknamePrefsKey, string.Empty);
        lastSessionUserId = PlayerPrefs.GetString(LastSessionUserIdPrefsKey, string.Empty);
    }

    private void RememberSessionForRecovery(string sessionName, string nickname, string userId)
    {
        lastSessionName = sessionName;
        lastSessionNickname = nickname;
        lastSessionUserId = userId;

        PlayerPrefs.SetString(LastSessionNamePrefsKey, lastSessionName);
        PlayerPrefs.SetString(LastSessionNicknamePrefsKey, lastSessionNickname ?? string.Empty);
        PlayerPrefs.SetString(LastSessionUserIdPrefsKey, lastSessionUserId ?? string.Empty);
        PlayerPrefs.Save();
    }

    private void ClearRecoverySession()
    {
        lastSessionName = string.Empty;
        lastSessionNickname = string.Empty;
        lastSessionUserId = string.Empty;

        PlayerPrefs.DeleteKey(LastSessionNamePrefsKey);
        PlayerPrefs.DeleteKey(LastSessionNicknamePrefsKey);
        PlayerPrefs.DeleteKey(LastSessionUserIdPrefsKey);
        PlayerPrefs.Save();
    }

    private IEnumerator WaitForTask(Task task)
    {
        while (task != null && !task.IsCompleted)
        {
            yield return null;
        }

        if (task != null && task.IsFaulted)
        {
            Debug.LogException(task.Exception);
        }
    }

    private void UpdateNetworkStatus(string message, Color color)
    {
        if (networkStatusText == null) return;

        networkStatusText.text = message;
        networkStatusText.color = color;
    }

    private void StopSessionRecovery()
    {
        if (reconnectRoutine != null)
        {
            StopCoroutine(reconnectRoutine);
            reconnectRoutine = null;
        }

        isRecoveringSession = false;
    }

    private void TryStartSessionRecovery(string reason)
    {
        if (!enableSessionRecovery || intentionalShutdown)
            return;

        if (string.IsNullOrEmpty(lastSessionName))
        {
            Debug.LogWarning($"[SessionRecovery] No cached session to recover. Reason: {reason}");
            UpdateNetworkStatus("Disconnected. Returning to lobby.", Color.red);
            SceneManager.LoadScene(lobbySceneName);
            return;
        }

        if (reconnectRoutine != null)
            return;

        reconnectRoutine = StartCoroutine(SessionRecoveryRoutine(reason));
    }

    private IEnumerator SessionRecoveryRoutine(string reason)
    {
        isRecoveringSession = true;
        Debug.LogWarning($"[SessionRecovery] Trying to recover session '{lastSessionName}'. Reason: {reason}");

        yield return null;

        int sceneIndex = GetSceneIndex(gameplaySceneName);
        if (sceneIndex < 0)
        {
            Debug.LogError($"[SessionRecovery] '{gameplaySceneName}' 씬을 찾을 수 없습니다!");
            FinishSessionRecovery(false);
            yield break;
        }

        for (int attempt = 1; attempt <= maxReconnectAttempts; attempt++)
        {
            UpdateNetworkStatus($"Reconnecting... ({attempt}/{maxReconnectAttempts})", Color.yellow);

            var runner = runnerInsatance;
            if (runner == null)
            {
                Debug.LogError("[SessionRecovery] NetworkRunner is missing.");
                break;
            }

            if (runner.IsRunning)
            {
                suppressShutdownSceneLoad = true;
                var shutdownTask = runner.Shutdown();
                yield return WaitForTask(shutdownTask);
                suppressShutdownSceneLoad = false;
            }

            var args = new StartGameArgs()
            {
                SessionName = lastSessionName,
                GameMode = GameMode.Shared,
                Scene = SceneRef.FromIndex(sceneIndex),
                CustomLobbyName = lobbyName,
            };

            intentionalShutdown = false;
            var startTask = runner.StartGame(args);
            yield return WaitForTask(startTask);

            float timeout = 0f;
            while (timeout < 5f)
            {
                if (runner.IsRunning && runner.SessionInfo.Name == lastSessionName)
                {
                    UpdateNetworkStatus("Reconnected to session.", Color.green);
                    FinishSessionRecovery(true);
                    yield break;
                }

                timeout += Time.deltaTime;
                yield return null;
            }

            float delay = reconnectDelaySeconds + (reconnectBackoffSeconds * (attempt - 1));
            yield return new WaitForSeconds(delay);
        }

        FinishSessionRecovery(false);
    }

    private void FinishSessionRecovery(bool success)
    {
        reconnectRoutine = null;
        isRecoveringSession = false;
        suppressShutdownSceneLoad = false;

        if (success)
        {
            Debug.Log($"[SessionRecovery] Session recovered: {lastSessionName}");
            return;
        }

        Debug.LogWarning($"[SessionRecovery] Failed to recover session: {lastSessionName}");
        UpdateNetworkStatus("Reconnect failed. Returning to lobby.", Color.red);
        SceneManager.LoadScene(lobbySceneName);
    }

    public static void ReturnToLobby()
    {
        var runner = NetworkManager.runnerInsatance;
        if (runner == null)
        {
            SceneManager.LoadScene("LobbyScene");
            return;
        }

        var manager = runner.GetComponent<NetworkManager>();
        if (manager != null)
        {
            manager.intentionalShutdown = true;
            manager.StopSessionRecovery();
            manager.ClearRecoverySession();
        }

        var playerObject = runner.GetPlayerObject(runner.LocalPlayer);
        if (playerObject != null)
        {
            runner.Despawn(playerObject);
        }

        runner.Shutdown(true, ShutdownReason.Ok);
    }

    // -------------------------------
    // Fusion 콜백
    // -------------------------------
    void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner)
    {
        if (networkStatusText != null)
        {
            networkStatusText.text = "Connected to network.";
            networkStatusText.color = Color.green;
        }

        if (isRecoveringSession)
        {
            Debug.Log($"[SessionRecovery] Connected while recovering session '{lastSessionName}'.");
        }

        RefreshRoomListFromServer();
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        if (networkStatusText != null)
        {
            networkStatusText.text = "Failed to connect.";
            networkStatusText.color = Color.cyan;
        }

        Debug.LogWarning($"[NetworkManager] Connect failed: {reason}");
        TryStartSessionRecovery($"ConnectFailed: {reason}");
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log($"[NetworkManager] Runner Shutdown: {shutdownReason}");

        if (suppressShutdownSceneLoad || isRecoveringSession || reconnectRoutine != null)
            return;

        if (intentionalShutdown || shutdownReason == ShutdownReason.Ok)
        {
            SceneManager.LoadScene(lobbySceneName);
            return;
        }

        TryStartSessionRecovery($"Shutdown: {shutdownReason}");
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        StartCoroutine(SpawnAfterSceneLoad());
    }

    private IEnumerator SpawnAfterSceneLoad()
    {
        yield return null;

        var spawner = FindFirstObjectByType<PlayerSpawner>();
        if (spawner != null)
            spawner.SpawnLocalPlayer();
        else
            Debug.Log("[NetworkManager] PlayerSpawner not found in scene. Skipping lobby-style player spawn.");
    }


    public void GetRoomInfo(string roomId, Action<RoomInfoResult> callback)
    {
        StartCoroutine(GetRoomInfoRoutine(roomId, callback));
    }

    private IEnumerator GetRoomInfoRoutine(string roomId, Action<RoomInfoResult> callback)
    {
        string escapedRoomId = UnityWebRequest.EscapeURL(roomId);
        string url = $"http://localhost:8000/api/rooms/{escapedRoomId}/info";
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                RoomInfoResponse response = JsonUtility.FromJson<RoomInfoResponse>(request.downloadHandler.text);
                if (response.isSuccess)
                {
                    callback?.Invoke(response.result);
                }
            }
            else
            {
                Debug.LogError($"방 정보 로드 실패: {request.error}");
            }
        }
    }


    // --- 나머지 콜백 (빈 구현 유지) ---
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.LogWarning($"[NetworkManager] Disconnected from server: {reason}");
        TryStartSessionRecovery($"Disconnected: {reason}");
    }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
    {
        Debug.LogWarning("[NetworkManager] Host migration requested. Trying session recovery.");
        TryStartSessionRecovery("HostMigration");
    }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        // --- 1. 로비 목록용 (기존 로직 유지): 외부에 참여자 명단을 노출하기 위함 ---
        if (runner.IsSharedModeMasterClient)
        {
            if (player != runner.LocalPlayer) 
            {
                // 입장한 사람의 닉네임을 가져와서 세션 프로퍼티에 추가 (로비 리스트 업데이트용)
                string newNickname = PlayerPrefs.GetString("PlayerNickname", "User"); 
                var props = runner.SessionInfo.Properties;

                if (props.TryGetValue("UserNames", out var existingNames))
                {
                    string currentList = existingNames.PropertyValue.ToString();
                    if (!currentList.Contains(newNickname))
                    {
                        string updated = $"{currentList} · {newNickname}";
                        Dictionary<string, SessionProperty> nextProps = new Dictionary<string, SessionProperty>();
                        foreach (var p in props) nextProps.Add(p.Key, p.Value);
                        nextProps["UserNames"] = updated;
                        
                        runner.SessionInfo.UpdateCustomProperties(nextProps);
                        Debug.Log($"[Lobby Sync] 세션 프로퍼티 'UserNames' 갱신 완료: {updated}");
                    }
                }
            }
        }

        // --- 2. 회의실 내부 UI용 (추가 로직): 지선님의 상세 명단 자동 업데이트 ---
        PlayerListUI ui = FindFirstObjectByType<PlayerListUI>();
        if (ui != null)
        {
            StartCoroutine(DelayedRefresh(ui));
        }
    }

    private IEnumerator DelayedRefresh(PlayerListUI ui)
    {
        yield return new WaitForSeconds(0.7f);
        ui.FetchDBRoomInfo(); // 서버 DB에서 방 주제, 방장 정보 등 상세 데이터 로드
    }
    
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
}

// JSON 파싱을 위한 클래스 (파일 끝에 추가하세요)
[System.Serializable]
public class RoomResponse {
    public bool isSuccess;
    public RoomResult result;
}

[System.Serializable]
public class RoomResult {
    public string room_id;
}

[System.Serializable]
public class RoomEnterResponse {
    public bool isSuccess;
    public RoomEnterResult result;
}

[System.Serializable]
public class RoomEnterResult {
    public string user_id;
}

[System.Serializable]
public class RoomListResponse {
    public bool isSuccess;
    public string code;
    public string message;
    public RoomListResult result;
}

[System.Serializable]
public class RoomListResult {
    public List<ServerRoomData> rooms;
}

[System.Serializable]
public class ServerRoomData {
    public string room_id;
    public string room_topic;
    public string created_at; // 서버에서 "2026-01-15 07:56:23..." 형식으로 옵니다.
}

//회의 정보 조회
[System.Serializable]
public class RoomInfoResponse {
    public bool isSuccess;
    public RoomInfoResult result;
}

[System.Serializable]
public class RoomInfoResult {
    public string room_topic;
    public List<UserInfo> users;
}

[System.Serializable]
public class UserInfo {
    public string nickname;
    public bool leader;
}
