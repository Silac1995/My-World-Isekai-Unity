Shader "UI/SegmentedFill"
{
    Properties
    {
        [HideInInspector] _MainTex ("Texture", 2D) = "white" {}
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)

        [Header(Values)]
        _FillAmount ("Fill Amount", Range(0, 1)) = 1.0
        _GhostFill ("Ghost Fill", Range(0, 1)) = 1.0
        _HealFlash ("Heal Flash", Range(0, 1)) = 0.0

        [Header(Colors)]
        [HDR] _HealthColor ("Primary Color", Color) = (0.2, 0.8, 0.2, 1)
        [HDR] _LowHealthColor ("Low Color", Color) = (0.8, 0.2, 0.2, 1)
        _LowHealthThreshold ("Low Threshold", Range(0, 1)) = 0.2
        
        [HDR] _GhostColor ("Ghost Color", Color) = (1, 0.8, 0.1, 1)
        [HDR] _EmptyColor ("Background Color", Color) = (0.1, 0.1, 0.1, 1)
        [HDR] _HealColor ("Flash Color", Color) = (1, 1, 1, 1)

        [Header(Ghost Settings)]
        _GhostDelay ("Ghost Delay", Float) = 0.5
        _GhostDrainSpeed ("Ghost Drain Speed", Float) = 0.5

        [Header(Style)]
        _EdgeSoftness ("Edge Softness", Range(0.0001, 0.05)) = 0.002
        _ShineStrength ("Shine", Range(0, 1)) = 0.15

        // Required for UI Masking
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
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

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;

            float _FillAmount;
            float _GhostFill;
            float _HealFlash;

            fixed4 _HealthColor;
            fixed4 _LowHealthColor;
            float _LowHealthThreshold;

            fixed4 _GhostColor;
            fixed4 _EmptyColor;
            fixed4 _HealColor;

            float _EdgeSoftness;
            float _ShineStrength;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPos = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPos);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;

                // 1. Sample base texture to support sprite alpha shapes (UI corners, etc)
                fixed4 texColor = tex2D(_MainTex, uv);
                
                // 2. Determine Fill and Ghost boundaries with strict anti-aliasing (smooth edge)
                // Use a tiny softness to prevent hard pixelation, but keep it sharp enough for UI.
                float fillMask = smoothstep(_FillAmount + _EdgeSoftness, _FillAmount - _EdgeSoftness, uv.x);
                float ghostMask = smoothstep(_GhostFill + _EdgeSoftness, _GhostFill - _EdgeSoftness, uv.x);

                // 3. Determine Health Color (lerp between Low and Primary based on total FillAmount)
                float lowHealthFactor = smoothstep(_LowHealthThreshold + 0.1, _LowHealthThreshold - 0.1, _FillAmount);
                fixed4 primaryColor = lerp(_HealthColor, _LowHealthColor, lowHealthFactor);

                // 4. Flash additive effect
                primaryColor += _HealColor * _HealFlash;

                // 5. Compose the Layers
                // Start with Background empty color
                fixed4 finalColor = _EmptyColor;
                
                // Add Ghost Color layering
                float justGhostMask = saturate(ghostMask - fillMask);
                finalColor = lerp(finalColor, _GhostColor, justGhostMask);

                // Add Main Fill Color layering over Ghost
                finalColor = lerp(finalColor, primaryColor, fillMask);

                // 6. Subtle top shine for UI volume
                float shine = smoothstep(0.5, 1.0, uv.y) * _ShineStrength;
                finalColor.rgb += shine * ghostMask; // Shine over anything filled (ghost + main)

                // 7. Apply Unity UI standard properties
                finalColor.a *= texColor.a; // Clip to sprite outline
                finalColor *= IN.color;     // Allow local vertex/Image component tinting

                // 8. Canvas Masking clipping
                finalColor.a *= UnityGet2DClipping(IN.worldPos.xy, _ClipRect);

                return finalColor;
            }
            ENDCG
        }
    }
}