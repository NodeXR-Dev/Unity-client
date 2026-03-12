using UnityEngine;
using TMPro;

public class VRKeyboardOpener : MonoBehaviour
{
    private TouchScreenKeyboard _keyboard;
    public TMP_InputField targetInputField;

    public void OpenSystemKeyboard()
    {
        // 기존에 입력되어 있던 텍스트를 키보드에 전달하며 엽니다.
        _keyboard = TouchScreenKeyboard.Open(targetInputField.text, TouchScreenKeyboardType.Default);
    }

    private void Update()
    {
        if (_keyboard != null)
        {
            // 키보드에서 입력한 내용을 인풋필드에 실시간 반영
            targetInputField.text = _keyboard.text;

            // 엔터(Done)를 누르거나 키보드가 닫히면 참조 해제
            if (_keyboard.status == TouchScreenKeyboard.Status.Done || 
                _keyboard.status == TouchScreenKeyboard.Status.Canceled)
            {
                _keyboard = null;
            }
        }
    }
}