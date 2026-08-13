using System;
using System.Collections.Generic;

// 회의 리포트(요약) 응답 DTO. (서버 feat/report-overview-fields 기준, 2026-08-12)
//   GET /report/{room_id} → { isSuccess, code, message, result: ReportDto }
//
// 서버가 주는 값과 시안의 차이:
//   - 참여자별 "22분 9초" 같은 발화 '시간' 은 없다. utterances 에 발화 길이 컬럼이 없어
//     산출이 불가능하다. ratio 자체가 발화 '횟수' 기반이므로 화면에도 "N회" 로 적는다.
//   - keywords 는 그래프 노드 텍스트 원문이라 시안 칩보다 길 수 있다. 길이 제한은
//     클라에서 자른다(ReportDto.ShortKeywords 참고).

[Serializable]
public class ReportParticipantRatioDto
{
    public string user_id;
    public string nickname;
    public float ratio;            // 0~100
    public int utterance_count;    // 발화 횟수
}

[Serializable]
public class ReportDto
{
    public string topic;
    public List<string> participants = new List<string>();
    public List<ReportParticipantRatioDto> participants_ratio =
        new List<ReportParticipantRatioDto>();

    // 서버 필드명이 대문자 D 다(final_2D_image). JsonUtility 는 이름이 정확히 같아야 매칭된다.
    public string final_2D_image;

    // ISO8601 문자열. JsonUtility 가 DateTime 을 직접 파싱하지 못해 string 으로 받는다.
    public string started_at;
    public string ended_at;
    public int duration_seconds;
    public List<string> keywords = new List<string>();
    public int total_utterance_count;

    // --- 편의 변환 (직렬화 대상 아님) ---

    public DateTime? StartedAt => ParseIso(started_at);
    public DateTime? EndedAt => ParseIso(ended_at);

    private static DateTime? ParseIso(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        return DateTime.TryParse(
            s, null,
            System.Globalization.DateTimeStyles.AdjustToUniversal |
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out DateTime v)
            ? v.ToLocalTime()
            : (DateTime?)null;
    }

    // 시안 표기: "2026.08.09 · 09:20 – 10:05 · 45분"
    public string HeaderLine()
    {
        DateTime? s = StartedAt, e = EndedAt;
        if (s == null) return "";
        string date = s.Value.ToString("yyyy.MM.dd");
        string span = e != null
            ? $"{s.Value:HH:mm} – {e.Value:HH:mm}"
            : s.Value.ToString("HH:mm");
        return $"{date}  ·  {span}  ·  {DurationText()}";
    }

    public string DurationText()
    {
        int m = duration_seconds / 60;
        if (m < 1) return "1분 미만";
        if (m < 60) return $"{m}분";
        return $"{m / 60}시간 {m % 60}분";
    }

    // 키워드 칩용. 서버는 노드 텍스트 원문을 주므로 길면 잘라서 쓴다.
    public List<string> ShortKeywords(int maxCount = 3, int maxChars = 14)
    {
        var list = new List<string>();
        if (keywords == null) return list;
        foreach (string k in keywords)
        {
            if (string.IsNullOrWhiteSpace(k)) continue;
            string t = k.Trim();
            if (t.Length > maxChars) t = t.Substring(0, maxChars - 1) + "…";
            list.Add(t);
            if (list.Count >= maxCount) break;
        }
        return list;
    }
}

[Serializable]
public class ReportResponse
{
    public bool isSuccess;
    public string code;
    public string message;
    public ReportDto result;
}
