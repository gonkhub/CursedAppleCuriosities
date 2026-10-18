// Deadlock's glass and other see-through surfaces: tinted, mostly clear face-on, more opaque and reflective at grazing
// angles (fresnel), lit by the main light. Texture x vertex colour (Deadlock's tints), like the world surfaces.
Shader "CursedApple/DeadlockGlass"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Tint (alpha = face-on opacity)", Color) = (0.75, 0.85, 0.9, 0.22)
        _EdgeColor ("Edge reflection", Color) = (0.85, 0.8, 0.75, 1)
        _FresnelPower ("Fresnel power", Float) = 3
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color, _EdgeColor;
            half _FresnelPower;
            fixed4 _LightColor0;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 n : TEXCOORD1;
                float3 v : TEXCOORD2;
                fixed4 color : COLOR;
                UNITY_FOG_COORDS(3)
            };
            v2f vert (appdata i)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(i.vertex);
                o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.n = UnityObjectToWorldNormal(i.normal);
                o.v = WorldSpaceViewDir(i.vertex);
                o.color = i.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.n), v = normalize(i.v);
                float fres = pow(1 - saturate(abs(dot(n, v))), _FresnelPower);
                fixed3 tex = tex2D(_MainTex, i.uv).rgb * i.color.rgb * _Color.rgb;
                float ndl = saturate(dot(n, normalize(_WorldSpaceLightPos0.xyz))) * 0.5 + 0.5;
                fixed3 lit = tex * (UNITY_LIGHTMODEL_AMBIENT.rgb + _LightColor0.rgb * ndl * 0.6);
                fixed4 c = fixed4(lerp(lit, _EdgeColor.rgb * (UNITY_LIGHTMODEL_AMBIENT.rgb + _LightColor0.rgb * 0.5), fres * 0.6), saturate(_Color.a + fres * 0.55));
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
}
