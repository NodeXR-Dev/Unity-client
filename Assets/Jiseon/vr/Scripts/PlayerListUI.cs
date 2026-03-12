using UnityEngine;
using TMPro;
using Fusion;
using System.Collections.Generic;

public class PlayerListUI : MonoBehaviour
{
    [Header("상단 주제 (Topic)")]
    public TextMeshProUGUI roomTopicText; 

    [Header("참여자 카드 설정")]
    public GameObject playerCardPrefab; // 만들어둔 카드 프리팹
    public Transform cardParent;        // Horizontal Layout Group이 있는 Content 오브젝트

    private NetworkRunner _runner;
    private string _dbRoomTopic = "";
    private List<UserInfo> _dbUsers = new List<UserInfo>();
    private bool _isDataLoaded = false;

    // 현재 생성된 카드들을 추적하기 위한 리스트
    private Dictionary<string, GameObject> _activeCards = new Dictionary<string, GameObject>();

    void Update()
    {
        if (_runner == null || !_runner.IsRunning)
        {
            _runner = FindFirstObjectByType<NetworkRunner>();
            if (_runner != null && _runner.IsRunning)
            {
                if (!_isDataLoaded) FetchDBRoomInfo();
            }
        }

        if (_runner != null && _runner.IsRunning)
        {
            UpdateUI();
        }
    }

    public void FetchDBRoomInfo()
    {
        if (_runner == null || string.IsNullOrEmpty(_runner.SessionInfo.Name)) return;

        _isDataLoaded = true;
        string roomId = _runner.SessionInfo.Name;

        NetworkManager.runnerInsatance.GetComponent<NetworkManager>().GetRoomInfo(roomId, (result) => {
            _dbRoomTopic = result.room_topic;
            _dbUsers = result.users;
            Debug.Log("서버로부터 참여자 명단을 갱신했습니다.");
        });
    }

    void UpdateUI()
    {
        // 1. 주제 텍스트 업데이트
        if (roomTopicText != null)
        {
            roomTopicText.text = string.IsNullOrEmpty(_dbRoomTopic) ? "회의 정보를 불러오는 중..." : _dbRoomTopic;
        }

        // 2. 참여자 카드 업데이트
        if (_dbUsers == null || cardParent == null || playerCardPrefab == null) return;

        // 중복 방지를 위해 현재 DB 인원과 UI 카드 개수가 다를 때만 갱신 (간단한 최적화)
        // 실제로는 더 정밀한 비교가 필요하지만, 우선은 닉네임 리스트를 기반으로 생성합니다.
        HashSet<string> currentNicknames = new HashSet<string>();

        foreach (var user in _dbUsers)
        {
            if (currentNicknames.Contains(user.nickname)) continue;
            currentNicknames.Add(user.nickname);

            // 해당 유저의 카드가 이미 생성되어 있는지 확인
            if (!_activeCards.ContainsKey(user.nickname))
            {
                CreatePlayerCard(user);
            }
        }

        // 나간 유저의 카드는 삭제 (나중에 구현하거나, 일단은 생성 위주로!)
    }

    void CreatePlayerCard(UserInfo user)
    {
        GameObject newCard = Instantiate(playerCardPrefab, cardParent);
        PlayerCardScript cardScript = newCard.GetComponent<PlayerCardScript>();

        if (cardScript != null)
        {
            string displayName = user.nickname;
            string myNick = PlayerPrefs.GetString("PlayerNickname", "");
            if (displayName == myNick) displayName += " (나)";

            // 1. 닉네임 설정
            cardScript.nicknameText.text = displayName;

            // 2. 렌더 텍스처 연결 (핵심!)
            StartCoroutine(LinkFaceRoutine(user.nickname, cardScript));
        }

        _activeCards.Add(user.nickname, newCard);
    }

    System.Collections.IEnumerator LinkFaceRoutine(string nickname, PlayerCardScript card)
    {
        // 1. 캐릭터가 완전히 생성될 때까지 대기
        yield return new WaitForSeconds(1.0f);

        GameObject targetPlayer = null;

        // 2. 씬에 있는 모든 'Player' 태그 오브젝트 중 이름이 닉네임과 일치하는 놈 찾기
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        
        foreach (GameObject p in players)
        {
            // 오브젝트 이름에 닉네임이 포함되어 있는지 확인 (가장 확실함!)
            if (p.name.Contains(nickname))
            {
                targetPlayer = p;
                break;
            }
        }

        // 3. 만약 못 찾았다면 그냥 이름으로 직접 검색
        if (targetPlayer == null) targetPlayer = GameObject.Find(nickname);

        if (targetPlayer != null)
        {
            // 나만의 도화지 생성
            RenderTexture personalRT = new RenderTexture(256, 256, 16);
            personalRT.Create();

            // 캐릭터 안의 카메라 찾기
            Camera faceCam = targetPlayer.GetComponentInChildren<Camera>();

            if (faceCam != null)
            {
                faceCam.targetTexture = personalRT;
                // [수정 완료] 아까 만든 Setup 함수 호출!
                card.Setup(nickname, personalRT); 
                
                Debug.Log($"{nickname} 얼굴 연결 성공!");
            }
        }
        else
        {
            Debug.LogWarning($"{nickname} 캐릭터를 찾을 수 없어 얼굴 연결을 실패했습니다.");
        }
    }
}