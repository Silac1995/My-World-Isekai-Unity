Shader "UI/HealthBar"
{
    Properties
    {
        // Driven by UI_SegmentedBar.cs
        _FillAmount     ("Fill Amount",     Range(0,1)) = 1.0
        _GhostFill      ("Ghost Fill",      Range(0,1)) = 1.0
        _HealFlash      ("Heal Flash",      Range(0,1)) = 0.0

        // Colors
        _HealthColor        ("Health Color",        Color) = (0.18, 0.85, 0.25, 1)
        _LowHealthColor     ("Low Health Color",    Color) = (0.85, 0.18, 0.18, 1)
        _LowHealthThreshold ("Low Health %",        Range(0,1)) = 0.25
        _EmptyColor         ("Empty Color",         Color) = (0.08, 0.08, 0.08, 1)
        _GhostColor         ("Ghost Color",         Color) = (1, 0.85, 0.1, 1)
        _HealColor          ("Heal Color",          Color) = (0.4, 1.0, 0.5, 1)

        // Shine
        _ShineStrength  ("Shine Strength",  Range(0,1))  = 0.25
        _ShineSharpness ("Shine Sharpness", Range(1,20)) = 8

        // Unity UI required
        _MainTex ("Main Texture", 2D) = "white" {}
        _Color   ("Tint",         Color) = (1,1,1,1)

        [HideInInspector] _StencilComp      ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil          ("Stencil ID",         Float) = 0
        [HideInInspector] _StencilOp        ("Stencil Operation",  Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask  ("Stencil Read Mask",  Float) = 255
        [HideInInspector] _ColorMask        ("Color Mask",         Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"             = "Transparent"
            "IgnoreProjector"   = "True"
            "RenderType"        = "Transparent"
            "PreviewType"       = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref       [_Stencil]
            Comp      [_StencilComp]
            Pass      [_StencilOp]
            ReadMask  [_StencilReadMask]
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
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPos : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float     _FillAmount;
            float     _GhostFill;
            float     _HealFlash;
            fixed4    _HealthColor;
            fixed4    _LowHealthColor;
            float     _LowHealthThreshold;
            fixed4    _EmptyColor;
            fixed4    _GhostColor;
            fixed4    _HealColor;
            float     _ShineStrength;
            float     _ShineSharpness;
            sampler2D _MainTex;
            fixed4    _Color;
            fixed4    _TextureSampleAdd;
            float4    _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPos = v.vertex;
                OUT.vertex   = UnityObjectToClipPos(OUT.worldPos);
                OUT.texcoord = v.texcoord;
                OUT.color    = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float u = IN.texcoord.x;
                float v = IN.texcoord.y;

                bool isFilled = u <= _FillAmount;
                bool isGhost  = !isFilled && (u <= _GhostFill);

                // Health color fading to red when low
                float  lowT     = smoothstep(_LowHealthThreshold + 0.05,
                                             _LowHealthThreshold - 0.05,
                                             _FillAmount);
                fixed4 barColor = lerp(_HealthColor, _LowHealthColor, lowT);

                // Heal flash
                barColor = lerp(barColor, _HealColor, _HealFlash);

                // Shine
                barColor.rgb += pow(v, _ShineSharpness) * _ShineStrength;

                fixed4 col = isFilled ? barColor   :
                             isGhost  ? _GhostColor :
                                        _EmptyColor ;

                col.a *= UnityGet2DClipping(IN.worldPos.xy, _ClipRect);
                col   *= IN.color;

                return col;
            }
            ENDCG
        }
    }
}