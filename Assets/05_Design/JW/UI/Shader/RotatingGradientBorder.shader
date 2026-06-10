Shader "UI/RotatingGradientBorder"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color       ("Tint", Color) = (1,1,1,1)
        _GradientTex ("Gradient Texture", 2D) = "white" {}
        _BgColor     ("Background Color", Color) = (0.15, 0.15, 0.16, 0.92)
        _Speed        ("Rotation Speed",     Float) = 0.15
        _BorderWidth  ("Border Width px",    Float) = 4.0
        _CornerRadius ("Corner Radius px",   Float) = 40.0
        _RectWidth    ("Rect Width",         Float) = 600.0
        _RectHeight   ("Rect Height",        Float) = 64.0
        _StencilComp      ("Stencil Comparison",  Float) = 8
        _Stencil          ("Stencil ID",           Float) = 0
        _StencilOp        ("Stencil Operation",    Float) = 0
        _StencilWriteMask ("Stencil Write Mask",   Float) = 255
        _StencilReadMask  ("Stencil Read Mask",    Float) = 255
        _ColorMask        ("Color Mask",           Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float4 color    : COLOR;
                float2 uv       : TEXCOORD0;
                float4 worldPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            sampler2D _GradientTex;
            fixed4    _Color;
            float4    _ClipRect;
            fixed4    _BgColor;
            float     _Speed;
            float     _BorderWidth;
            float     _CornerRadius;
            float     _RectWidth;
            float     _RectHeight;

            v2f vert(appdata v)
            {
                v2f o;
                o.worldPos = v.vertex;
                o.vertex   = UnityObjectToClipPos(v.vertex);
                o.uv       = TRANSFORM_TEX(v.uv, _MainTex);
                o.color    = v.color * _Color;
                return o;
            }

            float roundedRectSDF(float2 p, float2 halfSize, float r)
            {
                float2 d = abs(p) - halfSize + r;
                return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - r;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 halfSize = float2(_RectWidth, _RectHeight) * 0.5;
                float2 p = (i.uv - 0.5) * float2(_RectWidth, _RectHeight);

                float outerD = roundedRectSDF(p, halfSize, _CornerRadius);
                float innerD = roundedRectSDF(p, halfSize - _BorderWidth,
                                              max(_CornerRadius - _BorderWidth, 0.0));

                float outerMask  = smoothstep( 1.0, -1.0, outerD);
                float innerMask  = smoothstep( 1.0, -1.0, innerD);
                float borderMask = outerMask - innerMask;

                float angle = atan2(p.y, p.x) / (2.0 * 3.14159265) + 0.5;
                float t = frac(angle - _Time.y * _Speed);

                fixed4 gradColor = tex2D(_GradientTex, float2(t, 0.5));

                fixed4 col;
                col.rgb = lerp(_BgColor.rgb, gradColor.rgb, saturate(borderMask));
                col.a   = (saturate(innerMask) * _BgColor.a
                         + saturate(borderMask) * gradColor.a)
                         * saturate(outerMask);
                col.a  *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                col    *= i.color;
                return col;
            }
            ENDCG
        }
    }
}