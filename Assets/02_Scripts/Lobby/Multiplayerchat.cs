using Fusion;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;
using UnityEngine.EventSystems;

public class Multiplayerchat : NetworkBehaviour
{
    public TMP_InputField input;
    public TMP_InputField usernameInput;
    public string username = "default";

    [Header("Chat UI References")]
    public Transform chatContentParent;
    public GameObject chatTextPrefab;
    public TMP_Dropdown playerDropdown;

    [Header("Chat Panel")]
    public GameObject chatPanel;
    
    [Header("Chat Sound")]
    public AudioClip chatSound;
    private AudioSource audioSource;


    private Dictionary<string, List<string>> chatLogs = new Dictionary<string, List<string>>();
    private readonly List<GameObject> _renderedMessages = new();
    private string currentChannel = "ALL";

    void Awake()
    {
        // ����(Submit)�� �ٷ� ����
        if (input != null)
        {
            // ������: ���� Ÿ���� ���ۿ� �°� ���� (���ϸ� �ּ�ó��)
            // SingleLine �Ǵ� MultiLineSubmit ����
            input.lineType = TMP_InputField.LineType.SingleLine;

            input.onSubmit.AddListener(_ => SubmitFromInput());
            // �Ϻ� �÷���/�������� onSubmit�� �� �� ���� ������ onEndEdit�� ������� ��� ����
            // input.onEndEdit.AddListener(_ => SubmitFromInput());
        }
    }
    void Start()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
    }
    private void PlayChatSound()
    {
        if (chatSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(chatSound);
        }
    }


    void OnDestroy()
    {
        if (input != null)
        {
            input.onSubmit.RemoveListener(_ => SubmitFromInput());
            // input.onEndEdit.RemoveListener(_ => SubmitFromInput());
        }
    }

    // ��ǲ�ʵ忡�� ���ͷ� ȣ��Ǵ� �� ���� �Լ�
    private void SubmitFromInput()
    {
        // ��Ŀ�� + �� ���ڿ� üũ
        if (!input.isFocused) return;
        if (string.IsNullOrWhiteSpace(input.text)) return;

        CallMessagePRC();

        // ���� �Ŀ��� �Է� ��� ġ�� ��Ŀ�� ����
        input.ActivateInputField();
        input.MoveTextEnd(false);
    }

    void Update()
    {
        // ����: ����/�ѹ��е� ���� ���� ���� (IME ȯ�� �� onSubmit ���� ���)
        if (input.isFocused && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
        {
            SubmitFromInput();
            return;
        }

        // ��ǲ ��Ŀ�� �߿� �ٸ� Ű ���� ����
        if (input.isFocused)
            return;

        if (Input.GetKeyDown(KeyCode.Y))
        {
            ShowAllPlayerNames();
        }

        if (Input.GetKeyDown(KeyCode.T))
        {
            if (chatPanel != null)
            {
                bool isActive = !chatPanel.activeSelf;
                chatPanel.SetActive(isActive);

                if (isActive)
                {
                    Cursor.visible = true;
                    Cursor.lockState = CursorLockMode.None;

                    // ��ǲ �ʵ忡 �ٽ� ��Ŀ�� �ֱ�
                    if (EventSystem.current != null)
                    {
                        EventSystem.current.SetSelectedGameObject(input.gameObject);
                        input.ActivateInputField();
                    }

                    input.ActivateInputField();
                }
                else
                {
                    Cursor.visible = false;
                    Cursor.lockState = CursorLockMode.Locked;

                    // ���õ� UI ���� (Ȥ�ó� �浹 ������)
                    EventSystem.current.SetSelectedGameObject(null);
                }
            }
        }


    }

    void ShowAllPlayerNames()
    {
        foreach (var playerRef in Runner.ActivePlayers)
        {
            NetworkObject playerObj = Runner.GetPlayerObject(playerRef);
            if (playerObj != null)
            {
                PlayerInfo info = playerObj.GetComponent<PlayerInfo>();
                if (info != null)
                {
                    Debug.Log($"���� �濡 �ִ� �÷��̾�: {info.playerName}");
                }
            }
        }
    }

    public void RefreshPlayerDropdown()
    {
        playerDropdown.ClearOptions();
        List<string> names = new List<string> { "ALL" };

        foreach (var playerRef in Runner.ActivePlayers)
        {
            var obj = Runner.GetPlayerObject(playerRef);
            if (obj == null) continue;

            var info = obj.GetComponent<PlayerInfo>();
            if (info == null) continue;

            string name = info.playerName.ToString();

            // �ڱ� �ڽ��� "memo"�� ǥ��
            if (playerRef == Runner.LocalPlayer)
            {
                name = "memo";
            }

            if (!string.IsNullOrEmpty(name))
            {
                names.Add(name);
            }
        }

        playerDropdown.AddOptions(names);
        playerDropdown.value = 0;
    }

    public void RemovePlayerFromDropdown(string nameToRemove)
    {
        List<string> currentOptions = new List<string>();
        playerDropdown.options.ForEach(opt => currentOptions.Add(opt.text));

        if (currentOptions.Contains(nameToRemove))
        {
            currentOptions.Remove(nameToRemove);
            playerDropdown.ClearOptions();
            playerDropdown.AddOptions(currentOptions);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_RemovePlayerName(string name)
    {
        RemovePlayerFromDropdown(name);
    }

    public void OnLeaveRoomButtonPressed()
    {
        string myName = username;
        StartCoroutine(RemoveNameAndReturnToLobby(myName));
    }

    IEnumerator RemoveNameAndReturnToLobby(string name)
    {
        RPC_RemovePlayerName(name);
        RemovePlayerFromDropdown(name);
        yield return new WaitForSeconds(0.3f);
        NetworkManager.ReturnToLobby();
    }

    public void SetUnername()
    {
        username = usernameInput.text;
        var localPlayer = Runner.GetPlayerObject(Runner.LocalPlayer);
        if (localPlayer != null)
        {
            var info = localPlayer.GetComponent<PlayerInfo>();
            if (info != null)
                info.SetPlayerName(username);
        }
        RPC_RefreshDropdownForAll();
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_RefreshDropdownForAll()
    {
        RefreshPlayerDropdown();
    }

    public void OnDropdownValueChanged()
    {
        string selected = playerDropdown.options[playerDropdown.value].text;
        currentChannel = selected;
        DisplayChatLogForChannel(currentChannel);
    }

    void DisplayChatLogForChannel(string channel)
    {
        ClearRenderedMessages();

        if (chatLogs.TryGetValue(channel, out var messages))
        {
            foreach (var msg in messages)
            {
                SpawnChatMessageUI(msg);
            }
        }
    }

    public void CallMessagePRC()
    {
        string message = input.text;
        if (string.IsNullOrEmpty(message)) return;

        string target = playerDropdown.options[playerDropdown.value].text;
        input.text = "";

        if (target == "ALL")
        {
            RPC_SendPublicMessage(username, message);
        }
        else if (target == "memo")
        {
            string formatted = $"[memo] {message}";
            AppendToChat("memo", formatted);
            if (currentChannel == "memo")
                AddMessageToUI(formatted);
        }
        else
        {
            RPC_SendWhisper(username, target, message);
        }

        PlayChatSound();
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_SendPublicMessage(string sender, string message)
    {
        string formatted = $"<color=red>{sender}:</color> {message}";
        AppendToChat("ALL", formatted);
        if (currentChannel == "ALL")
            AddMessageToUI(formatted);

        PlayChatSound();
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_SendWhisper(string sender, string receiver, string message)
    {
        var localPlayer = Runner.GetPlayerObject(Runner.LocalPlayer);
        if (localPlayer == null) return;

        var info = localPlayer.GetComponent<PlayerInfo>();
        if (info == null) return;

        string myName = info.playerName.ToString();

        if (myName == sender || myName == receiver)
        {
            //string formatted = $"<color=green>{sender} > {receiver}:</color> {message}";
            string formatted = $"<color=green>{sender}:</color> {message}";
            string channelKey = (myName == sender) ? receiver : sender;

            AppendToChat(channelKey, formatted);

            if (currentChannel == channelKey)
                AddMessageToUI(formatted);

            PlayChatSound();
        }
    }

    void AppendToChat(string channel, string message)
    {
        if (!chatLogs.ContainsKey(channel))
            chatLogs[channel] = new List<string>();

        chatLogs[channel].Add(message);
    }

    void AddMessageToUI(string message)
    {
        SpawnChatMessageUI(message);
    }

    void SpawnChatMessageUI(string message)
    {
        if (!chatContentParent || !chatTextPrefab)
        {
            Debug.LogWarning("[Chat] chatContentParent 또는 chatTextPrefab이 비어있어 UI를 생성할 수 없습니다.");
            return;
        }

        var chatObj = Instantiate(chatTextPrefab, chatContentParent);
        var tmp = chatObj.GetComponent<TMP_Text>();
        if (tmp != null) tmp.text = message;
        _renderedMessages.Add(chatObj);
    }

    void ClearRenderedMessages()
    {
        for (int i = _renderedMessages.Count - 1; i >= 0; i--)
        {
            if (_renderedMessages[i])
                Destroy(_renderedMessages[i]);
        }

        _renderedMessages.Clear();
    }
}
