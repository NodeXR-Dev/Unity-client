using System;
using UnityEngine;

public enum MvpFlowState
{
    Welcome,
    CreateRoom,
    JoinRoom,
    Briefing,
    Design,
    Review,
    Generating,
    Result,
    History,
    ThreeD,
    Complete
}

[Serializable]
public class MvpSessionData
{
    public string roomId;
    public string roomName;
    public string topic;
    public string goal;
    public string nickname;
    public string password;
    public string userId;
    public bool online;

    public string ModeLabel => online ? "서버 연결됨" : "체험 모드";

    public void Reset()
    {
        roomId = "";
        roomName = "";
        topic = "";
        goal = "";
        nickname = "";
        password = "";
        userId = "";
        online = false;
    }
}

[Serializable]
public class MvpCreateRoomRequest
{
    public string room_topic;
    public string password;
    public string nickname;
}

[Serializable]
public class MvpEnterRoomRequest
{
    public string room_id;
    public string nickname;
    public string password;
}

[Serializable]
public class MvpCreateRoomEnvelope
{
    public bool isSuccess;
    public string code;
    public string message;
    public MvpCreateRoomResult result;
}

[Serializable]
public class MvpCreateRoomResult
{
    public string room_id;
    public string room_topic;
    public string password;
    public string leader;
    public string created_at;
}

[Serializable]
public class MvpEnterRoomEnvelope
{
    public bool isSuccess;
    public string code;
    public string message;
    public MvpEnterRoomResult result;
}

[Serializable]
public class MvpEnterRoomResult
{
    public string room_id;
    public string user_id;
}

public sealed class MvpSketchHistoryItem
{
    public Texture2D texture;
    public string title;
    public string summary;
    public DateTime createdAt;
    public bool fromServer;
}
