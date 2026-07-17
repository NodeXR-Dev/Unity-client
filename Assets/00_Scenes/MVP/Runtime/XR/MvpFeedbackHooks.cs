using UnityEngine;

// GraphManager의 시맨틱 이벤트(생성/연결/삭제)에 오디오+햅틱 피드백을 붙인다.
// 입력 경로(제스처/키보드/추천/멀티플레이 원격)와 무관하게 '무슨 일이 일어났는지'를
// 소리로 확정해 준다 — 손이 시야 밖에 있어도 성공을 놓치지 않게.
[DisallowMultipleComponent]
public class MvpFeedbackHooks : MonoBehaviour
{
    [SerializeField] private GraphManager _graph;
    [SerializeField] private MvpAudioCue _audio;
    [SerializeField] private float _startupSuppressSeconds = 1.3f;

    private bool _subscribed;
    private float _readyTime;

    private void Awake()
    {
        if (_audio == null)
        {
            _audio = GetComponent<MvpAudioCue>();
            if (_audio == null)
                _audio = gameObject.AddComponent<MvpAudioCue>();
        }
    }

    private void OnEnable()
    {
        Resolve();
        Subscribe();
        // 씬 시작 시 시드/초기 그래프 로드로 이벤트가 쏟아지는 구간은 소리를 억제.
        _readyTime = Time.unscaledTime + _startupSuppressSeconds;
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Resolve()
    {
        if (_graph == null)
            _graph = FindFirstObjectByType<GraphManager>();
        if (_audio == null)
            _audio = GetComponent<MvpAudioCue>();
    }

    private void Subscribe()
    {
        if (_graph == null || _subscribed) return;
        _graph.OnEdgeCreated += HandleEdgeCreated;
        _graph.OnEdgeDeleted += HandleEdgeDeleted;
        _graph.OnNodeCreated += HandleNodeCreated;
        _graph.OnNodeDeleted += HandleNodeDeleted;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (_graph == null || !_subscribed) return;
        _graph.OnEdgeCreated -= HandleEdgeCreated;
        _graph.OnEdgeDeleted -= HandleEdgeDeleted;
        _graph.OnNodeCreated -= HandleNodeCreated;
        _graph.OnNodeDeleted -= HandleNodeDeleted;
        _subscribed = false;
    }

    private bool Ready => _audio != null && Time.unscaledTime >= _readyTime;

    private void HandleEdgeCreated(EdgeData edge)
    {
        if (!Ready) return;
        _audio.PlayHaptic(MvpAudioCue.Cue.Connect, 0.55f, 0.07f);
    }

    private void HandleEdgeDeleted(string edgeId)
    {
        if (!Ready) return;
        _audio.Play(MvpAudioCue.Cue.Disconnect);
    }

    private void HandleNodeCreated(
        string nodeId, string text, string parentId,
        string subGraphId, Vector3 position)
    {
        if (!Ready) return;
        _audio.PlayHaptic(MvpAudioCue.Cue.NodeSpawn, 0.35f, 0.05f);
    }

    private void HandleNodeDeleted(string nodeId)
    {
        if (!Ready) return;
        _audio.Play(MvpAudioCue.Cue.NodeDelete);
    }
}
