using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem; // 새 입력 시스템 추가
using System.Collections.Generic;

namespace NodeXR
{
    public class UIClickDebugger : MonoBehaviour
    {
        void Update()
        {
            // 새 입력 시스템 방식으로 마우스 왼쪽 클릭 체크
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                PointerEventData eventData = new PointerEventData(EventSystem.current);
                // 현재 마우스 위치 가져오기
                eventData.position = Mouse.current.position.ReadValue();

                List<RaycastResult> results = new List<RaycastResult>();
                
                if (EventSystem.current != null)
                {
                    EventSystem.current.RaycastAll(eventData, results);

                    if (results.Count > 0)
                    {
                        Debug.Log("<color=cyan><b>[UI Click Debugger]</b></color> 클릭 지점에 있는 오브젝트 목록:");
                        for (int i = 0; i < results.Count; i++)
                        {
                            // [0]번이 실제 클릭을 먹고 있는 범인입니다!
                            Debug.Log($"<color=yellow>[{i}] {results[i].gameObject.name}</color> (Layer: {LayerMask.LayerToName(results[i].gameObject.layer)})");
                        }
                    }
                    else
                    {
                        Debug.Log("<color=red>[UI Click Debugger]</color> 클릭된 UI가 아무것도 없습니다.");
                    }
                }
            }
        }
    }
}