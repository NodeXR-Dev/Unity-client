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
        // 화면 갱신은 느리게, 표본 수집은 매 프레임.
        // 한 프레임 단위로 껐다 켜지는 것을 0.2초 간격으로 재면 놓친다.
        TrackFlicker();
        TrackLine();

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
    // 패널 깜빡임 추적: 레이가 아니라 UI 쪽이 껐다 켜지는 경우를 잡는다.
    // ------------------------------------------------------------------

    private readonly Dictionary<string, float> _alphaLast = new Dictionary<string, float>();
    private readonly Dictionary<string, List<float>> _alphaFlips =
        new Dictionary<string, List<float>>();
    private readonly List<float> _stepFlips = new List<float>();
    private string _lastStep = null;
    private Component _guide;
    private FieldInfo _stepField;

    private void TrackFlicker()
    {
        float now = Time.unscaledTime;

        if (_guide == null)
        {
            foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mb != null && mb.GetType().Name == "MvpLobbyFlowGuide")
                {
                    _guide = mb;
                    _stepField = mb.GetType().GetField("_current",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    break;
                }
            }
        }

        if (_guide != null && _stepField != null)
        {
            string step = "" + _stepField.GetValue(_guide);
            if (_lastStep != null && step != _lastStep)
                _stepFlips.Add(now);
            _lastStep = step;
            _stepFlips.RemoveAll(t => now - t > Window);
        }

        foreach (Canvas cv in FindObjectsByType<Canvas>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (cv == null || cv.transform.root != cv.transform ||
                !cv.name.StartsWith("Lobby"))
                continue;

            CanvasGroup cg = cv.GetComponent<CanvasGroup>();
            float a = cg != null ? cg.alpha : (cv.gameObject.activeInHierarchy ? 1f : 0f);

            if (!_alphaFlips.ContainsKey(cv.name))
            {
                _alphaFlips[cv.name] = new List<float>();
                _alphaLast[cv.name] = a;
            }

            // 보임/안보임이 뒤집힌 횟수만 센다(미세한 값 변화는 무시).
            // 알파뿐 아니라 오브젝트가 꺼지는 경우도 같이 본다.
            bool wasOn = _alphaLast[cv.name] > 0.5f;
            bool isOn = a > 0.5f && cv.gameObject.activeInHierarchy;
            if (wasOn != isOn)
                _alphaFlips[cv.name].Add(now);
            _alphaLast[cv.name] = isOn ? Mathf.Max(a, 0.51f) : 0f;
            _alphaFlips[cv.name].RemoveAll(t => now - t > Window);
        }

        // 인터랙터는 계속 켜져 있는데 "무엇을 잡고 있는지"만 흔들리는 경우.
        // 그러면 버튼 하이라이트가 켜졌다 꺼졌다 하며 UI 가 깜빡이는 것처럼 보인다.
        foreach (Component ray in _rays)
        {
            if (ray == null || _stateProp == null)
                continue;

            object s = SafeGet(_stateProp, ray);
            string cur = s != null ? s.ToString() : "-";
            if (!_stateFlips.ContainsKey(ray))
            {
                _stateFlips[ray] = new List<float>();
                _stateLast[ray] = cur;
            }
            if (_stateLast[ray] != cur)
                _stateFlips[ray].Add(now);
            _stateLast[ray] = cur;
            _stateFlips[ray].RemoveAll(t => now - t > Window);
        }
    }

    private readonly Dictionary<Component, List<float>> _stateFlips =
        new Dictionary<Component, List<float>>();
    private readonly Dictionary<Component, string> _stateLast =
        new Dictionary<Component, string>();

    // ------------------------------------------------------------------
    // 선이 실제로 꺼지는 순간을 잰다.
    //
    // RayInteractorRayVisual.UpdateVisual():
    //     if (State == Disabled || (_hideWhenNoInteractable && Interactable == null))
    //         _renderer.enabled = false;
    //
    // 즉 선이 껌뻑이는 원인은 둘 중 하나뿐이다.
    //   (A) 인터랙터가 Disabled 로 꺼진다        -> 게이트/ActiveState 문제
    //   (B) 잡고 있던 대상(Interactable)이 사라진다 -> 히트 판정 문제
    // 어느 쪽인지 세어서 구분한다.
    // ------------------------------------------------------------------

    private readonly List<Component> _visuals = new List<Component>();
    private readonly Dictionary<Component, bool> _lineLast = new Dictionary<Component, bool>();
    private readonly Dictionary<Component, List<float>> _lineFlips =
        new Dictionary<Component, List<float>>();
    private int _causeDisabled;
    private int _causeNoTarget;

    private void TrackLine()
    {
        float now = Time.unscaledTime;

        if (_visuals.Count == 0)
        {
            foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mb != null && mb.GetType().Name == "RayInteractorRayVisual")
                {
                    _visuals.Add(mb);
                    _lineFlips[mb] = new List<float>();
                    _lineLast[mb] = true;
                }
            }
        }

        foreach (Component v in _visuals)
        {
            if (v == null)
                continue;

            FieldInfo rf = v.GetType().GetField("_renderer",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var rend = rf != null ? rf.GetValue(v) as Renderer : null;
            if (rend == null)
                continue;

            bool on = rend.enabled;
            if (_lineLast[v] != on)
            {
                _lineFlips[v].Add(now);

                // 꺼진 순간에만 원인을 기록한다.
                if (!on)
                {
                    FieldInfo inf = v.GetType().GetField("_rayInteractor",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    Component ray = inf != null ? inf.GetValue(v) as Component : null;
                    if (ray != null)
                    {
                        object st = SafeGet(ray.GetType().GetProperty("State"), ray);
                        object it = SafeGet(ray.GetType().GetProperty("Interactable"), ray);
                        if (st != null && st.ToString() == "Disabled")
                            _causeDisabled++;
                        else if (it == null)
                            _causeNoTarget++;
                    }
                }
            }
            _lineLast[v] = on;
            _lineFlips[v].RemoveAll(t => now - t > Window);
        }
    }

    private int LineFlips()
    {
        int n = 0;
        foreach (var kv in _lineFlips)
            n += kv.Value.Count;
        return n;
    }

    /// <summary>진단 결과를 코드에서 읽기 위한 것(에디터 확인용).</summary>
    public string Report()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("단계 = " + _lastStep + "  / 최근 " + Window + "초 단계 바뀜 " +
                      _stepFlips.Count + "회");
        foreach (var kv in _alphaFlips)
            sb.AppendLine("  " + kv.Key + " 보임 뒤집힘 " + kv.Value.Count + "회  (현재 alpha " +
                          _alphaLast[kv.Key].ToString("F2") + ")");
        return sb.ToString();
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
        sb.AppendLine("<b>패널</b>  단계 " + _lastStep +
                      "  바뀜 " + Warn(_stepFlips.Count));
        foreach (var kv in _alphaFlips)
        {
            if (kv.Value.Count == 0 && _alphaLast[kv.Key] < 0.5f)
                continue;   // 계속 꺼져 있는 것은 굳이 보여주지 않는다
            sb.AppendLine("   " + kv.Key.Replace("LobbyCanvas", "LC") +
                          "  alpha " + _alphaLast[kv.Key].ToString("F1") +
                          "  깜빡임 " + Warn(kv.Value.Count));
        }

        sb.AppendLine();
        sb.AppendLine("<b>선이 꺼진 횟수 " + Warn(LineFlips()) + "</b>" +
                      "   원인: 인터랙터꺼짐 " + Warn(_causeDisabled) +
                      " / 대상놓침 " + Warn(_causeNoTarget));

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

            int stateFlips = _stateFlips.ContainsKey(ray) ? _stateFlips[ray].Count : 0;

            sb.AppendLine(
                (on ? "<color=#7CFF7C>ON </color>" : "<color=#FF7C7C>OFF</color>") +
                "  " + Side(ray) + " " + Short(ray.name) +
                "   " + state + " / " + holding +
                "   껐켰 " + Warn(blinks) + "  잡았다놨다 " + Warn(stateFlips));
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

    private static string Warn(int n) =>
        n > 0 ? "<color=#FFD24A>" + n + "</color>" : "0";

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
