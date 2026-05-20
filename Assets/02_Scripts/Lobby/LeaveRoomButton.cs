using UnityEngine;
using Fusion;

public class LeaveRoomButton : MonoBehaviour
{
    public NetworkRunner runner;

    // 버튼 OnClick에 연결
    public void OnLeaveRoomButtonPressed()
    {
        if (runner == null)
        {
            Debug.LogError("[LeaveRoomButton] NetworkRunner가 연결되지 않았습니다.");
            return;
        }

        LeaveRoom();
    }

    void LeaveRoom()
    {
        // 세션 종료
        runner.Shutdown();

        // 로비 씬으로 이동
        NetworkManager.ReturnToLobby();
    }
}
