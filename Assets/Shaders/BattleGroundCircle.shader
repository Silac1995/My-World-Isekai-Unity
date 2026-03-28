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
            CBUFFER_END

            // Declared outside CBuffer so MaterialPropertyBlock can override per-instance.
            // Note: this disables SRP Batcher for this shader, which is acceptable for the
            // small number of battle circles active at any time.
            half _FadeFactor;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Distance from UV center, normalized to [0, 1]
                float2 center = float2(0.5, 0.5);
                float dist = distance(input.uv, center) * 2.0;

                // Ring mask: inner and outer edges with softness
                float inner = smoothstep(_InnerRadius - _Softness, _InnerRadius + _Softness, dist);
                float outer = 1.0 - smoothstep(_OuterRadius - _Softness, _OuterRadius + _Softness, dist);
                float ring = inner * outer;

                // Pulse animation (uses _Time.y which pauses with game — intentional for battle visuals)
                float pulse = 1.0 - (_PulseIntensity * (sin(_Time.y * _PulseSpeed) * 0.5 + 0.5));

                half4 col = _Color;
                col.a = ring * pulse * _Color.a * _FadeFactor;

                clip(col.a - 0.01);
                return col;
            }
            ENDHLSL
        }
    }
}
