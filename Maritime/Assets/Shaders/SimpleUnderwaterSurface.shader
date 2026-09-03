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
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

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

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = normalize(_WorldSpaceCameraPos - worldPos);
                o.uv = worldPos.xz * _WaveTiling;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Two panning normal-map samples (classic water trick) so the ripple
                // pattern doesn't repeat in an obviously tiled/static way.
                float2 uv1 = i.uv + _Time.y * _WaveSpeed * float2(1, 0.6);
                float2 uv2 = i.uv * 1.37 - _Time.y * _WaveSpeed * float2(0.7, 1);
                float3 n1 = UnpackNormal(tex2D(_WaveTex, uv1));
                float3 n2 = UnpackNormal(tex2D(_WaveTex, uv2));
                float3 waveNormal = normalize(n1 + n2);

                // Bright wobbly streaks where the combined wave normal is steep -
                // reads as sunlight refracting through the underside of the surface.
                float slope = saturate(length(waveNormal.xy) * 1.8);
                float highlight = pow(slope, _HighlightPower) * _HighlightIntensity;

                float rim = 1 - saturate(abs(dot(normalize(i.worldNormal), normalize(i.viewDir))));
                rim = pow(rim, _RimPower);

                fixed4 col = _Color;
                col.rgb += _HighlightColor.rgb * highlight;
                col.rgb = lerp(col.rgb, _RimColor.rgb, rim * _RimColor.a * 0.6);
                col.a = saturate(_Color.a + highlight * 0.5 + rim * 0.3);
                return col;
            }
            ENDCG
        }
    }
}
