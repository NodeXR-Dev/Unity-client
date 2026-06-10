using Fusion;
using UnityEngine;
using TMPro;

public class PlayerSpawner : MonoBehaviour
{
    public GameObject playerPrefab;
    [SerializeField] private bool useSpawnerTransform = true;
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

        if (runner.GetPlayerObject(runner.LocalPlayer) != null)
        {
            Debug.Log("[PlayerSpawner] Local player object already exists. Skip spawn.");
            return;
        }

        Vector3 spawnPosition = useSpawnerTransform ? transform.position : spawnPoint;
        Quaternion spawnRotation = useSpawnerTransform ? transform.rotation : Quaternion.identity;

        runner.Spawn(
            playerPrefab,
            spawnPosition,
            spawnRotation,
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
