// Deadlock map surfaces: base colour texture x vertex colour (Deadlock's per-object tints and paint), optional cutout.
Shader "CursedApple/DeadlockWorld"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Cutoff", Range(0, 1)) = 0.5
        _Smoothness ("Smoothness", Range(0, 1)) = 0.1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200
        Cull [_Cull]
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma multi_compile __ _ALPHATEST_ON
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _Color;
        half _Cutoff, _Smoothness;
        struct Input { float2 uv_MainTex; float4 color : COLOR; };
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            #ifdef _ALPHATEST_ON
            clip(c.a - _Cutoff);
            #endif
            o.Albedo = c.rgb * IN.color.rgb;
            o.Metallic = 0;
            o.Smoothness = _Smoothness;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
