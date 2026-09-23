using UnityEngine;

namespace MyXonotic.Gameplay
{
    /// <summary>Loops the map's upstream cdtrack (original OGG) at a modest volume.</summary>
    public sealed class MapMusic : MonoBehaviour
    {
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 0.35f;
        AudioSource _source;

        void Start()
        {
            if (clip == null || Application.isBatchMode) return;
            _source = gameObject.AddComponent<AudioSource>();
            _source.clip = clip;
            _source.loop = true;
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.volume = volume * GameSettings.MusicVolume / GameSettings.DefaultMusicVolume;
            _source.Play();
        }

        /// dev.18: re-read the music volume from GameSettings (called when the slider moves).
        public void ApplyVolume()
        {
            if (_source != null) _source.volume = volume * GameSettings.MusicVolume / GameSettings.DefaultMusicVolume;
        }
    }
}
