/*
 * 파일명: MeetingRoomHistoryTimelineTicks.cs
 * 목적: 하단 바 위에 "히스토리가 바뀌는 지점(스냅샷)"마다 틱(눈금)을 표시한다.
 *
 * 컨트롤러가 스냅샷 목록을 받은 뒤 SetCount(스냅샷 수) 를 호출하면,
 * 바를 따라 0(과거)~1(현재)까지 균등 위치에 틱 이미지를 생성한다.
 *
 * 이 오브젝트는 슬라이더 위에 겹쳐 깔리는 RectTransform(track) 이고,
 * 틱은 raycastTarget=false 라 슬라이더 드래그를 방해하지 않는다.
 */
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MeetingRoomHistoryTimelineTicks : MonoBehaviour
{
    [Tooltip("틱이 퍼질 영역. 비우면 자기 자신의 RectTransform.")]
    [SerializeField] private RectTransform track;

    [Header("틱 외형")]
    [SerializeField] private float tickWidth = 4f;
    [SerializeField] private float tickHeight = 30f;
    [Tooltip("좌우 끝 여백(핸들 반지름쯤). 양 끝 틱이 핸들 이동 범위와 맞도록.")]
    [SerializeField] private float horizontalInset = 10f;
    [SerializeField] private Color tickColor = new Color(1f, 1f, 1f, 0.85f);

    private readonly List<GameObject> _ticks = new List<GameObject>();

    // 스냅샷 수만큼 균등 위치에 틱 생성 (0=과거 왼쪽, 마지막=현재 오른쪽 끝)
    public void SetCount(int count)
    {
        Clear();
        if (count <= 0) return;

        RectTransform area = track != null ? track : (RectTransform)transform;
        float width = area.rect.width;
        float usable = Mathf.Max(0f, width - horizontalInset * 2f);
        float left = -usable * 0.5f;

        for (int i = 0; i < count; i++)
        {
            float t = count <= 1 ? 0.5f : i / (float)(count - 1);

            var go = new GameObject($"Tick_{i}", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(area, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(tickWidth, tickHeight);
            rt.anchoredPosition = new Vector2(left + usable * t, 0f);

            var img = go.GetComponent<Image>();
            img.color = tickColor;
            img.raycastTarget = false; // 슬라이더 드래그 방해 방지

            _ticks.Add(go);
        }
    }

    public void Clear()
    {
        foreach (var g in _ticks)
        {
            if (g == null) continue;
            if (Application.isPlaying) Destroy(g);
            else DestroyImmediate(g);
        }
        _ticks.Clear();
    }
}
