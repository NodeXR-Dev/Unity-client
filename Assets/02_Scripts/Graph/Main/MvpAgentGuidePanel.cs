using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 에이전트 대사 패널.
//
// 서버가 발화를 분석해 보내는 AGENT_GUIDE(제약 위반 경고 / 결정 근거 복기)를 받아
// 시야 앞에 띄운다. 닫기 버튼을 누르면 사라진다.
//
// [씬에 놓고 켠다] 시안(01_Prefabs/Graph/Main/AgentPanel)을 씬에 꺼진 채로 두고 필요할 때 켠다.
//   목표·확인 패널과 같은 방식 — 모양은 에디터에서 보고 맞추고, 코드는 내용과 표시만 맡는다.
//
// [시야를 따라온다] 발화 도중 어디를 보고 있을지 모른다. 보드에 붙이면 등지고 있을 때 못 본다.
//   확인 패널과 같은 규약(카메라 앞 0.85m + MvpGentleFollow).
[DisallowMultipleComponent]
public class MvpAgentGuidePanel : MonoBehaviour
{
    [Tooltip("비우면 런타임에 찾는다.")]
    [SerializeField] private GraphSyncClient _syncClient;

    [Header("씬 오브젝트 이름")]
    [SerializeField] private string _panelName = "AgentPanel";
    [SerializeField] private string _textName  = "AgentText";
    [SerializeField] private string _closeButtonName = "Button_Close";

    [Header("배치")]
    [SerializeField] private float _distance = 0.85f;
    [Tooltip("따라다니는 캔버스 위에서의 패널 크기. " +
             "AgentPanel 은 1812x802 라 0.23 이면 ConfirmPanel(840x368 x 0.5)과 비슷해진다. " +
             "씬에 놓인 localScale 은 무시하고 이 값을 쓴다.")]
    [SerializeField] private float _panelScale = 0.23f;

    [Tooltip("이 시간이 지나면 자동으로 닫는다. 0 이면 닫기 버튼으로만 닫는다.")]
    [SerializeField] private float _autoHideSeconds;

    private GraphSyncClient _subscribed;
    private Canvas _followCanvas;
    private GameObject _panel;
    private TMP_Text _text;
    private Coroutine _autoHide;

    // GraphSyncClient 는 수신을 메인스레드로 마샬링하지만, 여기서 한 번 더 큐에 담아 둔다.
    // 호출 경로가 바뀌어도 Unity 오브젝트 접근이 백그라운드에서 일어나지 않게 하기 위함이다.
    private readonly Queue<GraphSyncClient.AgentGuide> _pending =
        new Queue<GraphSyncClient.AgentGuide>();

    private void OnEnable()  => Subscribe();
    private void OnDisable() => Unsubscribe();

    private void Update()
    {
        if (_subscribed == null)
            Subscribe();

        while (_pending.Count > 0)
            Show(_pending.Dequeue());
    }

    private void Subscribe()
    {
        if (_syncClient == null)
            _syncClient = FindFirstObjectByType<GraphSyncClient>(FindObjectsInactive.Include);
        if (_syncClient == null || _subscribed == _syncClient)
            return;

        Unsubscribe();
        _syncClient.OnAgentGuide += Enqueue;
        _subscribed = _syncClient;
    }

    private void Unsubscribe()
    {
        if (_subscribed == null)
            return;
        _subscribed.OnAgentGuide -= Enqueue;
        _subscribed = null;
    }

    private void Enqueue(GraphSyncClient.AgentGuide guide) => _pending.Enqueue(guide);

    private void Show(GraphSyncClient.AgentGuide guide)
    {
        if (_panel == null)
            _panel = FindSceneObject(_panelName);
        if (_panel == null)
        {
            Debug.LogWarning(
                "[MVP Agent] 대사 패널을 찾지 못했습니다 — 씬에 '" + _panelName +
                "' 이 있는지 확인하세요. 대사=" + guide.Message);
            return;
        }

        // 이미 떠 있으면 캔버스를 다시 만들지 않고 내용만 갈아 끼운다.
        if (_followCanvas == null)
        {
            _followCanvas = CreateFollowCanvas();
            _panel.transform.SetParent(_followCanvas.transform, false);
            if (_panel.transform is RectTransform rect)
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition3D = Vector3.zero;
                rect.localRotation = Quaternion.identity;
                // 스케일은 반드시 여기서 정한다. 씬에서 패널을 옮기면 에디터가 '월드 크기 유지'로
                // localScale 을 건드리는데, 그 값이 따라오면 캔버스 스케일과 곱해져 크기가 무너진다.
                rect.localScale = Vector3.one * _panelScale;
            }
            MvpPanelButtonBinder.Wire(_panel, _closeButtonName, Hide);
        }
        else
        {
            // 다시 띄우는 것이므로 시선 앞으로 옮겨 준다.
            PlaceInFront(_followCanvas.transform);
        }

        if (_text == null)
            _text = FindText(_panel, _textName);
        if (_text != null)
            _text.text = guide.Message;
        else
            Debug.LogWarning(
                "[MVP Agent] '" + _textName + "' 을 찾지 못해 대사를 넣지 못했습니다.");

        _panel.SetActive(true);
        Debug.Log(
            "[MVP Agent] 대사 표시(" + guide.GuideType + "): " + guide.Message);

        if (_autoHide != null)
        {
            StopCoroutine(_autoHide);
            _autoHide = null;
        }
        if (_autoHideSeconds > 0f)
            _autoHide = StartCoroutine(HideAfter(_autoHideSeconds));
    }

    private IEnumerator HideAfter(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        Hide();
    }

    // 닫기. 씬에 놓아 둔 오브젝트라 파기하지 않고 꺼 두기만 한다(다음 대사에 재사용).
    // 따라다니는 캔버스는 우리가 만든 것이므로 지운다 — 지우기 전에 패널을 떼어 내야
    // 자식이라는 이유로 같이 사라지지 않는다.
    private void Hide()
    {
        if (_autoHide != null)
        {
            StopCoroutine(_autoHide);
            _autoHide = null;
        }

        if (_panel != null)
        {
            _panel.SetActive(false);
            _panel.transform.SetParent(transform, false);
        }

        if (_followCanvas != null)
        {
            Destroy(_followCanvas.gameObject);
            _followCanvas = null;
        }
    }

    // 확인 패널(MvpClassroomFlow.CreateFollowCanvas)과 같은 값으로 맞춘다.
    private Canvas CreateFollowCanvas()
    {
        GameObject root = new GameObject(
            "MvpAgentGuideCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(1200f, 700f);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 420;             // 확인 패널(400)보다 위
        canvas.worldCamera = Camera.main;

        PlaceInFront(root.transform);
        root.transform.localScale = Vector3.one * 0.0009f;
        root.AddComponent<MvpGentleFollow>().Configure(_distance);
        return canvas;
    }

    private void PlaceInFront(Transform target)
    {
        Camera cam = Camera.main;
        if (cam == null || target == null)
            return;

        Vector3 forward = cam.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;
        forward.Normalize();

        target.position = cam.transform.position + forward * _distance;
        target.rotation = Quaternion.LookRotation(forward);
    }

    // 씬 전체에서 이름으로 찾는다(비활성 포함). 패널은 꺼진 채로 놓여 있다.
    private static GameObject FindSceneObject(string objectName)
    {
        foreach (Transform t in
                 FindObjectsByType<Transform>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t != null && t.name == objectName)
                return t.gameObject;
        }
        return null;
    }

    // 이름이 맞는 자식을 먼저 찾고, 없으면 패널 안의 첫 TMP_Text 를 쓴다
    // (시안에서 이름이 바뀌어도 대사가 안 나오는 일은 없게).
    private static TMP_Text FindText(GameObject panel, string textName)
    {
        TMP_Text[] texts = panel.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text text in texts)
        {
            if (text != null && text.name == textName)
                return text;
        }
        return texts.Length > 0 ? texts[0] : null;
    }
}
