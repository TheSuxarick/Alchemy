using System.Collections.Generic;
using Alchemy.Audio;
using UnityEngine;

namespace Alchemy.Gameplay
{
    public class WaveManager2D : MonoBehaviour
    {
        public enum WavePhase
        {
            WaveCombat,
            BossPhase,
            Intermission
        }

        [Header("Arena Bounds")]
        public int mapWidthTiles = 48;
        public int mapHeightTiles = 48;
        public Vector2 bossGladeCenter = new Vector2(16.5f, 0f);
        public float intermissionSeconds = 4.0f;

        public int CurrentWave { get; private set; } = 1;
        public WavePhase CurrentPhase { get; private set; } = WavePhase.Intermission;

        private readonly List<GameObject> _activeEnemies = new List<GameObject>();
        private float _intermissionTimer;

        private string _bannerTitle = "";
        private string _bannerSub = "";
        private Color _bannerColor = Color.white;
        private float _bannerTimer;
        private float _bannerDuration = 2.4f;
        private Sprite[] _bannerVfxFrames;

        private GUIStyle _hudTitleStyle;
        private GUIStyle _hudSubStyle;
        private GUIStyle _bannerTitleStyle;
        private GUIStyle _bannerSubStyle;

        public void Initialize(int widthTiles, int heightTiles, Vector2 gladeCenter)
        {
            mapWidthTiles = widthTiles;
            mapHeightTiles = heightTiles;
            bossGladeCenter = gladeCenter;
            StartWave(1);
        }

        public void RegisterSpawnedEnemy(GameObject enemyGo)
        {
            if (enemyGo != null && !_activeEnemies.Contains(enemyGo))
            {
                _activeEnemies.Add(enemyGo);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (_bannerTimer > 0f) _bannerTimer -= dt;

            PruneDeadEnemies();

            switch (CurrentPhase)
            {
                case WavePhase.WaveCombat:
                    if (_activeEnemies.Count == 0)
                    {
                        StartBossPhase();
                    }
                    break;

                case WavePhase.BossPhase:
                    if (_activeEnemies.Count == 0)
                    {
                        CompleteWaveAndStartIntermission();
                    }
                    break;

                case WavePhase.Intermission:
                    _intermissionTimer -= dt;
                    if (_intermissionTimer <= 0f)
                    {
                        StartWave(CurrentWave + 1);
                    }
                    break;
            }
        }

        private void PruneDeadEnemies()
        {
            for (int i = _activeEnemies.Count - 1; i >= 0; i--)
            {
                if (_activeEnemies[i] == null)
                {
                    _activeEnemies.RemoveAt(i);
                }
            }
        }

        private void StartWave(int waveNumber)
        {
            CurrentWave = Mathf.Max(1, waveNumber);
            CurrentPhase = WavePhase.WaveCombat;
            _activeEnemies.Clear();

            int slimeCount = 4 + CurrentWave * 2;
            int mageCount = 1 + CurrentWave;

            SpawnSlimes(slimeCount, nearPoint: null, minPlayerDist: 6.0f);
            SpawnMages(mageCount, nearPoint: null, minPlayerDist: 7.0f);

            var lib = PvfxVfxLibrary.Instance;
            ShowBanner(
                $"WAVE {CurrentWave}",
                $"{slimeCount} Slimes · {mageCount} Elemental Mages",
                new Color(0.45f, 0.9f, 1f),
                2.4f,
                lib != null ? lib.waveStart : null);

            AudioManager.Instance.PlaySfx("rune_success", 0.8f, 0.02f);
        }

        private void StartBossPhase()
        {
            CurrentPhase = WavePhase.BossPhase;

            var player = FindFirstObjectByType<PlayerController2D>();
            Vector3 spawnPos = PickBossSpawnPosition(player != null ? player.transform.position : Vector3.zero);

            var lib = PvfxVfxLibrary.Instance;
            if (lib != null && lib.bossPortal != null)
            {
                SpellImpactVfx.Spawn(spawnPos, lib.bossPortal, 15f, 2.4f);
            }

            var bossGo = new GameObject($"MinotaurBoss_Wave{CurrentWave}");
            bossGo.transform.position = spawnPos;
            var boss = bossGo.AddComponent<MinotaurBossEnemy2D>();
            boss.ConfigureForWave(CurrentWave, this, asEcho: false);
            RegisterSpawnedEnemy(bossGo);

            // Escort enemies around the boss scale with wave difficulty!
            int escortSlimes = 1 + CurrentWave;
            int escortMages = CurrentWave >= 2 ? Mathf.Min(4, CurrentWave - 1) : 0;

            SpawnSlimes(escortSlimes, nearPoint: (Vector2)spawnPos, minPlayerDist: 3.0f);
            if (escortMages > 0)
            {
                SpawnMages(escortMages, nearPoint: (Vector2)spawnPos, minPlayerDist: 3.5f);
            }

            ShowBanner(
                $"WAVE {CurrentWave} BOSS: MINOTAUR!",
                $"Escort: {escortSlimes} Slimes" + (escortMages > 0 ? $" · {escortMages} Mages" : ""),
                new Color(1f, 0.35f, 0.25f),
                2.8f,
                lib != null ? lib.bossAnnounce : null);

            AudioManager.Instance.PlaySfx("player_respawn", 0.9f, -0.3f);
        }

        private void CompleteWaveAndStartIntermission()
        {
            CurrentPhase = WavePhase.Intermission;
            _intermissionTimer = intermissionSeconds;

            var player = FindFirstObjectByType<PlayerController2D>();
            if (player != null)
            {
                float heal = player.maxHealth * 0.30f;
                player.currentHealth = Mathf.Min(player.maxHealth, player.currentHealth + heal);
                player.SpawnHeadPopup($"+{Mathf.RoundToInt(heal)} HP!", new Color(0.45f, 1f, 0.55f), 16);

                var libHeal = PvfxVfxLibrary.Instance;
                if (libHeal != null && libHeal.playerHeal != null)
                {
                    SpellImpactVfx.Spawn(player.transform.position, libHeal.playerHeal, 15f, 1.8f);
                }
            }

            var lib = PvfxVfxLibrary.Instance;
            ShowBanner(
                $"WAVE {CurrentWave} CLEARED!",
                "Player healed +30% HP · Next wave incoming...",
                new Color(0.45f, 1f, 0.55f),
                intermissionSeconds - 0.3f,
                lib != null ? lib.playerHeal : null);

            AudioManager.Instance.PlaySfx("player_respawn", 0.85f, 0.05f);
        }

        private Vector3 PickBossSpawnPosition(Vector3 playerPos)
        {
            if (Vector2.Distance(playerPos, bossGladeCenter) >= 6.5f)
            {
                return new Vector3(bossGladeCenter.x, bossGladeCenter.y, 0f);
            }
            return new Vector3(-12f, 0f, 0f);
        }

        private void SpawnSlimes(int count, Vector2? nearPoint, float minPlayerDist)
        {
            string[] controllerPaths =
            {
                "Animations/Slime_Green",
                "Animations/Slime_Blue",
                "Animations/Slime_Red"
            };

            var controllers = new List<RuntimeAnimatorController>();
            foreach (string p in controllerPaths)
            {
                var ctrl = Resources.Load<RuntimeAnimatorController>(p);
                if (ctrl != null) controllers.Add(ctrl);
            }
            if (controllers.Count == 0) return;

            var player = FindFirstObjectByType<PlayerController2D>();
            Vector2 playerPos = player != null ? (Vector2)player.transform.position : Vector2.zero;

            for (int i = 0; i < count; i++)
            {
                Vector2 pos = FindValidSpawnPos(playerPos, minPlayerDist, nearPoint);
                var slimeGo = new GameObject($"Wave{CurrentWave}_Slime_{i + 1}");
                slimeGo.transform.position = new Vector3(pos.x, pos.y, 0f);

                var slime = slimeGo.AddComponent<SlimeEnemy2D>();
                slime.maxHealth = 60f + (CurrentWave - 1) * 14f;
                slime.currentHealth = slime.maxHealth;
                slime.animatorController = controllers[i % controllers.Count];
                var anim = slimeGo.GetComponent<Animator>();
                if (anim != null) anim.runtimeAnimatorController = slime.animatorController;

                RegisterSpawnedEnemy(slimeGo);
            }
        }

        private void SpawnMages(int count, Vector2? nearPoint, float minPlayerDist)
        {
            MageElement[] elements =
            {
                MageElement.Fire,
                MageElement.Lightning,
                MageElement.Water,
                MageElement.Darkness
            };

            var player = FindFirstObjectByType<PlayerController2D>();
            Vector2 playerPos = player != null ? (Vector2)player.transform.position : Vector2.zero;

            for (int i = 0; i < count; i++)
            {
                Vector2 pos = FindValidSpawnPos(playerPos, minPlayerDist, nearPoint);
                MageElement elem = elements[(i + CurrentWave) % elements.Length];
                var mageGo = new GameObject($"Wave{CurrentWave}_Mage_{elem}_{i + 1}");
                mageGo.transform.position = new Vector3(pos.x, pos.y, 0f);

                var mage = mageGo.AddComponent<ElementalMageEnemy2D>();
                mage.InitializeElement(elem);
                mage.maxHealth = 110f + (CurrentWave - 1) * 25f;
                mage.currentHealth = mage.maxHealth;

                RegisterSpawnedEnemy(mageGo);
            }
        }

        private Vector2 FindValidSpawnPos(Vector2 playerPos, float minPlayerDist, Vector2? nearPoint)
        {
            float halfW = mapWidthTiles * 0.5f - 4f;
            float halfH = mapHeightTiles * 0.5f - 4f;

            for (int attempt = 0; attempt < 30; attempt++)
            {
                Vector2 candidate;
                if (nearPoint.HasValue)
                {
                    float ang = Random.Range(0f, Mathf.PI * 2f);
                    float rad = Random.Range(2.2f, 4.8f);
                    candidate = nearPoint.Value + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
                    candidate.x = Mathf.Clamp(candidate.x, -halfW, halfW);
                    candidate.y = Mathf.Clamp(candidate.y, -halfH, halfH);
                }
                else
                {
                    candidate = new Vector2(Random.Range(-halfW, halfW), Random.Range(-halfH, halfH));
                    float riverCenterX = 9f + Mathf.Sin(candidate.y * 0.18f) * 4.5f;
                    if (Mathf.Abs(candidate.x - riverCenterX) < 2.6f) continue;
                }

                if (Vector2.Distance(candidate, playerPos) >= minPlayerDist)
                {
                    return candidate;
                }
            }

            return new Vector2(Random.Range(-10f, 10f), Random.Range(-10f, 10f));
        }

        private void ShowBanner(string title, string sub, Color color, float duration, Sprite[] vfxFrames)
        {
            _bannerTitle = title;
            _bannerSub = sub;
            _bannerColor = color;
            _bannerDuration = Mathf.Max(0.5f, duration);
            _bannerTimer = _bannerDuration;
            _bannerVfxFrames = vfxFrames;
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawWaveHud();
            DrawAnnouncementBanner();
        }

        private void DrawWaveHud()
        {
            Rect box = new Rect(16f, 14f, 220f, 52f);
            Color prev = GUI.color;
            GUI.color = new Color(0.06f, 0.07f, 0.1f, 0.78f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = prev;

            string phaseText = CurrentPhase switch
            {
                WavePhase.WaveCombat => $"ENEMIES LEFT: {_activeEnemies.Count}",
                WavePhase.BossPhase => $"BOSS FIGHT · LEFT: {_activeEnemies.Count}",
                WavePhase.Intermission => $"NEXT WAVE IN: {Mathf.Max(0f, _intermissionTimer):0.0}s",
                _ => ""
            };

            GUI.Label(new Rect(box.x + 10f, box.y + 4f, box.width - 20f, 22f), $"WAVE {CurrentWave}", _hudTitleStyle);
            GUI.Label(new Rect(box.x + 10f, box.y + 26f, box.width - 20f, 20f), phaseText, _hudSubStyle);
        }

        private void DrawAnnouncementBanner()
        {
            if (_bannerTimer <= 0f || string.IsNullOrEmpty(_bannerTitle)) return;

            float elapsed = _bannerDuration - _bannerTimer;
            float alpha = _bannerTimer < 0.4f ? Mathf.Clamp01(_bannerTimer / 0.4f) : 1f;

            float w = Mathf.Min(520f, Screen.width * 0.7f);
            float h = 76f;
            float x = (Screen.width - w) * 0.5f;
            float y = Screen.height * 0.18f;

            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.05f, 0.08f, 0.82f * alpha);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = prev;

            if (_bannerVfxFrames != null && _bannerVfxFrames.Length > 0)
            {
                int frameIdx = Mathf.FloorToInt(elapsed * 14f) % _bannerVfxFrames.Length;
                Sprite s = _bannerVfxFrames[frameIdx];
                PvfxVfxLibrary.DrawSpriteInGui(new Rect(x + 12f, y + 6f, 64f, 64f), s, new Color(1f, 1f, 1f, alpha));
                PvfxVfxLibrary.DrawSpriteInGui(new Rect(x + w - 76f, y + 6f, 64f, 64f), s, new Color(1f, 1f, 1f, alpha));
            }

            _bannerTitleStyle.normal.textColor = new Color(_bannerColor.r, _bannerColor.g, _bannerColor.b, alpha);
            _bannerSubStyle.normal.textColor = new Color(0.92f, 0.94f, 0.98f, alpha);

            GUI.Label(new Rect(x + 70f, y + 10f, w - 140f, 32f), _bannerTitle, _bannerTitleStyle);
            GUI.Label(new Rect(x + 70f, y + 42f, w - 140f, 24f), _bannerSub, _bannerSubStyle);
        }

        private void EnsureStyles()
        {
            if (_hudTitleStyle != null) return;

            _hudTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.86f, 0.45f) }
            };
            _hudSubStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.88f, 0.92f, 0.98f) }
            };
            _bannerTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _bannerSubStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
        }
    }
}
