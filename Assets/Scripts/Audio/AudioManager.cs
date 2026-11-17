using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Alchemy.Audio
{
    /// <summary>
    /// Centralized audio manager handling pooled SFX playback, scene-aware looping music with
    /// crossfades, time-slow music ducking, and persisted master volume.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public const string MasterVolumePrefsKey = "MasterVolume";
        private const int SfxPoolSize = 14;
        private const float BaseMusicVolume = 0.42f;
        private const float TimeSlowDuckMultiplier = 0.35f;

        private static AudioManager _instance;

        private readonly Dictionary<string, AudioClip> _clipCache = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        private readonly List<AudioSource> _sfxPool = new List<AudioSource>(SfxPoolSize);
        private int _nextPoolIndex;

        private AudioSource _musicPrimary;
        private AudioSource _musicSecondary;
        private string _currentMusicKey;
        private float _musicFadeT = 1f;
        private float _duckMultiplier = 1f;
        private float _targetDuckMultiplier = 1f;

        public static AudioManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    EnsureCreated();
                }
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            EnsureCreated();
        }

        private static void EnsureCreated()
        {
            if (_instance != null) return;

            var existing = FindFirstObjectByType<AudioManager>();
            if (existing != null)
            {
                _instance = existing;
                return;
            }

            var go = new GameObject("AudioManager");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AudioManager>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            float savedVol = PlayerPrefs.GetFloat(MasterVolumePrefsKey, 1f);
            AudioListener.volume = Mathf.Clamp01(savedVol);

            _musicPrimary = CreateSource("MusicPrimary", loop: true);
            _musicSecondary = CreateSource("MusicSecondary", loop: true);

            for (int i = 0; i < SfxPoolSize; i++)
            {
                _sfxPool.Add(CreateSource($"Sfx_{i}", loop: false));
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
            UpdateMusicForScene(SceneManager.GetActiveScene().name);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                _instance = null;
            }
        }

        private AudioSource CreateSource(string childName, bool loop)
        {
            var child = new GameObject(childName);
            child.transform.SetParent(transform, false);
            var src = child.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = 0f;
            src.ignoreListenerPause = true;
            return src;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _targetDuckMultiplier = 1f;
            UpdateMusicForScene(scene.name);
        }

        private void UpdateMusicForScene(string sceneName)
        {
            bool isMenu = !string.IsNullOrEmpty(sceneName) &&
                          sceneName.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) >= 0;
            PlayMusic(isMenu ? "music_menu" : "music_gameplay");
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            _duckMultiplier = Mathf.MoveTowards(_duckMultiplier, _targetDuckMultiplier, dt * 4.5f);

            if (_musicFadeT < 1f)
            {
                _musicFadeT = Mathf.Min(1f, _musicFadeT + dt * 1.4f);
                if (_musicFadeT >= 1f && _musicSecondary.isPlaying)
                {
                    _musicSecondary.Stop();
                }
            }

            float targetVol = BaseMusicVolume * _duckMultiplier;
            _musicPrimary.volume = targetVol * _musicFadeT;
            _musicSecondary.volume = targetVol * (1f - _musicFadeT);
        }

        public void SetMasterVolume(float volume)
        {
            float clamped = Mathf.Clamp01(volume);
            AudioListener.volume = clamped;
            PlayerPrefs.SetFloat(MasterVolumePrefsKey, clamped);
            PlayerPrefs.Save();
        }

        public void SetTimeSlowDuck(bool active)
        {
            _targetDuckMultiplier = active ? TimeSlowDuckMultiplier : 1f;
        }

        public void PlayMusic(string musicName)
        {
            if (string.IsNullOrEmpty(musicName) || string.Equals(_currentMusicKey, musicName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            AudioClip clip = LoadClip($"Audio/Music/{musicName}");
            if (clip == null) return;

            _currentMusicKey = musicName;

            // Swap primary & secondary so previous track fades out smoothly
            (_musicPrimary, _musicSecondary) = (_musicSecondary, _musicPrimary);

            _musicPrimary.clip = clip;
            _musicPrimary.volume = 0f;
            _musicPrimary.Play();
            _musicFadeT = _musicSecondary.isPlaying ? 0f : 1f;
        }

        public void PlayUi(string sfxName, float volume = 0.85f)
        {
            PlaySfx(sfxName, volume, pitchJitter: 0f);
        }

        public void PlaySfx(string sfxName, float volume = 1f, float pitchJitter = 0.06f)
        {
            if (string.IsNullOrEmpty(sfxName)) return;
            AudioClip clip = LoadClip($"Audio/SFX/{sfxName}");
            PlaySfx(clip, volume, pitchJitter);
        }

        public void PlaySfx(AudioClip clip, float volume = 1f, float pitchJitter = 0.06f)
        {
            if (clip == null || _sfxPool.Count == 0) return;

            AudioSource src = AcquireSfxSource();
            src.pitch = pitchJitter > 0f ? 1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter) : 1f;
            src.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        private AudioSource AcquireSfxSource()
        {
            for (int i = 0; i < _sfxPool.Count; i++)
            {
                int idx = (_nextPoolIndex + i) % _sfxPool.Count;
                if (!_sfxPool[idx].isPlaying)
                {
                    _nextPoolIndex = (idx + 1) % _sfxPool.Count;
                    return _sfxPool[idx];
                }
            }

            AudioSource fallback = _sfxPool[_nextPoolIndex];
            _nextPoolIndex = (_nextPoolIndex + 1) % _sfxPool.Count;
            return fallback;
        }

        private AudioClip LoadClip(string resourcePath)
        {
            if (_clipCache.TryGetValue(resourcePath, out AudioClip cached))
            {
                return cached;
            }

            AudioClip loaded = Resources.Load<AudioClip>(resourcePath);
            _clipCache[resourcePath] = loaded;
            return loaded;
        }
    }
}
