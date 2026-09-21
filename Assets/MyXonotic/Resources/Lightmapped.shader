// Diffuse texture x baked lightmap (uv2), with vertex-colour fallback.
// Mobile-oriented: one texture fetch + one lightmap fetch, no dynamic lights.
Shader "MyXonotic/Lightmapped"
{
    Properties
    {
        _MainTex ("Diffuse", 2D) = "white" {}
        _LightMap ("Lightmap", 2D) = "white" {}
        _LightMode ("Light mode (0=flat,1=lightmap,2=vertex)", Float) = 1
        _LightScale ("Light scale", Float) = 2
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
        _Cull ("Cull", Float) = 2
        _Tint ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull [_Cull]
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma shader_feature_local _ALPHATEST_ON
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_ST;
            sampler2D _LightMap;
            float _LightMode, _LightScale, _Cutoff;
            fixed4 _Tint;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; fixed4 color : COLOR; UNITY_FOG_COORDS(2) };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uv2 = v.uv2;
                o.color = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv) * _Tint;
                #ifdef _ALPHATEST_ON
                clip(tex.a - _Cutoff);
                #endif
                fixed3 light = fixed3(1,1,1);
                if (_LightMode > 1.5) light = i.color.rgb * _LightScale;
                else if (_LightMode > 0.5) light = tex2D(_LightMap, i.uv2).rgb * _LightScale;
                fixed4 c = fixed4(tex.rgb * light, tex.a);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
