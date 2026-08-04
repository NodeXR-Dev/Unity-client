using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class MeetingRoomSpawnLayout : MonoBehaviour
{
    [Header("Center")]
    [SerializeField] private Transform centerTransform;
    [SerializeField] private string centerObjectName = "CenterCube";
    [SerializeField] private bool useOwnTransformAsCenter = true;

    [Header("Layout")]
    [SerializeField, Min(0f)] private float spawnRadius = 2.2f;
    [SerializeField] private float spawnHeightOffset = 0f;
    [SerializeField, Min(0f)] private float occupiedRadius = 1.1f;
    [SerializeField, Min(0f)] private float extraRingSpacing = 1.5f;
    [SerializeField, Min(0)] private int maxExtraRings = 3;
    [SerializeField, Min(1)] private int minimumSlotCount = 1;
    [SerializeField] private float slotCenterAngleDegrees = 90f;
    [SerializeField, Range(1f, 360f)] private float slotArcDegrees = 360f;
    [SerializeField] private bool faceCenterOnSpawn = true;
    [SerializeField] private bool preferLocalPlayerSlot = true;
    [SerializeField] private bool includePlayerInfoObjects = true;

    [Header("Gizmos")]
    [SerializeField, Min(1)] private int gizmoPreviewPlayerCount = 6;
    [SerializeField] private bool drawGizmosWhenUnselected = true;
    [SerializeField, Min(0.01f)] private float gizmoPointRadius = 0.12f;

    public bool TryGetSpawnPose(
        NetworkRunner runner,
        out Vector3 spawnPosition,
        out Quaternion spawnRotation)
    {
        if (!TryResolveCenterTransform(out Transform center))
        {
            spawnPosition = default;
            spawnRotation = default;
            return false;
        }

        Vector3 centerPosition = GetSpawnCenterPosition(center);
        List<Vector3> occupiedPositions = CollectOccupiedPositions(runner);
        int expectedPlayerCount = GetExpectedPlayerCount(runner, occupiedPositions.Count);
        int slotCount = GetSlotCount(expectedPlayerCount);
        int startSlot = GetStartSlot(runner, slotCount);

        for (int ring = 0; ring <= maxExtraRings; ring++)
        {
            float radius = spawnRadius + (ring * extraRingSpacing);

            for (int offset = 0; offset < slotCount; offset++)
            {
                int slot = (startSlot + offset) % slotCount;
                Vector3 candidate = GetSlotPosition(centerPosition, radius, slot, slotCount);

                if (!IsOccupied(candidate, occupiedPositions))
                {
                    spawnPosition = candidate;
                    spawnRotation = GetSpawnRotation(candidate, centerPosition);
                    return true;
                }
            }
        }

        float fallbackRadius = spawnRadius + ((maxExtraRings + 1) * extraRingSpacing);
        int fallbackSlot = startSlot % slotCount;
        spawnPosition = GetSlotPosition(centerPosition, fallbackRadius, fallbackSlot, slotCount);
        spawnRotation = GetSpawnRotation(spawnPosition, centerPosition);
        return true;
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
                    includesLocalPlayer = true;
            }

            if (!includesLocalPlayer && runner.LocalPlayer != PlayerRef.None)
                activeCount++;
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
                    AddOccupiedPosition(occupiedPositions, playerObject.transform.position);
            }
        }

        if (!includePlayerInfoObjects)
            return occupiedPositions;

#if UNITY_2023_1_OR_NEWER
        PlayerInfo[] playerInfos = FindObjectsByType<PlayerInfo>(FindObjectsSortMode.None);
#else
        PlayerInfo[] playerInfos = FindObjectsOfType<PlayerInfo>();
#endif

        foreach (PlayerInfo playerInfo in playerInfos)
        {
            if (playerInfo != null)
                AddOccupiedPosition(occupiedPositions, playerInfo.transform.position);
        }

        return occupiedPositions;
    }

    private static void AddOccupiedPosition(List<Vector3> occupiedPositions, Vector3 position)
    {
        const float duplicateDistance = 0.05f;

        foreach (Vector3 occupiedPosition in occupiedPositions)
        {
            if (SqrDistanceXZ(occupiedPosition, position) <= duplicateDistance * duplicateDistance)
                return;
        }

        occupiedPositions.Add(position);
    }

    private bool IsOccupied(Vector3 candidate, List<Vector3> occupiedPositions)
    {
        float occupiedRadiusSqr = occupiedRadius * occupiedRadius;

        foreach (Vector3 occupiedPosition in occupiedPositions)
        {
            if (SqrDistanceXZ(candidate, occupiedPosition) <= occupiedRadiusSqr)
                return true;
        }

        return false;
    }

    private int GetSlotCount(int playerCount)
    {
        return Mathf.Max(1, Mathf.Max(minimumSlotCount, playerCount));
    }

    private int GetStartSlot(NetworkRunner runner, int slotCount)
    {
        if (!preferLocalPlayerSlot || runner == null || slotCount <= 1)
            return 0;

        int index = 0;
        foreach (PlayerRef player in runner.ActivePlayers)
        {
            if (player == runner.LocalPlayer)
                return index % slotCount;

            index++;
        }

        return 0;
    }

    private Vector3 GetSlotPosition(Vector3 centerPosition, float radius, int slot, int slotCount)
    {
        Vector3 direction = GetSlotDirection(slot, slotCount);
        return centerPosition + (direction * radius);
    }

    private Vector3 GetSlotDirection(int slot, int slotCount)
    {
        float angle = GetSlotAngle(slot, slotCount);
        float radians = angle * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)).normalized;
    }

    private float GetSlotAngle(int slot, int slotCount)
    {
        if (slotCount <= 1)
            return slotCenterAngleDegrees;

        if (slotArcDegrees >= 359.9f)
        {
            if (slotCount == 4)
            {
                switch (slot)
                {
                    case 0:
                        return 90f;
                    case 1:
                        return 0f;
                    case 2:
                        return -90f;
                    default:
                        return 180f;
                }
            }

            return slotCenterAngleDegrees - ((360f / slotCount) * slot);
        }

        if (slot <= 0)
            return slotCenterAngleDegrees;

        float halfArc = Mathf.Clamp(slotArcDegrees, 1f, 360f) * 0.5f;
        int pairIndex = (slot + 1) / 2;
        int maxPairIndex = Mathf.Max(1, Mathf.CeilToInt((slotCount - 1) * 0.5f));
        float angleStep = halfArc / maxPairIndex;
        float side = slot % 2 == 1 ? -1f : 1f;

        return slotCenterAngleDegrees + (side * pairIndex * angleStep);
    }

    private Quaternion GetSpawnRotation(Vector3 spawnPosition, Vector3 centerPosition)
    {
        if (!faceCenterOnSpawn)
            return transform.rotation;

        Vector3 lookDirection = centerPosition - spawnPosition;
        lookDirection.y = 0f;

        if (lookDirection.sqrMagnitude <= 0.0001f)
            return Quaternion.identity;

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
            return true;

        if (!string.IsNullOrWhiteSpace(centerObjectName))
        {
            GameObject centerObject = GameObject.Find(centerObjectName);
            if (centerObject != null)
            {
                resolvedCenter = centerObject.transform;
                return true;
            }
        }

        if (useOwnTransformAsCenter)
        {
            resolvedCenter = transform;
            return true;
        }

        return false;
    }

    private void OnValidate()
    {
        spawnRadius = Mathf.Max(0f, spawnRadius);
        occupiedRadius = Mathf.Max(0f, occupiedRadius);
        extraRingSpacing = Mathf.Max(0f, extraRingSpacing);
        maxExtraRings = Mathf.Max(0, maxExtraRings);
        minimumSlotCount = Mathf.Max(1, minimumSlotCount);
        slotArcDegrees = Mathf.Clamp(slotArcDegrees, 1f, 360f);
        gizmoPreviewPlayerCount = Mathf.Max(1, gizmoPreviewPlayerCount);
        gizmoPointRadius = Mathf.Max(0.01f, gizmoPointRadius);
    }

    private void OnDrawGizmos()
    {
        if (drawGizmosWhenUnselected)
            DrawSpawnGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmosWhenUnselected)
            DrawSpawnGizmos();
    }

    private void DrawSpawnGizmos()
    {
        if (!TryResolveCenterTransform(out Transform center))
            return;

        Vector3 centerPosition = GetSpawnCenterPosition(center);
        int slotCount = GetSlotCount(gizmoPreviewPlayerCount);

        Gizmos.color = new Color(0.25f, 0.9f, 1f, 0.95f);
        if (slotArcDegrees >= 359.9f)
            DrawFlatCircle(centerPosition, spawnRadius, 80);
        else
            DrawFlatArc(
                centerPosition,
                spawnRadius,
                slotCenterAngleDegrees,
                slotArcDegrees,
                80);

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
            return;

        Vector3 previousPoint = GetCirclePoint(center, radius, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = (360f / segments) * i * Mathf.Deg2Rad;
            Vector3 nextPoint = GetCirclePoint(center, radius, angle);
            Gizmos.DrawLine(previousPoint, nextPoint);
            previousPoint = nextPoint;
        }
    }

    private static void DrawFlatArc(
        Vector3 center,
        float radius,
        float centerAngleDegrees,
        float arcDegrees,
        int segments)
    {
        if (radius <= 0f || segments < 1)
            return;

        float clampedArcDegrees = Mathf.Clamp(arcDegrees, 1f, 360f);
        float startAngleDegrees =
            centerAngleDegrees - (clampedArcDegrees * 0.5f);
        Vector3 previousPoint = GetCirclePoint(
            center,
            radius,
            startAngleDegrees * Mathf.Deg2Rad);

        for (int i = 1; i <= segments; i++)
        {
            float angleDegrees =
                startAngleDegrees +
                ((clampedArcDegrees / segments) * i);
            Vector3 nextPoint = GetCirclePoint(
                center,
                radius,
                angleDegrees * Mathf.Deg2Rad);
            Gizmos.DrawLine(previousPoint, nextPoint);
            previousPoint = nextPoint;
        }
    }

    private static Vector3 GetCirclePoint(
        Vector3 center,
        float radius,
        float radians)
    {
        return center + new Vector3(
            Mathf.Cos(radians) * radius,
            0f,
            Mathf.Sin(radians) * radius);
    }

    private static float SqrDistanceXZ(Vector3 a, Vector3 b)
    {
        float x = a.x - b.x;
        float z = a.z - b.z;
        return (x * x) + (z * z);
    }
}
