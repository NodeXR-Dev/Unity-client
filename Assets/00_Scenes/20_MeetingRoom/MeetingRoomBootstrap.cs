using UnityEngine;
using UnityEngine.SceneManagement;

  public class MeetingRoomBootstrap : MonoBehaviour
  {
      [SerializeField] private string graphSceneName = "MeetingRoom_GraphContent";
      [SerializeField] private string interactionSceneName = "MeetingRoom_Interaction";

      void Start()
      {
          LoadAdditive(graphSceneName);
          LoadAdditive(interactionSceneName);
      }

      void LoadAdditive(string sceneName)
      {
          if (string.IsNullOrEmpty(sceneName)) return;
          if (SceneManager.GetSceneByName(sceneName).isLoaded) return;
          SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
      }
  }