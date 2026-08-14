using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class SessionListEntry : MonoBehaviour
{
    public TextMeshProUGUI roomName, participantNames;
    public TextMeshProUGUI startTimeText; 
    public Button joinButton;       // [참여하기] 버튼
    public Button endedButton;      // [종료됨] 버튼 (지선님이 새로 추가할 변수!)

    private SessionInfo sessionInfo;

    // 1. 실시간 세션용 (Photon)
    public void Setup(SessionInfo info)
    {
        sessionInfo = info;

        // 버튼 스위칭: 참여 On / 종료 Off
        joinButton.gameObject.SetActive(true);
        if (endedButton != null) endedButton.gameObject.SetActive(false);
        
        joinButton.interactable = true;

        string displayName = info.Name; 
        if (info.Properties.TryGetValue("DisplayTopic", out var topicProp))
            displayName = topicProp.PropertyValue.ToString();
        roomName.text = displayName; 

        if (info.Properties.TryGetValue("StartTime", out var timeProp))
            startTimeText.text = timeProp.PropertyValue.ToString();
        else
            startTimeText.text = "--:--";

        LoadParticipantNames(info.Name);

        joinButton.onClick.RemoveAllListeners();
        joinButton.onClick.AddListener(() =>
        {
            FindFirstObjectByType<NetworkManager>().RequestJoinSession(sessionInfo);
        });
    }

    // 2. 과거 DB 데이터용
    public void SetupFromDB(ServerRoomData data, bool isActive)
    {
        roomName.text = data.room_topic;
        
        if (data.created_at.Length >= 16)
            startTimeText.text = data.created_at.Substring(11, 5);
        else
            startTimeText.text = data.created_at;

        LoadParticipantNames(data.room_id);

        // --- 지선님이 말씀하신 버튼 활성화/비활성화 핵심 로직 ---
        if (isActive)
        {
            // 아직 살아있는 방이면 [참여하기]만 보이기
            joinButton.gameObject.SetActive(true);
            if (endedButton != null) endedButton.gameObject.SetActive(false);

            joinButton.interactable = true;
            joinButton.onClick.RemoveAllListeners();

            // 리스너를 지우기만 하고 다시 붙이지 않아, 살아있는 방인데도 눌러도
            // 아무 일이 없었다. 두 번째 사람이 방에 들어갈 방법이 없던 원인이다.
            // 실시간 세션 경로(Setup)와 달리 여기에는 SessionInfo 가 없으므로
            // room_id 로 Photon 세션 목록에서 되찾아 넘긴다.
            string roomId = data.room_id;
            joinButton.onClick.AddListener(() =>
            {
                NetworkManager manager = FindFirstObjectByType<NetworkManager>();
                if (manager == null)
                    return;

                SessionInfo live = manager.FindCachedSession(roomId);
                if (live == null)
                {
                    // 목록을 그린 뒤 방이 닫혔을 수 있다.
                    Debug.LogWarning(
                        "[로비] 참여하려는 방이 더 이상 열려 있지 않습니다: " + roomId);
                    return;
                }

                manager.RequestJoinSession(live);
            });
        }
        else
        {
            // 이미 종료된 과거 방이면 [종료됨] 버튼만 보이기
            joinButton.gameObject.SetActive(false);
            if (endedButton != null)
            {
                endedButton.gameObject.SetActive(true);
                endedButton.interactable = false; // 클릭 안 되게 막기
            }
        }
    }

    // --- 이하 기존 명단 로드 로직 동일 ---
    private void LoadParticipantNames(string roomId)
    {
        participantNames.text = "불러오는 중...";
        NetworkManager.runnerInsatance.GetComponent<NetworkManager>().GetRoomInfo(roomId, (dbResult) => 
        {
            if (dbResult != null && dbResult.users != null)
            {
                List<string> names = new List<string>();
                string leaderName = "";
                foreach(var u in dbResult.users) {
                    names.Add(u.nickname);
                    if(u.leader) leaderName = u.nickname;
                }
                participantNames.text = ProcessParticipantList(names, leaderName);
            }
        });
    }

    private string ProcessParticipantList(List<string> names, string leaderName)
    {
        if (names == null || names.Count == 0) return "참가자 없음";
        List<string> uniqueNames = new List<string>();
        foreach (string n in names) if (!uniqueNames.Contains(n)) uniqueNames.Add(n);

        int leaderIndex = uniqueNames.IndexOf(leaderName);
        if (leaderIndex == -1) leaderIndex = 0; 

        uniqueNames[leaderIndex] = "<color=#FFD700><sprite name=\"checkmark--filled\"></color> " + uniqueNames[leaderIndex];
        return string.Join(" · ", uniqueNames);
    }
}