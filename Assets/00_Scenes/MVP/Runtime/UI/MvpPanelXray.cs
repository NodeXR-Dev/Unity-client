using System.Collections.Generic;
using Oculus.Interaction.Input;
using UnityEngine;

// 패널 리치스루(X-ray): 손끝을 패널 '너머'로 뻗으면 패널이 반투명해지고
// 레이캐스트를 놓아 준다 — 앞 패널이 뒤의 노드/보드 조작을 막지 않게 한다.
// 손을 다시 빼면 원래대로 돌아온다. (손 추적이 없는 데스크톱에서는 아무 것도 하지 않음)
[DisallowMultipleComponent]
public class MvpPanelXray : MonoBehaviour
{
    private const float BehindDepth = 0.04f;   // 패널 면보다 4cm 이상 너머
    private const float EdgeMargin = 60f;      // 패널 가장자리 여유(rect px)
    private const float XrayAlpha = 0.22f;

    private readonly List<IHand> _hands = new List<IHand>();
    private CanvasGroup _group;
    private RectTransform _rect;
    private float _nextHandScan;
    // VR 레이는 CanvasGroup 이 아니라 PointableCanvas 의 BoxCollider 를 맞으므로,
    // X-ray 중에는 콜라이더도 함께 꺼야 레이가 뒤로 통과한다.
    private Collider[] _colliders = new Collider[0];

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _group = GetComponent<CanvasGroup>();
        if (_group == null)
            _group = gameObject.AddComponent<CanvasGroup>();
    }

    private void LateUpdate()
    {
        if (_rect == null || _group == null)
            return;

        if (Time.unscaledTime >= _nextHandScan)
        {
            _nextHandScan = Time.unscaledTime + 0.5f;
            RefreshHands();
            _colliders = GetComponents<Collider>();
        }

        float target = AnyFingertipBehindPanel() ? XrayAlpha : 1f;
        _group.alpha = Mathf.Lerp(
            _group.alpha, target, 10f * Time.unscaledDeltaTime);

        bool solid = _group.alpha > 0.72f;
        _group.blocksRaycasts = solid;
        _group.interactable = solid;
        foreach (Collider collider in _colliders)
            if (collider != null && collider.enabled != solid)
                collider.enabled = solid;
    }

    private bool AnyFingertipBehindPanel()
    {
        foreach (IHand hand in _hands)
        {
            if (hand == null || !hand.IsTrackedDataValid)
                continue;

            Pose tip;
            if (!hand.GetJointPose(HandJointId.HandIndexTip, out tip))
                continue;

            // 깊이: 패널 법선(+Z, 사용자 반대쪽) 기준으로 면 너머인지.
            float depth = Vector3.Dot(
                tip.position - _rect.position, _rect.forward);
            if (depth < BehindDepth)
                continue;

            // 평면 범위: 패널 rect 안(여유 포함)에서 뻗었을 때만.
            Vector3 local = _rect.InverseTransformPoint(tip.position);
            Vector2 half = _rect.sizeDelta * 0.5f;
            if (Mathf.Abs(local.x) <= half.x + EdgeMargin &&
                Mathf.Abs(local.y) <= half.y + EdgeMargin)
                return true;
        }
        return false;
    }

    // MvpNodeContactGate 와 같은 방식 — 손마다 실제 Hand 를 우선해 한 개만 남긴다.
    private void RefreshHands()
    {
        _hands.Clear();
        Dictionary<Handedness, IHand> best =
            new Dictionary<Handedness, IHand>();

        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            IHand candidate = behaviour as IHand;
            if (candidate == null)
                continue;

            Handedness side;
            try { side = candidate.Handedness; }
            catch { continue; }

            if (!best.TryGetValue(side, out IHand current) ||
                Prefer(candidate, current))
                best[side] = candidate;
        }

        foreach (IHand hand in best.Values)
            if (hand.IsConnected && hand.IsTrackedDataValid)
                _hands.Add(hand);
    }

    private static bool Prefer(IHand candidate, IHand current)
    {
        if (candidate == null) return false;
        if (current == null) return true;
        bool cValid = candidate.IsConnected && candidate.IsTrackedDataValid;
        bool xValid = current.IsConnected && current.IsTrackedDataValid;
        if (cValid != xValid) return cValid;
        return candidate.GetType().Name == "Hand" &&
               current.GetType().Name != "Hand";
    }
}
