using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;

// Quest에서 손 레이로 입력칸을 선택했을 때 Unity/Android 화면 키보드를 열도록 보조한다.
// 에디터에서는 기존 마우스·키보드 입력을 그대로 사용한다.
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_InputField))]
public class MvpXrKeyboardInput :
    MonoBehaviour,
    IPointerClickHandler,
    ISelectHandler
{

    private TMP_InputField _input;

    private void Awake()
    {
        Configure(GetComponent<TMP_InputField>());
    }

    private void OnEnable()
    {
        ApplyKeyboardSettings();
    }

    public void Configure(TMP_InputField input)
    {
        if (input != null)
            _input = input;
        ApplyKeyboardSettings();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        OpenForNode();
    }

    public void OpenForNode()
    {
        if (_input == null || _input.readOnly)
        return;

        NodeView node = _input.GetComponentInParent<NodeView>();
        if (node != null)
        {
            MvpNodeInteractionController interaction =
            FindFirstObjectByType<MvpNodeInteractionController>();
            if (interaction != null)
            {
                interaction.TryOpenNodeKeyboard(node, _input);
                return;
            }

            MvpWorldKeyboard.Open(_input);
            return;
        }

        // 노드 외의 입력칸(예: 직접 만들 부품)도 Quest/Link에서
        // 동일한 공간 키보드를 사용해야 입력과 완료 이벤트가 일관된다.
        MvpWorldKeyboard.Open(_input);
    }

    public void OnSelect(BaseEventData eventData)
    {
        ApplyKeyboardSettings();
    }


    private void ApplyKeyboardSettings()
    {
        if (_input == null)
            return;

        _input.shouldHideSoftKeyboard = false;
        _input.shouldHideMobileInput = false;
    }

}
