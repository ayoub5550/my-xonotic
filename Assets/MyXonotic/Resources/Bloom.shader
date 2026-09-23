// dev.18: lightweight mobile bloom (MobileBloom.cs). Four passes on a quarter
// resolution buffer: 0 = threshold + downsample, 1 = horizontal blur,
// 2 = vertical blur, 3 = composite (source + bloom * _Intensity).
// Fixed 5-tap kernels, no HDR, no keywords: a handful of shader variants only.
Shader "MyXonotic/Bloom"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _BloomTex ("Bloom", 2D) = "black" {}
        _Threshold ("Threshold", Float) = 0.7
        _Intensity ("Intensity", Float) = 0.8
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex; float4 _MainTex_TexelSize;
        sampler2D _BloomTex;
        float _Threshold, _Intensity;
        struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
        v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
        fixed4 blur(float2 uv, float2 step)
        {
            fixed4 c = tex2D(_MainTex, uv) * 0.2270270270;
            c += tex2D(_MainTex, uv + step * 1.3846153846) * 0.3162162162;
            c += tex2D(_MainTex, uv - step * 1.3846153846) * 0.3162162162;
            c += tex2D(_MainTex, uv + step * 3.2307692308) * 0.0702702703;
            c += tex2D(_MainTex, uv - step * 3.2307692308) * 0.0702702703;
            return c;
        }
        ENDCG
        Pass // 0 threshold
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                float l = max(c.r, max(c.g, c.b));
                float k = saturate((l - _Threshold) / max(1e-3, 1.0 - _Threshold));
                return fixed4(c.rgb * k, 1);
            }
            ENDCG
        }
        Pass // 1 horizontal
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag(v2f i) : SV_Target { return blur(i.uv, float2(_MainTex_TexelSize.x, 0)); }
            ENDCG
        }
        Pass // 2 vertical
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag(v2f i) : SV_Target { return blur(i.uv, float2(0, _MainTex_TexelSize.y)); }
            ENDCG
        }
        Pass // 3 composite
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                fixed3 b = tex2D(_BloomTex, i.uv).rgb;
                c.rgb = saturate(c.rgb + b * _Intensity);
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
