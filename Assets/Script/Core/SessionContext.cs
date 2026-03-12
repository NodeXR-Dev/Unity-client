using UnityEngine;

namespace NodeXR
{
    public class SessionContext : MonoBehaviour
    {
        [Header("Identity")]
        public string roomId = "00000000-0000-0000-0000-000000000000";
        public string userId = "00000000-0000-0000-0000-000000000000";

        [Header("State")]
        // SharedTypes.cs에 정의된 UnityPhase를 사용합니다.
        public UnityPhase phase = UnityPhase.BASIC_DISCUSS;

        [Header("UI State")]
        public string currentCategoryName;
        public string coreImgUrl;

        // 후보 3장 (카드 index 0..2)
        public string[] candidateNodeIds = new string[3];
        public string[] candidateImgUrls = new string[3];
        public string[] candidateLabels  = new string[3];

        public int selectedIndex = -1;

        public void ClearCandidates()
        {
            for (int i = 0; i < 3; i++)
            {
                candidateNodeIds[i] = null;
                candidateImgUrls[i] = null;
                candidateLabels[i] = null;
            }
            selectedIndex = -1;
        }
    }
}