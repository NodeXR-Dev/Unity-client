using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class returntolobby : MonoBehaviour
{
    public void returnlobby()
    {
        NetworkManager.ReturnToLobby();
    }

    public void gotolobby()
    {
        SceneManager.LoadScene("LobbyScene");
    }
}
