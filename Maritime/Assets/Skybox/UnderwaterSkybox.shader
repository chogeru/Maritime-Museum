// Gradient skybox for the underwater state.
// AquasUnderwaterTrigger calls material.SetColor("_SkyTint", ...) every frame to drive
// the color with depth. _SkyTint is the colour at the horizon, so it keeps matching
// RenderSettings.fogColor there and distant geometry blends into the sky seamlessly;
// the view direction then brightens it toward the surface and darkens it toward the seabed.
Shader "Maritime/UnderwaterSkybox"
{
    Properties
    {
        _SkyTint ("Sky Tint (horizon)", Color) = (0.01, 0.04, 0.1, 1)
        _UpBoost ("Toward Surface Brightness", Range(1, 3)) = 1.45
        _DownDarken ("Toward Seabed Darkness", Range(0, 1)) = 0.55
        _GradientFalloff ("Gradient Falloff", Range(0.2, 4)) = 1.1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _SkyTint;
            float _UpBoost;
            float _DownDarken;
            float _GradientFalloff;

            struct appdata { float4 vertex : POSITION; };
            struct v2f    { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                // skybox mesh vertices double as view directions
                o.dir = v.vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float h = normalize(i.dir).y;               // -1 at the seabed, +1 at the surface
                float up   = pow(saturate( h), _GradientFalloff);
                float down = pow(saturate(-h), _GradientFalloff);

                // horizon stays exactly _SkyTint so it still matches the fog colour there
                float3 col = _SkyTint.rgb;
                col = lerp(col, col * _UpBoost,    up);
                col = lerp(col, col * _DownDarken, down);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
