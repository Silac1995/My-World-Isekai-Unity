Shader "UI/TargetIndicator"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _HealthPercent ("Health Percent", Range(0, 1)) = 1.0
        
        _BobSpeed ("Bobbing Speed", Float) = 3.0
        _BobAmplitude ("Bobbing Amplitude", Float) = 15.0

        // Required UI properties
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
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord  : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float _HealthPercent;
            float _BobSpeed;
            float _BobAmplitude;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                
                // Canvas-space bobbing offset using unscaled shader time (_Time.y)
                // This completely removes the floating logic from CPU!
                float bobOffset = sin(_Time.y * _BobSpeed) * _BobAmplitude;
                float4 bobbedVertex = IN.vertex;
                bobbedVertex.y += bobOffset;
                
                OUT.worldPosition = bobbedVertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, IN.texcoord) * IN.color;

                // Calculate health color
                // 0.0 -> Red, 0.5 -> Yellow, 1.0 -> Green
                fixed4 colorHigh = fixed4(0, 1, 0, 1); // Green
                fixed4 colorMid  = fixed4(1, 1, 0, 1); // Yellow
                fixed4 colorLow  = fixed4(1, 0, 0, 1); // Red

                fixed4 healthColor = lerp(colorLow, colorMid, saturate(_HealthPercent * 2.0));
                healthColor = lerp(healthColor, colorHigh, saturate((_HealthPercent - 0.5) * 2.0));

                // Apply color but preserve texture's original alpha
                col.rgb *= healthColor.rgb;
                col.a *= healthColor.a;

                return col;
            }
            ENDCG
        }
    }
}
