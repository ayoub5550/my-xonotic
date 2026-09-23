// dev.18: translucent variant of MyXonotic/Lightmapped for Q3 stages with
// "blendfunc blend" / GL_SRC_ALPHA (glass, water, soft grates, dp_water):
// alpha blend, no depth write, drawn after opaque geometry.
Shader "MyXonotic/LightmappedBlend"
{
    Properties
    {
        _MainTex ("Diffuse", 2D) = "white" {}
        _LightMap ("Lightmap", 2D) = "white" {}
        _GlowTex ("Glow / fullbright (DarkPlaces *_glow)", 2D) = "black" {}
        _HasGlow ("Has glow (0/1)", Float) = 0
        _Scroll ("tcMod scroll (s,t per second)", Vector) = (0,0,0,0)
        _LightMode ("Light mode (0=flat,1=lightmap,2=vertex)", Float) = 1
        _LightScale ("Light scale", Float) = 2
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
        _Cull ("Cull", Float) = 2
        _Tint ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Cull [_Cull]
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_ST;
            sampler2D _LightMap;
            sampler2D _GlowTex;
            float _HasGlow;
            float4 _Scroll;
            float _LightMode, _LightScale, _Cutoff;
            fixed4 _Tint;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; fixed4 color : COLOR; UNITY_FOG_COORDS(2) };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                // dev.18: tcMod scroll (Q3 shader) — texture units per second, Q3 t axis points down.
                o.uv = TRANSFORM_TEX(v.uv, _MainTex) + float2(_Scroll.x, -_Scroll.y) * _Time.y;
                o.uv2 = v.uv2;
                o.color = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            fixed4 shade(v2f i, bool alphaTest)
            {
                fixed4 tex = tex2D(_MainTex, i.uv) * _Tint;
                if (alphaTest) clip(tex.a - _Cutoff);
                fixed3 light = fixed3(1,1,1);
                if (_LightMode > 1.5) light = i.color.rgb * _LightScale;
                else if (_LightMode > 0.5) light = tex2D(_LightMap, i.uv2).rgb * _LightScale;
                fixed4 c = fixed4(tex.rgb * light, tex.a);
                // dev.18: DarkPlaces draws <texture>_glow fullbright on top (unlit by the lightmap).
                // Screen-blend the glow (never exceeds 1): plain addition on top of the x2 lightmap
                // clipped Atelier's light strips to flat white slabs on the A15 (dev.18 Test Lab run 1).
                if (_HasGlow > 0.5) { fixed3 g = tex2D(_GlowTex, i.uv).rgb * _Tint.rgb; c.rgb = 1 - (1 - saturate(c.rgb)) * (1 - saturate(g)); }
                return c;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = shade(i, false);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
