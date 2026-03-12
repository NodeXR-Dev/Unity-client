using UnityEngine;
using TMPro;
using System;

public class LobbyMeetingUI : MonoBehaviour
{
    [Header("Top Clock UI (Left)")]
    [SerializeField] private TextMeshProUGUI timeText;     // 시간 (예: 13:27)
    [SerializeField] private TextMeshProUGUI dateFullText; // 년 월 일 (예: 2026년 2월 12일)
    [SerializeField] private TextMeshProUGUI dayText;      // 요일 (예: 목요일)

    void Update()
    {
        UpdateLobbyUI();
    }

    void UpdateLobbyUI()
{
    DateTime now = DateTime.Now;

    // 1. 좌측 상단 시계/날짜/요일은 그대로 실시간 업데이트 (Update에서 돌아가도 됨)
    if (timeText != null) timeText.text = now.ToString("HH:mm");
    if (dateFullText != null) dateFullText.text = now.ToString("yyyy년 M월 d일");
    if (dayText != null) dayText.text = now.ToString("dddd");
}
}