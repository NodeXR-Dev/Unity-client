using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;

public class PlayerSpawner : MonoBehaviour
{
    [Header("Prefab")]
    public GameObject playerPrefab;
    [SerializeField] private bool useSpawnerTransform = true;
    public Vector3 spawnPoint = new Vector3(0, 1, 0);
    public TMP_InputField nameInputField;

    [Header("Meeting Room Spawn")]
    [SerializeField] private bool useCenterCubeSpawn = true;
    [SerializeField] private Transform centerTransform;
    [SerializeField] private string centerObjectName = "CenterCube";
    [SerializeField, Min(0f)] private float spawnRadius = 2.2f;
    [SerializeField] private float spawnHeightOffset = 0f;
    [SerializeField, Min(0f)] private float occupiedRadius = 1.1f;
    [SerializeField, Min(0f)] private float extraRingSpacing = 1.5f;
    [SerializeField, Min(0)] private int maxExtraRings = 3;
    [SerializeField] private bool faceCenterOnSpawn = true;

    [Header("XR Rig")]
    [SerializeField] private bool moveLocalXRRigToSpawn = true;
    [SerializeField] private string cameraRigObjectName = "[BuildingBlock] Camera Rig";

    [Header("Gizmos")]
    [SerializeField, Min(1)] private int gizmoPreviewPlayerCount = 5;
    [SerializeField] private bool drawGizmosWhenUnselected = true;
    [SerializeField, Min(0.01f)] private float gizmoPointRadius = 0.12f;

    public void SpawnLocalPlayer()
    {
        var runner = NetworkManager.runnerInsatance;

        if (runner == null)
        {
            Debug.LogError("[PlayerSpawner] Runner is missing.");
            return;
        }

        if (runner.LocalPlayer == PlayerRef.None)
        {
            Debug.LogError("[PlayerSpawner] LocalPlayer is missing.");
            return;
        }

        if (runner.GetPlayerObject(runner.LocalPlayer) != null)
        {
            Debug.Log("[PlayerSpawner] Local player object already exists. Skip spawn.");
            return;
        }

        GetSpawnPose(runner, out Vector3 spawnPosition, out Quaternion spawnRotation);

        if (moveLocalXRRigToSpawn)
        {
            MoveLocalXRRig(spawnPosition, spawnRotation);
        }

        runner.Spawn(
            playerPrefab,
            spawnPosition,
            spawnRotation,
            runner.LocalPlayer,
            (runner, obj) =>
            {
                runner.SetPlayerObject(runner.LocalPlayer, obj);

                var info = obj.GetComponent<PlayerInfo>();
                if (info != null)
                {
                    string playerName = nameInputField != null && !string.IsNullOrEmpty(nameInputField.text)
                        ? nameInputField.text
                        : "Tester";

                    info.SetPlayerName(playerName);
                }
            }
        );
    }

    private void GetSpawnPose(NetworkRunner runner, out Vector3 spawnPosition, out Quaternion spawnRotation)
    {
        if (useCenterCubeSpawn && TryResolveCenterTransform(out Transform center))
        {
            GetCenterCubeSpawnPose(runner, center, out spawnPosition, out spawnRotation);
            return;
        }

        if (useCenterCubeSpawn)
        {
            Debug.LogWarning($"[PlayerSpawner] Could not find '{centerObjectName}'. Falling back to default spawn.");
        }

        spawnPosition = useSpawnerTransform ? transform.position : spawnPoint;
        spawnRotation = useSpawnerTransform ? transform.rotation : Quaternion.identity;
    }

    private void GetCenterCubeSpawnPose(
        NetworkRunner runner,
        Transform center,
        out Vector3 spawnPosition,
        out Quaternion spawnRotation)
    {
        Vector3 centerPosition = GetSpawnCenterPosition(center);
        List<Vector3> occupiedPositions = CollectOccupiedPositions(runner);
        int slotCount = GetSlotCount(GetExpectedPlayerCount(runner, occupiedPositions.Count));

        for (int ring = 0; ring <= maxExtraRings; ring++)
        {
            float radius = spawnRadius + (ring * extraRingSpacing);

            for (int slot = 0; slot < slotCount; slot++)
            {
                Vector3 candidate = GetSlotPosition(centerPosition, radius, slot, slotCount);

                if (!IsOccupied(candidate, occupiedPositions))
                {
                    spawnPosition = candidate;
                    spawnRotation = GetSpawnRotation(candidate, centerPosition);
                    return;
                }
            }
        }

        float fallbackRadius = spawnRadius + ((maxExtraRings + 1) * extraRingSpacing);
        int fallbackSlot = occupiedPositions.Count % slotCount;
        spawnPosition = GetSlotPosition(centerPosition, fallbackRadius, fallbackSlot, slotCount);
        spawnRotation = GetSpawnRotation(spawnPosition, centerPosition);
    }

    private int GetExpectedPlayerCount(NetworkRunner runner, int occupiedCount)
    {
        int activeCount = 0;
        bool includesLocalPlayer = false;

        if (runner != null)
        {
            foreach (PlayerRef player in runner.ActivePlayers)
            {
                activeCount++;

                if (player == runner.LocalPlayer)
                {
                    includesLocalPlayer = true;
                }
            }

            if (!includesLocalPlayer)
            {
                activeCount++;
            }
        }

        return Mathf.Max(1, Mathf.Max(activeCount, occupiedCount + 1));
    }

    private List<Vector3> CollectOccupiedPositions(NetworkRunner runner)
    {
        List<Vector3> occupiedPositions = new List<Vector3>();

        if (runner != null)
        {
            foreach (PlayerRef player in runner.ActivePlayers)
            {
                NetworkObject playerObject = runner.GetPlayerObject(player);

                if (playerObject != null)
                {
                    AddOccupiedPosition(occupiedPositions, playerObject.transform.position);
                }
            }
        }

#if UNITY_2023_1_OR_NEWER
        PlayerInfo[] playerInfos = FindObjectsByType<PlayerInfo>(FindObjectsSortMode.None);
#else
        PlayerInfo[] playerInfos = FindObjectsOfType<PlayerInfo>();
#endif

        foreach (PlayerInfo playerInfo in playerInfos)
        {
            if (playerInfo != null)
            {
                AddOccupiedPosition(occupiedPositions, playerInfo.transform.position);
            }
        }

        return occupiedPositions;
    }

    private static void AddOccupiedPosition(List<Vector3> occupiedPositions, Vector3 position)
    {
        const float duplicateDistance = 0.05f;

        foreach (Vector3 occupiedPosition in occupiedPositions)
        {
            if (SqrDistanceXZ(occupiedPosition, position) <= duplicateDistance * duplicateDistance)
            {
                return;
            }
        }

        occupiedPositions.Add(position);
    }

    private bool IsOccupied(Vector3 candidate, List<Vector3> occupiedPositions)
    {
        float occupiedRadiusSqr = occupiedRadius * occupiedRadius;

        foreach (Vector3 occupiedPosition in occupiedPositions)
        {
            if (SqrDistanceXZ(candidate, occupiedPosition) <= occupiedRadiusSqr)
            {
                return true;
            }
        }

        return false;
    }

    private int GetSlotCount(int playerCount)
    {
        return Mathf.Max(1, playerCount);
    }

    private Vector3 GetSlotPosition(Vector3 centerPosition, float radius, int slot, int slotCount)
    {
        Vector3 direction = GetSlotDirection(slot, slotCount);
        return centerPosition + (direction * radius);
    }

    private Vector3 GetSlotDirection(int slot, int slotCount)
    {
        if (slotCount <= 1)
        {
            return Vector3.forward;
        }

        if (slotCount == 4)
        {
            switch (slot)
            {
                case 0:
                    return Vector3.forward;
                case 1:
                    return Vector3.right;
                case 2:
                    return Vector3.back;
                default:
                    return Vector3.left;
            }
        }

        float angle = 90f - ((360f / slotCount) * slot);
        float radians = angle * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)).normalized;
    }

    private Quaternion GetSpawnRotation(Vector3 spawnPosition, Vector3 centerPosition)
    {
        if (!faceCenterOnSpawn)
        {
            return useSpawnerTransform ? transform.rotation : Quaternion.identity;
        }

        Vector3 lookDirection = centerPosition - spawnPosition;
        lookDirection.y = 0f;

        if (lookDirection.sqrMagnitude <= 0.0001f)
        {
            return Quaternion.identity;
        }

        return Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
    }

    private Vector3 GetSpawnCenterPosition(Transform center)
    {
        Vector3 centerPosition = center.position;
        centerPosition.y += spawnHeightOffset;
        return centerPosition;
    }

    private bool TryResolveCenterTransform(out Transform resolvedCenter)
    {
        resolvedCenter = centerTransform;

        if (resolvedCenter != null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(centerObjectName))
        {
            return false;
        }

        GameObject centerObject = GameObject.Find(centerObjectName);
        if (centerObject == null)
        {
            return false;
        }

        resolvedCenter = centerObject.transform;
        return true;
    }

    private void MoveLocalXRRig(Vector3 spawnPosition, Quaternion spawnRotation)
    {
        Transform rigTransform = ResolveLocalXRRigTransform();
        if (rigTransform == null)
        {
            return;
        }

        Vector3 eulerAngles = spawnRotation.eulerAngles;
        Quaternion yawOnlyRotation = Quaternion.Euler(0f, eulerAngles.y, 0f);
        rigTransform.SetPositionAndRotation(spawnPosition, yawOnlyRotation);
    }

    private Transform ResolveLocalXRRigTransform()
    {
        if (!string.IsNullOrWhiteSpace(cameraRigObjectName))
        {
            GameObject rigObject = GameObject.Find(cameraRigObjectName);
            if (rigObject != null)
            {
                return rigObject.transform;
            }
        }

#if UNITY_2023_1_OR_NEWER
        Unity.XR.CoreUtils.XROrigin xrOrigin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
#else
        Unity.XR.CoreUtils.XROrigin xrOrigin = FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
#endif
        return xrOrigin != null ? xrOrigin.transform : null;
    }

    private void OnValidate()
    {
        spawnRadius = Mathf.Max(0f, spawnRadius);
        occupiedRadius = Mathf.Max(0f, occupiedRadius);
        extraRingSpacing = Mathf.Max(0f, extraRingSpacing);
        maxExtraRings = Mathf.Max(0, maxExtraRings);
        gizmoPreviewPlayerCount = Mathf.Max(1, gizmoPreviewPlayerCount);
        gizmoPointRadius = Mathf.Max(0.01f, gizmoPointRadius);
    }

    private void OnDrawGizmos()
    {
        if (drawGizmosWhenUnselected)
        {
            DrawSpawnGizmos();
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmosWhenUnselected)
        {
            DrawSpawnGizmos();
        }
    }

    private void DrawSpawnGizmos()
    {
        if (!useCenterCubeSpawn || !TryResolveCenterTransform(out Transform center))
        {
            return;
        }

        Vector3 centerPosition = GetSpawnCenterPosition(center);
        int slotCount = GetSlotCount(gizmoPreviewPlayerCount);

        Gizmos.color = new Color(0.25f, 0.9f, 1f, 0.95f);
        DrawFlatCircle(centerPosition, spawnRadius, 80);

        for (int slot = 0; slot < slotCount; slot++)
        {
            Vector3 slotPosition = GetSlotPosition(centerPosition, spawnRadius, slot, slotCount);

            Gizmos.color = new Color(1f, 0.65f, 0.2f, 0.55f);
            DrawFlatCircle(slotPosition, occupiedRadius, 48);

            Gizmos.color = new Color(0.25f, 0.9f, 1f, 0.95f);
            Gizmos.DrawSphere(slotPosition, gizmoPointRadius);
        }
    }

    private static void DrawFlatCircle(Vector3 center, float radius, int segments)
    {
        if (radius <= 0f || segments < 3)
        {
            return;
        }

        Vector3 previousPoint = center + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = (360f / segments) * i * Mathf.Deg2Rad;
            Vector3 nextPoint = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previousPoint, nextPoint);
            previousPoint = nextPoint;
        }
    }

    private static float SqrDistanceXZ(Vector3 a, Vector3 b)
    {
        float x = a.x - b.x;
        float z = a.z - b.z;
        return (x * x) + (z * z);
    }
}
