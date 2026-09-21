using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// Keeps local builds fast: the built-in Standard / Legacy shaders expand into tens of
    /// thousands of variants (lightmaps, fog, shadows, deferred). The game renders with
    /// MyXonotic/VertexColor; Standard is only a fallback, so keep a minimal forward set.
    /// </summary>
    sealed class ShaderVariantStripper : IPreprocessShaders
    {
        static readonly HashSet<string> AllowedKeywords = new HashSet<string>
        {
            "DIRECTIONAL", "UNITY_HDR_ON", "_ALPHATEST_ON", "_ALPHABLEND_ON", "_ALPHAPREMULTIPLY_ON",
        };

        public int callbackOrder => 0;

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            var name = shader.name;
            if (name.StartsWith("MyXonotic/") || name.StartsWith("UI/") || name.StartsWith("Hidden/")
                || name.StartsWith("Sprites/") || name.StartsWith("Skybox/"))
                return;
            if (snippet.passType == PassType.Deferred || snippet.passType == PassType.ShadowCaster
                || snippet.passType == PassType.Meta || snippet.passType == PassType.ForwardAdd
                || snippet.passType == PassType.MotionVectors)
            {
                data.Clear();
                return;
            }
            for (var i = data.Count - 1; i >= 0; i--)
            {
                var keep = true;
                foreach (var keyword in data[i].shaderKeywordSet.GetShaderKeywords())
                {
                    var keywordName = keyword.name;
                    if (string.IsNullOrEmpty(keywordName)) continue;
                    if (!AllowedKeywords.Contains(keywordName)) { keep = false; break; }
                }
                if (!keep) data.RemoveAt(i);
            }
        }
    }
}
