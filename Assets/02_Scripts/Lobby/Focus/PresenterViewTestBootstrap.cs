using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

public class PresenterViewTestBootstrap : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Photon Test Session")]
    [SerializeField] private string sessionName = "PresenterViewTestRoom";
    [SerializeField] private string lobbyName = "PresenterViewTestLobby";
    [SerializeField] private bool autoStart = true;

    [Header("Player")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private bool autoSpawnPlayer = true;
    [SerializeField] private Vector3 spawnOrigin = new Vector3(0f, 1f, 0f);
    [SerializeField] private float spawnSpacing = 2.2f;
    [SerializeField] private int spawnGridColumns = 4;

    [Header("Test Environment")]
    [SerializeField] private bool createTestEnvironment = true;
    [SerializeField] private string testCameraRigObjectName = "[BuildingBlock] Camera Rig";

    private NetworkRunner runner;
    private Coroutine startRoutine;
    private Coroutine spawnRoutine;
    private string lastStatus = "Idle";

    public NetworkRunner Runner => runner;
    public string SessionName => sessionName;
    public string LastStatus => lastStatus;
    public bool IsRunning => runner != null && runner.IsRunning;

    private void Awake()
    {
        EnsureRunner();

        if (createTestEnvironment)
        {
            CreateTestEnvironment();
        }
    }

    private void Start()
    {
        if (autoStart)
        {
            StartTestSession();
        }
    }

    private void OnDestroy()
    {
        if (runner != null)
        {
            runner.RemoveCallbacks(this);
        }
    }

    public void StartTestSession()
    {
        if (startRoutine != null)
        {
            return;
        }

        startRoutine = StartCoroutine(StartTestSessionRoutine());
    }

    private IEnumerator StartTestSessionRoutine()
    {
        EnsureRunner();

        if (runner.IsRunning)
        {
            lastStatus = $"Already joined {runner.SessionInfo.Name}";
            QueueSpawnLocalPlayer();
            startRoutine = null;
            yield break;
        }

        lastStatus = $"Joining {sessionName}...";

        NetworkManager.runnerInsatance = runner;
        runner.ProvideInput = true;
        runner.RemoveCallbacks(this);
        runner.AddCallbacks(this);

        NetworkSceneManagerDefault sceneManager = GetComponent<NetworkSceneManagerDefault>();
        if (sceneManager == null)
        {
            sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();
        }

        var startTask = runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = sessionName,
            CustomLobbyName = lobbyName,
            IsVisible = true,
            IsOpen = true,
            SceneManager = sceneManager
        });

        while (!startTask.IsCompleted)
        {
            yield return null;
        }

        if (startTask.IsFaulted || startTask.IsCanceled)
        {
            lastStatus = $"Join failed: {sessionName}";
            if (startTask.Exception != null)
            {
                Debug.LogException(startTask.Exception);
            }
        }
        else
        {
            lastStatus = $"Joined {sessionName}";
            QueueSpawnLocalPlayer();
        }

        startRoutine = null;
    }

    private void EnsureRunner()
    {
        if (runner != null)
        {
            return;
        }

        runner = GetComponent<NetworkRunner>();
        if (runner == null)
        {
            runner = gameObject.AddComponent<NetworkRunner>();
        }

        NetworkManager.runnerInsatance = runner;
    }

    private void QueueSpawnLocalPlayer()
    {
        if (!autoSpawnPlayer || spawnRoutine != null)
        {
            return;
        }

        spawnRoutine = StartCoroutine(SpawnLocalPlayerWhenReady());
    }

    private IEnumerator SpawnLocalPlayerWhenReady()
    {
        while (runner == null || !runner.IsRunning || runner.LocalPlayer == PlayerRef.None)
        {
            yield return null;
        }

        yield return null;

        if (playerPrefab == null)
        {
            lastStatus = "Player prefab is missing.";
            Debug.LogError("[PresenterViewTestBootstrap] Player prefab is missing.");
            spawnRoutine = null;
            yield break;
        }

        if (runner.GetPlayerObject(runner.LocalPlayer) != null)
        {
            spawnRoutine = null;
            yield break;
        }

        int playerIndex = Mathf.Max(0, runner.LocalPlayer.PlayerId - 1);
        Vector3 spawnPosition = GetSpawnPosition(playerIndex);
        Quaternion spawnRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

        runner.Spawn(
            playerPrefab,
            spawnPosition,
            spawnRotation,
            runner.LocalPlayer,
            (spawnRunner, obj) =>
            {
                spawnRunner.SetPlayerObject(spawnRunner.LocalPlayer, obj);

                PlayerInfo playerInfo = obj.GetComponent<PlayerInfo>();
                if (playerInfo != null)
                {
                    playerInfo.SetPlayerName($"Presenter Test {spawnRunner.LocalPlayer.PlayerId}");
                }
            });

        lastStatus = $"Spawned local player {runner.LocalPlayer.PlayerId}";
        spawnRoutine = null;
    }

    private Vector3 GetSpawnPosition(int playerIndex)
    {
        int columns = Mathf.Max(1, spawnGridColumns);
        int row = playerIndex / columns;
        int column = playerIndex % columns;
        float x = (column - ((columns - 1) * 0.5f)) * spawnSpacing;
        float z = row * spawnSpacing;
        return spawnOrigin + new Vector3(x, 0f, z);
    }

    private void CreateTestEnvironment()
    {
        EnsureTestCameraRigProxy();

        if (GameObject.Find("Presenter View Test Floor") != null)
        {
            return;
        }

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Presenter View Test Floor";
        floor.transform.position = Vector3.zero;
        floor.transform.localScale = new Vector3(2.5f, 1f, 2.5f);
        ApplyColor(floor, new Color(0.22f, 0.25f, 0.28f));

        CreateMarkerCube("Presenter View Marker A", new Vector3(-3f, 0.5f, 4f), new Color(0.85f, 0.2f, 0.18f));
        CreateMarkerCube("Presenter View Marker B", new Vector3(0f, 0.5f, 5.5f), new Color(0.2f, 0.65f, 0.9f));
        CreateMarkerCube("Presenter View Marker C", new Vector3(3f, 0.5f, 4f), new Color(0.95f, 0.78f, 0.2f));
    }

    private void EnsureTestCameraRigProxy()
    {
        if (string.IsNullOrWhiteSpace(testCameraRigObjectName) || GameObject.Find(testCameraRigObjectName) != null)
        {
            return;
        }

        GameObject rigProxy = new GameObject(testCameraRigObjectName);
        Transform cameraTransform = Camera.main != null ? Camera.main.transform : null;

        if (cameraTransform != null)
        {
            rigProxy.transform.SetParent(cameraTransform, false);
            rigProxy.transform.localPosition = Vector3.zero;
            rigProxy.transform.localRotation = Quaternion.identity;
        }
    }

    private void CreateMarkerCube(string markerName, Vector3 position, Color color)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = markerName;
        marker.transform.position = position;
        marker.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
        ApplyColor(marker, color);
    }

    private void ApplyColor(GameObject target, Color color)
    {
        Renderer targetRenderer = target.GetComponent<Renderer>();
        if (targetRenderer == null)
        {
            return;
        }

        Shader shader = FindTestObjectShader();
        if (shader == null)
        {
            targetRenderer.material.color = color;
            return;
        }

        Material material = new Material(shader)
        {
            name = $"{target.name} Material"
        };

        SetMaterialColor(material, color);
        targetRenderer.sharedMaterial = material;
    }

    private Shader FindTestObjectShader()
    {
        return Shader.Find("Universal Render Pipeline/Lit") ??
               Shader.Find("Universal Render Pipeline/Simple Lit") ??
               Shader.Find("Universal Render Pipeline/Unlit") ??
               Shader.Find("Standard") ??
               Shader.Find("Unlit/Color");
    }

    private void SetMaterialColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }

    public void OnPlayerJoined(NetworkRunner callbackRunner, PlayerRef player)
    {
        if (player == callbackRunner.LocalPlayer)
        {
            QueueSpawnLocalPlayer();
        }
    }

    public void OnSceneLoadDone(NetworkRunner callbackRunner)
    {
        QueueSpawnLocalPlayer();
    }

    public void OnShutdown(NetworkRunner callbackRunner, ShutdownReason shutdownReason)
    {
        lastStatus = $"Shutdown: {shutdownReason}";
    }

    void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner callbackRunner)
    {
        lastStatus = $"Connected: {sessionName}";
    }

    public void OnConnectFailed(NetworkRunner callbackRunner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        lastStatus = $"Connect failed: {reason}";
    }

    void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner callbackRunner, NetDisconnectReason reason)
    {
        lastStatus = $"Disconnected: {reason}";
    }

    public void OnObjectExitAOI(NetworkRunner callbackRunner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner callbackRunner, NetworkObject obj, PlayerRef player) { }
    public void OnPlayerLeft(NetworkRunner callbackRunner, PlayerRef player) { }
    public void OnInput(NetworkRunner callbackRunner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner callbackRunner, PlayerRef player, NetworkInput input) { }
    public void OnConnectRequest(NetworkRunner callbackRunner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnUserSimulationMessage(NetworkRunner callbackRunner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner callbackRunner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner callbackRunner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner callbackRunner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner callbackRunner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner callbackRunner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadStart(NetworkRunner callbackRunner) { }
}
