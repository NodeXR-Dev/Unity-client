using UnityEngine;

public static class MvpFallbackSketchGenerator
{
    private const int Width = 720;
    private const int Height = 460;

    // 설계(부품·요구사항)를 반영해 물로켓 스케치를 그린다.
    public static Texture2D CreateWaterRocketSketch(MvpRocketDesign design)
    {
        if (design == null)
            design = new MvpRocketDesign();

        Texture2D texture = new Texture2D(
            Width, Height, TextureFormat.RGBA32, false);
        texture.name = "MvpWaterRocketSketch";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Color32[] pixels = new Color32[Width * Height];
        DrawBackground(pixels);
        DrawRocket(pixels, design);
        DrawRequirementBadges(pixels, design);

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private static void DrawBackground(Color32[] pixels)
    {
        Color skyTop = new Color32(218, 235, 255, 255);
        Color skyBottom = new Color32(247, 250, 255, 255);
        for (int y = 0; y < Height; y++)
        {
            float t = y / (float)(Height - 1);
            Color row = Color.Lerp(skyBottom, skyTop, t);
            for (int x = 0; x < Width; x++)
                pixels[y * Width + x] = row;
        }

        DrawCloud(pixels, 112, 350, 1.0f);
        DrawCloud(pixels, 585, 320, 0.8f);

        Color32 grid = new Color32(190, 211, 238, 80);
        for (int x = 42; x < Width; x += 54)
            DrawLine(pixels, x, 34, x, Height - 34, grid, 1);
        for (int y = 36; y < Height; y += 54)
            DrawLine(pixels, 34, y, Width - 34, y, grid, 1);

        DrawCircle(pixels, 540, 352, 56, new Color32(255, 224, 132, 255));
        DrawCircle(pixels, 540, 352, 42, new Color32(255, 239, 184, 255));
    }

    private static void DrawRocket(Color32[] pixels, MvpRocketDesign design)
    {
        Color32 outline = new Color32(44, 67, 112, 255);
        Color32 bottle = new Color32(248, 252, 255, 255);
        Color32 accent = design.Accent;
        Color32 water = new Color32(60, 195, 218, 225);
        Color32 orange = new Color32(245, 153, 73, 255);

        int cx = 350;
        int bodyHalfW = Mathf.RoundToInt(52f / design.bodySlim);
        int bodyCenterY = 218;
        int bodyHalfH = Mathf.RoundToInt(112f * design.bodyLength);
        int bodyTop = Mathf.Min(bodyCenterY + bodyHalfH, 348);
        int bodyBottom = Mathf.Max(bodyCenterY - bodyHalfH, 120);

        int noseHeight = design.pointedNose ? 108 : 70;

        // --- 날개(먼저 그려 몸통 뒤로 보내기) ---
        DrawFins(pixels, design, cx, bodyHalfW, bodyBottom, outline, accent);

        // --- 노즐 ---
        FillRoundedRect(pixels, cx - 28, bodyBottom - 44, cx + 28, bodyBottom + 6, 10, outline);
        FillRoundedRect(pixels, cx - 22, bodyBottom - 40, cx + 22, bodyBottom + 2, 7, orange);

        // --- 몸통(병) ---
        FillRoundedRect(pixels,
            cx - bodyHalfW - 9, bodyBottom - 4, cx + bodyHalfW + 9, bodyTop + 4, 26, outline);
        FillRoundedRect(pixels,
            cx - bodyHalfW, bodyBottom + 3, cx + bodyHalfW, bodyTop - 3, 20, bottle);

        // --- 물 채움 ---
        int innerBottom = bodyBottom + 6;
        int innerTop = bodyTop - 6;
        int waterTop = innerBottom +
            Mathf.RoundToInt((innerTop - innerBottom) * design.waterFill);
        FillRoundedRect(pixels,
            cx - bodyHalfW + 4, innerBottom, cx + bodyHalfW - 4, waterTop, 16, water);
        for (int x = cx - bodyHalfW + 12; x <= cx + bodyHalfW - 12; x += 14)
            DrawCircle(pixels, x, waterTop + ((x / 14) % 2) * 4, 4,
                new Color32(235, 252, 255, 220));

        // --- 라벨 밴드 ---
        int bandY = bodyCenterY;
        FillRect(pixels, cx - bodyHalfW - 3, bandY - 12, cx + bodyHalfW + 3, bandY + 12, accent);
        FillRect(pixels, cx - bodyHalfW + 14, bandY - 8, cx + bodyHalfW - 14, bandY + 8,
            new Color32(228, 232, 255, 255));

        // --- 노즈콘 ---
        if (design.pointedNose)
        {
            FillTriangle(pixels,
                new Vector2Int(cx - bodyHalfW, bodyTop),
                new Vector2Int(cx + bodyHalfW, bodyTop),
                new Vector2Int(cx, bodyTop + noseHeight),
                outline);
            FillTriangle(pixels,
                new Vector2Int(cx - bodyHalfW + 8, bodyTop + 4),
                new Vector2Int(cx + bodyHalfW - 8, bodyTop + 4),
                new Vector2Int(cx, bodyTop + noseHeight - 10),
                orange);
        }
        else
        {
            DrawDome(pixels, cx, bodyTop, bodyHalfW + 6, noseHeight + 8, outline);
            DrawDome(pixels, cx, bodyTop + 4, bodyHalfW - 2, noseHeight, orange);
        }

        // --- 발사 연기/받침 힌트 ---
        for (int i = 0; i < 5; i++)
        {
            int y = bodyBottom - 52 - i * 10;
            int radius = 12 + i * 5;
            DrawCircleOutline(pixels, cx, y, radius,
                new Color32(69, 180, 224, (byte)(170 - i * 20)), 3);
        }
    }

    private static void DrawFins(
        Color32[] pixels, MvpRocketDesign design,
        int cx, int bodyHalfW, int bodyBottom, Color32 outline, Color32 accent)
    {
        int finW = Mathf.RoundToInt(64f * design.finSpan);
        int finH = Mathf.RoundToInt(96f * Mathf.Lerp(0.85f, 1.15f, design.finSpan - 0.6f));
        int topY = bodyBottom + 96;
        int botY = bodyBottom + 6;

        DrawOneFin(pixels, cx - bodyHalfW, topY, botY, -finW, finH, outline, accent);
        DrawOneFin(pixels, cx + bodyHalfW, topY, botY, finW, finH, outline, accent);

        if (design.finCount >= 4)
        {
            DrawOneFin(pixels, cx - bodyHalfW, topY + 44, botY + 44,
                -Mathf.RoundToInt(finW * 0.6f), Mathf.RoundToInt(finH * 0.6f), outline, accent);
            DrawOneFin(pixels, cx + bodyHalfW, topY + 44, botY + 44,
                Mathf.RoundToInt(finW * 0.6f), Mathf.RoundToInt(finH * 0.6f), outline, accent);
        }
    }

    private static void DrawOneFin(
        Color32[] pixels, int baseX, int topY, int botY,
        int outX, int finH, Color32 outline, Color32 accent)
    {
        FillTriangle(pixels,
            new Vector2Int(baseX, topY),
            new Vector2Int(baseX, botY - 12),
            new Vector2Int(baseX + outX, botY - finH),
            outline);
        FillTriangle(pixels,
            new Vector2Int(baseX, topY - 6),
            new Vector2Int(baseX, botY - 6),
            new Vector2Int(baseX + Mathf.RoundToInt(outX * 0.9f), botY - finH + 8),
            accent);
    }

    // 요구사항이 연결된 만큼 오른쪽에 색 배지를 쌓아, 어떤 아이디어가 반영됐는지 시각화한다.
    private static void DrawRequirementBadges(Color32[] pixels, MvpRocketDesign design)
    {
        int x0 = 612;
        int x1 = 694;
        int y = 400;
        int step = 30;

        DrawBadgeRow(pixels, design.bodyReqs.Count, ref y, step, x0, x1,
            new Color32(90, 96, 234, 255));
        DrawBadgeRow(pixels, design.finReqs.Count, ref y, step, x0, x1,
            new Color32(245, 153, 73, 255));
        DrawBadgeRow(pixels, design.noseReqs.Count, ref y, step, x0, x1,
            new Color32(243, 123, 114, 255));
    }

    private static void DrawBadgeRow(
        Color32[] pixels, int count, ref int y, int step,
        int x0, int x1, Color32 color)
    {
        for (int i = 0; i < count && y > 40; i++)
        {
            FillRoundedRect(pixels, x0, y - 11, x1, y + 11, 10,
                new Color32(255, 255, 255, 235));
            FillRoundedRect(pixels, x0 + 3, y - 8, x0 + 20, y + 8, 6, color);
            FillRect(pixels, x0 + 26, y - 3, x1 - 8, y + 3,
                new Color32(206, 214, 232, 255));
            y -= step;
        }
    }

    // 반원 돔(둥근 노즈콘). base 위쪽(y>=baseY)만 그린다.
    private static void DrawDome(
        Color32[] pixels, int centerX, int baseY,
        int halfW, int height, Color32 color)
    {
        for (int y = baseY; y <= baseY + height; y++)
        {
            float ny = (y - baseY) / (float)height;   // 0..1
            float span = Mathf.Sqrt(Mathf.Max(0f, 1f - ny * ny)) * halfW;
            int spanI = Mathf.RoundToInt(span);
            for (int x = centerX - spanI; x <= centerX + spanI; x++)
                SetPixel(pixels, x, y, color);
        }
    }

    // ----- 아래는 저수준 드로잉 헬퍼 (width/height 는 상수 사용) -----

    private static void DrawCloud(Color32[] pixels, int x, int y, float scale)
    {
        Color32 cloud = new Color32(255, 255, 255, 220);
        DrawCircle(pixels, x, y, Mathf.RoundToInt(25 * scale), cloud);
        DrawCircle(pixels,
            x + Mathf.RoundToInt(28 * scale),
            y + Mathf.RoundToInt(8 * scale),
            Mathf.RoundToInt(32 * scale), cloud);
        DrawCircle(pixels,
            x + Mathf.RoundToInt(65 * scale), y,
            Mathf.RoundToInt(25 * scale), cloud);
        FillRoundedRect(pixels,
            x - Mathf.RoundToInt(22 * scale),
            y - Mathf.RoundToInt(20 * scale),
            x + Mathf.RoundToInt(88 * scale),
            y + Mathf.RoundToInt(18 * scale),
            Mathf.RoundToInt(18 * scale), cloud);
    }

    private static void FillRect(
        Color32[] pixels, int x0, int y0, int x1, int y1, Color32 color)
    {
        x0 = Mathf.Clamp(x0, 0, Width - 1);
        x1 = Mathf.Clamp(x1, 0, Width - 1);
        y0 = Mathf.Clamp(y0, 0, Height - 1);
        y1 = Mathf.Clamp(y1, 0, Height - 1);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                pixels[y * Width + x] = Blend(pixels[y * Width + x], color);
    }

    private static void FillRoundedRect(
        Color32[] pixels, int x0, int y0, int x1, int y1, int radius, Color32 color)
    {
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                int cx = Mathf.Clamp(x, x0 + radius, x1 - radius);
                int cy = Mathf.Clamp(y, y0 + radius, y1 - radius);
                int dx = x - cx;
                int dy = y - cy;
                if (dx * dx + dy * dy <= radius * radius)
                    SetPixel(pixels, x, y, color);
            }
        }
    }

    private static void FillTriangle(
        Color32[] pixels, Vector2Int a, Vector2Int b, Vector2Int c, Color32 color)
    {
        int minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
        int maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
        int minY = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
        int maxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));

        float area = Edge(a, b, c);
        if (Mathf.Approximately(area, 0f)) return;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2Int p = new Vector2Int(x, y);
                float w0 = Edge(b, c, p);
                float w1 = Edge(c, a, p);
                float w2 = Edge(a, b, p);
                if ((w0 >= 0f && w1 >= 0f && w2 >= 0f) ||
                    (w0 <= 0f && w1 <= 0f && w2 <= 0f))
                    SetPixel(pixels, x, y, color);
            }
        }
    }

    private static float Edge(Vector2Int a, Vector2Int b, Vector2Int p)
    {
        return (p.x - a.x) * (b.y - a.y) -
               (p.y - a.y) * (b.x - a.x);
    }

    private static void DrawCircle(
        Color32[] pixels, int centerX, int centerY, int radius, Color32 color)
    {
        int r2 = radius * radius;
        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                int dx = x - centerX;
                int dy = y - centerY;
                if (dx * dx + dy * dy <= r2)
                    SetPixel(pixels, x, y, color);
            }
        }
    }

    private static void DrawCircleOutline(
        Color32[] pixels, int centerX, int centerY, int radius, Color32 color, int thickness)
    {
        int outer = radius * radius;
        int innerRadius = Mathf.Max(0, radius - thickness);
        int inner = innerRadius * innerRadius;
        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                int dx = x - centerX;
                int dy = y - centerY;
                int distance = dx * dx + dy * dy;
                if (distance <= outer && distance >= inner)
                    SetPixel(pixels, x, y, color);
            }
        }
    }

    private static void DrawLine(
        Color32[] pixels, int x0, int y0, int x1, int y1, Color32 color, int thickness)
    {
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int steps = Mathf.Max(dx, dy);
        if (steps == 0)
        {
            DrawCircle(pixels, x0, y0, thickness, color);
            return;
        }

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
            int y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
            DrawCircle(pixels, x, y, Mathf.Max(1, thickness / 2), color);
        }
    }

    private static void SetPixel(Color32[] pixels, int x, int y, Color32 color)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return;
        int index = y * Width + x;
        pixels[index] = Blend(pixels[index], color);
    }

    private static Color32 Blend(Color32 background, Color32 foreground)
    {
        float alpha = foreground.a / 255f;
        return new Color32(
            (byte)Mathf.RoundToInt(Mathf.Lerp(background.r, foreground.r, alpha)),
            (byte)Mathf.RoundToInt(Mathf.Lerp(background.g, foreground.g, alpha)),
            (byte)Mathf.RoundToInt(Mathf.Lerp(background.b, foreground.b, alpha)),
            255);
    }
}
