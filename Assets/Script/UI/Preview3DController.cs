using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using GLTFast;

namespace NodeXR
{
    public class Preview3DController : MonoBehaviour
    {
        [Header("UI Elements")]
        public Canvas previewCanvas;
        public Button btnClose;
        public Button btnRefresh;

        [Header("Refresh Button Assets")]
        public Sprite refreshActiveSprite;
        public Sprite refreshInactiveSprite;
        private Image _refreshBtnImg;

        [Header("3D Scene Setup")]
        public Transform modelAnchor;
        public Image floorDisk;
        public float modelYOffset = 0.01f;
        public float targetSize = 0.2f;

        [Header("Orientation Fix")]
        [Tooltip("상하/전후가 뒤집히는 모델 보정용. 일반적으로 (180,180,0)이면 해결되는 경우가 많음.")]
        public Vector3 modelRotationFixEuler = new Vector3(0f, 180f, 0f);

        [Tooltip("모델이 항상 카메라(유저)를 바라보게 할지. 필요 없으면 끄세요.")]
        public bool faceToCameraYawOnly = false;

        public event Action OnClosed;
        public event Action OnRefreshRequested;

        private GameObject _spawned;
        private SceneRefs _refs;

        private void Awake()
        {
            _refs = GetComponentInParent<SceneRefs>();
            if (_refs == null) _refs = FindFirstObjectByType<SceneRefs>();

            if (btnRefresh) _refreshBtnImg = btnRefresh.GetComponent<Image>();

            if (btnClose)
            {
                btnClose.onClick.RemoveAllListeners();
                btnClose.onClick.AddListener(Close);
            }

            if (btnRefresh)
            {
                btnRefresh.onClick.RemoveAllListeners();
                btnRefresh.onClick.AddListener(HandleRefreshClick);
            }

            SetVisible(false);
        }

        public void OpenAndGenerate(string path = null)
        {
            Debug.Log("[3D] OpenAndGenerate start");

            if (_refs == null)
            {
                _refs = GetComponentInParent<SceneRefs>() ?? FindFirstObjectByType<SceneRefs>();
                if (_refs == null) return;
            }

            SetVisible(true);
            ShowMockModel();

            if (!string.IsNullOrEmpty(path))
            {
                Debug.Log($"[Preview3D] Load from provided path: {path}");
                StartCoroutine(LoadModelFromUrl(path));
            }
            else
            {
                Request3DGeneration();
            }
        }

        private void HandleRefreshClick()
        {
            Debug.Log("[Preview3D] Refresh clicked");
            OnRefreshRequested?.Invoke();
            Request3DGeneration();
        }

        public void Request3DGeneration()
        {
            if (_refs == null || _refs.server == null || _refs.session == null) return;

            string currentCoreUrl = _refs.session.coreImgUrl;
            string targetAssetId = "";

            var nodes = _refs.server.LastDto?.graph_state?.nodes;

            if (nodes != null && !string.IsNullOrEmpty(currentCoreUrl))
            {
                var foundNode = nodes.FirstOrDefault(n =>
                    !string.IsNullOrEmpty(n.img_url) && currentCoreUrl.Contains(n.img_url)
                );
                if (foundNode != null) targetAssetId = foundNode.node_id;
            }

            if (string.IsNullOrEmpty(targetAssetId))
            {
                Debug.LogError("[Preview3D] Failed to extract asset id");
                SetRefreshButtonState(true);
                return;
            }

            SetRefreshButtonState(false);

            StartCoroutine(_refs.server.Generate3D(_refs.session.roomId, targetAssetId, (success, glbUrl) =>
            {
                if (!success || string.IsNullOrEmpty(glbUrl))
                {
                    Debug.LogError("[Preview3D] 3D generation failed on server");
                    SetRefreshButtonState(true);
                    return;
                }

                string resolvedUrl = ResolveAssetUrl(glbUrl);

                Debug.Log($"[Preview3D] httpBaseUrl={_refs.server.httpBaseUrl}");
                Debug.Log($"[Preview3D] glbUrl(from server)={glbUrl}");
                Debug.Log($"[Preview3D] resolvedUrl(final)={resolvedUrl}");

                StartCoroutine(LoadModelFromUrl(resolvedUrl));
            }));
        }

        /// <summary>
        /// 서버가 localhost/127.0.0.1을 반환하는 구버전일 때만 host를 교체.
        /// ngrok(https)로 이미 내려오면 그대로 사용.
        /// </summary>
        private string ResolveAssetUrl(string glbUrl)
        {
            try
            {
                var u = new Uri(glbUrl);

                if (!string.Equals(u.Host, "localhost", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(u.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
                {
                    return glbUrl;
                }

                var apiBase = new Uri(_refs.server.httpBaseUrl);
                var builder = new UriBuilder(u)
                {
                    Host = apiBase.Host
                };

                return builder.Uri.ToString();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Preview3D] ResolveAssetUrl failed, use original. url={glbUrl}, err={e.Message}");
                return glbUrl;
            }
        }

        private IEnumerator LoadModelFromUrl(string url)
        {
            Debug.Log($"[Preview3D] Download start: {url}");

            string fileName = "temp_model.glb";
            string localPath = System.IO.Path.Combine(Application.persistentDataPath, fileName);
            string fileProtocolPath = "file://" + localPath;

            try
            {
                if (System.IO.File.Exists(localPath))
                    System.IO.File.Delete(localPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Preview3D] Could not delete old file: {e.Message}");
            }

            using (var www = new UnityEngine.Networking.UnityWebRequest(url, UnityEngine.Networking.UnityWebRequest.kHttpVerbGET))
            {
                www.timeout = 180;
                www.downloadHandler = new UnityEngine.Networking.DownloadHandlerFile(localPath);
                www.disposeDownloadHandlerOnDispose = true;

                www.SetRequestHeader("ngrok-skip-browser-warning", "1");

                yield return www.SendWebRequest();

                if (www.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    Debug.LogError(
                        "[Preview3D] Download Failed\n" +
                        $"- url: {url}\n" +
                        $"- result: {www.result}\n" +
                        $"- code: {www.responseCode}\n" +
                        $"- error: {www.error}"
                    );
                    SetRefreshButtonState(true);
                    yield break;
                }
            }

            long fileSize = 0;
            try
            {
                var fi = new System.IO.FileInfo(localPath);
                fileSize = fi.Exists ? fi.Length : 0;
            }
            catch { }

            Debug.Log($"[Preview3D] File saved: {localPath} (size={fileSize} bytes)");

            if (fileSize < 1024)
            {
                Debug.LogError("[Preview3D] File too small. Possibly got HTML warning/error page.");
                SetRefreshButtonState(true);
                yield break;
            }

            var gltf = new GltfImport();
            var loadTask = gltf.Load(fileProtocolPath);

            yield return new WaitUntil(() => loadTask.IsCompleted);

            if (!loadTask.Result)
            {
                Debug.LogError($"[Preview3D] glTF load failed: {fileProtocolPath}");
                SetRefreshButtonState(true);
                yield break;
            }

            ClearModel();

            _spawned = new GameObject("GeneratedModel_Root");
            _spawned.transform.SetParent(modelAnchor, false);

            var instantiateTask = gltf.InstantiateMainSceneAsync(_spawned.transform);
            yield return new WaitUntil(() => instantiateTask.IsCompleted);

            if (!instantiateTask.Result)
            {
                Debug.LogError("[Preview3D] InstantiateMainSceneAsync failed");
                SetRefreshButtonState(true);
                yield break;
            }

            PostProcessModel(_spawned);
            Debug.Log("[Preview3D] Success! Model loaded from local file.");
            SetRefreshButtonState(false);
        }

        private void PostProcessModel(GameObject root)
        {
            if (root == null) return;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            // 머티리얼 셰이더 보정
            foreach (var r in renderers)
            {
                if (r == null) continue;

                r.gameObject.layer = LayerMask.NameToLayer("Default");

                foreach (var mat in r.materials)
                {
                    if (mat == null) continue;

                    Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
                    if (urpShader != null) mat.shader = urpShader;
                    else mat.shader = Shader.Find("Standard");

                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
                    else if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
                }
            }

            // Bounds 계산
            Bounds bounds = new Bounds(renderers[0].bounds.center, Vector3.zero);
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            // 스케일 노멀라이즈
            float currentMaxSide = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (currentMaxSide > 0f)
            {
            // PostProcessModel 함수 내부
            float scaleFactor = targetSize / currentMaxSide;

            // ✅ [최종 처방] Y(상하)와 Z(앞뒤)를 모두 반전시킵니다.
            // 이렇게 하면 glTF 좌표계와 유니티 UI 좌표계 간의 충돌을 강제로 해결할 수 있습니다.
            root.transform.localScale = new Vector3(scaleFactor, -scaleFactor, -scaleFactor);

            // 위치 보정 (모델이 바닥 아래로 꺼지면 이 값을 키우세요)
            root.transform.localPosition = new Vector3(0, modelYOffset, 0);

            // 로테이션은 0으로 초기화해서 코드에서 꼬이지 않게 합니다.
            root.transform.localRotation = Quaternion.identity;
            }

            // 위치
            root.transform.localPosition = new Vector3(0, modelYOffset, 0);

            // ✅ 기본 방향/뒤집힘 보정 (상하 + 앞뒤)
            root.transform.localRotation = Quaternion.Euler(modelRotationFixEuler);

            // (선택) 카메라를 바라보게(Yaw만)
            if (faceToCameraYawOnly)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 dir = cam.transform.position - root.transform.position;
                    dir.y = 0f;

                    if (dir.sqrMagnitude > 0.0001f)
                    {
                        // LookRotation으로 yaw 맞춘 뒤, 기본 보정 회전도 곱해줌
                        root.transform.rotation =
                            Quaternion.LookRotation(dir.normalized, Vector3.up) *
                            Quaternion.Euler(modelRotationFixEuler);
                    }
                }
            }

            // 디버그: anchor/child 회전 확인
            if (modelAnchor != null)
            {
                Debug.Log($"[3D][RotCheck] anchor={modelAnchor.rotation.eulerAngles} / modelLocal={root.transform.localRotation.eulerAngles}");
            }
        }

        public void SetVisible(bool visible)
        {
            if (previewCanvas != null) previewCanvas.gameObject.SetActive(visible);
        }

        public void SetRefreshButtonState(bool isError)
        {
            if (btnRefresh == null || _refreshBtnImg == null) return;
            btnRefresh.interactable = isError;
            _refreshBtnImg.sprite = isError ? refreshActiveSprite : refreshInactiveSprite;
        }

        public void ShowMockModel()
        {
            ClearModel();
            if (modelAnchor == null) return;

            _spawned = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = _spawned.GetComponent<Collider>();
            if (col != null) Destroy(col);

            _spawned.transform.SetParent(modelAnchor, false);
            _spawned.transform.localPosition = new Vector3(0, modelYOffset, 0);

            // 목업에도 동일 보정 적용(테스트용)
            _spawned.transform.localRotation = Quaternion.Euler(modelRotationFixEuler);

            _spawned.transform.localScale = Vector3.one * 0.18f;

            var renderer = _spawned.GetComponent<Renderer>();
            if (renderer)
            {
                Shader defaultShader = Shader.Find("Universal Render Pipeline/Lit");
                if (defaultShader == null) defaultShader = Shader.Find("Standard");
                renderer.material = new Material(defaultShader);

                Color mockColor = new Color(0.7f, 0.7f, 0.7f, 0.4f);
                if (defaultShader.name.Contains("Universal Render Pipeline"))
                {
                    renderer.material.SetColor("_BaseColor", mockColor);
                    renderer.material.SetFloat("_Surface", 1);
                    renderer.material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                else
                {
                    renderer.material.color = mockColor;
                }
            }
        }

        public void ClearModel()
        {
            if (_spawned)
            {
                Destroy(_spawned);
                _spawned = null;
            }
        }

        public void Close()
        {
            SetVisible(false);
            ClearModel();
            OnClosed?.Invoke();
        }
    }
}
