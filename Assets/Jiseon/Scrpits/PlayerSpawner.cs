using Fusion;
using UnityEngine;
using TMPro;

public class PlayerSpawner : MonoBehaviour
{
    public GameObject playerPrefab;
    public Vector3 spawnPoint = new Vector3(0, 1, 0);
    public TMP_InputField nameInputField;

    public void SpawnLocalPlayer()
    {
        var runner = NetworkManager.runnerInsatance;

        if (runner == null)
        {
            Debug.LogError("Runner ����");
            return;
        }

        if (runner.LocalPlayer == null)
        {
            Debug.LogError("LocalPlayer ����");
            return;
        }

        runner.Spawn(
            playerPrefab,
            spawnPoint,
            Quaternion.identity,
            runner.LocalPlayer,
            (runner, obj) =>
            {
                runner.SetPlayerObject(runner.LocalPlayer, obj);

                var info = obj.GetComponent<PlayerInfo>();
                if (info != null)
                {
                    string name = nameInputField != null && !string.IsNullOrEmpty(nameInputField.text)
                        ? nameInputField.text
                        : "Tester";

                    info.SetPlayerName(name);
                }
            }
        );
    }
}
