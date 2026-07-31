using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// MVP 씬의 2D 생성 컨트롤러 참조와 사용자용 생성 버튼을 한 번에 배치한다.
// 씬 YAML을 직접 수정하지 않고 이 메뉴를 통해서만 배선한다.
public static class MvpGenerate2DSetup
{
    private const string SeedUserId =
        "11111111-1111-1111-1111-111111111111";
    private const string FontPath =
        "Assets/TextMesh Pro/Fonts/NotoSansKR-Regular SDF.asset";

    [MenuItem("Tools/MVP/Setup 2D Sketch Generation")]
    public static void Setup2DSketchGeneration()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[MVP 2D] Play 모드를 종료한 뒤 메뉴를 실행하세요.");
            return;
        }

        GraphManager graphManager =
            Object.FindFirstObjectByType<GraphManager>();
        GraphSyncClient syncClient =
            Object.FindFirstObjectByType<GraphSyncClient>();
        Generate2DController controller =
            Object.FindFirstObjectByType<Generate2DController>();
        MainSketchView sketchView =
            Object.FindFirstObjectByType<MainSketchView>();

        if (graphManager == null || syncClient == null ||
            controller == null || sketchView == null)
        {
            Debug.LogError(
                "[MVP 2D] GraphManager, GraphSyncClient, " +
                "Generate2DController, MainSketchView가 모두 필요합니다.");
            return;
        }

        RawImage centerImage = FindCenterImage(sketchView);
        if (centerImage == null)
        {
            Debug.LogError("[MVP 2D] MainSketchPanel에서 SketchImage RawImage를 찾지 못했습니다.");
            return;
        }

        RectTransform controls = GetOrCreateControls(
            sketchView.transform);
        Button generateButton = GetOrCreateButton(controls);
        TMP_Text statusText = GetOrCreateStatusText(controls);
        SetLayerRecursively(
            controls.gameObject,
            sketchView.gameObject.layer);

        Undo.RecordObject(controller, nameof(Generate2DController));
        var controllerSo = new SerializedObject(controller);
        SetReference(controllerSo, "_syncClient", syncClient);
        SetReference(controllerSo, "_graphManager", graphManager);
        SetReference(controllerSo, "_centerImage", centerImage);
        SetReference(controllerSo, "_generateButton", generateButton);
        SetReference(controllerSo, "_statusText", statusText);
        controllerSo.ApplyModifiedProperties();

        Undo.RecordObject(syncClient, nameof(GraphSyncClient));
        var syncSo = new SerializedObject(syncClient);
        SerializedProperty userId = syncSo.FindProperty("_userId");
        if (userId != null && string.IsNullOrWhiteSpace(userId.stringValue))
            userId.stringValue = SeedUserId;
        syncSo.ApplyModifiedProperties();

        Undo.RecordObject(generateButton, nameof(Button));
        generateButton.onClick = new Button.ButtonClickedEvent();
        UnityEventTools.AddPersistentListener(
            generateButton.onClick,
            controller.RequestGenerateGraphAll);
        EditorUtility.SetDirty(generateButton);

        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        Selection.activeGameObject = controls.gameObject;
        EditorGUIUtility.PingObject(controls.gameObject);

        Debug.Log(
            "[MVP 2D] 생성 버튼/상태 UI와 Generate2DController 배선 완료. " +
            "Ctrl+S 후 Play에서 백엔드 연결 상태로 버튼을 누르세요.");
    }

    private static RawImage FindCenterImage(MainSketchView sketchView)
    {
        foreach (RawImage image in
                 sketchView.GetComponentsInChildren<RawImage>(true))
        {
            if (image.gameObject.name == "SketchImage")
                return image;
        }
        return null;
    }

    private static RectTransform GetOrCreateControls(Transform panel)
    {
        Transform existing = panel.Find("Generate2DControls");
        RectTransform rect;
        if (existing != null)
        {
            rect = existing.GetComponent<RectTransform>();
        }
        else
        {
            var go = new GameObject(
                "Generate2DControls",
                typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Create 2D Generate Controls");
            rect = go.GetComponent<RectTransform>();
            rect.SetParent(panel, false);
        }

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 330f);
        rect.sizeDelta = new Vector2(520f, 120f);
        return rect;
    }

    private static Button GetOrCreateButton(RectTransform controls)
    {
        Transform existing = controls.Find("Generate2DButton");
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(
                "Generate2DButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            Undo.RegisterCreatedObjectUndo(go, "Create 2D Generate Button");
            go.transform.SetParent(controls, false);
        }

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 20f);
        rect.sizeDelta = new Vector2(320f, 70f);

        Image image = go.GetComponent<Image>();
        image.color = new Color(0.10f, 0.35f, 0.88f, 0.96f);
        image.raycastTarget = true;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.86f, 0.92f, 1f, 1f);
        colors.pressedColor = new Color(0.68f, 0.80f, 1f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.48f, 0.53f, 0.62f, 0.7f);
        colors.colorMultiplier = 1f;
        button.colors = colors;

        TMP_Text label = GetOrCreateText(
            rect,
            "Label",
            "2D 스케치 생성",
            28f);
        Stretch(label.rectTransform);
        label.color = Color.white;
        label.fontStyle = FontStyles.Bold;
        return button;
    }

    private static TMP_Text GetOrCreateStatusText(
        RectTransform controls)
    {
        TMP_Text status = GetOrCreateText(
            controls,
            "Status",
            "부품과 속성을 연결한 뒤 생성하세요.",
            18f);
        RectTransform rect = status.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, -40f);
        rect.sizeDelta = new Vector2(520f, 34f);
        status.color = new Color(0.82f, 0.88f, 0.98f, 1f);
        return status;
    }

    private static TMP_Text GetOrCreateText(
        Transform parent,
        string name,
        string text,
        float fontSize)
    {
        Transform existing = parent.Find(name);
        TextMeshProUGUI label;
        if (existing != null)
        {
            label = existing.GetComponent<TextMeshProUGUI>();
        }
        else
        {
            var go = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            Undo.RegisterCreatedObjectUndo(go, "Create 2D Generate Text");
            go.transform.SetParent(parent, false);
            label = go.GetComponent<TextMeshProUGUI>();
        }

        TMP_FontAsset font =
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        return label;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        foreach (Transform child in root.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    private static void SetReference(
        SerializedObject serialized,
        string propertyName,
        Object value)
    {
        SerializedProperty property =
            serialized.FindProperty(propertyName);
        if (property == null)
        {
            Debug.LogWarning(
                $"[MVP 2D] 직렬화 필드를 찾지 못했습니다: {propertyName}");
            return;
        }
        property.objectReferenceValue = value;
    }
}
