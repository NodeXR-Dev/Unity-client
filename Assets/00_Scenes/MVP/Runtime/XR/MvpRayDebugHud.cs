using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;

/// <summary>
/// 임시 진단용 표시기. 헤드셋에서 레이가 왜 깜빡이는지 눈으로 보기 위한 것이다.
///
/// 에디터에는 HMD 가 없어 레이를 실제로 겨눠볼 수 없다. 그래서 추측으로 고치면
/// 엉뚱한 곳을 건드리게 되므로(실제로 한 번 그랬다), 기기에서 값을 직접 읽는다.
///
/// 보여주는 것:
///   - 손/컨트롤러 활성 상태 (어느 쪽이 잡고 있는지)
///   - 레이 인터랙터 4개가 지금 켜져 있는지, 무엇을 잡고 있는지
///   - 최근 3초 동안 각 레이가 몇 번 켜졌다 꺼졌는지  ← 깜빡임의 증거
///
/// 진단이 끝나면 이 파일을 지운다.
/// Meta SDK 타입은 리플렉션으로 읽는다(어셈블리 참조를 새로 걸지 않기 위해).
/// </summary>
public class MvpRayDebugHud : MonoBehaviour
{
    private const float Interval = 0.2f;
    private const float Window = 3f;

    private TextMeshProUGUI _text;
    private Transform _panel;
    private float _next;

    private readonly List<Component> _rays = new List<Component>();
    private readonly Dictionary<Component, bool> _lastOn = new Dictionary<Component, bool>();
    private readonly Dictionary<Component, List<float>> _toggles =
        new Dictionary<Component, List<float>>();

    private PropertyInfo _stateProp;
    private PropertyInfo _hasInteractableProp;

    // ------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (scene != "MvpLobby" && scene != "MVP_SH")
            return;
        if (FindFirstObjectByType<MvpRayDebugHud>() != null)
            return;
        new GameObject("MvpRayDebugHud").AddComponent<MvpRayDebugHud>();
    }

    private void Update()
    {
        if (Time.unscaledTime < _next)
            return;
        _next = Time.unscaledTime + Interval;

        if (_rays.Count == 0)
            CollectRays();
        if (_text == null)
            BuildPanel();
        if (_text == null)
            return;

        TrackToggles();
        FollowCamera();
        _text.text = Compose();
    }

    // ------------------------------------------------------------------

    private void CollectRays()
    {
        foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mb == null || mb.GetType().Name != "RayInteractor")
                continue;

            _rays.Add(mb);
            _lastOn[mb] = mb.gameObject.activeInHierarchy;
            _toggles[mb] = new List<float>();

            if (_stateProp == null)
            {
                _stateProp = mb.GetType().GetProperty("State");
                _hasInteractableProp = mb.GetType().GetProperty("HasInteractable");
            }
        }
    }

    // 켜짐/꺼짐이 바뀐 순간을 기록한다. 이 횟수가 곧 "깜빡임"이다.
    private void TrackToggles()
    {
        float now = Time.unscaledTime;
        foreach (Component ray in _rays)
        {
            if (ray == null)
                continue;

            bool on = ray.gameObject.activeInHierarchy &&
                      (ray as Behaviour) != null && ((Behaviour)ray).enabled;

            if (_lastOn.TryGetValue(ray, out bool was) && was != on)
                _toggles[ray].Add(now);
            _lastOn[ray] = on;

            _toggles[ray].RemoveAll(t => now - t > Window);
        }
    }

    private string Compose()
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("<b>손 / 컨트롤러</b>");
        sb.AppendLine(ActiveStateLine("HandActiveState", "손") +
                      "    " + ActiveStateLine("ControllerActiveState", "컨트롤러"));

        sb.AppendLine();
        sb.AppendLine("<b>레이 (최근 " + Window + "초 깜빡임)</b>");

        foreach (Component ray in _rays)
        {
            if (ray == null)
                continue;

            bool on = ray.gameObject.activeInHierarchy &&
                      (ray as Behaviour) != null && ((Behaviour)ray).enabled;
            int blinks = _toggles[ray].Count;

            string state = "-";
            string holding = "-";
            if (on && _stateProp != null)
            {
                object s = SafeGet(_stateProp, ray);
                if (s != null)
                    state = s.ToString();
                object h = SafeGet(_hasInteractableProp, ray);
                if (h is bool b)
                    holding = b ? "잡음" : "없음";
            }

            sb.AppendLine(
                (on ? "<color=#7CFF7C>ON </color>" : "<color=#FF7C7C>OFF</color>") +
                "  " + Side(ray) + " " + Short(ray.name) +
                "   " + state + " / " + holding +
                "   깜빡임 " + (blinks > 0 ? "<color=#FFD24A>" + blinks + "</color>" : "0"));
        }

        return sb.ToString();
    }

    private string ActiveStateLine(string typeName, string label)
    {
        var parts = new List<string>();
        foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mb == null || mb.GetType().Name != typeName)
                continue;

            PropertyInfo p = mb.GetType().GetProperty("Active");
            object v = p != null ? SafeGet(p, mb) : null;
            string side = mb.transform.root != null && mb.name.Contains("Left") ? "L" :
                          mb.name.Contains("Right") ? "R" : Side(mb);
            parts.Add(side + "=" + (v is bool bb ? (bb ? "ON" : "off") : "?"));
        }
        return label + " " + string.Join(" ", parts.ToArray());
    }

    private static object SafeGet(PropertyInfo p, object target)
    {
        if (p == null)
            return null;
        try { return p.GetValue(target); }
        catch { return null; }
    }

    private static string Side(Component c)
    {
        Transform t = c.transform;
        while (t != null)
        {
            if (t.name.StartsWith("Left")) return "L";
            if (t.name.StartsWith("Right")) return "R";
            t = t.parent;
        }
        return "?";
    }

    private static string Short(string name) =>
        name.Replace("Interactor", string.Empty);

    // ------------------------------------------------------------------

    private void BuildPanel()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        var go = new GameObject("Panel", typeof(Canvas));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.sizeDelta = new Vector2(1100f, 700f);
        rt.localScale = Vector3.one * 0.0007f;
        _panel = rt;

        var bgGo = new GameObject("BG", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        var bgRt = bgGo.GetComponent<RectTransform>();
        bgRt.SetParent(rt, false);
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        var bg = bgGo.GetComponent<UnityEngine.UI.Image>();
        bg.color = new Color(0.03f, 0.05f, 0.09f, 0.88f);
        bg.raycastTarget = false;   // 진단기가 레이를 가로채면 안 된다

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.SetParent(rt, false);
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(24f, 24f);
        textRt.offsetMax = new Vector2(-24f, -24f);

        _text = textGo.GetComponent<TextMeshProUGUI>();
        _text.fontSize = 34f;
        _text.color = Color.white;
        _text.raycastTarget = false;
        _text.richText = true;

        // 한글 글리프가 있는 폰트를 씬에서 빌려온다.
        foreach (TextMeshProUGUI sample in FindObjectsByType<TextMeshProUGUI>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (sample != null && sample != _text && sample.font != null)
            {
                _text.font = sample.font;
                break;
            }
        }
    }

    // 시야 왼쪽 아래에 붙어 따라다닌다(본 UI 를 가리지 않게).
    private void FollowCamera()
    {
        Camera cam = Camera.main;
        if (cam == null || _panel == null)
            return;

        Transform c = cam.transform;
        _panel.position = c.position + c.forward * 0.9f - c.right * 0.42f - c.up * 0.22f;
        _panel.rotation = Quaternion.LookRotation(_panel.position - c.position, Vector3.up);
    }
}
