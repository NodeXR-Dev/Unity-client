using System;
using UnityEngine;
using UnityEngine.UI;

// 디자이너 시안 패널(ConfirmPanel / GoalPannel …)의 버튼을 런타임에 배선한다.
//
// 프리팹의 onClick 은 씬 오브젝트를 참조할 수 없다. 그래서 시안 버튼은 프리팹에
// Button 컴포넌트만 붙여 두고, 실제 동작은 패널을 띄우는 쪽에서 여기로 연결한다.
// (Button_2D / Button_3D / Report 를 배선한 것과 같은 이유)
//
// [IconButton 과의 공존] 시안 버튼에는 디자이너 IconButton 이 붙어 있다.
//   IconButton 은 hover/pressed 스프라이트 교체만 하고 클릭 이벤트가 없어서 Button 이 필요했다.
//   Unity 이벤트 시스템은 같은 오브젝트의 모든 핸들러에 포인터 이벤트를 전달하므로
//   둘은 그대로 공존한다 — Button 은 클릭, IconButton 은 시각 표현을 맡는다.
//
//   단 Button 의 Transition 은 꺼야 한다. 시안 프리팹은 ColorTint 로 되어 있는데,
//   그대로 두면 hover 시 **스프라이트 교체 + Unity 색 덧칠(0.96 회색)** 이 겹쳐
//   두 번 어두워진다. 디자이너가 스프라이트에 이미 상태를 그려 넣었으므로 덧칠은 군더더기다.
public static class MvpPanelButtonBinder
{
    // 패널 안에서 이름으로 버튼을 찾아 콜백을 건다. 못 찾으면 경고를 남기고 null 을 준다.
    public static Button Wire(GameObject panel, string buttonName, Action onClick)
    {
        if (panel == null)
            return null;

        Button button = FindButton(panel, buttonName);
        if (button == null)
        {
            Debug.LogWarning(
                "[MvpPanelButtonBinder] '" + panel.name + "' 안에서 버튼 '" +
                buttonName + "' 을 찾지 못했습니다. 시안 프리팹의 자식 이름이 바뀌었는지, " +
                "Button 컴포넌트가 붙어 있는지 확인하세요.");
            return null;
        }

        // 시각 표현은 IconButton 이 갖는다(위 주석 참고).
        button.transition = Selectable.Transition.None;

        // 패널을 다시 띄울 때 콜백이 쌓이지 않게 비우고 건다.
        button.onClick.RemoveAllListeners();
        if (onClick != null)
            button.onClick.AddListener(() => onClick());

        return button;
    }

    // 버튼을 잠그거나 푼다.
    // IconButton.isDisabled 는 Button.interactable 과 별개라서, 한쪽만 바꾸면
    // '눌리지는 않는데 멀쩡해 보이는' 버튼이 된다. 둘을 같이 맞춘다.
    public static void SetInteractable(Button button, bool interactable)
    {
        if (button == null)
            return;

        button.interactable = interactable;

        IconButton icon = button.GetComponent<IconButton>();
        if (icon != null)
            icon.SetDisabled(!interactable);
    }

    // 이름이 같은 자식이 여럿이면 첫 번째를 쓴다. 비활성 자식도 찾는다.
    private static Button FindButton(GameObject panel, string buttonName)
    {
        foreach (Button candidate in
                 panel.GetComponentsInChildren<Button>(true))
        {
            if (candidate != null && candidate.name == buttonName)
                return candidate;
        }
        return null;
    }
}
