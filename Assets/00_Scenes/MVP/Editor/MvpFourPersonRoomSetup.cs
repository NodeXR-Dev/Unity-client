#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// MVP 회의실의 테이블과 의자를 네 명이 서로 마주 보고 손을 뻗기 좋은 크기로 정리한다.
// 씬 YAML을 직접 수정하지 않고 이 메뉴를 통해서만 배치를 저장한다.
public static class MvpFourPersonRoomSetup
{
    private const string ScenePath =
        "Assets/00_Scenes/MVP/MVP.unity";
    private const float TargetLongSide = 1.80f;
    private const float TargetShortSide = 1.05f;
    private const float ChairSideGap = 0.48f;
    private const float ChairAlongOffset = 0.46f;

    [MenuItem("Tools/MVP/Apply 4 Person Meeting Layout")]
    public static void ApplyFourPersonLayout()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogWarning(
                "[MVP Room] MVP.unity를 연 뒤 실행해 주세요. 현재 씬: " +
                scene.path);
            return;
        }

        Transform table = FindTable(scene);
        if (table == null ||
            !TryGetRendererBounds(table, out Bounds tableBounds))
        {
            Debug.LogError(
                "[MVP Room] Table_01 또는 렌더러 경계를 찾지 못했습니다.");
            return;
        }

        ResizeTable(table, tableBounds);
        Physics.SyncTransforms();
        TryGetRendererBounds(table, out tableBounds);

        List<Transform> chairs = FindChairs(scene, tableBounds.center);
        if (chairs.Count < 4)
        {
            Debug.LogError(
                "[MVP Room] Chair_02가 네 개보다 적습니다. 현재: " +
                chairs.Count);
            return;
        }

        ArrangeFourChairs(table, tableBounds, chairs);

        MvpWorkspaceLayout workspace =
            UnityEngine.Object.FindFirstObjectByType<MvpWorkspaceLayout>();
        if (workspace != null)
        {
            Undo.RecordObject(
                workspace,
                "Refresh MVP Workspace For Four Person Room");
            workspace.ApplyPanelPreview();
            EditorUtility.SetDirty(workspace);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log(
            "[MVP Room] 4인 회의 배치 완료 · 테이블 " +
            tableBounds.size.x.ToString("F2") + "m × " +
            tableBounds.size.z.ToString("F2") +
            "m, 활성 의자 4개");
    }

    private static Transform FindTable(Scene scene)
    {
        Transform[] transforms =
            Resources.FindObjectsOfTypeAll<Transform>();
        foreach (Transform candidate in transforms)
        {
            if (candidate != null &&
                candidate.gameObject.scene == scene &&
                candidate.name == "Table_01")
                return candidate;
        }

        return null;
    }

    private static List<Transform> FindChairs(
        Scene scene,
        Vector3 tableCenter)
    {
        List<Transform> chairs = new List<Transform>();
        Transform[] transforms =
            Resources.FindObjectsOfTypeAll<Transform>();

        foreach (Transform candidate in transforms)
        {
            if (candidate == null ||
                candidate.gameObject.scene != scene ||
                !candidate.name.StartsWith(
                    "Chair_02",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            // Chair_02 루트만 사용하고 내부 메시 자식은 제외한다.
            if (candidate.parent != null &&
                candidate.parent.name.StartsWith(
                    "Chair_02",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            if (candidate.GetComponentInChildren<Renderer>(true) == null)
                continue;

            chairs.Add(candidate);
        }

        chairs.Sort((left, right) =>
        {
            float leftDistance =
                (left.position - tableCenter).sqrMagnitude;
            float rightDistance =
                (right.position - tableCenter).sqrMagnitude;
            int distanceOrder =
                leftDistance.CompareTo(rightDistance);
            if (distanceOrder != 0)
                return distanceOrder;
            return string.Compare(
                left.name,
                right.name,
                StringComparison.Ordinal);
        });
        return chairs;
    }

    private static void ResizeTable(
        Transform table,
        Bounds bounds)
    {
        Vector3 right = Horizontal(table.right, Vector3.right);
        Vector3 forward = Horizontal(table.forward, Vector3.forward);
        float rightSize = ProjectedSize(bounds.size, right);
        float forwardSize = ProjectedSize(bounds.size, forward);

        bool rightIsLong = rightSize >= forwardSize;
        float targetRight =
            rightIsLong ? TargetLongSide : TargetShortSide;
        float targetForward =
            rightIsLong ? TargetShortSide : TargetLongSide;

        Vector3 scale = table.localScale;
        scale.x *= targetRight / Mathf.Max(0.05f, rightSize);
        scale.z *= targetForward / Mathf.Max(0.05f, forwardSize);

        Undo.RecordObject(table, "Resize MVP Table For Four People");
        table.localScale = scale;
        EditorUtility.SetDirty(table);
    }

    private static void ArrangeFourChairs(
        Transform table,
        Bounds tableBounds,
        List<Transform> chairs)
    {
        Vector3 right = Horizontal(table.right, Vector3.right);
        Vector3 forward = Horizontal(table.forward, Vector3.forward);
        float rightSize = ProjectedSize(tableBounds.size, right);
        float forwardSize = ProjectedSize(tableBounds.size, forward);

        Vector3 longAxis;
        Vector3 shortAxis;
        if (rightSize >= forwardSize)
        {
            longAxis = right;
            shortAxis = forward;
        }
        else
        {
            longAxis = forward;
            shortAxis = right;
        }

        float shortHalf =
            Mathf.Min(rightSize, forwardSize) * 0.5f;
        float rowDistance = shortHalf + ChairSideGap;

        Vector3[] positions =
        {
            tableBounds.center -
            longAxis * ChairAlongOffset -
            shortAxis * rowDistance,
            tableBounds.center +
            longAxis * ChairAlongOffset -
            shortAxis * rowDistance,
            tableBounds.center -
            longAxis * ChairAlongOffset +
            shortAxis * rowDistance,
            tableBounds.center +
            longAxis * ChairAlongOffset +
            shortAxis * rowDistance
        };

        for (int i = 0; i < chairs.Count; i++)
        {
            Transform chair = chairs[i];
            Undo.RecordObject(
                chair.gameObject,
                "Arrange MVP Four Person Chairs");
            Undo.RecordObject(
                chair,
                "Arrange MVP Four Person Chairs");

            bool active = i < 4;
            chair.gameObject.SetActive(active);
            if (!active)
            {
                EditorUtility.SetDirty(chair.gameObject);
                continue;
            }

            Vector3 position = positions[i];
            position.y = chair.position.y;
            chair.position = position;

            Vector3 inward = Vector3.ProjectOnPlane(
                tableBounds.center - position,
                Vector3.up).normalized;
            if (inward.sqrMagnitude > 0.001f)
                chair.rotation =
                    Quaternion.LookRotation(inward, Vector3.up);

            EditorUtility.SetDirty(chair);
            EditorUtility.SetDirty(chair.gameObject);
        }
    }

    private static Vector3 Horizontal(
        Vector3 value,
        Vector3 fallback)
    {
        value = Vector3.ProjectOnPlane(value, Vector3.up);
        return value.sqrMagnitude > 0.001f
            ? value.normalized
            : fallback;
    }

    private static float ProjectedSize(
        Vector3 axisAlignedSize,
        Vector3 axis)
    {
        axis = new Vector3(
            Mathf.Abs(axis.x),
            Mathf.Abs(axis.y),
            Mathf.Abs(axis.z));
        return Vector3.Dot(axisAlignedSize, axis);
    }

    private static bool TryGetRendererBounds(
        Transform root,
        out Bounds bounds)
    {
        bounds = new Bounds(root.position, Vector3.zero);
        Renderer[] renderers =
            root.GetComponentsInChildren<Renderer>(true);
        bool found = false;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }
}
#endif
