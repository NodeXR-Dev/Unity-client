using UnityEngine;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using NodeXR.UI;
using System.Linq;

namespace NodeXR
{
    public class AppStateMachine : MonoBehaviour
    {
        [Header("Refs")]
        public SceneRefs refs;
        public PillStatusManager pillManager;
        public NextButtonView nextButtonView;
        public string wsPath = "/ws/graph_event/";

        [Header("Decision Graph")]
        public DecisionGraphManager graphManager;

        [Header("3D Preview")]
        public Preview3DController preview3D;

        [Header("UI Positioning")]
        public GameObject worldUiRoot;
        private bool _isInitialPositioned = false;

        private int _lastSelectedIndex = -1;
        private string _selectedCategoryName = "";

        // ✅ 카테고리 선택을 서버에 보내기 위한 id 저장
        private string _selectedCategoryId = "";

        private bool _isGenerating3D = false;

        public UnityPhase CurrentPhase => (refs != null && refs.session != null) ? refs.session.phase : UnityPhase.BASIC_DISCUSS;

        private void Awake()
        {
            if (!refs) refs = FindFirstObjectByType<SceneRefs>();
            if (!pillManager) pillManager = FindFirstObjectByType<PillStatusManager>();
            if (nextButtonView) nextButtonView.SetDisabled();
        }

        private void Start()
        {
            if (refs == null || refs.server == null) return;

            refs.server.OnGraphEvent += HandleGraphEvent;

            if (refs.categoryCanvas)
                refs.categoryCanvas.OnSelectCategory += HandleCategoryOptionSelected;

            if (preview3D)
                preview3D.OnRefreshRequested += OnClickStart3D;

            if (!string.IsNullOrEmpty(refs.session.roomId))
            {
                refs.server.ConnectRoomWS(refs.session.roomId, wsPath);
                EnterPhase(UnityPhase.BASIC_DISCUSS);
            }
        }

        private float _wsCheckTimer = 0f;

        private void Update()
        {
            if (refs?.server == null || refs.session == null) return;
            if (string.IsNullOrEmpty(refs.session.roomId)) return;

            _wsCheckTimer += Time.deltaTime;
            if (_wsCheckTimer > 3f)
            {
                _wsCheckTimer = 0f;
                refs.server.EnsureWSConnected(refs.session.roomId, wsPath);
            }
        }


        public void SubmitUtterance(string text)
        {
            Debug.Log($"<color=yellow>[UTTERANCE]</color> phase={CurrentPhase} text={text}");

            if (string.IsNullOrWhiteSpace(text)) return;
            if (pillManager) pillManager.SetStatus(PillStatusManager.Status.Processing);

            var serverPhase = (CurrentPhase == UnityPhase.CATEGORY_DISCUSS) ? "CATEGORY_DISCUSS" : "BASIC_DISCUSS";

            StartCoroutine(refs.server.PostUtterance(refs.session.roomId, refs.session.userId, serverPhase, text, ok =>
            {
                if (!ok && pillManager) pillManager.SetStatus(PillStatusManager.Status.Error);
            }));
        }

        public void RepositionUI()
        {
            if (worldUiRoot == null) return;
            StartCoroutine(Co_RepositionRoutine());
        }

        private IEnumerator Co_RepositionRoutine()
        {
            int retry = 0;
            while (retry < 5)
            {
                Transform cam = Camera.main.transform;

                if (cam.position.magnitude > 0.1f)
                {
                    Vector3 targetPos = cam.position + (cam.forward * 1.5f);
                    targetPos.y = cam.position.y;

                    Vector3 lookDir = targetPos - cam.position;
                    lookDir.y = 0;

                    worldUiRoot.transform.position = targetPos;
                    worldUiRoot.transform.rotation = Quaternion.LookRotation(lookDir);
                    worldUiRoot.SetActive(true);

                    Debug.Log($"<color=green>[UI] 배치 성공 (시도 횟수: {retry})</color>");
                    yield break;
                }

                retry++;
                yield return new WaitForSeconds(0.2f);
            }
        }

        public void OnNextButtonClicked()
        {
            if (CurrentPhase != UnityPhase.CATEGORY_SELECT && _lastSelectedIndex == -1) return;

            switch (CurrentPhase)
            {
                case UnityPhase.BASIC_DISCUSS:
                case UnityPhase.CATEGORY_DISCUSS:
                    ProcessSelectionAndFetchCategories();
                    break;

                case UnityPhase.CATEGORY_SELECT:
                    if (!string.IsNullOrEmpty(_selectedCategoryName) && !string.IsNullOrEmpty(_selectedCategoryId))
                    {
                        if (pillManager) pillManager.UpdateStatusText(_selectedCategoryName);
                        graphManager.CreateNextStep(_selectedCategoryName, null);
                        EnterPhase(UnityPhase.CATEGORY_DISCUSS);
                    }
                    else
                    {
                        Debug.LogWarning("[Category] Not selected yet (name/id empty).");
                        if (pillManager) pillManager.SetStatus(PillStatusManager.Status.Error);
                    }
                    break;

            }

            if (nextButtonView) nextButtonView.SetDisabled();
        }

        private void ProcessSelectionAndFetchCategories()
        {
            UpdateCorePanelFromSelection();
            FetchCategoriesFromServer();
        }

        private void HandleGraphEvent(GraphEventDto dto)
        {
            Debug.Log($"<color=lime>[WS][RECV]</color> event={dto?.@event}");

            if (dto == null) return;

            RepositionUI();

            // core 이미지 갱신
            if (!string.IsNullOrWhiteSpace(dto.core_img_url))
            {
                refs.session.coreImgUrl = dto.core_img_url;
                StartCoroutine(refs.textureDownloader.DownloadTexture(dto.core_img_url, tex =>
                {
                    if (tex != null && refs.corePanel != null)
                        refs.corePanel.SetPreviewTexture(tex);
                }));
            }

            var ev = ParseEvent(dto.@event);

            // ✅ 키워드 이벤트도 처리
            if (ev == GraphEventType.NODE_KEYWORD_UPDATE)
            {
                if (pillManager) pillManager.SetStatus(PillStatusManager.Status.Processing);

                // 후보 목록은 일단 비우고 로딩만 보여주기 (선택)
                refs.session.ClearCandidates();
                UpdateCandidateTextures(); // ClearCardTexture + 로딩 표시가 이미 들어가 있어서 그대로 활용 가능
                return;
            }

            if (ev == GraphEventType.NODE_IMAGE_UPDATE)
            {
                if (pillManager) pillManager.SetStatus(PillStatusManager.Status.Success);
                StartCoroutine(DelayedResetStatus(2f));

                ApplyCandidatesFromGraph(dto);
                UpdateCandidateTextures();
            }
        }


        /// <summary>
        /// ✅ 카테고리 버튼 클릭 시:
        /// 1) 이름 저장
        /// 2) (중요) categoryCanvas에서 category_id를 찾아서 저장
        /// 3) 서버 /api/categories/select 호출해서 ACTIVE 반영
        /// </summary>
        private void HandleCategoryOptionSelected(string categoryName)
        {
            Debug.Log($"[WSDBG] room={refs.session.roomId} state={refs.server.WsState} url={refs.server.WsConnectedUrl}");
            
            _selectedCategoryName = categoryName;
            _selectedCategoryId = "";

            // 일단 next는 비활성 (서버 반영 성공 후 켜기)
            if (nextButtonView) nextButtonView.SetDisabled();

            if (pillManager) pillManager.UpdateStatusText(categoryName);

            if (refs.categoryCanvas != null && refs.categoryCanvas.TryGetCategoryId(categoryName, out var categoryId))
            {
                _selectedCategoryId = categoryId;

                    StartCoroutine(refs.server.SelectCategory(refs.session.roomId, categoryId, ok =>
                    {
                        if (ok)
                        {
                            Debug.Log($"[CategorySelect] OK name={categoryName} id={categoryId}");

                            // ✅ (핵심) 선택 성공 직후 그래프를 다시 가져와 후보를 확실히 채움
                            StartCoroutine(refs.server.GetGraphStateForDebug(refs.session.roomId, (state) =>
                            {
                                if (state?.nodes != null)
                                {
                                    // ASSET 3개 뽑기 (너가 기존에 쓰던 ApplyCandidatesFromGraph 로직과 동일하게)
                                    var assets = state.nodes
                                        .Where(n => n.node_type == "ASSET" && !string.IsNullOrEmpty(n.img_url))
                                        .ToList();

                                    refs.session.ClearCandidates();
                                    for (int i = 0; i < 3 && i < assets.Count; i++)
                                    {
                                        refs.session.candidateNodeIds[i] = assets[i].node_id;
                                        refs.session.candidateImgUrls[i] = assets[i].img_url;
                                    }

                                    UpdateCandidateTextures(); // ✅ 기존 함수 재사용
                                }
                                else
                                {
                                    Debug.LogWarning("[CategorySelect] GraphState empty after select");
                                }

                                if (nextButtonView) nextButtonView.SetActive();
                            }));
                        }
                        else
                        {
                            Debug.LogError($"[CategorySelect] FAIL name={categoryName} id={categoryId}");
                            _selectedCategoryId = "";
                            if (pillManager) pillManager.SetStatus(PillStatusManager.Status.Error);
                        }
                    }));

            }
            else
            {
                Debug.LogError($"[CategorySelect] category_id not found for name={categoryName}");
                if (pillManager) pillManager.SetStatus(PillStatusManager.Status.Error);
            }
        }

        public void EnterPhase(UnityPhase p)
        {
            Debug.Log($"<color=cyan>[PHASE]</color> Enter {p}");

            if (refs == null) return;

            refs.session.phase = p;

            if (refs.categoryCanvas) refs.categoryCanvas.Show(p == UnityPhase.CATEGORY_SELECT);
            if (refs.hud) refs.hud.ApplyPhase(p);

            _lastSelectedIndex = -1;

            // ✅ 카테고리 선택 화면 들어갈 때 선택값 초기화
            if (p == UnityPhase.CATEGORY_SELECT)
            {
                _selectedCategoryName = "";
                _selectedCategoryId = "";
            }

            UpdatePillStatusByPhase(p);
        }

        private void UpdateCandidateTextures()
        {
            var latestController = graphManager.GetLatestController();
            if (latestController == null) return;

            latestController.OnSelectIndex -= HandleCandidateSelected;
            latestController.OnSelectIndex += HandleCandidateSelected;

            string[] urls = refs.session.candidateImgUrls;
            for (int i = 0; i < 3; i++)
            {
                int idx = i;

                latestController.ClearCardTexture(idx);

                if (idx >= urls.Length || string.IsNullOrEmpty(urls[idx]))
                {
                    latestController.SetCardLoading(idx, false);
                    continue;
                }

                latestController.SetCardLoading(idx, true);

                StartCoroutine(refs.textureDownloader.DownloadTexture(urls[idx], tex =>
                {
                    if (latestController == null) return;

                    if (tex != null)
                        latestController.SetCardTexture(idx, tex);

                    latestController.SetCardLoading(idx, false);
                }));
            }
        }

        private void HandleCandidateSelected(int idx)
        {
            _lastSelectedIndex = idx;
            if (nextButtonView) nextButtonView.SetActive();

            if (refs.session.candidateNodeIds != null && idx < refs.session.candidateNodeIds.Length)
            {
                var nodeId = refs.session.candidateNodeIds[idx];
                if (!string.IsNullOrWhiteSpace(nodeId))
                    StartCoroutine(refs.server.Select2D(refs.session.roomId, nodeId, ok => { }));
            }
        }

        private void UpdateCorePanelFromSelection()
        {
            if (_lastSelectedIndex < 0 || _lastSelectedIndex >= refs.session.candidateImgUrls.Length) return;

            string url = refs.session.candidateImgUrls[_lastSelectedIndex];
            refs.session.coreImgUrl = url;

            StartCoroutine(refs.textureDownloader.DownloadTexture(url, tex =>
            {
                if (tex != null && refs.corePanel != null)
                    refs.corePanel.SetPreviewTexture(tex);
            }));
        }

        private void UpdatePillStatusByPhase(UnityPhase p)
        {
            if (!pillManager) return;

            switch (p)
            {
                case UnityPhase.BASIC_DISCUSS:
                    pillManager.UpdateStatusText("스케치");
                    pillManager.SetStatus(PillStatusManager.Status.Default);
                    break;

                case UnityPhase.CATEGORY_SELECT:
                    pillManager.SetStatus(PillStatusManager.Status.SelectCategory);
                    break;

                case UnityPhase.CATEGORY_DISCUSS:
                    pillManager.SetStatus(PillStatusManager.Status.Default);
                    break;
            }
        }

        private IEnumerator DelayedResetStatus(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (pillManager) pillManager.SetStatus(PillStatusManager.Status.Default);
        }

        private GraphEventType ParseEvent(string e)
        {
            if (string.IsNullOrWhiteSpace(e)) return GraphEventType.UNKNOWN;
            if (e.Contains("NODE_IMAGE_UPDATE")) return GraphEventType.NODE_IMAGE_UPDATE;
            if (e.Contains("NODE_KEYWORD_UPDATE")) return GraphEventType.NODE_KEYWORD_UPDATE; // ✅ 추가
            return GraphEventType.UNKNOWN;
        }


        private void ApplyCandidatesFromGraph(GraphEventDto dto)
        {
            refs.session.ClearCandidates();
            var nodes = dto?.graph_state?.nodes;
            if (nodes == null) return;

            var assets = nodes
                .Where(n => n.node_type == "ASSET" && !string.IsNullOrEmpty(n.img_url))
                .ToList();

            // ✅ 최신 3개만 쓰기 (보수적으로 뒤에서 가져오기)
            int count = Mathf.Min(3, assets.Count);
            for (int i = 0; i < count; i++)
            {
                var a = assets[assets.Count - 1 - i]; // 뒤에서부터
                refs.session.candidateNodeIds[i] = a.node_id;
                refs.session.candidateImgUrls[i] = a.img_url;
            }
        }


        private void FetchCategoriesFromServer()
        {
            if (pillManager) pillManager.SetStatus(PillStatusManager.Status.Processing);

            // ✅ 이전 선택값 초기화 (중요)
            _selectedCategoryName = "";
            _selectedCategoryId = "";
            if (nextButtonView) nextButtonView.SetDisabled();

            StartCoroutine(refs.server.GetCategories(refs.session.roomId, (items) =>
            {
                if (items != null && items.Count > 0)
                {
                    refs.categoryCanvas.BuildFromItems(items);
                    EnterPhase(UnityPhase.CATEGORY_SELECT);
                }
                else
                {
                    if (pillManager) pillManager.SetStatus(PillStatusManager.Status.Error);
                }
            }));
        }


        public void OnClickStart3D()
        {
            Debug.Log("<color=red>[3D] Button Click Detected!</color>");

            if (_isGenerating3D)
            {
                Debug.LogWarning("[3D] Blocked: Already processing.");
                return;
            }

            string currentCoreUrl = refs.session.coreImgUrl;
            if (string.IsNullOrEmpty(currentCoreUrl))
            {
                Debug.LogError("[3D] Error: No Core Image URL found.");
                return;
            }

            string targetNodeId = "";
            var nodes = refs.server.LastDto?.graph_state?.nodes;
            if (nodes != null)
            {
                var foundNode = nodes.FirstOrDefault(n => !string.IsNullOrEmpty(n.img_url) && currentCoreUrl.Contains(n.img_url));
                if (foundNode != null)
                {
                    targetNodeId = foundNode.node_id;
                    Debug.Log($"<color=white>[3D Target]</color> Selected Node ID: <b>{targetNodeId}</b>");
                }
            }

            if (string.IsNullOrEmpty(targetNodeId))
            {
                Debug.LogError("[3D] Error: Could not find a matching Node ID for this image.");
                return;
            }

            _isGenerating3D = true;
            if (pillManager) pillManager.SetStatus(PillStatusManager.Status.Processing);

            Debug.Log($"<color=cyan>[3D Step 1]</color> Sending Select2D with Node ID: {targetNodeId}");

            StartCoroutine(refs.server.Select2D(refs.session.roomId, targetNodeId, (selectOk) =>
            {
                if (selectOk)
                {
                    Debug.Log("<color=green>[3D Step 1 Success]</color> Server accepted Node ID.");
                    StartCoroutine(Co_SafeGenerate3D(targetNodeId));
                }
                else
                {
                    _isGenerating3D = false;
                    if (pillManager) pillManager.SetStatus(PillStatusManager.Status.Error);
                    Debug.LogError($"[3D Step 1 Fail] Server rejected Node ID: {targetNodeId}");
                }
            }));
        }

        private IEnumerator Co_SafeGenerate3D(string assetId)
        {
            Debug.Log("<color=orange>[3D Wait]</color> Waiting 1.5s for DB synchronization...");
            yield return new WaitForSeconds(1.5f);

            if (preview3D != null) preview3D.OpenAndGenerate();

            Debug.Log("<color=yellow>[3D Step 2]</color> Requesting 3D Generation...");

            yield return refs.server.Generate3D(refs.session.roomId, assetId, (success, glbUrl) =>
            {
                if (success && !string.IsNullOrEmpty(glbUrl))
                {
                    Debug.Log($"<color=green>[3D Success]</color> GLB URL Received: {glbUrl}");
                    StartCoroutine(DownloadAndLoadGLB(glbUrl));
                }
                else
                {
                    _isGenerating3D = false;
                    Debug.LogError("[3D Step 2 Fail] Server returned error or empty URL.");
                    if (pillManager) pillManager.SetStatus(PillStatusManager.Status.Error);
                    if (preview3D) preview3D.SetRefreshButtonState(true);
                }
            });
        }

        private IEnumerator DownloadAndLoadGLB(string url)
        {
            string finalUrl = url.Replace("localhost", new Uri(refs.server.httpBaseUrl).Host); 
            finalUrl += $"?t={DateTime.Now.Ticks}";

            Debug.Log($"<color=cyan>[Download]</color> GLB: {finalUrl}");

            using (UnityWebRequest www = UnityWebRequest.Get(finalUrl))
            {
                www.timeout = 30;
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"<color=red>[Download Fail]</color> {www.error}");
                    _isGenerating3D = false;
                    yield break;
                }

                byte[] results = www.downloadHandler.data;
                string fileName = "temp_model_" + DateTime.Now.Ticks + ".glb";
                string localPath = Path.Combine(Application.persistentDataPath, fileName);

                File.WriteAllBytes(localPath, results);

                if (preview3D != null)
                    preview3D.OpenAndGenerate("file://" + localPath);

                _isGenerating3D = false;
                if (pillManager) pillManager.SetStatus(PillStatusManager.Status.Success);
            }
        }

        public void OnHitButton()
        {
            var voiceHandler = GetComponent<VoiceInputHandler>();
            if (voiceHandler == null)
                voiceHandler = UnityEngine.Object.FindFirstObjectByType<VoiceInputHandler>();

            if (voiceHandler != null)
            {
                voiceHandler.ToggleMic();

                if (pillManager != null)
                {
                    if (voiceHandler.isRecording)
                    {
                        pillManager.SetStatus(PillStatusManager.Status.Listening);
                        if (CurrentPhase == UnityPhase.CATEGORY_DISCUSS)
                            pillManager.UpdateStatusText(_selectedCategoryName + " 논의 중...");
                    }
                    else
                    {
                        pillManager.SetStatus(PillStatusManager.Status.Processing);
                    }
                }

                Debug.Log($"<color=cyan>[Button]</color> OnHitButton 실행됨. Phase: {CurrentPhase}, Recording: {voiceHandler.isRecording}");
            }
        }
    }
}
