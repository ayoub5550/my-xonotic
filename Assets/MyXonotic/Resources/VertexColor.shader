Shader "MyXonotic/VertexColor"
{
    Properties
    {
        _Color ("Debug tint", Color) = (1,1,1,1)
        _VertexWeight ("Use vertex lighting", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Cull Back
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; UNITY_FOG_COORDS(0) };
            fixed4 _Color;
            float _VertexWeight;
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float directional = 0.45 + 0.55 * saturate(dot(
                    UnityObjectToWorldNormal(v.normal), normalize(float3(0.3,0.8,0.4))));
                // Primitive meshes have no COLOR stream: zero means debug directional lighting.
                fixed3 color = dot(v.color.rgb, fixed3(1,1,1)) < 0.005
                    ? fixed3(directional,directional,directional) : v.color.rgb;
                o.color = fixed4(lerp(fixed3(directional,directional,directional), color, _VertexWeight), 1) * _Color;
                UNITY_TRANSFER_FOG(o,o.pos);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = i.color;
                UNITY_APPLY_FOG(i.fogCoord,c);
                return c;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
