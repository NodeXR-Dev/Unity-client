using UnityEngine;
using UnityEngine.UI;

// 음성 버튼 옆의 작은 상태 점.
//  - Idle      : 숨김
//  - Preparing : 앰버, 느린 깜빡임 (모델 추출·초기화 중)
//  - Listening : 레드, 심장박동 펄스 + 마이크 입력 세기에 따라 커짐
//    → "지금 내 목소리가 들어가고 있다"를 말없이 보여 준다.
[DisallowMultipleComponent]
public class MvpVoiceIndicator : MonoBehaviour
{
    public enum State { Idle, Preparing, Listening }

    private static readonly Color PreparingColor =
        new Color(0.95f, 0.73f, 0.29f, 1f);
    private static readonly Color ListeningColor =
        new Color(0.95f, 0.36f, 0.40f, 1f);

    private Image _dot;
    private RectTransform _rect;
    private Vector2 _baseSize;
    private State _state = State.Idle;
    private MvpOnDeviceDictation _levelSource;

    // 버튼 위 지정 위치에 점을 만들어 붙인다.
    public static MvpVoiceIndicator Attach(
        Transform parent, Vector2 position, float size)
    {
        Image dot = MvpStudentUiFactory.CreatePanel(
            parent, "VoiceIndicator", position,
            new Vector2(size, size), ListeningColor, false);
        dot.raycastTarget = false;

        MvpVoiceIndicator indicator =
            dot.gameObject.AddComponent<MvpVoiceIndicator>();
        indicator._dot = dot;
        indicator._rect = dot.rectTransform;
        indicator._baseSize = new Vector2(size, size);
        indicator.Apply();
        return indicator;
    }

    public void SetState(State state, MvpOnDeviceDictation levelSource = null)
    {
        _state = state;
        _levelSource = levelSource;
        Apply();
    }

    private void Apply()
    {
        if (_dot == null)
            return;
        _dot.enabled = _state != State.Idle;
        if (_state == State.Preparing)
            _dot.color = PreparingColor;
        else if (_state == State.Listening)
            _dot.color = ListeningColor;
        if (_rect != null)
            _rect.sizeDelta = _baseSize;
    }

    private void Update()
    {
        if (_dot == null || _state == State.Idle)
            return;

        if (_state == State.Preparing)
        {
            // 느린 깜빡임
            Color color = PreparingColor;
            color.a = 0.45f + 0.55f * Mathf.PingPong(Time.unscaledTime * 1.6f, 1f);
            _dot.color = color;
            return;
        }

        // Listening: 심장박동 + 입력 레벨 반응
        float pulse = 0.9f + 0.1f * Mathf.Sin(Time.unscaledTime * 7f);
        float level = _levelSource != null ? _levelSource.Level : 0f;
        float scale = pulse * (1f + level * 0.9f);
        _rect.sizeDelta = _baseSize * scale;

        Color c = ListeningColor;
        c.a = 0.8f + 0.2f * Mathf.Clamp01(level * 2f);
        _dot.color = c;
    }
}
