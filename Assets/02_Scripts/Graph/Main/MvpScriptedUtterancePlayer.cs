using UnityEngine;

// 시연용 대본 발화 재생기.
//
// 온디바이스 STT 가 한국어 고유명사("모터", "송풍기")를 자주 틀린다. 시연 영상에서는
// 서버 에이전트(제약 위반 경고 / 결정 근거 복기)가 정확히 트리거되어야 하는데,
// 인식이 한 글자만 어긋나도 제약이 등록되지 않아 판정이 통째로 어긋난다.
//
// 에이전트는 **텍스트가 서버에 도달하기만 하면** 동작한다. 그래서 마이크를 누를 때마다
// 대본의 다음 문장을 그대로 올린다. 촬영자는 카메라 앞에서 같은 대사를 말하면 되고,
// 화면에는 인식 결과가 아니라 대본 문장이 뜬다.
//
// 끄면(_enabled = false) 평소처럼 실제 음성 인식을 쓴다. 시연이 끝나면 꺼 두면 된다.
[DisallowMultipleComponent]
public class MvpScriptedUtterancePlayer : MonoBehaviour
{
    [Tooltip("켜면 마이크 버튼이 음성 인식 대신 아래 대본을 순서대로 보낸다.")]
    [SerializeField] private bool _useScript = true;

    [Tooltip("대본을 다 쓰면 처음으로 돌아간다. 끄면 마지막 뒤로는 실제 음성 인식으로 넘어간다.")]
    [SerializeField] private bool _loop;

    [Tooltip("발화마다 노드를 만든다. 시연에서는 보통 끈다 — " +
             "에이전트(제약 위반·근거 복기)는 발화 텍스트만 있으면 되고, " +
             "노드는 대본대로 배치하고 싶지 대사마다 하나씩 생기면 방해가 된다.")]
    [SerializeField] private bool _createNodes;

    [Tooltip("직전 발화 후 이 시간(초)이 지나야 다음 대사가 나간다. " +
             "서버가 발화 하나를 처리하는 데 1~4초가 걸리는데, 그 전에 다음 발화가 들어가면 " +
             "토픽이 갈라져 앞서 등록한 제약이 문맥에서 빠진다(에이전트가 안 걸린다).")]
    [SerializeField] private float _minIntervalSeconds = 5f;

    private float _nextAllowedTime;

    [TextArea(2, 4)]
    [Tooltip("한 줄에 한 발화. 위에서부터 순서대로 나간다.")]
    [SerializeField]
    private string[] _lines =
    {
        "비 오는 날 학교 현관에 젖은 우산 때문에 바닥이 미끄러워. 우산 비닐 대신 여러 번 쓸 수 있는 물기 제거기를 만들면 어때?",
        "접은 우산을 통에 넣고 발판을 밟으면, 안쪽의 세 패드가 모여서 물기를 닦는 구조로 해보자.",
        "제약은 두 가지로 정하자. 모터나 열선 같은 전기 부품은 사용하지 않고, 재료비는 5만 원 이하로 하자.",
        "전기 부품을 빼는 이유는 물이 닿을 때 감전 위험을 줄이고, 우리 힘으로 쉽게 만들기 위해서야.",
        "좋아. 전기 없이 발판이 자전거 브레이크 케이블을 당기는 방식으로 결정하자.",
        "패드 뒤에는 스프링을 넣자. 그러면 굵기가 다른 우산도 너무 세게 눌리지 않을 거야.",
        "그래도 빨리 말리려면 아래에 작은 모터와 송풍기를 넣는 게 낫지 않을까?",
        "맞아. 모터는 빼고, 패드에 세로 홈을 내서 닦은 뒤에도 공기가 통하게 하자.",
        "우리가 모터를 쓰지 않기로 한 이유가 정확히 뭐였지?",
        "겉모양은 둥근 원통으로 하고, 앞에 투명창을 내서 물받이가 찼는지 볼 수 있게 하자.",
    };

    private int _index;

    // 지금 대본을 쓸 수 있는 상태인가. 대본을 다 썼고 반복이 아니면 false —
    // 그때부터는 실제 음성 인식으로 돌아간다.
    public bool HasNext =>
        _useScript && _lines != null && _lines.Length > 0 &&
        (_loop || _index < _lines.Length);

    // 다음 대사를 실제 발화와 같은 경로로 흘려보낸다.
    // 보냈으면 true — 호출부는 이때 음성 인식을 시작하지 않는다.
    public bool SendNext(MvpVoiceRequirementController voice)
    {
        if (!HasNext || voice == null)
            return false;

        // 너무 빨리 연달아 누르면 서버 토픽이 갈라진다. 그래도 '대본 모드가 처리했다'로
        // true 를 돌려준다 — 여기서 false 를 주면 호출부가 음성 인식을 켜 버린다.
        if (Time.unscaledTime < _nextAllowedTime)
        {
            float remain = _nextAllowedTime - Time.unscaledTime;
            Debug.Log(
                $"[MVP Script] 아직 {remain:0.0}초 남았습니다 — 앞 발화 처리 중");
            return true;
        }

        if (_index >= _lines.Length)
            _index = 0;   // _loop

        string line = _lines[_index];
        _index++;

        if (string.IsNullOrWhiteSpace(line))
            return false;

        _nextAllowedTime = Time.unscaledTime + Mathf.Max(0f, _minIntervalSeconds);

        Debug.Log(
            $"[MVP Script] 대본 발화 {_index}/{_lines.Length}: {line}");
        voice.SubmitText(line, _createNodes);
        return true;
    }

    // 리허설을 다시 돌릴 때. 인스펙터 컨텍스트 메뉴에서도 부를 수 있다.
    [ContextMenu("대본 처음으로")]
    public void Rewind() => _index = 0;

    public int NextIndex => _index;
    public int LineCount => _lines != null ? _lines.Length : 0;
}
