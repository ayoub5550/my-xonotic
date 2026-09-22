using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Plays <see cref="CharacterRig"/> clips on a bone hierarchy that drives a
    /// SkinnedMeshRenderer (GPU skinning). State selection is simple and
    /// data-driven from the owning actor's motion: idle / run / runbackwards /
    /// strafeleft / straferight / jump / dieone-deadone. Frames are
    /// interpolated (lerp position, slerp rotation) so low-fps clips stay
    /// smooth. Not a port of Xonotic's animation blending (no upper-body
    /// overlay yet).
    /// </summary>
    public sealed class CharacterAnimator : MonoBehaviour
    {
        public CharacterRig Rig;
        public Transform[] Bones;
        public SkinnedMeshRenderer Skin;

        public string CurrentClip => _clip >= 0 && Rig != null ? Rig.Clips[_clip].Name : "";
        public bool IsDead { get; private set; }

        int _clip = -1;
        float _time;
        bool _finished;
        float _requestedLock; // seconds the requested clip cannot be overridden by motion

        /// Build the bone hierarchy and skinned renderer for <paramref name="rig"/> under <paramref name="parent"/>.
        public static CharacterAnimator Create(Transform parent, CharacterRig rig, Mesh skinnedMesh, Material[] materials)
        {
            var root = new GameObject("Rig_" + rig.ModelName);
            root.transform.SetParent(parent, false);
            int n = rig.JointCount;
            var bones = new Transform[n];
            for (int j = 0; j < n; j++)
            {
                var b = new GameObject(rig.JointNames[j]).transform;
                bones[j] = b;
            }
            for (int j = 0; j < n; j++)
            {
                int p = rig.JointParents[j];
                bones[j].SetParent(p >= 0 && p < n ? bones[p] : root.transform, false);
                rig.GetBind(j, out var t, out var r, out var s);
                bones[j].localPosition = t;
                bones[j].localRotation = Normalize(r);
                bones[j].localScale = s;
            }
            var skinGO = new GameObject("Skin");
            skinGO.transform.SetParent(root.transform, false);
            var smr = skinGO.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = skinnedMesh;
            smr.sharedMaterials = materials;
            smr.bones = bones;
            smr.rootBone = n > 0 ? bones[0] : root.transform;
            smr.updateWhenOffscreen = true;
            smr.quality = SkinQuality.Bone4;
            var anim = root.AddComponent<CharacterAnimator>();
            anim.Rig = rig;
            anim.Bones = bones;
            anim.Skin = smr;
            anim.Play("idle", true);
            return anim;
        }

        static Quaternion Normalize(Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return m < 1e-6f ? Quaternion.identity : new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
        }

        /// Starts a clip by name (no-op if already playing and <paramref name="restart"/> is false).
        public bool Play(string name, bool restart = false)
        {
            if (Rig == null) return false;
            int idx = Rig.FindClip(name);
            if (idx < 0) return false;
            if (idx == _clip && !restart) return true;
            _clip = idx;
            _time = 0f;
            _finished = false;
            return true;
        }

        /// Drive the locomotion state from the owner's horizontal velocity/facing and grounding.
        public void SetMotion(Vector3 velocity, Vector3 forward, bool grounded)
        {
            if (IsDead) return;
            if (_requestedLock > 0f) return;
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            string clip;
            if (!grounded) clip = "jump";
            else if (flat.magnitude < 0.8f) clip = "idle";
            else
            {
                Vector3 f = new Vector3(forward.x, 0f, forward.z).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, f);
                float fwd = Vector3.Dot(flat.normalized, f);
                float side = Vector3.Dot(flat.normalized, right);
                if (Mathf.Abs(fwd) >= Mathf.Abs(side)) clip = fwd >= 0f ? "run" : "runbackwards";
                else clip = side >= 0f ? "straferight" : "strafeleft";
            }
            Play(clip);
        }

        /// Called by Actor when the owner dies: death clip, then hold the dead pose.
        public void OnDeath()
        {
            IsDead = true;
            _requestedLock = 0f;
            if (!Play(Random.value < 0.5f ? "dieone" : "dietwo", true)) Play("deadone", true);
            if (Skin != null) Skin.enabled = true;
        }

        public void OnRespawn()
        {
            IsDead = false;
            _requestedLock = 0f;
            if (Skin != null) Skin.enabled = true;
            Play("idle", true);
        }

        /// Short one-shot overlay (e.g. "shoot", "painone") that blocks motion selection while it plays.
        public void PlayOneShot(string name)
        {
            if (IsDead) return;
            if (Play(name, true) && _clip >= 0)
            {
                var c = Rig.Clips[_clip];
                _requestedLock = c.FrameCount / Mathf.Max(1f, c.FramesPerSecond);
            }
        }

        void Update()
        {
            if (ArenaBootstrap.IsPaused) return;
            Step(Time.deltaTime);
        }

        /// Advance the clip and pose the bones (plain method for tests).
        public void Step(float dt)
        {
            if (Rig == null || _clip < 0 || Bones == null) return;
            if (_requestedLock > 0f) { _requestedLock -= dt; if (_requestedLock <= 0f) _requestedLock = 0f; }
            var clip = Rig.Clips[_clip];
            float fps = clip.FramesPerSecond > 0f ? clip.FramesPerSecond : 20f;
            if (!_finished) _time += dt;
            float framePos = _time * fps;
            int count = Mathf.Max(1, clip.FrameCount);
            int f0, f1; float blend;
            if (clip.Loop)
            {
                framePos = Mathf.Repeat(framePos, count);
                f0 = Mathf.FloorToInt(framePos);
                f1 = (f0 + 1) % count;
                blend = framePos - f0;
            }
            else
            {
                if (framePos >= count - 1)
                {
                    framePos = count - 1;
                    if (!_finished)
                    {
                        _finished = true;
                        // Death clips settle into the matching dead pose.
                        if (IsDead && clip.Name.StartsWith("die", System.StringComparison.OrdinalIgnoreCase))
                        {
                            string dead = clip.Name.EndsWith("two", System.StringComparison.OrdinalIgnoreCase) ? "deadtwo" : "deadone";
                            if (Play(dead, true)) { _finished = true; clip = Rig.Clips[_clip]; count = Mathf.Max(1, clip.FrameCount); framePos = 0f; }
                        }
                    }
                }
                f0 = Mathf.FloorToInt(framePos);
                f1 = Mathf.Min(count - 1, f0 + 1);
                blend = framePos - f0;
            }
            int a = clip.FirstFrame + f0, b = clip.FirstFrame + f1;
            int n = Mathf.Min(Bones.Length, Rig.JointCount);
            for (int j = 0; j < n; j++)
            {
                Rig.GetPose(a, j, out var t0, out var r0, out var s0);
                Rig.GetPose(b, j, out var t1, out var r1, out var s1);
                var bone = Bones[j];
                bone.localPosition = Vector3.Lerp(t0, t1, blend);
                bone.localRotation = Quaternion.Slerp(Normalize(r0), Normalize(r1), blend);
                bone.localScale = Vector3.Lerp(s0, s1, blend);
            }
        }
    }
}
