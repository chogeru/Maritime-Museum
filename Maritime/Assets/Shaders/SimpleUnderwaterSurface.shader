Shader "Maritime/SimpleUnderwaterSurface"
{
    Properties
    {
        _Color ("Base Color", Color) = (0.15, 0.35, 0.45, 0.85)
        _WaveTex ("Wave Normal Texture", 2D) = "bump" {}
        _WaveTiling ("Wave Tiling", Float) = 0.018
        _WaveSpeed ("Wave Speed", Float) = 0.03
        _HighlightColor ("Wave Highlight Color", Color) = (0.9, 1.0, 1.0, 1)
        _HighlightPower ("Highlight Sharpness", Range(1, 16)) = 3
        _HighlightIntensity ("Highlight Intensity", Range(0, 4)) = 0.7
        _RimColor ("Rim Color", Color) = (0.8, 0.95, 1.0, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2
        _SwellScale ("Swell Scale (large slow waves)", Range(0.05, 1)) = 0.25
        _ChopScale ("Chop Scale (fine ripples)", Range(1, 8)) = 3.7
        _Distortion ("Wave Distortion", Range(0, 0.3)) = 0.08
        _SunGlint ("Sun Glint Intensity", Range(0, 8)) = 2.5
        _SunGlintPower ("Sun Glint Sharpness", Range(4, 256)) = 48
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_FOG_COORDS(3)
            };

            sampler2D _WaveTex;
            float4 _WaveTex_ST;
            float _WaveTiling;
            float _WaveSpeed;
            fixed4 _Color;
            fixed4 _HighlightColor;
            float _HighlightPower;
            float _HighlightIntensity;
            fixed4 _RimColor;
            float _RimPower;
            float _SwellScale;
            float _ChopScale;
            float _Distortion;
            float _SunGlint;
            float _SunGlintPower;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = normalize(_WorldSpaceCameraPos - worldPos);
                o.uv = worldPos.xz * _WaveTiling;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Three normal-map layers at clearly separated scales and speeds. Using
                // very different rates is what stops the surface reading as one texture
                // sliding past - the layers drift out of step and keep reforming.
                float t = _Time.y * _WaveSpeed;

                // slow broad swell, also used to warp the faster layers so nothing moves in a straight line
                float2 uvSwell = i.uv * _SwellScale + t * 0.35 * float2(1.0, 0.35);
                float3 nSwell = UnpackNormal(tex2D(_WaveTex, uvSwell));
                float2 warp = nSwell.xy * _Distortion;

                float2 uvMid  = i.uv + warp + t * float2(-0.55, 0.85);
                float2 uvChop = i.uv * _ChopScale + warp * 1.8 + t * 2.6 * float2(0.75, -0.45);

                float3 nMid  = UnpackNormal(tex2D(_WaveTex, uvMid));
                float3 nChop = UnpackNormal(tex2D(_WaveTex, uvChop));

                // weight the layers so the swell dominates the shape and the chop only adds detail
                float3 waveNormal = normalize(nSwell * 1.0 + nMid * 0.75 + nChop * 0.35);

                // Bright wobbly streaks where the combined wave normal is steep -
                // reads as sunlight refracting through the underside of the surface.
                float slope = saturate(length(waveNormal.xy) * 1.8);
                float highlight = pow(slope, _HighlightPower) * _HighlightIntensity;

                // Sun seen THROUGH the surface. A half-vector specular is wrong here: looking
                // up from below, the view and light directions nearly cancel, so it never
                // lights up. What we want is how closely the line of sight points back along
                // the sunlight - that peaks when the viewer looks toward the sun - with the
                // wave normal jittering it so the disc breaks into shifting glitter.
                float3 V = normalize(i.viewDir);          // surface -> camera (points down)
                float3 L = normalize(_WorldSpaceLightPos0.xyz); // toward the sun (points up)
                float3 lookDir = -V;                      // camera -> surface (points up)
                float3 jitter = normalize(lookDir + float3(waveNormal.x, 0, waveNormal.y) * 0.65);
                float glint = pow(saturate(dot(jitter, L)), _SunGlintPower) * _SunGlint;

                float rim = 1 - saturate(abs(dot(normalize(i.worldNormal), V)));
                rim = pow(rim, _RimPower);

                fixed4 col = _Color;
                col.rgb += _HighlightColor.rgb * highlight;
                col.rgb += _LightColor0.rgb * glint;
                col.rgb = lerp(col.rgb, _RimColor.rgb, rim * _RimColor.a * 0.6);
                col.a = saturate(_Color.a + highlight * 0.5 + rim * 0.3 + glint * 0.4);

                // Without this the surface never fades into the distance, so it stays dark
                // where everything else has gone to fog colour and a hard band appears at the horizon.
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
