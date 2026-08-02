Shader "UI/RotatingGradientBorder"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite (미사용)", 2D) = "white" {}
        _GradientTex   ("Border Gradient", 2D) = "white" {}
        _BgGradientTex ("Background Gradient", 2D) = "white" {}
        _BgAngle       ("BG Angle (deg)", Float) = 0
        _Speed         ("Rotation Speed", Float) = 0.15
        _BorderWidth   ("Border Width (px)", Float) = 4
        _CornerRadius  ("Corner Radius (px)", Float) = 40
        _RectWidth     ("Rect Width (px)", Float) = 780
        _RectHeight    ("Rect Height (px)", Float) = 150

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"
            "PreviewType"="Plane" "CanUseSpriteAtlas"="False"
        }

        Stencil
        {
            Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp]
            ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _GradientTex;
            sampler2D _BgGradientTex;
            float _BgAngle, _Speed;
            float _BorderWidth, _CornerRadius, _RectWidth, _RectHeight;
            float4 _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex   = UnityObjectToClipPos(o.worldPosition);
                o.texcoord = v.texcoord;
                o.color    = v.color;
                return o;
            }

            float RoundedBoxSDF(float2 p, float2 hs, float r)
            {
                float2 q = abs(p) - (hs - r);
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 size = float2(_RectWidth, _RectHeight);
                float2 hs   = size * 0.5;
                float2 p    = (i.texcoord - 0.5) * size;

                float r  = min(_CornerRadius, min(hs.x, hs.y));
                float bw = min(_BorderWidth,  min(hs.x, hs.y));

                float d  = RoundedBoxSDF(p, hs, r);
                float aa = max(fwidth(d), 1e-4);

                float outer = 1.0 - smoothstep(-aa, aa, d);
                float inner = 1.0 - smoothstep(-aa, aa, d + bw);
                float ring  = saturate(outer - inner);

                // 배경 : _BgAngle 방향 선형 그라디언트
                float  a   = radians(_BgAngle);
                float2 dir = float2(cos(a), sin(a));
                float  ext = max(abs(dir.x) + abs(dir.y), 1e-4);
                float  tBg = saturate(dot(i.texcoord - 0.5, dir) / ext + 0.5);
                fixed4 bg  = tex2D(_BgGradientTex, float2(tBg, 0.5));

                // 테두리 : 중심 기준 각도 + 시간
                float  ang = atan2(p.y, p.x) * (1.0 / (2.0 * UNITY_PI)) + 0.5;
                float  tB  = frac(ang + _Time.y * _Speed);
                fixed4 bc  = tex2D(_GradientTex, float2(tB, 0.5));

                // 배경 위에 테두리를 source-over 합성
                float  fillA   = bg.a * inner;
                float  strokeA = bc.a * ring;
                float  outA    = strokeA + fillA * (1.0 - strokeA);
                float3 outRGB  = (bc.rgb * strokeA + bg.rgb * fillA * (1.0 - strokeA)) / max(outA, 1e-4);

                fixed4 col = fixed4(outRGB, outA) * i.color;  // CanvasGroup 알파/Image.color 반영

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif

                clip(col.a - 0.001);
                return col;
            }
        ENDCG
        }
    }
}