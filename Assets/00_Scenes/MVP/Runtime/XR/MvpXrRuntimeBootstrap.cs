using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// MVP 씬이 실행될 때 XR UI·그래프 어댑터와 손 비주얼 안전장치를 준비한다.
public static class MvpXrRuntimeBootstrap
{
    private const string MvpScenePath =
        "Assets/00_Scenes/MVP/MVP.unity";

    private static readonly List<Renderer> DuplicateRenderers =
        new List<Renderer>();
    private static readonly List<MonoBehaviour> DuplicateHandVisuals =
        new List<MonoBehaviour>();

    internal static int SuppressedRendererCount => DuplicateRenderers.Count;
    internal static int SuppressedHandVisualCount => DuplicateHandVisuals.Count;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureMvpXrAdapters()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != MvpScenePath)
            return;

        GameObject app = GameObject.Find("MvpApp");
        if (app == null)
            app = new GameObject("MvpApp");

        if (app.GetComponent<MvpWristSettingsMenu>() == null)
            app.AddComponent<MvpWristSettingsMenu>();
        if (app.GetComponent<MvpXrInteractionBridge>() == null)
            app.AddComponent<MvpXrInteractionBridge>();
        if (app.GetComponent<MvpMeetingRoomPlayerController>() == null)
            app.AddComponent<MvpMeetingRoomPlayerController>();
        if (app.GetComponent<MvpNodeInteractionController>() == null)
            app.AddComponent<MvpNodeInteractionController>();

        MvpXrGraphLinkController graphLink =
            app.GetComponent<MvpXrGraphLinkController>();
        if (graphLink == null)
            graphLink = app.AddComponent<MvpXrGraphLinkController>();

        graphLink.Configure(
            UnityEngine.Object.FindFirstObjectByType<GraphManager>());

        MvpHandVisualGuard guard =
            app.GetComponent<MvpHandVisualGuard>();
        if (guard == null)
            guard = app.AddComponent<MvpHandVisualGuard>();
        guard.EnforceNow();
    }

    internal static void RefreshDuplicateHandVisualCache()
    {
        DuplicateRenderers.Clear();
        DuplicateHandVisuals.Clear();

        Transform[] transforms =
            Resources.FindObjectsOfTypeAll<Transform>();

        // Comprehensive 리그가 실제 손 한 세트를 소유한다.
        // 별도 Hand Tracking Building Block은 입력까지 중복되므로 루트 자체를 끈다.
        foreach (Transform candidate in transforms)
        {
            if (candidate == null ||
                !candidate.gameObject.scene.IsValid() ||
                !candidate.name.StartsWith(
                    "[BuildingBlock] Hand Tracking ",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (Renderer renderer in
                     candidate.GetComponentsInChildren<Renderer>(true))
                SuppressRenderer(renderer);

            candidate.gameObject.SetActive(false);
        }

        // 합성 손 데이터는 포크/그랩 제약 계산에 필요하므로 비활성화하지 않는다.
        // 대신 합성 손 아래의 HandVisual만 멈춰 SDK가 메시를 다시 켜지 못하게 한다.
        foreach (MonoBehaviour behaviour in
                 Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            if (behaviour == null ||
                !behaviour.gameObject.scene.IsValid() ||
                behaviour.GetType().FullName !=
                    "Oculus.Interaction.HandVisual" ||
                !IsSyntheticVisualPath(behaviour.transform))
                continue;

            if (!DuplicateHandVisuals.Contains(behaviour))
                DuplicateHandVisuals.Add(behaviour);

            foreach (Renderer renderer in
                     behaviour.GetComponentsInChildren<Renderer>(true))
                AddDuplicateRenderer(renderer);
        }

        // HandSphereMap의 관절 구체와 synthetic/reticle 하위 메시도 렌더에서 제외한다.
        // 실제 OVRLeft/RightHandVisual과 그 안의 OpenXR 대체 메시는 그대로 둔다.
        foreach (Renderer renderer in
                 Resources.FindObjectsOfTypeAll<Renderer>())
        {
            if (renderer == null ||
                !renderer.gameObject.scene.IsValid() ||
                !IsSyntheticVisualPath(renderer.transform))
                continue;

            AddDuplicateRenderer(renderer);
        }

        ForceCachedDuplicateVisualsOff();
    }

    internal static void ForceCachedDuplicateVisualsOff()
    {
        foreach (MonoBehaviour visual in DuplicateHandVisuals)
        {
            if (visual != null && visual.enabled)
                visual.enabled = false;
        }

        foreach (Renderer renderer in DuplicateRenderers)
            SuppressRenderer(renderer);
    }


    private static void AddDuplicateRenderer(Renderer renderer)
    {
        if (renderer == null || DuplicateRenderers.Contains(renderer))
            return;

        DuplicateRenderers.Add(renderer);
        SuppressRenderer(renderer);
    }

    private static void SuppressRenderer(Renderer renderer)
    {
        if (renderer == null)
            return;

        // enabled는 HandVisual이 다시 바꿀 수 있지만 forceRenderingOff는 렌더러 단계에서 유지된다.
        renderer.enabled = false;
        renderer.forceRenderingOff = true;
    }

    private static bool IsSyntheticVisualPath(Transform candidate)
    {
        for (Transform current = candidate;
             current != null;
             current = current.parent)
        {
            string name = current.name;
            if (name.IndexOf(
                    "Synthetic",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf(
                    "HandSphereMap",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf(
                    "HandReticle",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }
}

// Meta HandVisual의 LateUpdate보다 뒤, 실제 렌더 직전에도 합성 메시를 차단한다.
[DisallowMultipleComponent]
[DefaultExecutionOrder(32000)]
public class MvpHandVisualGuard : MonoBehaviour
{
    private float _nextCacheRefresh;

    private void OnEnable()
    {
        Application.onBeforeRender += HandleBeforeRender;
        EnforceNow();
        Debug.Log(
            "[MVP Hand] 실제 손 비주얼 1세트만 유지합니다. 합성 HandVisual " +
            MvpXrRuntimeBootstrap.SuppressedHandVisualCount +
            "개, 보조 Renderer " +
            MvpXrRuntimeBootstrap.SuppressedRendererCount +
            "개를 숨겼습니다.");
    }

    private void OnDisable()
    {
        Application.onBeforeRender -= HandleBeforeRender;
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= _nextCacheRefresh)
        {
            _nextCacheRefresh = Time.unscaledTime + 1f;
            MvpXrRuntimeBootstrap.RefreshDuplicateHandVisualCache();
            return;
        }

        MvpXrRuntimeBootstrap.ForceCachedDuplicateVisualsOff();
    }

    private void HandleBeforeRender()
    {
        MvpXrRuntimeBootstrap.ForceCachedDuplicateVisualsOff();
    }

    public void EnforceNow()
    {
        _nextCacheRefresh = Time.unscaledTime + 1f;
        MvpXrRuntimeBootstrap.RefreshDuplicateHandVisualCache();
    }
}