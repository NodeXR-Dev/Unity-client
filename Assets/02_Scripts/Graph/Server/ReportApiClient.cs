using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

// 회의 리포트(요약) 조회 서버 REST 경계. (서버 feat/report-overview-fields, 2026-08-12)
//   GET /report/{room_id} → result: ReportDto
//
// host/room_id 는 GraphSyncClient 재사용(다른 API 클라이언트와 동일 관례).
// 리포트는 회의를 마칠 때 한 번 조회하는 성격이라 캐시하지 않는다 — 마지막 발화·최종
// 이미지가 바뀌면 값도 달라져야 한다.
public class ReportApiClient : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private GraphSyncClient _syncClient;   // host/room_id 재사용

    [Header("옵션")]
    [Tooltip("응답 대기 상한(초). 리포트는 집계 쿼리라 생성 API 만큼 오래 걸리지 않는다.")]
    [SerializeField] private float _timeoutSeconds = 15f;

    private ReportDto _last;
    public ReportDto Last => _last;

    private void Awake()
    {
        if (_syncClient == null)
            _syncClient = GetComponent<GraphSyncClient>();
        if (_syncClient == null)
            _syncClient = FindFirstObjectByType<GraphSyncClient>();
    }

    // 리포트 조회. onDone(report) — 실패 시 null.
    public void GetReport(Action<ReportDto> onDone)
    {
        if (_syncClient == null ||
            string.IsNullOrEmpty(_syncClient.Host) ||
            string.IsNullOrEmpty(_syncClient.RoomId))
        {
            Debug.LogWarning("[ReportApiClient] 실패: GraphSyncClient(host/room_id)가 비어 있습니다.");
            onDone?.Invoke(null);
            return;
        }
        StartCoroutine(CoGetReport(onDone));
    }

    private IEnumerator CoGetReport(Action<ReportDto> onDone)
    {
        // 다른 API 와 달리 /api 접두사가 없다 — main.py 가 report_router 를 prefix 없이
        // include 한다. 실서버 /openapi.json 으로 확인한 경로다.
        string url =
            $"{ServerAddress.Http(_syncClient.Host)}/report/" +
            UnityWebRequest.EscapeURL(_syncClient.RoomId);

        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = Mathf.Max(1, Mathf.RoundToInt(_timeoutSeconds));
            Debug.Log($"[ReportApiClient] GET {url}");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    $"[ReportApiClient] /report 실패: {req.error} (code={req.responseCode}) " +
                    $"body={req.downloadHandler?.text}");
                onDone?.Invoke(null);
                yield break;
            }

            string raw = req.downloadHandler.text;
            Debug.Log($"[ReportApiClient] 응답(code={req.responseCode}): {raw}");

            ReportResponse res = null;
            try { res = JsonUtility.FromJson<ReportResponse>(raw); }
            catch (Exception e)
            {
                Debug.LogWarning($"[ReportApiClient] 응답 파싱 실패: {e.Message}");
            }

            if (res == null || res.result == null)
            {
                Debug.LogWarning("[ReportApiClient] 응답에 result 가 없습니다.");
                onDone?.Invoke(null);
                yield break;
            }

            _last = res.result;
            Debug.Log(
                $"[ReportApiClient] 리포트 조회 완료 — 참여자 {_last.participants_ratio.Count}명 / " +
                $"총 발화 {_last.total_utterance_count}회 / 키워드 {_last.keywords.Count}개 / " +
                $"{_last.DurationText()}");
            onDone?.Invoke(_last);
        }
    }

    [ContextMenu("Test: 리포트 조회")]
    private void TestGetReport() =>
        GetReport(r =>
        {
            if (r == null) { Debug.Log("[ReportApiClient] 조회 실패"); return; }
            Debug.Log($"[ReportApiClient] {r.topic} / {r.HeaderLine()}");
            foreach (var p in r.participants_ratio)
                Debug.Log($"  {p.nickname}: {p.ratio}% ({p.utterance_count}회)");
        });
}
