// Simple textured transparent/additive particle shader.
// Replaces third-party materials that referenced Unity built-in fileID 211
// (Particles/Standard Unlit) which fails to resolve in this project.
// Blend mode is driven by _SrcBlend / _DstBlend so one shader covers both
// alpha-blended bubbles and additive light shafts.
Shader "Maritime/FxParticle"
{
    Properties
    {
        _MainTex  ("Texture", 2D)          = "white" {}
        _Color    ("Tint Color", Color)    = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.BlendMode)]
        _SrcBlend ("Src Blend", Float)     = 5   // SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)]
        _DstBlend ("Dst Blend", Float)     = 10  // OneMinusSrcAlpha

        // Everything below defaults to "off" so existing materials using this shader
        // (bubbles, motes) keep rendering exactly as they did.

        [Toggle] _AdditiveFog ("Fog fades to black (for additive blending)", Float) = 0

        _CameraFadeNear ("Camera Fade - fully faded at", Float) = 0
        _CameraFadeFar  ("Camera Fade - fully visible by", Float) = 0

        _SoftFadeDistance ("Soft Depth Fade distance (0 = off)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4    _MainTex_ST;
            fixed4    _Color;
            float     _AdditiveFog;
            float     _CameraFadeNear;
            float     _CameraFadeFar;
            float     _SoftFadeDistance;

            sampler2D_float _CameraDepthTexture;
            float4 _CameraDepthTexture_TexelSize;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
                fixed4 color : COLOR;
                float4 projPos : TEXCOORD2;
                float  eyeDepth : TEXCOORD3;
                UNITY_FOG_COORDS(1)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos   = UnityObjectToClipPos(v.vertex);
                o.uv    = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                o.projPos = ComputeScreenPos(o.pos);
                COMPUTE_EYEDEPTH(o.eyeDepth);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * i.color;

                // Fade out where the quad is about to intersect the camera, so walking through a
                // light shaft does not flash a full-screen wash of white.
                if (_CameraFadeFar > _CameraFadeNear)
                {
                    col.a *= saturate((i.eyeDepth - _CameraFadeNear) / (_CameraFadeFar - _CameraFadeNear));
                }

                // Soften the hard line where the quad cuts into the seabed or a rock. Needs the
                // camera depth texture; where that is unavailable the term resolves to 1 and this
                // is simply a no-op rather than making the effect vanish.
                if (_SoftFadeDistance > 0)
                {
                    float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.projPos)));
                    float diff = sceneZ - i.eyeDepth;
                    float soft = (sceneZ > 0.001) ? saturate(diff / _SoftFadeDistance) : 1.0;
                    col.a *= soft;
                }

                // Additive blending adds the fog colour instead of being buried by it, which makes
                // distant particles get brighter in thick fog. Fading them to black is the additive
                // equivalent of fading to the fog colour.
                if (_AdditiveFog > 0.5)
                {
                    // Note: no premultiply here - the SrcAlpha blend factor already applies alpha,
                    // so darkening rgb toward black is all that is needed to fade the contribution.
                    UNITY_APPLY_FOG_COLOR(i.fogCoord, col, fixed4(0, 0, 0, 0));
                }
                else
                {
                    UNITY_APPLY_FOG(i.fogCoord, col);
                }
                return col;
            }
            ENDCG
        }
    }
    Fallback "Transparent/Diffuse"
}
