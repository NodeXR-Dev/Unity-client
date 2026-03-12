using UnityEngine;
using NodeXR.UI;

namespace NodeXR
{
    public class SceneRefs : MonoBehaviour
    {
        [Header("Core")]
        public AppStateMachine app;
        public SessionContext session;

        [Header("Networking")]
        public ServerClient server;
        public TextureDownloader textureDownloader;

        [Header("UI")]
        public MainHUDController hud;
        public CandidateCanvasController candidates;
        public CategoryCanvasController categoryCanvas;
        public CoreImagePanelView corePanel;
        public Preview3DController preview3D;
    }
}
