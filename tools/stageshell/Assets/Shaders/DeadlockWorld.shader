// Deadlock map surfaces, lit as Deadlock lights them: base colour (and environment_blend's second layer, revealed by its mask
// against the vertex paint weight) x vertex tint, modulated by the biplanar world overlay, times Deadlock's own baked
// irradiance lightmap (emission). A small share of the albedo stays in the regular lighting, so the game's own lights
// (the sun, explosions, glows) still touch the map. Without a lightmap (_CacLightmapOn = 0) it is a plain lit surface.
Shader "CursedApple/DeadlockWorld"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Tint1 ("Layer 1 tint (masked)", Color) = (1, 1, 1, 1)
        _Cutoff ("Cutoff", Range(0, 1)) = 0.5
        _Smoothness ("Smoothness", Range(0, 1)) = 0.1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _SelfIllum ("Self illumination", Float) = 0
        _TintMask ("Alpha is the tint mask", Float) = 1

        _Tex2 ("Layer 2 albedo", 2D) = "white" {}
        _Tint2 ("Layer 2 tint", Color) = (1, 1, 1, 1)
        _Reveal2 ("Layer 2 reveal mask", 2D) = "gray" {}
        _RevealOffset ("Reveal offset", Float) = 0
        _RevealSoftness ("Reveal softness", Float) = 0.125
        _RevealInvert ("Reveal invert", Float) = 0

        _Overlay ("World overlay", 2D) = "gray" {}
        _OverlayTiling ("Overlay tiling (m)", Float) = 13
        _OverlayDark ("Overlay darkness contrast", Float) = 1
        _OverlayBright ("Overlay brightness contrast", Float) = 1
        _OverlayStrength1 ("Overlay strength (layer 1)", Float) = 1
        _OverlayStrength2 ("Overlay strength (layer 2)", Float) = 1
        _OverlayMask1 ("Overlay mask (layer 1): 0 none, 1 tint mask, 2 inverted", Float) = 1
        _OverlayMask2 ("Overlay mask (layer 2)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200
        Cull [_Cull]
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow vertex:vert
        #pragma multi_compile __ _ALPHATEST_ON
        #pragma multi_compile __ CAC_LAYER2
        #pragma multi_compile __ CAC_OVERLAY
        #pragma target 3.5

        sampler2D _MainTex, _Tex2, _Reveal2, _Overlay;
        fixed4 _Color, _Tint1, _Tint2;
        half _Cutoff, _Smoothness, _SelfIllum, _TintMask;
        float _RevealOffset, _RevealSoftness, _RevealInvert;
        float _OverlayTiling, _OverlayDark, _OverlayBright, _OverlayStrength1, _OverlayStrength2, _OverlayMask1, _OverlayMask2;

        // Set once by the mod for every Deadlock stage.
        sampler2D _CacLightmap;
        float _CacLightmapOn, _CacExposure, _CacDynamic;
        float4 _CacFallback;

        struct Input
        {
            float2 uv_MainTex;
            float2 lm;
            float4 color : COLOR;
            float3 worldPos;
            float3 wn;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.lm = v.texcoord3.xy; // the mesh's fourth UV set: Deadlock's lightmap UVs ((-1, -1) = none)
            o.wn = UnityObjectToWorldNormal(v.normal);
        }

        // Deadlock's GetRevealThreshold: the reveal mask stretched around its middle by the offset, inside the softness band.
        float RevealThreshold(float reveal)
        {
            float halfOffset = _RevealOffset * 0.5;
            float mid = 0.5 + halfOffset;
            float remapped = reveal < 0.5 ? saturate(reveal * 2.0) * mid : saturate((reveal - 0.5) * 2.0) * (0.5 - halfOffset) + mid;
            return saturate(clamp(remapped, 1e-4, 0.9999)) * (1.0 - 2.0 * _RevealSoftness) + _RevealSoftness;
        }

        // Deadlock's GetOverlayFactor: a signed overlay (grey = no change) pushed brighter or darker by its contrasts.
        float3 OverlayFactor(float3 overlay)
        {
            float3 s = overlay * 2.0 - 1.0;
            float3 brighten = (1.0 - pow(max(1e-4, 1.0 - max(0.0, s)), _OverlayBright)) * _OverlayBright;
            float3 darken = (pow(max(1e-4, 1.0 + min(0.0, s)), _OverlayDark) - 1.0) * _OverlayDark;
            return max(0.0, brighten + darken + 1.0);
        }

        float MaskMode(float mode, float tintMask) { return mode < 0.5 ? 1.0 : (mode > 1.5 ? 1.0 - tintMask : tintMask); }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            #ifdef _ALPHATEST_ON
            clip(c.a - _Cutoff);
            #endif
            // Every tint (the material's, the object's, the vertex paint: all in the vertex colour) applies only where the
            // layer's tint mask (its texture's alpha) says so, as in Deadlock.
            float mask1 = lerp(1.0, c.a, _TintMask);
            float3 albedo = lerp(c.rgb, c.rgb * _Tint1.rgb * IN.color.rgb, mask1);
            float mask = mask1;
            float layer2 = 0;
            #ifdef CAC_LAYER2
            float reveal = tex2D(_Reveal2, IN.uv_MainTex).r;
            reveal = _RevealInvert > 0.5 ? 1.0 - reveal : reveal;
            float blend = IN.color.a;
            layer2 = blend >= 0.999 ? 1.0 : (blend <= 1e-5 ? 0.0 : smoothstep(-_RevealSoftness, _RevealSoftness, blend - RevealThreshold(reveal)));
            fixed4 c2 = tex2D(_Tex2, IN.uv_MainTex);
            float mask2 = lerp(1.0, c2.a, _TintMask);
            float3 albedo2 = lerp(c2.rgb, c2.rgb * _Tint2.rgb * IN.color.rgb, mask2);
            albedo = lerp(albedo, albedo2, layer2);
            mask = lerp(mask1, mask2, layer2);
            #endif
            #ifdef CAC_OVERLAY
            // Biplanar: the two planes the surface faces most, blended once either is ~35 degrees off its axis.
            float3 n = abs(normalize(IN.wn));
            float3 p = IN.worldPos / _OverlayTiling;
            float2 uvX = p.zy, uvY = p.xz, uvZ = p.xy;
            float3 w = saturate((n - 0.5773) * 2.3657441);
            w /= max(1e-4, w.x + w.y + w.z);
            float3 ov = tex2D(_Overlay, uvX).rgb * w.x + tex2D(_Overlay, uvY).rgb * w.y + tex2D(_Overlay, uvZ).rgb * w.z;
            if (w.x + w.y + w.z < 1e-3) ov = tex2D(_Overlay, n.y > 0.5 ? uvY : uvZ).rgb;
            float strength = lerp(_OverlayStrength1 * MaskMode(_OverlayMask1, mask), _OverlayStrength2 * MaskMode(_OverlayMask2, mask), layer2);
            albedo = lerp(albedo, albedo * OverlayFactor(ov), strength);
            #endif

            float3 baked = IN.lm.x >= 0.0 ? tex2D(_CacLightmap, IN.lm).rgb : _CacFallback.rgb;
            float lit = _CacLightmapOn;
            o.Albedo = albedo * lerp(1.0, _CacDynamic, lit);
            o.Emission = albedo * (baked * _CacExposure * lit + _SelfIllum);
            o.Metallic = 0;
            o.Smoothness = _Smoothness;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
