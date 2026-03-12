using UnityEngine;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;
using NodeXR; 

public class DecisionGraphManager : MonoBehaviour
{
    [Header("Settings")]
    public GameObject stepNodePrefab;    
    public Transform centerTransform;    
    
    [Header("Layout Tuning")]
    public float nodeOriginalHeight = 800f; 

    private List<CanvasGroup> historyNodes = new List<CanvasGroup>();

private void Awake()
    {
        // [수정] 무조건 Clear하지 말고, 씬에 이미 노드가 있다면 리스트에 넣어줍니다.
        historyNodes.Clear();
        var existingNode = centerTransform.GetComponentInChildren<CanvasGroup>();
        if (existingNode != null)
        {
            historyNodes.Add(existingNode);
            // 시작 시 노드그래프가 안 보이길 원하시면 아래 주석을 해제하세요.
            // existingNode.gameObject.SetActive(false); 
        }
    }

    public void CreateNextStep(string selectedCategoryName, Texture2D[] newTextures)
    {
        Debug.Log($"<color=yellow>[Graph]</color> CreateNextStep 호출됨! 카테고리: {selectedCategoryName}");

        if (stepNodePrefab == null || centerTransform == null) {
            Debug.LogError("[Graph] Prefab이나 CenterTransform이 비어있습니다!");
            return;
        }

        // [1] 위치 소환: 소현님 눈앞으로 centerTransform 이동
        Vector3 camPos = Camera.main.transform.position;
        Vector3 camForward = Camera.main.transform.forward;
        camForward.y = 0; 
        centerTransform.position = camPos + camForward.normalized * 1.5f;
        centerTransform.LookAt(new Vector3(camPos.x, centerTransform.position.y, camPos.z));
        centerTransform.Rotate(0, 180, 0);

        // [2] 실제 노드 생성
        GameObject newNode = Instantiate(stepNodePrefab, centerTransform);
        newNode.transform.localPosition = Vector3.zero;
        newNode.transform.localRotation = Quaternion.identity;
        newNode.SetActive(true); // 혹시 프리팹이 꺼져있을 경우를 대비
        
        CanvasGroup newCG = newNode.GetComponent<CanvasGroup>() ?? newNode.AddComponent<CanvasGroup>();
        historyNodes.Insert(0, newCG);

        // [3] 라벨 설정 로직
        var controller = newNode.GetComponentInChildren<CandidateCanvasController>();
        if (controller != null)
        {
            string finalLabel = "";
            
            // historyNodes.Count가 1이면 방금 만든 게 '진짜 첫 번째' 노드입니다.
            if (historyNodes.Count == 1)
            {
                // 첫 발화(basicdiscuss) 후 결과물일 때
                finalLabel = string.IsNullOrEmpty(selectedCategoryName) ? "분석 결과" : selectedCategoryName;
                newNode.name = "RootNode_FirstStep";
            }
            else
            {
                // 그 다음 단계부터
                finalLabel = selectedCategoryName;
                newNode.name = $"StepNode_{historyNodes.Count}_{selectedCategoryName}";
            }

            controller.SetCategoryLabel(finalLabel);
            
            if (newTextures != null)
            {
                for (int i = 0; i < newTextures.Length; i++)
                {
                    if (i < 3 && newTextures[i] != null) controller.SetCardTexture(i, newTextures[i]);
                }
            }
        }

        newNode.transform.SetAsFirstSibling(); 
        RefreshNodesLayout();
        UpdateVisuals(newNode);
        
        Debug.Log($"<color=lime>[Graph]</color> {historyNodes.Count}번째 노드 생성 성공: {newNode.name}");
    }

    // --- 아래 함수들은 기존과 동일하게 유지 ---
    public void RefreshNodesLayout()
    {
        for (int i = 0; i < historyNodes.Count; i++)
        {
            if (historyNodes[i] == null) continue;
            ApplyHistoryEffect(historyNodes[i], i);
        }
    }

    private void ApplyHistoryEffect(CanvasGroup cg, int depth)
    {
        float alpha = Mathf.Max(1.0f - (depth * 0.3f), 0.3f);
        float scale = Mathf.Max(1.0f - (depth * 0.15f), 0.6f);

        cg.alpha = alpha;
        cg.transform.localScale = new Vector3(scale, scale, 1f);
        
        if (cg.TryGetComponent<LayoutElement>(out var layout))
            layout.preferredHeight = nodeOriginalHeight * scale;
        
        cg.interactable = (depth == 0);
        cg.blocksRaycasts = (depth == 0);
    }

    private void UpdateVisuals(GameObject node)
    {
        Transform connector = node.transform.Find("Vertical_Connector");
        if (connector != null)
            connector.gameObject.SetActive(historyNodes.Count > 1);

        Canvas.ForceUpdateCanvases();
        if (centerTransform is RectTransform rect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
    }

    public CandidateCanvasController GetLatestController()
    {
        // historyNodes 리스트가 비어있지 않은지 확인하고 첫 번째(최신) 노드의 컨트롤러를 반환합니다.
        if (historyNodes != null && historyNodes.Count > 0 && historyNodes[0] != null)
        {
            return historyNodes[0].GetComponentInChildren<CandidateCanvasController>();
        }
        return null;
    }
}