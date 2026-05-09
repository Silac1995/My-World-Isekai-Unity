Shader "MWI/GhostOutline"
{
    // Inverted-hull outline shader. Cull Front so only the back faces of the mesh
    // render; the vertex shader inflates each position along its normal by
    // _OutlineWidth so the back-face shell extends OUTSIDE the original mesh's
    // silhouette → produces a clean outline. Color pulses over time for a
    // "shining" feel.
    //
    // Authored 2026-05-06 for Building.EnsureConstructionGhostVisual outline pass.
    // See Assets/Shaders/GhostPlacement.shader for the companion ghost-fill pass.
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.5, 0.85, 1.0, 1.0)
        _OutlineWidth ("Outline Width", Range(0, 0.5)) = 0.05
        _PulseAmplitude ("Pulse Amplitude", Range(0, 1)) = 0.35
        _PulseSpeed ("Pulse Speed", Float) = 2.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+200"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "UniversalForward" }
            Cull Front
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
                float _PulseAmplitude;
                float _PulseSpeed;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // Inflate vertex along its normal in object space, then transform.
                float3 inflated = input.positionOS.xyz + input.normalOS * _OutlineWidth;
                output.positionCS = TransformObjectToHClip(inflated);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // Time-based pulse for the "shining" feel. _Time.y is seconds.
                // pulse ∈ [1 - amplitude, 1 + amplitude] then clamped to [0, 1].
                float pulse = 1.0 + _PulseAmplitude * sin(_Time.y * _PulseSpeed);
                half3  rgb   = saturate(_OutlineColor.rgb * pulse);
                half   alpha = _OutlineColor.a;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
