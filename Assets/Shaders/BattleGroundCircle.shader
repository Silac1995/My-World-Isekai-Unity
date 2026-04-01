Shader "Custom/BattleGroundCircle"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
        _InnerRadius ("Inner Radius", Range(0, 1)) = 0.3
        _OuterRadius ("Outer Radius", Range(0, 1)) = 0.5
        _Softness ("Softness", Range(0, 1)) = 0.05
        _PulseSpeed ("Pulse Speed", Float) = 0.0
        _PulseIntensity ("Pulse Intensity", Range(0, 1)) = 0.2
        [HideInInspector] _FadeFactor ("Fade Factor", Range(0, 1)) = 1.0
        [HideInInspector] _InitProgress ("Initiative Progress", Range(0, 1)) = 0.0
        [HideInInspector] _InitFlash ("Initiative Flash", Range(0, 1)) = 0.0
        _InitInner ("Init Ring Inner", Range(0, 1)) = 0.52
        _InitOuter ("Init Ring Outer", Range(0, 1)) = 0.58
        [HDR] _InitColor ("Init Ring Color", Color) = (1, 1, 1, 0.5)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+1"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        Pass
        {
            Name "BattleCircleDecal"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _InnerRadius;
                half _OuterRadius;
                half _Softness;
                half _PulseSpeed;
                half _PulseIntensity;
                half _InitInner;
                half _InitOuter;
                half4 _InitColor;
            CBUFFER_END

            // Per-instance via MaterialPropertyBlock — outside CBuffer.
            half _FadeFactor;
            half _InitProgress;
            half _InitFlash;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 centered = input.uv - 0.5;
                float dist = length(centered) * 2.0;

                // === Color ring ===
                float inner = smoothstep(_InnerRadius - _Softness, _InnerRadius + _Softness, dist);
                float outer = 1.0 - smoothstep(_OuterRadius - _Softness, _OuterRadius + _Softness, dist);
                float ring = inner * outer;

                float pulse = 1.0 - (_PulseIntensity * (sin(_Time.y * _PulseSpeed) * 0.5 + 0.5));

                half4 col = _Color;
                col.a = ring * pulse * _Color.a * _FadeFactor;

                // === Initiative arc ring ===
                // Angle: atan2 mapped to 0-1 clockwise from top (12 o'clock)
                float angle = atan2(centered.x, centered.y); // top = 0, CW positive
                float angleFrac = angle / 6.28318530718 + 0.5; // remap [-pi,pi] -> [0,1]

                float iInner = smoothstep(_InitInner - _Softness, _InitInner + _Softness, dist);
                float iOuter = 1.0 - smoothstep(_InitOuter - _Softness, _InitOuter + _Softness, dist);
                float initRing = iInner * iOuter;

                // Arc mask: visible where angleFrac < _InitProgress
                float arcEdge = smoothstep(_InitProgress, _InitProgress - 0.02, angleFrac);
                float initArc = initRing * arcEdge;

                // Flash glow: brief additive burst when initiative fills
                half3 flashGlow = _InitColor.rgb * _InitFlash * 2.0 * initRing;

                half3 initArcColor = _InitColor.rgb * initArc * _InitColor.a;
                half initArcAlpha = initArc * _InitColor.a * _FadeFactor;

                // Composite: color ring + initiative arc (premultiplied add)
                half3 finalRgb = col.rgb * col.a + initArcColor * _FadeFactor + flashGlow * _FadeFactor;
                half finalAlpha = saturate(col.a + initArcAlpha);

                clip(finalAlpha - 0.01);
                return half4(finalRgb / max(finalAlpha, 0.001), finalAlpha);
            }
            ENDHLSL
        }
    }
}
