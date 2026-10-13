// The stage's edge: invisible until a player comes close, then a faint grid that brightens as they near it.
// _CacPlayerPos is set every frame by the mod (the local player's position).
Shader "CursedApple/Barrier"
{
    Properties
    {
        _Color ("Color", Color) = (0.35, 0.85, 1, 0.9)
        _FadeRadius ("Fade radius (m)", Float) = 14
        _GridScale ("Grid size (m)", Float) = 1.25
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color;
            float _FadeRadius, _GridScale;
            float4 _CacPlayerPos;
            struct v2f { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; };
            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float d = distance(i.wp, _CacPlayerPos.xyz);
                float fade = saturate(1 - d / _FadeRadius);
                fade *= fade;
                float2 g = abs(frac(float2(i.wp.x + i.wp.z, i.wp.y) / _GridScale) - 0.5);
                float lines = smoothstep(0.40, 0.48, max(g.x, g.y));
                float a = fade * (0.12 + 0.88 * lines) * _Color.a;
                return float4(_Color.rgb, a);
            }
            ENDCG
        }
    }
}
