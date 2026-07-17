using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public static class MvpStudentUiFactory
{
    public static readonly Color Ink = new Color32(23, 35, 61, 255);
    public static readonly Color MutedInk = new Color32(96, 112, 140, 255);
    public static readonly Color Primary = new Color32(90, 96, 234, 255);
    public static readonly Color PrimaryDark = new Color32(65, 71, 201, 255);
    public static readonly Color Mint = new Color32(53, 201, 154, 255);
    public static readonly Color Cyan = new Color32(49, 191, 208, 255);
    // 어두운 글래스 패널 위 버튼용 저휘도 변형 — 밝은 파스텔(Cyan/Mint) 버튼이
    // 딥블루 패널에서 씻겨 보이는 문제를 피하고, 흰 라벨과 4.5:1 이상을 유지한다.
    public static readonly Color CyanDeep = new Color32(21, 96, 116, 255);
    public static readonly Color MintDeep = new Color32(18, 110, 86, 255);
    // 액션바의 보조 행동 버튼 톤(주 행동만 강조색을 갖는 위계 유지용).
    public static readonly Color GlassAction = new Color(0.13f, 0.17f, 0.27f, 1f);
    public static readonly Color Amber = new Color32(242, 185, 75, 255);
    public static readonly Color Coral = new Color32(243, 123, 114, 255);
    public static readonly Color Surface = new Color32(247, 249, 254, 255);
    public static readonly Color SurfaceBlue = new Color32(234, 240, 255, 255);
    public static readonly Color Border = new Color32(217, 226, 243, 255);

    // 밝은 화면 위 의미 색상. 작은 글자도 4.5:1 이상의 대비를 유지한다.
    public static readonly Color SuccessInk = new Color32(0, 105, 88, 255);
    public static readonly Color InfoInk = new Color32(0, 105, 135, 255);
    public static readonly Color WarningInk = new Color32(132, 82, 0, 255);
    public static readonly Color DangerInk = new Color32(164, 45, 55, 255);

    // XR 공간 패널용 고대비 색상. 밝은 회의실에서도 경계와 깊이를 유지한다.
    public static readonly Color DeepSpace =
        new Color32(12, 22, 49, 242);
    public static readonly Color GlassBlue =
        new Color32(29, 48, 89, 224);
    public static readonly Color ElectricBlue =
        new Color32(70, 90, 218, 255);
    public static readonly Color HoloCyan =
        new Color32(88, 225, 236, 255);

    private static Sprite _roundedSprite;
    private static TMP_FontAsset _font;
    private static bool _fallbacksRegistered;

    public static TMP_FontAsset Font
    {
        get
        {
            if (_font != null) return _font;

            TMP_FontAsset[] fonts =
                Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            foreach (TMP_FontAsset font in fonts)
            {
                if (font != null &&
                    font.name.IndexOf("MvpNotoSansKR",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _font = font;
                    break;
                }
            }

            if (_font == null)
                _font = TMP_Settings.defaultFontAsset;
            RegisterMvpFontFallback(fonts, _font);
            return _font;
        }
    }

    private static void RegisterMvpFontFallback(
        TMP_FontAsset[] loadedFonts,
        TMP_FontAsset mvpFont)
    {
        if (_fallbacksRegistered || mvpFont == null)
            return;

        foreach (TMP_FontAsset candidate in loadedFonts)
        {
            if (candidate == null || candidate == mvpFont)
                continue;
            if (!string.Equals(
                    candidate.name,
                    "NotoSansKR-Regular SDF",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            if (candidate.fallbackFontAssetTable == null)
                candidate.fallbackFontAssetTable =
                    new List<TMP_FontAsset>();
            if (!candidate.fallbackFontAssetTable.Contains(mvpFont))
                candidate.fallbackFontAssetTable.Add(mvpFont);
        }

        _fallbacksRegistered = true;
    }

    public static RectTransform CreateRect(
        Transform parent,
        string name,
        Vector2 position,
        Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        SetRect(rect, position, size);
        return rect;
    }

    public static Image CreatePanel(
        Transform parent,
        string name,
        Vector2 position,
        Vector2 size,
        Color color,
        bool shadow = true)
    {
        RectTransform rect = CreateRect(parent, name, position, size);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = RoundedSprite;
        image.type = Image.Type.Sliced;
        image.color = color;

        if (shadow)
        {
            Shadow effect = rect.gameObject.AddComponent<Shadow>();
            effect.effectColor = new Color(0.08f, 0.14f, 0.30f, 0.16f);
            effect.effectDistance = new Vector2(0f, -9f);
            effect.useGraphicAlpha = true;
        }

        return image;
    }

    public static TMP_Text CreateText(
            Transform parent,
            string name,
            string value,
            Vector2 position,
            Vector2 size,
            float fontSize,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center,
            bool bold = false,
            Color? color = null,
            int maxLines = 3)
    {
        RectTransform rect = CreateRect(parent, name, position, size);
        TextMeshProUGUI text =
            rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = Font;
        text.text = value ?? "";
        text.fontSize = fontSize;
        text.enableAutoSizing = true;
        text.fontSizeMin = Mathf.Max(16f, fontSize * 0.68f);
        text.fontSizeMax = fontSize;
        text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        text.alignment = alignment;
        text.color = color ?? Ink;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.maxVisibleLines = Mathf.Max(1, maxLines);
        text.lineSpacing = 2f;
        text.margin = new Vector4(10f, 5f, 10f, 5f);
        text.extraPadding = true;
        text.raycastTarget = false;
        return text;
    }

    public static Button CreateButton(
            Transform parent,
            string name,
            string label,
            Vector2 position,
            Vector2 size,
            Color color,
            UnityAction onClick,
            float fontSize = 25f)
    {
        Image image = CreatePanel(
            parent, name, position, size, color, true);
        image.raycastTarget = true;

        Outline rim = image.gameObject.AddComponent<Outline>();
        rim.effectColor = new Color(0.72f, 0.92f, 1f, 0.22f);
        rim.effectDistance = new Vector2(1.5f, -1.5f);
        rim.useGraphicAlpha = true;

        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation
        {
            mode = Navigation.Mode.None
        };

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor =
            new Color(0.92f, 1f, 1f, 1f);
        colors.pressedColor =
            new Color(0.72f, 0.82f, 0.95f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor =
            new Color(0.58f, 0.64f, 0.74f, 0.52f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.06f;
        button.colors = colors;

        if (onClick != null)
            button.onClick.AddListener(onClick);

        TMP_Text text = CreateText(
            image.transform,
            "Label",
            label,
            Vector2.zero,
            size - new Vector2(24f, 12f),
            fontSize,
            TextAlignmentOptions.Center,
            true,
            ReadableTextColor(color),
            2);

        text.margin = new Vector4(2f, 0f, 2f, 0f);
        text.extraPadding = false;
        Stretch(text.rectTransform, new Vector2(12f, 8f));

        MvpPressFeedback feedback =
            image.gameObject.AddComponent<MvpPressFeedback>();
        feedback.ApplyStudentProfile();
        return button;
    }

    public static TMP_InputField CreateInput(
            Transform parent,
            string name,
            string placeholder,
            Vector2 position,
            Vector2 size,
            float fontSize = 23f)
    {
        Image image = CreatePanel(
            parent,
            name,
            position,
            size,
            Color.white,
            false);
        image.raycastTarget = true;

        Outline outline = image.gameObject.AddComponent<Outline>();
        outline.effectColor = Border;
        outline.effectDistance = new Vector2(2f, -2f);

        TMP_InputField input =
            image.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = image;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.contentType = TMP_InputField.ContentType.Standard;
        input.characterLimit = 80;
        input.selectionColor = new Color(
            Primary.r, Primary.g, Primary.b, 0.28f);

        RectTransform viewport =
            CreateRect(image.transform, "TextArea", Vector2.zero,
                size - new Vector2(28f, 12f));
        viewport.gameObject.AddComponent<RectMask2D>();

        TMP_Text valueText = CreateText(
            viewport,
            "Text",
            "",
            Vector2.zero,
            viewport.sizeDelta,
            fontSize,
            TextAlignmentOptions.MidlineLeft,
            false,
            Ink,
            1);
        Stretch(valueText.rectTransform, new Vector2(4f, 0f));

        TMP_Text placeholderText = CreateText(
            viewport,
            "Placeholder",
            placeholder,
            Vector2.zero,
            viewport.sizeDelta,
            fontSize,
            TextAlignmentOptions.MidlineLeft,
            false,
            new Color(MutedInk.r, MutedInk.g, MutedInk.b, 0.75f),
            1);
        placeholderText.fontStyle = FontStyles.Italic;
        Stretch(placeholderText.rectTransform, new Vector2(4f, 0f));

        input.textViewport = viewport;
        input.textComponent = valueText;
        input.placeholder = placeholderText;
        input.pointSize = fontSize;
        return input;
    }

    public static RawImage CreateRawImage(
        Transform parent,
        string name,
        Vector2 position,
        Vector2 size)
    {
        RectTransform rect = CreateRect(parent, name, position, size);
        RawImage image = rect.gameObject.AddComponent<RawImage>();
        image.color = Color.white;
        image.raycastTarget = false;
        return image;
    }

    public static void SetButtonColor(Button button, Color color)
    {
        if (button == null) return;
        Image image = button.targetGraphic as Image;
        if (image != null) image.color = color;

        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
            label.color = ReadableTextColor(color);
    }

    public static void SetRect(
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

    public static void Stretch(RectTransform rect, Vector2 padding)
    {
        if (rect == null) return;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(-padding.x * 2f, -padding.y * 2f);
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
    }

    private static Sprite RoundedSprite
    {
        get
        {
            if (_roundedSprite == null)
                _roundedSprite = BuildRoundedSprite();
            return _roundedSprite;
        }
    }

    private static Sprite BuildRoundedSprite()
    {
        const int size = 64;
        const int radius = 15;
        Texture2D texture = new Texture2D(
            size, size, TextureFormat.RGBA32, false);
        texture.name = "MvpRuntimeRoundedRect";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x, radius, size - radius - 1);
                float cy = Mathf.Clamp(y, radius, size - radius - 1);
                float dx = x - cx;
                float dy = y - cy;
                bool inside = dx * dx + dy * dy <= radius * radius;
                pixels[y * size + x] =
                    inside
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        return Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius));
    }


    public static Color ReadableTextColor(Color background)
    {
        float whiteContrast = ContrastRatio(Color.white, background);
        float inkContrast = ContrastRatio(Ink, background);
        return inkContrast >= whiteContrast ? Ink : Color.white;
    }

    public static Color ReadableAccentOnLight(Color accent)
    {
        if (ContrastRatio(accent, Surface) >= 4.5f)
            return accent;
        if (ColorDistanceSquared(accent, Mint) < 0.05f)
            return SuccessInk;
        if (ColorDistanceSquared(accent, Cyan) < 0.05f)
            return InfoInk;
        if (ColorDistanceSquared(accent, Amber) < 0.05f)
            return WarningInk;
        if (ColorDistanceSquared(accent, Coral) < 0.05f)
            return DangerInk;
        return Ink;
    }

    private static float ColorDistanceSquared(Color a, Color b)
    {
        float red = a.r - b.r;
        float green = a.g - b.g;
        float blue = a.b - b.b;
        return red * red + green * green + blue * blue;
    }

    private static float ContrastRatio(Color a, Color b)
    {
        float lighter = Mathf.Max(RelativeLuminance(a), RelativeLuminance(b));
        float darker = Mathf.Min(RelativeLuminance(a), RelativeLuminance(b));
        return (lighter + 0.05f) / (darker + 0.05f);
    }

    private static float RelativeLuminance(Color color)
    {
        return 0.2126f * LinearChannel(color.r) +
               0.7152f * LinearChannel(color.g) +
               0.0722f * LinearChannel(color.b);
    }

    private static float LinearChannel(float value)
    {
        return value <= 0.03928f
            ? value / 12.92f
            : Mathf.Pow((value + 0.055f) / 1.055f, 2.4f);
    }
}
