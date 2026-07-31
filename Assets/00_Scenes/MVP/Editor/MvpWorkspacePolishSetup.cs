using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

// MVP 물로켓 작업대를 하나의 제품 화면처럼 정리하는 에디터 도구.
// 디자이너 원본 프리팹은 수정하지 않고 현재 MVP 씬 인스턴스에만 적용한다.
public static class MvpWorkspacePolishSetup
{
    private const string FontSourcePath =
        "Assets/TextMesh Pro/Fonts/NotoSansKR-Regular.ttf";
    private const string FontPath =
        "Assets/00_Scenes/MVP/MvpNotoSansKR SDF.asset";


    private static readonly Color SurfaceColor =
        new Color(0.91f, 0.97f, 1f, 0.98f);
    private static readonly Color CardColor =
        new Color(1f, 1f, 1f, 0.99f);
    private static readonly Color ActionCardColor =
        new Color(0.95f, 0.94f, 1f, 0.99f);
    private static readonly Color PrimaryText =
        new Color(0.08f, 0.16f, 0.27f, 1f);
    private static readonly Color SecondaryText =
        new Color(0.27f, 0.39f, 0.51f, 1f);
    private static readonly Color Accent =
        new Color(0.35f, 0.36f, 0.92f, 1f);

    [MenuItem("Tools/MVP/Polish Water Rocket Workspace")]
    public static void PolishWorkspace()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[MVP Polish] Play 모드를 종료한 뒤 메뉴를 실행하세요.");
            return;
        }

        MainSketchView sketchView =
            Object.FindFirstObjectByType<MainSketchView>();
        Canvas canvas = sketchView != null
            ? sketchView.GetComponentInParent<Canvas>()
            : null;
        RectTransform panel = canvas != null
            ? canvas.transform as RectTransform
            : null;

        if (panel == null)
        {
            Debug.LogError(
                "[MVP Polish] MainSketchPanel Canvas를 찾지 못했습니다. " +
                "먼저 Tools > MVP > Add Sketch Panel을 실행하세요.");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(
            panel.gameObject,
            "Polish MVP Water Rocket Workspace");

        MvpWorkspacePolish polish =
            panel.GetComponent<MvpWorkspacePolish>();
        if (polish == null)
            polish = Undo.AddComponent<MvpWorkspacePolish>(panel.gameObject);

        SerializedObject polishSerialized = new SerializedObject(polish);
        SerializedProperty fontProperty =
            polishSerialized.FindProperty("_mvpFont");
        if (fontProperty != null)
            fontProperty.objectReferenceValue = EnsureMvpFont();
        SetSpriteReference(
            polishSerialized,
            "_allEmptySprite",
            "Assets/03_UI/Sprites/Graph/Joint/JointAll/joint_all_Empty-3.png");
        SetSpriteReference(
            polishSerialized,
            "_allConnectedSprite",
            "Assets/03_UI/Sprites/Graph/Joint/JointAll/joint_all_Connected.png");
        SetSpriteReference(
            polishSerialized,
            "_partEmptySprite",
            "Assets/03_UI/Sprites/Graph/Joint/JointPart/joint_part_Empty.png");
        SetSpriteReference(
            polishSerialized,
            "_partConnectedSprite",
            "Assets/03_UI/Sprites/Graph/Joint/JointPart/joint_part_Connected.png");
        SetSpriteReference(
            polishSerialized,
            "_addPartSprite",
            "Assets/03_UI/Sprites/Graph/Joint/JointPart/joint_part_Add.png");
        SerializedProperty roundedProperty =
            polishSerialized.FindProperty("_roundedUiSprite");
        if (roundedProperty != null)
            roundedProperty.objectReferenceValue =
                AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        polishSerialized.ApplyModifiedProperties();

        BuildSurface(panel);
        BuildHeader(panel);
        BuildSketchCard(panel);
        BuildActionCard(panel);
        BuildHistoryLabel(panel);

        polish.ApplyNow();
        StyleExistingControls(panel);
        ReorderLayers(panel);
        SetLayerRecursively(panel.gameObject, panel.gameObject.layer);

        MvpWorkspaceLayout layout =
            Object.FindFirstObjectByType<MvpWorkspaceLayout>();
        if (layout != null)
        {
            Undo.RecordObject(layout, "Apply Polished MVP Layout Profile");
            layout.ApplyRecommendedLayoutProfile();
            layout.ApplyPanelPreview();
            EditorUtility.SetDirty(layout);
        }

        EditorUtility.SetDirty(polish);
        EditorUtility.SetDirty(panel);
        EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
        Selection.activeGameObject = panel.gameObject;
        EditorGUIUtility.PingObject(panel.gameObject);

        Debug.Log(
            "[MVP Polish] 물로켓 작업대 폴리시 적용 완료. " +
            "상단 요구사항 노드, 부품 연결 바, 스케치, 생성 액션, 히스토리 순으로 정리했습니다.");
    }

    private static void BuildSurface(RectTransform panel)
    {
        Image surface = GetOrCreateImage(
            panel,
            "WorkspaceSurface",
            Vector2.zero,
            new Vector2(1780f, 940f),
            SurfaceColor);
        surface.transform.SetSiblingIndex(0);

        Outline outline = EnsureComponent<Outline>(surface.gameObject);
        outline.effectColor = new Color(0.55f, 0.76f, 0.90f, 0.75f);
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = true;

        Shadow shadow = EnsureComponent<Shadow>(surface.gameObject);
        shadow.effectColor = new Color(0.08f, 0.23f, 0.38f, 0.22f);
        shadow.effectDistance = new Vector2(0f, -10f);
        shadow.useGraphicAlpha = true;

        GetOrCreateImage(
            panel,
            "WorkspaceGlowA",
            new Vector2(-785f, -360f),
            new Vector2(170f, 170f),
            new Color(0.39f, 0.84f, 0.96f, 0.18f));

        GetOrCreateImage(
            panel,
            "WorkspaceGlowB",
            new Vector2(790f, 350f),
            new Vector2(150f, 150f),
            new Color(0.58f, 0.48f, 1f, 0.14f));

        GetOrCreateImage(
            panel,
            "HeaderAccent",
            new Vector2(-836f, 397f),
            new Vector2(20f, 20f),
            new Color(0.24f, 0.74f, 0.82f, 1f));

        GetOrCreateImage(
            panel,
            "HeaderDivider",
            new Vector2(0f, 352f),
            new Vector2(1640f, 3f),
            new Color(0.64f, 0.79f, 0.88f, 0.75f));
    }

    private static void BuildHeader(RectTransform panel)
    {
        Image badgeSurface = GetOrCreateImage(
            panel,
            "LabBadgeSurface",
            new Vector2(-700f, 401f),
            new Vector2(260f, 52f),
            new Color(0.91f, 0.90f, 1f, 1f));
        Shadow badgeShadow = EnsureComponent<Shadow>(badgeSurface.gameObject);
        badgeShadow.effectColor = new Color(0.15f, 0.18f, 0.45f, 0.14f);
        badgeShadow.effectDistance = new Vector2(0f, -3f);

        TMP_Text badgeText = GetOrCreateText(
            panel,
            "LabBadge",
            "물로켓 만들기",
            new Vector2(-700f, 401f),
            new Vector2(226f, 44f),
            21f,
            Accent,
            TextAlignmentOptions.Center,
            FontStyles.Bold);
        badgeText.transform.SetAsLastSibling();

        GetOrCreateText(
            panel,
            "WorkspaceTitle",
            "우리 팀 물로켓 설계실",
            new Vector2(-300f, 401f),
            new Vector2(520f, 58f),
            38f,
            PrimaryText,
            TextAlignmentOptions.Left,
            FontStyles.Bold);

        GetOrCreateText(
            panel,
            "WorkspaceSubtitle",
            "말로 모은 아이디어를 연결해서 한 장의 설계 그림으로 완성해요.",
            new Vector2(370f, 401f),
            new Vector2(700f, 52f),
            21f,
            SecondaryText,
            TextAlignmentOptions.Right,
            FontStyles.Normal);

        BuildStepChip(
            panel,
            "RequirementGuideChip",
            "1  요구사항 끌어 연결하기",
            new Vector2(-520f, 306f),
            new Vector2(480f, 64f),
            Accent,
            Color.white);

        BuildStepChip(
            panel,
            "StepChip2",
            "2  설계 그림 만들기",
            new Vector2(0f, 306f),
            new Vector2(480f, 64f),
            new Color(0.81f, 0.95f, 1f, 1f),
            new Color(0.08f, 0.31f, 0.42f, 1f));

        BuildStepChip(
            panel,
            "StepChip3",
            "3  친구들과 비교하기",
            new Vector2(520f, 306f),
            new Vector2(480f, 64f),
            new Color(0.83f, 0.98f, 0.91f, 1f),
            new Color(0.08f, 0.35f, 0.24f, 1f));

        Transform oldTitle = panel.Find("PartSectionTitle");
        if (oldTitle != null)
            oldTitle.gameObject.SetActive(false);

        Transform oldHint = panel.Find("PartSectionHint");
        if (oldHint != null)
            oldHint.gameObject.SetActive(false);
    }

    private static void BuildStepChip(
        Transform parent,
        string name,
        string label,
        Vector2 position,
        Vector2 size,
        Color background,
        Color foreground)
    {
        Image chip = GetOrCreateImage(
            parent,
            name,
            position,
            size,
            background);

        Shadow shadow = EnsureComponent<Shadow>(chip.gameObject);
        shadow.effectColor = new Color(0.12f, 0.22f, 0.38f, 0.12f);
        shadow.effectDistance = new Vector2(0f, -3f);

        GetOrCreateText(
            chip.rectTransform,
            "Label",
            label,
            Vector2.zero,
            new Vector2(size.x - 28f, size.y - 10f),
            23f,
            foreground,
            TextAlignmentOptions.Center,
            FontStyles.Bold);
    }

    private static void BuildSketchCard(RectTransform panel)
    {
        Image card = GetOrCreateImage(
            panel,
            "SketchCard",
            new Vector2(-300f, -180f),
            new Vector2(800f, 520f),
            CardColor);
        Outline outline = EnsureComponent<Outline>(card.gameObject);
        outline.effectColor = new Color(0.63f, 0.81f, 0.91f, 0.95f);
        outline.effectDistance = new Vector2(2f, -2f);

        Shadow shadow = EnsureComponent<Shadow>(card.gameObject);
        shadow.effectColor = new Color(0.08f, 0.22f, 0.36f, 0.14f);
        shadow.effectDistance = new Vector2(0f, -6f);

        GetOrCreateText(
            panel,
            "SketchSectionTitle",
            "설계 그림",
            new Vector2(-535f, 45f),
            new Vector2(300f, 50f),
            30f,
            PrimaryText,
            TextAlignmentOptions.Left,
            FontStyles.Bold);

        GetOrCreateText(
            panel,
            "SketchCaption",
            "연결한 아이디어가 한 장의 그림이 돼요",
            new Vector2(-145f, 44f),
            new Vector2(390f, 46f),
            18f,
            SecondaryText,
            TextAlignmentOptions.Right,
            FontStyles.Normal);

        GetOrCreateText(
            panel,
            "SketchPlaceholder",
            "아직 설계 그림이 없어요\n\n부품과 요구사항을 연결한 뒤\n오른쪽 버튼을 눌러보세요.",
            new Vector2(-300f, -165f),
            new Vector2(500f, 230f),
            24f,
            new Color(0.35f, 0.46f, 0.57f, 1f),
            TextAlignmentOptions.Center,
            FontStyles.Normal);
    }

    private static void BuildActionCard(RectTransform panel)
    {
        Image card = GetOrCreateImage(
            panel,
            "GenerateActionCard",
            new Vector2(510f, -180f),
            new Vector2(520f, 520f),
            ActionCardColor);
        Outline outline = EnsureComponent<Outline>(card.gameObject);
        outline.effectColor = new Color(0.67f, 0.64f, 0.98f, 0.88f);
        outline.effectDistance = new Vector2(2f, -2f);

        Shadow shadow = EnsureComponent<Shadow>(card.gameObject);
        shadow.effectColor = new Color(0.17f, 0.14f, 0.42f, 0.14f);
        shadow.effectDistance = new Vector2(0f, -6f);

        GetOrCreateText(
            panel,
            "GenerateSectionTitle",
            "그림으로 확인하기",
            new Vector2(510f, 45f),
            new Vector2(430f, 50f),
            30f,
            PrimaryText,
            TextAlignmentOptions.Center,
            FontStyles.Bold);

        GetOrCreateText(
            panel,
            "GenerateSectionHint",
            "준비되면 버튼을 눌러\n우리 팀 설계를 그림으로 확인해요.",
            new Vector2(510f, -33f),
            new Vector2(410f, 76f),
            21f,
            SecondaryText,
            TextAlignmentOptions.Center,
            FontStyles.Normal);

        Image statusSurface = GetOrCreateImage(
            panel,
            "GenerateStatusSurface",
            new Vector2(510f, -255f),
            new Vector2(410f, 90f),
            new Color(1f, 1f, 1f, 0.92f));
        Outline statusOutline = EnsureComponent<Outline>(statusSurface.gameObject);
        statusOutline.effectColor = new Color(0.74f, 0.72f, 0.96f, 0.85f);
        statusOutline.effectDistance = new Vector2(1f, -1f);

        GetOrCreateText(
            panel,
            "GenerateFooterHint",
            "연결을 바꾸면 새 그림도 바로 비교할 수 있어요.",
            new Vector2(510f, -370f),
            new Vector2(410f, 58f),
            18f,
            SecondaryText,
            TextAlignmentOptions.Center,
            FontStyles.Normal);
    }

    private static void BuildHistoryLabel(RectTransform panel)
    {
        GetOrCreateText(
            panel,
            "HistoryLabel",
            "그림 기록",
            new Vector2(-560f, -392f),
            new Vector2(190f, 40f),
            20f,
            SecondaryText,
            TextAlignmentOptions.Right,
            FontStyles.Bold);
    }

    private static void StyleExistingControls(RectTransform panel)
    {
        Transform buttonTransform =
            panel.Find("Generate2DControls/Generate2DButton");
        if (buttonTransform != null)
        {
            GameObject buttonObject = buttonTransform.gameObject;
            Shadow shadow = EnsureComponent<Shadow>(buttonObject);
            shadow.effectColor = new Color(0.18f, 0.16f, 0.48f, 0.30f);
            shadow.effectDistance = new Vector2(0f, -6f);

            Outline outline = EnsureComponent<Outline>(buttonObject);
            outline.effectColor = new Color(0.74f, 0.76f, 1f, 0.95f);
            outline.effectDistance = new Vector2(1f, -1f);

            Image buttonImage = buttonObject.GetComponent<Image>();
            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>(
                "UI/Skin/UISprite.psd");
            if (buttonImage != null && rounded != null)
            {
                buttonImage.sprite = rounded;
                buttonImage.type = Image.Type.Sliced;
            }

            if (buttonObject.GetComponent<MvpPressFeedback>() == null)
                Undo.AddComponent<MvpPressFeedback>(buttonObject);
        }

        foreach (Graphic graphic in
                 panel.GetComponentsInChildren<Graphic>(true))
        {
            string name = graphic.gameObject.name;
            if (name == "WorkspaceSurface" ||
                name == "WorkspaceGlowA" ||
                name == "WorkspaceGlowB" ||
                name == "LabBadgeSurface" ||
                name == "StepChip2" ||
                name == "StepChip3" ||
                name == "HeaderAccent" ||
                name == "HeaderDivider" ||
                name == "SketchCard" ||
                name == "GenerateActionCard" ||
                name == "GenerateStatusSurface" ||
                name == "RequirementGuideChip" ||
                graphic is TMP_Text)
            {
                graphic.raycastTarget = false;
            }
        }
    }

    private static void ReorderLayers(RectTransform panel)
    {
        SetSibling(panel, "WorkspaceSurface", 0);
        SetSibling(panel, "WorkspaceGlowA", 1);
        SetSibling(panel, "WorkspaceGlowB", 2);
        SetSibling(panel, "SketchCard", 3);
        SetSibling(panel, "GenerateActionCard", 4);
        SetSibling(panel, "GenerateStatusSurface", 5);

        SetSibling(panel, "BG", 6);
        SetSibling(panel, "SketchPlaceholder", 7);
        SetSibling(panel, "SketchImage", 8);
    }

    private static Image GetOrCreateImage(
        Transform parent,
        string name,
        Vector2 position,
        Vector2 size,
        Color color)
    {
        Transform existing = parent.Find(name);
        GameObject go;

        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.transform.SetParent(parent, false);
        }

        RectTransform rect = go.GetComponent<RectTransform>();
        SetCenteredRect(rect, position, size);

        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;

        Sprite rounded =
            AssetDatabase.GetBuiltinExtraResource<Sprite>(
                "UI/Skin/UISprite.psd");
        if (rounded != null)
        {
            image.sprite = rounded;
            image.type = Image.Type.Sliced;
        }

        return image;
    }

    private static TMP_Text GetOrCreateText(
            Transform parent,
            string name,
            string content,
            Vector2 position,
            Vector2 size,
            float fontSize,
            Color color,
            TextAlignmentOptions alignment,
            FontStyles style)
    {
        Transform existing = parent.Find(name);
        TextMeshProUGUI text;

        if (existing != null)
        {
            text = existing.GetComponent<TextMeshProUGUI>();
        }
        else
        {
            GameObject go = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.transform.SetParent(parent, false);
            text = go.GetComponent<TextMeshProUGUI>();
        }

        TMP_FontAsset font = EnsureMvpFont();
        if (font != null)
        {
            font.TryAddCharacters(content, true);
            EditorUtility.SetDirty(font);
            text.font = font;
        }

        SetCenteredRect(text.rectTransform, position, size);
        text.text = content;
        text.fontSize = fontSize;
        text.enableAutoSizing = true;
        text.fontSizeMin = Mathf.Max(15f, fontSize * 0.72f);
        text.fontSizeMax = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.fontStyle = style;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Truncate;
        text.lineSpacing = -2f;
        text.margin = new Vector4(4f, 2f, 4f, 2f);
        text.raycastTarget = false;
        return text;
    }

    private static T EnsureComponent<T>(GameObject go)
        where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(go);
    }

    private static void SetSibling(
        RectTransform panel,
        string childName,
        int index)
    {
        Transform child = panel.Find(childName);
        if (child != null)
            child.SetSiblingIndex(Mathf.Min(index, panel.childCount - 1));
    }

    private static void SetCenteredRect(
        RectTransform rect,
        Vector2 position,
        Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localPosition = new Vector3(
            rect.localPosition.x,
            rect.localPosition.y,
            0f);
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        foreach (Transform child in root.transform)
            SetLayerRecursively(child.gameObject, layer);
    }


    private static void SetSpriteReference(
            SerializedObject serialized,
            string propertyName,
            string assetPath)
    {
        SerializedProperty property =
            serialized.FindProperty(propertyName);
        if (property == null) return;

        property.objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
    }


    private static TMP_FontAsset EnsureMvpFont()
    {
        TMP_FontAsset font =
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font != null)
        {
            if (!font.isMultiAtlasTexturesEnabled)
            {
                font.isMultiAtlasTexturesEnabled = true;
                EditorUtility.SetDirty(font);
            }
            return font;
        }

        Font source =
            AssetDatabase.LoadAssetAtPath<Font>(FontSourcePath);
        if (source == null)
        {
            Debug.LogError(
                "[MVP Polish] 한글 원본 폰트를 찾지 못했습니다: " +
                FontSourcePath);
            return null;
        }

        font = TMP_FontAsset.CreateFontAsset(
            source,
            72,
            8,
            GlyphRenderMode.SDFAA,
            1024,
            1024,
            AtlasPopulationMode.Dynamic,
            true);
        font.name = "MvpNotoSansKR SDF";
        font.isMultiAtlasTexturesEnabled = true;

        AssetDatabase.CreateAsset(font, FontPath);
        if (font.atlasTexture != null)
        {
            font.atlasTexture.name = "MvpNotoSansKR Atlas";
            AssetDatabase.AddObjectToAsset(font.atlasTexture, font);
        }
        if (font.material != null)
        {
            font.material.name = "MvpNotoSansKR Material";
            AssetDatabase.AddObjectToAsset(font.material, font);
        }

        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(FontPath);
        return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
    }
}
