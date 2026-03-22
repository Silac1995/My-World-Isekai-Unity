Shader "UI/SparkleExplosion"
{
    Properties
    {
        _Color ("Sparkle Color", Color) = (1, 0.8, 0.2, 1)
        _Progress ("Explosion Progress", Range(0, 1)) = 0.0
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

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

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
                float2 texcoord  : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float _Progress;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.vertex = UnityObjectToClipPos(v.vertex);

                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            // Pseudo-random hash function
            float hash12(float2 p)
            {
                float3 p3  = frac(float3(p.xyx) * .1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord - 0.5;
                
                // Dist from center
                float dist = length(uv);
                
                // Angle
                float angle = atan2(uv.y, uv.x);
                
                // Convert angle to discrete "rays" (e.g., 20 rays)
                float rayCount = 20.0;
                float raySegment = floor(angle * rayCount / (2.0 * 3.14159));
                
                // Randomness per ray
                float randOffset = hash12(float2(raySegment, 13.37));
                float randSpeed = 0.5 + 0.5 * hash12(float2(raySegment, 42.0));
                float randSize = 0.5 + 0.5 * hash12(float2(raySegment, 77.7));
                
                // Smooth angle per ray (for softening edges of rays)
                float localAngle = frac(angle * rayCount / (2.0 * 3.14159)) - 0.5;
                
                // --- Sparkle Movement Logic ---
                // Base progress from 0 to 1
                float p = _Progress;
                
                // How far out the sparkle is based on speed
                float particleDist = p * randSpeed;
                
                // Distance to the particle center
                float distToParticle = abs(dist - particleDist);
                
                // Angle distance (makes the ray narrow)
                float angleDist = abs(localAngle);
                
                // Combine to form a shape (diamond-ish / star-ish)
                // We stretch it along the angular axis more as it goes outward
                float shape = (distToParticle * 15.0) + (angleDist * dist * 30.0 / randSize);
                
                // Brightness profile
                float alpha = saturate(1.0 - shape);
                
                // Make it twinkle/fade
                // Start bright, fade out at end of progress
                float alphaFade = saturate((1.0 - p) * 3.0); // Fade out quickly at the end
                
                // Add a second layer of smaller, slower sparkles
                float raySegment2 = floor((angle + 0.1) * 30.0 / (2.0 * 3.14159));
                float randSpeed2 = 0.2 + 0.4 * hash12(float2(raySegment2, 99.9));
                float particleDist2 = p * randSpeed2;
                float localAngle2 = frac((angle + 0.1) * 30.0 / (2.0 * 3.14159)) - 0.5;
                float shape2 = (abs(dist - particleDist2) * 20.0) + (abs(localAngle2) * dist * 40.0);
                float alpha2 = saturate(1.0 - shape2);
                
                // Combine layers
                float totalAlpha = saturate(alpha + alpha2) * alphaFade;
                
                // Prevent rendering if _Progress is 0 or 1
                totalAlpha *= step(0.001, p) * step(p, 0.999);
                
                fixed4 color = IN.color;
                
                // Make the core hotter (whiter) and edges colored
                fixed3 finalColor = lerp(color.rgb, fixed3(1,1,1), totalAlpha * 0.5);
                
                return fixed4(finalColor, totalAlpha * color.a);
            }
            ENDCG
        }
    }
}
