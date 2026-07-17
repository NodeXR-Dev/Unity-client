using TMPro;
using UnityEngine;
using UnityEngine.UI;

// MVP 씬 전용 작업대 폴리시.
// 런타임에 생성되는 파트 포트까지 같은 크기와 간격으로 정리하며 GraphData는 건드리지 않는다.
[DefaultExecutionOrder(650)]
public class MvpWorkspacePolish : MonoBehaviour
{
    [SerializeField] private RectTransform _panel;
    [SerializeField] private TMP_FontAsset _mvpFont;
    [Header("MVP 포트 스프라이트")]
    [SerializeField] private Sprite _allEmptySprite;
    [SerializeField] private Sprite _allConnectedSprite;
    [SerializeField] private Sprite _partEmptySprite;
    [SerializeField] private Sprite _partConnectedSprite;
    [SerializeField] private Sprite _addPartSprite;
    [SerializeField] private Sprite _roundedUiSprite;


    private int _lastPortSignature = int.MinValue;
    private int _lastNodeSignature = int.MinValue;

    private void Start()
    {
        ApplyNow();
    }

    private void LateUpdate()
    {
        ResolvePanel();
        int portSignature = GetPortSignature();
        int nodeSignature = GetNodeSignature();
        if (portSignature != _lastPortSignature ||
            nodeSignature != _lastNodeSignature)
        {
            ApplyNow();
        }
    }

    public void ApplyNow()
    {
        ResolvePanel();
        if (_panel == null)
        {
            Debug.LogWarning(
                "[MVP Polish] MainSketchPanel을 찾지 못했습니다.");
            return;
        }

        StyleSpatialChrome();
        StyleSketchArea();
        StyleMainPorts();
        HideLegacyControls();
        StyleNodeLabels();

        _lastPortSignature = GetPortSignature();
        _lastNodeSignature = GetNodeSignature();
    }

    private void ResolvePanel()
    {
        if (_panel != null) return;

        MainSketchView view = FindFirstObjectByType<MainSketchView>();
        Canvas canvas = view != null ? view.GetComponentInParent<Canvas>() : null;
        _panel = canvas != null ? canvas.transform as RectTransform : null;
    }

    private void StyleSpatialChrome()
    {
        RectTransform surface = FindRect("WorkspaceSurface");
        SetCenteredRect(surface, new Vector2(0f, -16f), new Vector2(1540f, 910f));
        SetDepth(surface, 34f);
        if (surface != null)
        {
            Image image = surface.GetComponent<Image>();
            if (image != null)
            {
                image.color = new Color(0.055f, 0.085f, 0.135f, 0.970f);
                image.raycastTarget = false;
                if (_roundedUiSprite != null)
                {
                    image.sprite = _roundedUiSprite;
                    image.type = Image.Type.Sliced;
                }
            }

            Outline outline = surface.GetComponent<Outline>();
            if (outline == null)
                outline = surface.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.38f, 0.48f, 0.68f, 0.22f);
            outline.effectDistance = new Vector2(2f, -2f);
        }

        SetVisible("WorkspaceGlowA", false);
        SetVisible("WorkspaceGlowB", false);

        RectTransform accent = FindRect("HeaderAccent");
        SetCenteredRect(accent, new Vector2(-706f, 398f), new Vector2(6f, 46f));
        SetDepth(accent, -12f);
        if (accent != null)
        {
            Image image = accent.GetComponent<Image>();
            if (image != null)
            {
                image.color = new Color(0.38f, 0.64f, 1f, 1f);
                image.raycastTarget = false;
            }
        }

        RectTransform divider = FindRect("HeaderDivider");
        SetCenteredRect(divider, new Vector2(0f, 352f), new Vector2(1370f, 1f));
        if (divider != null)
        {
            Image image = divider.GetComponent<Image>();
            if (image != null)
            {
                image.color = new Color(0.54f, 0.64f, 0.82f, 0.18f);
                image.raycastTarget = false;
            }
        }

        StyleChromeText(
            "WorkspaceTitle", "물로켓 설계",
            new Vector2(-455f, 398f), new Vector2(440f, 50f),
            32f, new Color(0.96f, 0.98f, 1f, 1f),
            TextAlignmentOptions.MidlineLeft, true);
        StyleChromeText(
            "WorkspaceSubtitle", "아이디어를 부품에 연결하고 설계를 함께 완성하세요",
            new Vector2(300f, 398f), new Vector2(760f, 40f),
            18f, new Color(0.67f, 0.73f, 0.84f, 1f),
            TextAlignmentOptions.MidlineRight, false);

        string[] hidden =
        {
            "LabBadgeSurface", "LabBadge", "RequirementGuideChip",
            "StepChip2", "StepChip3", "PartSectionHint",
            "GenerateSectionTitle", "GenerateSectionHint",
            "GenerateFooterHint", "HistoryLabel"
        };
        foreach (string name in hidden)
            SetVisible(name, false);

        SetVisible("PartSectionTitle", true);
        StyleChromeText(
            "PartSectionTitle", "설계 부품",
            new Vector2(-555f, 316f), new Vector2(300f, 38f),
            22f, new Color(0.90f, 0.93f, 0.98f, 1f),
            TextAlignmentOptions.MidlineLeft, true);
        StyleChromeText(
            "SketchSectionTitle", "설계 미리보기",
            new Vector2(-410f, 112f), new Vector2(330f, 38f),
            23f, new Color(0.92f, 0.95f, 1f, 1f),
            TextAlignmentOptions.MidlineLeft, true);
        StyleChromeText(
            "SketchCaption", "그림과 3D 결과를 한곳에서 확인합니다",
            new Vector2(345f, 112f), new Vector2(650f, 32f),
            17f, new Color(0.58f, 0.65f, 0.77f, 1f),
            TextAlignmentOptions.MidlineRight, false);
    }

    private void HideLegacyControls()
    {
        string[] legacyNames =
        {
            "RequirementGuideChip",
            "StepChip2",
            "StepChip3",
            "Generate2DControls",
            "GenerateSectionTitle",
            "GenerateSectionHint",
            "GenerateFooterHint",
            "GenerateStatusSurface",
            "GenerateActionCard",
            "HistoryLabel",
            "HistoryDots"
        };

        foreach (string name in legacyNames)
            SetVisible(name, false);
    }


    private void StyleChromeText(
        string path,
        string value,
        Vector2 position,
        Vector2 size,
        float fontSize,
        Color color,
        TextAlignmentOptions alignment,
        bool bold)
    {
        RectTransform rect = FindRect(path);
        SetCenteredRect(rect, position, size);
        SetDepth(rect, -14f);
        if (rect == null) return;

        TMP_Text text = rect.GetComponent<TMP_Text>();
        if (text == null) return;

        if (_mvpFont != null)
            text.font = _mvpFont;
        text.text = value;
        text.fontSize = fontSize;
        text.enableAutoSizing = true;
        text.fontSizeMin = Mathf.Max(15f, fontSize * 0.68f);
        text.fontSizeMax = fontSize;
        text.fontStyle =
            bold ? FontStyles.Bold : FontStyles.Normal;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.maxVisibleLines = 1;
        text.color = color;
        text.raycastTarget = false;
    }


    private void SetVisible(string path, bool visible)
    {
        RectTransform rect = FindRect(path);
        if (rect != null)
            rect.gameObject.SetActive(visible);
    }


    private void StyleSketchArea()
    {
        RectTransform sketchCard = FindRect("SketchCard");
        SetCenteredRect(sketchCard, new Vector2(0f, -112f), new Vector2(1180f, 472f));
        SetDepth(sketchCard, 10f);
        if (sketchCard != null)
        {
            Image card = sketchCard.GetComponent<Image>();
            if (card != null)
            {
                if (_roundedUiSprite != null)
                {
                    card.sprite = _roundedUiSprite;
                    card.type = Image.Type.Sliced;
                }
                card.color = new Color(0.065f, 0.095f, 0.145f, 0.99f);
                card.raycastTarget = false;
            }

            Outline rim = sketchCard.GetComponent<Outline>();
            if (rim == null)
                rim = sketchCard.gameObject.AddComponent<Outline>();
            rim.effectColor = new Color(0.46f, 0.56f, 0.74f, 0.20f);
            rim.effectDistance = new Vector2(1.5f, -1.5f);
        }

        RectTransform background = FindRect("BG");
        SetCenteredRect(background, new Vector2(0f, -132f), new Vector2(1120f, 382f));
        SetDepth(background, -6f);
        if (background != null)
        {
            Image image = background.GetComponent<Image>();
            if (image != null)
            {
                if (_roundedUiSprite != null)
                {
                    image.sprite = _roundedUiSprite;
                    image.type = Image.Type.Sliced;
                }
                image.color = new Color(0.955f, 0.965f, 0.980f, 1f);
                image.raycastTarget = false;
            }
            EnsureSketchGrid(background);
        }

        RectTransform sketch = FindRect("BG/SketchImage");
        SetCenteredRect(sketch, Vector2.zero, new Vector2(360f, 340f));
        SetDepth(sketch, -8f);
        if (sketch != null)
        {
            RawImage image = sketch.GetComponent<RawImage>();
            if (image != null)
            {
                image.raycastTarget = false;
                if (image.texture == null)
                    image.color = new Color(1f, 1f, 1f, 0f);
            }
        }

        RectTransform placeholder = FindRect("SketchPlaceholder");
        SetCenteredRect(placeholder, new Vector2(0f, -132f), new Vector2(720f, 110f));
        SetDepth(placeholder, -12f);
        if (placeholder != null)
        {
            TMP_Text text = placeholder.GetComponent<TMP_Text>();
            if (text != null)
            {
                if (_mvpFont != null)
                    text.font = _mvpFont;
                text.text = "아직 생성된 그림이 없어요\n부품을 구성한 뒤 그림을 생성해 보세요";
                text.fontSize = 21f;
                text.enableAutoSizing = true;
                text.fontSizeMin = 17f;
                text.fontSizeMax = 21f;
                text.fontStyle = FontStyles.Normal;
                text.alignment = TextAlignmentOptions.Center;
                text.color = new Color(0.32f, 0.39f, 0.50f, 1f);
                text.textWrappingMode = TextWrappingModes.Normal;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.maxVisibleLines = 2;
                text.lineSpacing = 5f;
                text.raycastTarget = false;
            }
        }
    }

    private void EnsureSketchGrid(RectTransform background)
    {
        if (background == null) return;

        RectTransform grid = background.Find("MvpHoloGrid") as RectTransform;
        if (grid == null)
        {
            grid = MvpStudentUiFactory.CreateRect(
                background, "MvpHoloGrid", Vector2.zero, new Vector2(1080f, 350f));
            grid.SetAsFirstSibling();

            for (int i = -5; i <= 5; i++)
                CreateGridLine(
                    grid, "V_" + i,
                    new Vector2(i * 96f, 0f), new Vector2(1f, 330f));

            for (int i = -2; i <= 2; i++)
                CreateGridLine(
                    grid, "H_" + i,
                    new Vector2(0f, i * 66f), new Vector2(1060f, 1f));
        }

        SetCenteredRect(grid, Vector2.zero, new Vector2(1080f, 350f));
        grid.SetAsFirstSibling();
        foreach (Image line in grid.GetComponentsInChildren<Image>(true))
        {
            line.color = new Color(0.18f, 0.32f, 0.52f, 0.055f);
            line.raycastTarget = false;
        }
    }

    private static void CreateGridLine(
        RectTransform parent,
        string name,
        Vector2 position,
        Vector2 size)
    {
        RectTransform line = MvpStudentUiFactory.CreateRect(
            parent,
            name,
            position,
            size);
        Image image = line.gameObject.AddComponent<Image>();
        image.color = new Color(0.25f, 0.68f, 0.86f, 0.10f);
        image.raycastTarget = false;
    }

    private static void SetDepth(RectTransform rect, float z)
    {
        if (rect == null) return;
        Vector3 position = rect.anchoredPosition3D;
        position.z = z;
        rect.anchoredPosition3D = position;
    }

    private void EnsurePartRail(RectTransform container)
    {
        RectTransform rail = FindRect("MvpPartRail");
        if (rail == null)
        {
            Image image = MvpStudentUiFactory.CreatePanel(
                _panel, "MvpPartRail",
                new Vector2(0f, 230f), new Vector2(1220f, 158f),
                new Color(0.070f, 0.100f, 0.150f, 0.94f), false);
            image.raycastTarget = false;
            rail = image.rectTransform;
        }

        SetCenteredRect(rail, new Vector2(0f, 230f), new Vector2(1220f, 158f));
        SetDepth(rail, 2f);
        Image surface = rail.GetComponent<Image>();
        if (surface != null)
        {
            surface.color = new Color(0.070f, 0.100f, 0.150f, 0.94f);
            surface.raycastTarget = false;
        }

        Outline outline = rail.GetComponent<Outline>();
        if (outline == null)
            outline = rail.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.42f, 0.52f, 0.70f, 0.16f);
        outline.effectDistance = new Vector2(1f, -1f);
        rail.SetSiblingIndex(Mathf.Max(0, container.GetSiblingIndex() - 1));
    }

    private void StyleMainPorts()
    {
        RectTransform allSlot = FindRect("AllPortSlot");
        if (allSlot != null)
            allSlot.gameObject.SetActive(false);

        RectTransform container = FindRect("PartPortContainer");
        if (container == null) return;

        SetCenteredRect(container, new Vector2(0f, 230f), new Vector2(1190f, 138f));
        SetDepth(container, -28f);
        EnsurePartRail(container);

        HorizontalLayoutGroup layout = container.GetComponent<HorizontalLayoutGroup>();
        if (layout != null)
        {
            layout.padding = new RectOffset(16, 16, 5, 5);
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
        }

        foreach (PartPort part in container.GetComponentsInChildren<PartPort>(true))
            StylePartPort(part.transform as RectTransform);

        foreach (AddPartPort add in container.GetComponentsInChildren<AddPartPort>(true))
            StyleAddPort(add.transform as RectTransform);

        LayoutRebuilder.ForceRebuildLayoutImmediate(container);
    }

    private void StylePartPort(RectTransform slot)
    {
        if (slot == null) return;

        SetSize(slot, new Vector2(154f, 126f));
        SetDepth(slot, -4f);
        PartPort part = slot.GetComponent<PartPort>();
        bool connected = part != null && part.IsConnected;

        Image root = slot.GetComponent<Image>();
        StyleGraphic(root, true);
        StyleSlotCard(root, new Color(0.075f, 0.095f, 0.145f, 1f));
        if (root != null)
        {
            Outline outline = root.GetComponent<Outline>();
            if (outline != null)
                outline.effectColor = connected
                    ? new Color(0.30f, 0.84f, 0.68f, 0.72f)
                    : new Color(0.48f, 0.58f, 0.76f, 0.24f);
        }

        RectTransform dotRect = slot.Find("DotImage") as RectTransform;
        SetCenteredRect(dotRect, new Vector2(0f, 18f), new Vector2(60f, 60f));
        Image dot = dotRect != null ? dotRect.GetComponent<Image>() : null;
        if (dot != null)
        {
            Sprite target = connected ? _partConnectedSprite : _partEmptySprite;
            if (target != null)
                dot.sprite = target;
            dot.color = connected
                ? new Color(0.72f, 1f, 0.88f, 1f)
                : new Color(0.88f, 0.92f, 1f, 1f);
            dot.raycastTarget = true;
        }

        StylePortLabel(
            slot.Find("LabelText") as RectTransform, null,
            new Vector2(0f, -39f), new Vector2(142f, 38f), 20f);

        SetCenteredRect(
            slot.Find("DeleteButton") as RectTransform,
            new Vector2(51f, 43f), new Vector2(36f, 36f));

        RectTransform rename = slot.Find("RenameField") as RectTransform;
        SetCenteredRect(rename, new Vector2(0f, -39f), new Vector2(142f, 40f));
        if (rename != null)
            StyleCompactInput(rename.GetComponent<TMP_InputField>());

        AddRuntimeFeedback(slot.gameObject);
    }

    private void StyleAddPort(RectTransform slot)
    {
        if (slot == null) return;

        SetSize(slot, new Vector2(154f, 126f));
        SetDepth(slot, -4f);
        Image root = slot.GetComponent<Image>();
        StyleGraphic(root, true);
        StyleSlotCard(root, new Color(0.065f, 0.105f, 0.150f, 1f));

        RectTransform button = slot.Find("EmptyDotButton") as RectTransform;
        SetCenteredRect(button, new Vector2(0f, 18f), new Vector2(60f, 60f));
        if (button != null)
        {
            Image dot = button.GetComponent<Image>();
            if (dot != null)
            {
                if (_addPartSprite != null)
                    dot.sprite = _addPartSprite;
                dot.color = new Color(0.55f, 0.78f, 1f, 1f);
                dot.raycastTarget = true;
            }
            AddRuntimeFeedback(button.gameObject);
        }

        RectTransform inputRect = slot.Find("InputField (TMP)") as RectTransform;
        SetCenteredRect(inputRect, new Vector2(0f, -39f), new Vector2(142f, 40f));
        if (inputRect != null)
            StyleCompactInput(inputRect.GetComponent<TMP_InputField>());

        RectTransform labelRect = slot.Find("MvpAddLabel") as RectTransform;
        if (labelRect == null)
        {
            TMP_Text label = MvpStudentUiFactory.CreateText(
                slot, "MvpAddLabel", "부품 추가",
                new Vector2(0f, -39f), new Vector2(142f, 38f),
                19f, TextAlignmentOptions.Center, true,
                new Color(0.70f, 0.80f, 0.94f, 1f), 1);
            labelRect = label.rectTransform;
        }

        if (labelRect != null)
            labelRect.gameObject.SetActive(
                inputRect == null || !inputRect.gameObject.activeSelf);
    }


    // 디자이너 프리팹을 바꾸지 않고 MVP 인스턴스의 긴 한국어 라벨만 안전하게 정리한다.
    private void StyleNodeLabels()
    {
        foreach (NodeView view in
                 FindObjectsByType<NodeView>(
                     FindObjectsSortMode.None))
        {
            if (view == null) continue;

            Transform nodeCanvas =
                view.transform.Find("Canvas");
            if (nodeCanvas != null)
            {
                StyleNodeAction(
                    nodeCanvas.Find("DeleteButton")
                        as RectTransform,
                    new Vector2(-2.25f, 0.10f));
                StyleNodeAction(
                    nodeCanvas.Find("AddButton")
                        as RectTransform,
                    new Vector2(2.25f, 0.10f));
                StyleNodeAction(
                    nodeCanvas.Find("ReferenceButton")
                        as RectTransform,
                    new Vector2(0f, 1.42f));
            }

            foreach (TMP_InputField input in
                     view.GetComponentsInChildren<TMP_InputField>(
                         true))
            {
                if (input == null ||
                    input.textComponent == null)
                    continue;

                RectTransform inputRect =
                    input.transform as RectTransform;
                if (inputRect != null &&
                    input.gameObject.name ==
                    "LabelInputField")
                {
                    inputRect.sizeDelta =
                        new Vector2(190f, 58f);
                }

                StyleNodeText(input.textComponent, false);
                if (input.placeholder is TMP_Text placeholder)
                    StyleNodeText(placeholder, true);
            }
        }
    }

    private static void StyleNodeAction(
            RectTransform rect,
            Vector2 position)
    {
        if (rect == null) return;

        // 디자이너 프리팹의 축소 스케일은 유지한다.
        // 여기서는 위치와 XR 포인터용 클릭 영역만 다듬는다.
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(68f, 68f);

        Image image = rect.GetComponent<Image>();
        if (image != null)
        {
            Color color = image.color;
            color.a = 0.78f;
            image.color = color;
            image.raycastTarget = true;
        }

        AddRuntimeFeedback(rect.gameObject);
    }


    private void StyleNodeText(TMP_Text text, bool placeholder)
    {
        text.fontSize = 23f;
        if (_mvpFont != null)
            text.font = _mvpFont;
        text.enableAutoSizing = true;
        text.fontSizeMin = 14f;
        text.fontSizeMax = 23f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.maxVisibleLines = 1;
        text.margin = new Vector4(8f, 4f, 8f, 4f);
        text.color = placeholder
            ? new Color(0.86f, 0.91f, 1f, 0.86f)
            : Color.white;
    }

    private int GetNodeSignature()
    {
        unchecked
        {
            int signature = 17;
            foreach (NodeView view in
                     FindObjectsByType<NodeView>(FindObjectsSortMode.None))
            {
                if (view == null) continue;
                signature = signature * 31 + view.GetInstanceID();
                foreach (TMP_InputField input in
                         view.GetComponentsInChildren<TMP_InputField>(true))
                {
                    signature = signature * 31 +
                        (input != null && input.text != null
                            ? input.text.GetHashCode()
                            : 0);
                }
            }
            return signature;
        }
    }

    private int GetPortSignature()
    {
        RectTransform container = FindRect("PartPortContainer");
        if (container == null) return -1;

        unchecked
        {
            int signature = container.childCount;
            foreach (PartPort part in container.GetComponentsInChildren<PartPort>(true))
                signature = signature * 31 + (part.IsConnected ? 1 : 0);

            foreach (AddPartPort add in container.GetComponentsInChildren<AddPartPort>(true))
            {
                TMP_InputField input = add.GetComponentInChildren<TMP_InputField>(true);
                signature = signature * 31 +
                    (input != null && input.gameObject.activeSelf ? 1 : 0);
            }

            RectTransform allRect = FindRect("AllPortSlot/AllPort");
            AllPort all = allRect != null ? allRect.GetComponent<AllPort>() : null;
            signature = signature * 31 +
                (all != null && all.IsConnected ? 1 : 0);
            return signature;
        }
    }

    private RectTransform FindRect(string path)
    {
        return _panel != null ? _panel.Find(path) as RectTransform : null;
    }


    private void StyleCompactInput(TMP_InputField input)
    {
        if (input == null) return;

        input.pointSize = 24f;
        StyleCompactInputText(input.textComponent, false);
        if (input.placeholder is TMP_Text placeholder)
            StyleCompactInputText(placeholder, true);
    }

    private void StyleCompactInputText(TMP_Text text, bool placeholder)
    {
        if (text == null) return;

        if (_mvpFont != null)
            text.font = _mvpFont;
        text.fontSize = 24f;
        text.enableAutoSizing = true;
        text.fontSizeMin = 16f;
        text.fontSizeMax = 24f;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.maxVisibleLines = 1;
        text.margin = new Vector4(5f, 2f, 5f, 2f);
        if (placeholder)
            text.color = new Color(0.32f, 0.43f, 0.53f, 0.75f);
        else
            text.color = new Color(0.08f, 0.16f, 0.27f, 1f);
    }
    private void StylePortLabel(
            RectTransform rect,
            string overrideText,
            Vector2 position,
            Vector2 size,
            float fontSize)
    {
        SetCenteredRect(rect, position, size);
        SetDepth(rect, -6f);
        if (rect == null) return;

        TMP_Text text = rect.GetComponent<TMP_Text>();
        if (text == null) return;

        if (!string.IsNullOrEmpty(overrideText))
            text.text = overrideText;
        if (_mvpFont != null)
            text.font = _mvpFont;
        text.fontSize = fontSize;
        text.enableAutoSizing = true;
        text.fontSizeMin = 15f;
        text.fontSizeMax = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.maxVisibleLines = 2;
        text.lineSpacing = -4f;
        text.margin = new Vector4(6f, 2f, 6f, 2f);
        text.raycastTarget = false;
    }


    private void StyleSlotCard(Image image, Color color)
    {
        if (image == null) return;

        if (_roundedUiSprite != null)
        {
            image.sprite = _roundedUiSprite;
            image.type = Image.Type.Sliced;
        }

        image.color = color;
        image.raycastTarget = true;

        Outline outline = image.GetComponent<Outline>();
        if (outline == null)
            outline = image.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.48f, 0.58f, 0.76f, 0.24f);
        outline.effectDistance = new Vector2(1.25f, -1.25f);

        Shadow shadow = image.GetComponent<Shadow>();
        if (shadow == null)
            shadow = image.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.24f);
        shadow.effectDistance = new Vector2(0f, -5f);
        shadow.useGraphicAlpha = true;
    }

    private static void StyleGraphic(Graphic graphic, bool raycastTarget)
    {
        if (graphic != null)
            graphic.raycastTarget = raycastTarget;
    }

    private static void AddRuntimeFeedback(GameObject target)
    {
        if (!Application.isPlaying || target == null) return;

        MvpPressFeedback feedback = target.GetComponent<MvpPressFeedback>();
        if (feedback == null)
            feedback = target.AddComponent<MvpPressFeedback>();

        feedback.ApplyStudentProfile();
    }

    private static void SetCenteredRect(
        RectTransform rect,
        Vector2 position,
        Vector2 size)
    {
        if (rect == null) return;

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
    }

    private static void SetSize(RectTransform rect, Vector2 size)
    {
        if (rect != null)
            rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect)
    {
        if (rect == null) return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
    }
}
