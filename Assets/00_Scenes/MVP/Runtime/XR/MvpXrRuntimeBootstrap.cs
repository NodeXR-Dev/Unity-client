using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// MVP 씬이 실행될 때 XR UI·그래프 어댑터와 손 비주얼 안전장치를 준비한다.
public static class MvpXrRuntimeBootstrap
{
    // MVP 폴더 안의 모든 씬이 대상이다. 예전엔 MVP.unity 한 개만 봤는데,
    // 작업용 사본(MVP_SH.unity 등)에서는 손목 메뉴·손 비주얼 가드가 아예 붙지 않았다.
    private const string MvpSceneFolder =
        "Assets/00_Scenes/MVP/";

    private static readonly List<Renderer> DuplicateRenderers =
        new List<Renderer>();
    private static readonly List<MonoBehaviour> DuplicateHandVisuals =
        new List<MonoBehaviour>();

    internal static int SuppressedRendererCount => DuplicateRenderers.Count;
    internal static int SuppressedHandVisualCount => DuplicateHandVisuals.Count;

    // AfterSceneLoad 는 앱이 시작할 때 "첫 씬"에서 한 번만 실행된다.
    // 로비를 0번 씬으로 함께 빌드하면 그 시점엔 MvpClassroomFlow 가 없어 그냥 돌아가고,
    // 이후 로비가 SceneManager 로 MVP_SH 를 열어도 이 메서드는 다시 불리지 않는다.
    //   → MVP_SH 만 빌드하면 손목 패널이 보이고, 로비와 함께 빌드하면 안 보이던 원인.
    //      다른 부품(Bridge/PlayerController/NodeInteraction/GraphLink)은 MVP_SH 씬에
    //      직접 놓여 있어 멀쩡했고, 씬에 없는 유일한 부품인 손목 메뉴만 사라졌다.
    // → 이후에 로드되는 씬도 받도록 sceneLoaded 를 함께 구독한다.
    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        EnsureMvpXrAdapters(SceneManager.GetActiveScene());
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureMvpXrAdapters(scene);
    }

    // 부품을 붙이는 판단은 전부 "이미 있으면 건너뛴다"라서 여러 번 불려도 안전하다.
    private static void EnsureMvpXrAdapters(Scene scene)
    {
        if (string.IsNullOrEmpty(scene.path) ||
            !scene.path.StartsWith(
                MvpSceneFolder, StringComparison.OrdinalIgnoreCase))
            return;

        // 폴더만 보면 같은 폴더의 로비 씬(MvpLobby)에도 회의실용 부품이 붙는다.
        // 손목 메뉴('회의실 나가기')·노드 조작·좌석 배치는 로비에서 의미가 없고,
        // 특히 좌석 배치는 로비 플레이어를 회의실 바닥 높이로 순간이동시킨다.
        // 실제 수업 씬인지는 MvpClassroomFlow 존재 여부로 판별한다.
        MvpClassroomFlow classroom =
            UnityEngine.Object.FindFirstObjectByType<MvpClassroomFlow>(
                FindObjectsInactive.Include);
        if (classroom == null)
            return;

        GameObject app = classroom.gameObject;

        if (app.GetComponent<MvpWristSettingsMenu>() == null)
            app.AddComponent<MvpWristSettingsMenu>();
        if (app.GetComponent<MvpXrInteractionBridge>() == null)
            app.AddComponent<MvpXrInteractionBridge>();
        if (app.GetComponent<MvpMeetingRoomPlayerController>() == null)
            app.AddComponent<MvpMeetingRoomPlayerController>();
        if (app.GetComponent<MvpNodeInteractionController>() == null)
            app.AddComponent<MvpNodeInteractionController>();
        // 에이전트 대사 패널(제약 위반 경고 / 결정 근거 복기). 캔버스를 스스로 만들므로
        // 씬 배선이 필요 없다 — 붙여 두기만 하면 AGENT_GUIDE 를 구독한다.
        if (app.GetComponent<MvpAgentGuidePanel>() == null)
            app.AddComponent<MvpAgentGuidePanel>();
        // 시연 대본 발화기. 붙여만 두고 인스펙터에서 Use Script 로 켜고 끈다.
        if (app.GetComponent<MvpScriptedUtterancePlayer>() == null)
            app.AddComponent<MvpScriptedUtterancePlayer>();

        // 화면 공유(발표자 시점) 부품은 여기서 붙이지 않는다.
        // MVP_SH 등에는 이미 씬에 PresenterViewSystem 오브젝트가 있고
        // (NetworkObject + Session + Renderer + UIActions + LocalCameraReference),
        // 여기서 또 붙이면 렌더러가 둘이 되어 서로 시점을 덮어쓴다.
        // 손목 메뉴는 FindFirstObjectByType 으로 씬의 UIActions 를 찾아 쓴다.

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