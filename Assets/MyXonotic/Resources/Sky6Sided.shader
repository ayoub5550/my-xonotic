// Independent direction-based six-side sky sampling for the ORIGINAL Xonotic
// skybox side images (env/<name>_{rt,lf,ft,bk,up,dn}) — NOT the material
// script's qer_editorimage credit/editor-preview texture that BspImportPipeline
// used to resolve here as an ordinary diffuse map. See
// BspImportPipeline.BuildSkyMaterial (Editor/Import/BspImportPipeline.cs) for
// where these six textures are assigned, and its provenance manifest entries
// "sky-rt".."sky-dn".
//
// Deliberately NOT a Unity Cubemap/skybox-camera pass: this shader is applied
// directly to the actual sky-tagged world triangles as an ordinary opaque,
// depth-tested material (Cull Back / ZWrite On / ZTest LEqual below), so
// indoor geometry drawn in front of a sky surface still occludes it exactly
// like any other opaque surface, and no image "shows through" unrelated
// triangles. Do not delete sky-tagged triangles from the imported mesh to get
// a sky look; that removes real occlusion and (with any later, separate
// distant-background skybox) exposes gaps as sky through walls/ceilings that
// should stay solid.
//
// CAVEAT (unverified without an actual Editor/device render — see AGENTS.md
// "do not claim visual parity without a real render"): face selection below
// assumes the common Quake3 env-box authoring convention rt/lf = source ±X,
// ft/bk = source ±Y, up/dn = source ±Z, with the standard per-axis cubemap-face
// UV parameterisation (Khronos convention) applied per face. This has not been
// checked against an actual in-game/in-editor screenshot of this project's
// six side images; a mirrored or 90/180-rotated face is a plausible remaining
// defect a real render could reveal. It is still a bounded, non-repeating use
// of all six original images instead of one stretched editor-preview texture.
Shader "MyXonotic/Sky6Sided"
{
    Properties
    {
        _SkyRt ("Sky +X / rt", 2D) = "black" {}
        _SkyLf ("Sky -X / lf", 2D) = "black" {}
        _SkyFt ("Sky +Y / ft", 2D) = "black" {}
        _SkyBk ("Sky -Y / bk", 2D) = "black" {}
        _SkyUp ("Sky +Z / up", 2D) = "black" {}
        _SkyDn ("Sky -Z / dn", 2D) = "black" {}
        _Tint ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull Back
            ZWrite On
            ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _SkyRt;
            sampler2D _SkyLf;
            sampler2D _SkyFt;
            sampler2D _SkyBk;
            sampler2D _SkyUp;
            sampler2D _SkyDn;
            fixed4 _Tint;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 worldPos : TEXCOORD0; UNITY_FOG_COORDS(1) };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            // Standard per-axis cubemap-face UV parameterisation (as used by
            // hardware cubemap sampling), applied here to our own six loose
            // 2D images instead of a combined Cubemap asset so each source
            // side image stays an individually provenance-tracked texture.
            fixed4 SampleFace(sampler2D tex, float sc, float tc, float ma)
            {
                float2 uv = (float2(sc, tc) / abs(ma) + 1.0) * 0.5;
                return tex2D(tex, uv);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 dirUnity = normalize(i.worldPos - _WorldSpaceCameraPos);
                // Undo this project's fixed Quake->Unity axis swap (Y<->Z; see
                // BspCoordinateSpace.QuakeDirectionToUnity) so face selection
                // below is expressed in the SOURCE map's axis convention the
                // six side images were authored against, not Unity's.
                float3 d = float3(dirUnity.x, dirUnity.z, dirUnity.y);
                float3 ad = abs(d);
                fixed4 c;
                if (ad.x >= ad.y && ad.x >= ad.z)
                {
                    c = d.x > 0
                        ? SampleFace(_SkyRt, -d.y, -d.z, d.x)
                        : SampleFace(_SkyLf,  d.y, -d.z, d.x);
                }
                else if (ad.y >= ad.x && ad.y >= ad.z)
                {
                    c = d.y > 0
                        ? SampleFace(_SkyFt,  d.x, -d.z, d.y)
                        : SampleFace(_SkyBk, -d.x, -d.z, d.y);
                }
                else
                {
                    c = d.z > 0
                        ? SampleFace(_SkyUp,  d.x, -d.y, d.z)
                        : SampleFace(_SkyDn,  d.x,  d.y, d.z);
                }
                c *= _Tint;
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
