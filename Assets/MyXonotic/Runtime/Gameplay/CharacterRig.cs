using System;
using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Skeleton + animation data extracted from one Xonotic IQM player model
    /// by <c>IqmCharacterImporter</c>, already converted to Unity space
    /// (metres, Y up). Poses are stored per frame per joint as local
    /// translation (3), rotation quaternion (4) and scale (3) = 10 floats, so
    /// <see cref="CharacterAnimator"/> can drive a bone hierarchy under a
    /// SkinnedMeshRenderer without any Unity animation import.
    /// </summary>
    public sealed class CharacterRig : ScriptableObject
    {
        [Serializable]
        public struct Clip
        {
            public string Name;
            public int FirstFrame;
            public int FrameCount;
            public float FramesPerSecond;
            public bool Loop;
        }

        public string ModelName;
        public string[] JointNames;
        public int[] JointParents;
        /// Bind-pose local TRS per joint (10 floats each).
        public float[] BindLocal;
        public Clip[] Clips;
        public int FrameCount;
        /// FrameCount * JointCount * 10 floats.
        public float[] Poses;

        public int JointCount => JointNames != null ? JointNames.Length : 0;

        public int FindClip(string name)
        {
            if (Clips == null) return -1;
            for (int i = 0; i < Clips.Length; i++)
                if (string.Equals(Clips[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// Reads the local TRS of <paramref name="joint"/> at <paramref name="frame"/>.
        public void GetPose(int frame, int joint, out Vector3 t, out Quaternion r, out Vector3 s)
        {
            int n = JointCount;
            frame = Mathf.Clamp(frame, 0, Mathf.Max(0, FrameCount - 1));
            int o = (frame * n + joint) * 10;
            if (Poses == null || o + 9 >= Poses.Length) { GetBind(joint, out t, out r, out s); return; }
            t = new Vector3(Poses[o], Poses[o + 1], Poses[o + 2]);
            r = new Quaternion(Poses[o + 3], Poses[o + 4], Poses[o + 5], Poses[o + 6]);
            s = new Vector3(Poses[o + 7], Poses[o + 8], Poses[o + 9]);
        }

        public void GetBind(int joint, out Vector3 t, out Quaternion r, out Vector3 s)
        {
            int o = joint * 10;
            if (BindLocal == null || o + 9 >= BindLocal.Length) { t = Vector3.zero; r = Quaternion.identity; s = Vector3.one; return; }
            t = new Vector3(BindLocal[o], BindLocal[o + 1], BindLocal[o + 2]);
            r = new Quaternion(BindLocal[o + 3], BindLocal[o + 4], BindLocal[o + 5], BindLocal[o + 6]);
            s = new Vector3(BindLocal[o + 7], BindLocal[o + 8], BindLocal[o + 9]);
        }
    }
}
