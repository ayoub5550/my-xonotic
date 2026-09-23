using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// dev.16: adds the Firebase Test Lab Game Loop intent-filter to the launcher
    /// activity of the generated Gradle project (Unity's own manifest stays the
    /// template; we patch the output so Unity keeps injecting its attributes).
    /// https://firebase.google.com/docs/test-lab/android/game-loop
    /// </summary>
    public sealed class GameLoopManifest : IPostGenerateGradleAndroidProject
    {
        public const string LoopsMetaData = "com.google.test.loops";
        public const int Loops = 2;

        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifest = Path.Combine(path, "src/main/AndroidManifest.xml");
            if (!File.Exists(manifest))
            {
                Debug.LogWarning("[GameLoopManifest] manifest not found at " + manifest);
                return;
            }
            string xml = File.ReadAllText(manifest);
            string patched = Patch(xml);
            if (patched != xml)
            {
                File.WriteAllText(manifest, patched);
                Debug.Log("[GameLoopManifest] TEST_LOOP intent-filter added to " + manifest);
            }
        }

        /// Pure (tested): returns the manifest with the TEST_LOOP filter + loops meta-data on the MAIN/LAUNCHER activity.
        public static string Patch(string manifestXml)
        {
            var doc = new XmlDocument();
            doc.LoadXml(manifestXml);
            var ns = new XmlNamespaceManager(doc.NameTable);
            const string android = "http://schemas.android.com/apk/res/android";
            ns.AddNamespace("android", android);

            // Always drop the incoming declaration so the writer emits a correct utf-8 one
            // (a stale Gradle project may still carry a bad encoding="utf-16" header).
            if (doc.FirstChild is XmlDeclaration decl) doc.RemoveChild(decl);

            if (doc.SelectSingleNode("//intent-filter/action[@android:name='" + GameLoop.Action + "']", ns) != null)
                return manifestXml.TrimStart().StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>") ? manifestXml : Serialize(doc);

            var launcher = doc.SelectSingleNode("//activity[intent-filter/category/@android:name='android.intent.category.LAUNCHER']", ns) as XmlElement;
            if (launcher == null) return manifestXml;

            var filter = doc.CreateElement("intent-filter");
            var action = doc.CreateElement("action");
            action.SetAttribute("name", android, GameLoop.Action);
            var category = doc.CreateElement("category");
            category.SetAttribute("name", android, "android.intent.category.DEFAULT");
            var data = doc.CreateElement("data");
            data.SetAttribute("mimeType", android, "application/javascript");
            filter.AppendChild(action);
            filter.AppendChild(category);
            filter.AppendChild(data);
            launcher.AppendChild(filter);

            var app = launcher.ParentNode as XmlElement;
            if (app != null && doc.SelectSingleNode("//meta-data[@android:name='" + LoopsMetaData + "']", ns) == null)
            {
                var meta = doc.CreateElement("meta-data");
                meta.SetAttribute("name", android, LoopsMetaData);
                meta.SetAttribute("value", android, Loops.ToString());
                app.AppendChild(meta);
            }

            return Serialize(doc);
        }

        /// Declare UTF-8 explicitly: a plain StringWriter would emit encoding="utf-16"
        /// while the file is written as UTF-8, which makes the Gradle manifest merger crash.
        static string Serialize(XmlDocument doc)
        {
            using (var ms = new MemoryStream())
            {
                using (var xw = XmlWriter.Create(ms, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = false, Encoding = new System.Text.UTF8Encoding(false) }))
                    doc.Save(xw);
                return System.Text.Encoding.UTF8.GetString(ms.ToArray());
            }
        }
    }
}
