using System.Collections.Generic;
using UnityEngine;

namespace Alchemy.Gameplay
{
    public class SimpleWorldGenerator2D : MonoBehaviour
    {
        [Header("Map Dimensions")]
        public int mapWidthTiles = 48;
        public int mapHeightTiles = 48;
        public int treeCount = 34;
        public int rockCount = 18;
        public int decorCount = 45;
        public int slimeCount = 8;
        public int mageCount = 4;
        public bool spawnMinotaurBoss = true;
        public bool useWaveSystem = true;
        public int randomSeed = 42;

        private static readonly Vector2 BossGladeCenter = new Vector2(16.5f, 0f);
        private const float BossGladeRadius = 5.5f;

        private Sprite _worldGroundSprite;
        private Sprite _fallbackTreeSprite;
        private Sprite _fallbackRockSprite;

        private readonly List<Sprite> _sproutTrees = new List<Sprite>();
        private readonly List<Sprite> _sproutRocks = new List<Sprite>();
        private readonly List<Sprite> _sproutDecor = new List<Sprite>();

        private void Awake()
        {
            LoadSproutLandsSprites();
            GenerateWorld();
        }

        private void LoadSproutLandsSprites()
        {
            _sproutTrees.Clear();
            _sproutRocks.Clear();
            _sproutDecor.Clear();

            Sprite[] loaded = Resources.LoadAll<Sprite>("Environment");
            if (loaded == null) return;

            for (int i = 0; i < loaded.Length; i++)
            {
                Sprite s = loaded[i];
                if (s == null) continue;
                string n = s.name.ToLowerInvariant();
                if (n.Contains("tree")) _sproutTrees.Add(s);
                else if (n.Contains("rock")) _sproutRocks.Add(s);
                else if (n.Contains("bush") || n.Contains("mushroom") || n.Contains("flower") || n.Contains("stump"))
                {
                    _sproutDecor.Add(s);
                }
            }
        }

        public void GenerateWorld()
        {
            LoadSproutLandsSprites();

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(transform.GetChild(i).gameObject);
            }

            Random.State prevState = Random.state;
            Random.InitState(randomSeed);

            // 1. Soft, seamless Sprout-Lands-palette meadow + winding river + wooden bridge
            const int ppu = 16;
            int texW = mapWidthTiles * ppu;
            int texH = mapHeightTiles * ppu;
            var groundTex = new Texture2D(texW, texH, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            // Soft Sprout Lands palette (seamless grass without harsh tile borders)
            Color grassBase = new Color(0.753f, 0.831f, 0.439f, 1f);   // #C0D470
            Color grassSoft = new Color(0.722f, 0.804f, 0.412f, 1f);   // subtle variation
            Color grassTuft = new Color(0.616f, 0.722f, 0.341f, 1f);   // soft blade
            Color grassFlower = new Color(0.945f, 0.882f, 0.545f, 1f); // tiny yellow blossom
            Color pathColor = new Color(0.847f, 0.725f, 0.525f, 1f);   // soft dirt path
            Color pathEdge = new Color(0.761f, 0.635f, 0.447f, 1f);
            Color waterDeep = new Color(0.451f, 0.761f, 0.827f, 1f);   // Sprout Lands teal-blue
            Color waterShallow = new Color(0.569f, 0.839f, 0.878f, 1f);
            Color sandBank = new Color(0.898f, 0.816f, 0.588f, 1f);

            var pixels = new Color[texW * texH];

            for (int py = 0; py < texH; py++)
            {
                float worldY = (py / (float)ppu) - mapHeightTiles * 0.5f;
                float riverCenterX = 9f + Mathf.Sin(worldY * 0.18f) * 4.5f;

                for (int px = 0; px < texW; px++)
                {
                    float worldX = (px / (float)ppu) - mapWidthTiles * 0.5f;
                    float distToRiver = Mathf.Abs(worldX - riverCenterX);
                    bool isHorizontalPath = Mathf.Abs(worldY) <= 1.1f && distToRiver > 2.2f;
                    bool isBridge = Mathf.Abs(worldY) <= 1.1f && distToRiver <= 2.2f;

                    // Soft organic noise instead of grid lines
                    int hash = (px * 73856093) ^ (py * 19349663);
                    Color c = ((hash & 7) == 0) ? grassSoft : grassBase;

                    if (((px * 31 + py * 17) % 137) == 0)
                    {
                        c = grassTuft;
                    }
                    else if (((px * 53 + py * 97) % 521) == 0)
                    {
                        c = grassFlower;
                    }

                    if (isBridge)
                    {
                        c = (py % 6 == 0) ? new Color(0.52f, 0.36f, 0.22f) : new Color(0.72f, 0.52f, 0.33f);
                    }
                    else if (distToRiver < 1.55f)
                    {
                        c = distToRiver < 0.9f ? waterDeep : waterShallow;
                        if (((px + py * 2) % 29) == 0 && (px % 4 < 2))
                        {
                            c = Color.Lerp(c, Color.white, 0.35f);
                        }
                    }
                    else if (distToRiver < 2.05f)
                    {
                        c = sandBank;
                    }
                    else if (isHorizontalPath)
                    {
                        c = Mathf.Abs(worldY) > 0.92f ? pathEdge : pathColor;
                    }

                    float distToBossGlade = Vector2.Distance(new Vector2(worldX, worldY), BossGladeCenter);
                    if (distToBossGlade <= BossGladeRadius && distToRiver >= 2.1f)
                    {
                        c = distToBossGlade > BossGladeRadius - 0.55f
                            ? new Color(0.56f, 0.54f, 0.44f, 1f)
                            : Color.Lerp(c, new Color(0.64f, 0.62f, 0.46f, 1f), 0.45f);
                    }

                    pixels[py * texW + px] = c;
                }
            }

            groundTex.SetPixels(pixels);
            groundTex.Apply();
            _worldGroundSprite = Sprite.Create(
                groundTex,
                new Rect(0, 0, texW, texH),
                new Vector2(0.5f, 0.5f),
                ppu);

            var groundGo = new GameObject("PixelGround");
            groundGo.transform.SetParent(transform, false);
            groundGo.transform.localPosition = Vector3.zero;
            var groundSr = groundGo.AddComponent<SpriteRenderer>();
            groundSr.sprite = _worldGroundSprite;
            groundSr.sortingOrder = -10000;

            // 2. Scatter Sprout Lands trees, rocks, and soft meadow decor
            if (_sproutTrees.Count == 0) _fallbackTreeSprite = CreatePixelTreeSprite();
            if (_sproutRocks.Count == 0) _fallbackRockSprite = CreatePixelRockSprite();

            SpawnProps("Tree", _sproutTrees, _fallbackTreeSprite, treeCount, 0.42f, 1.35f, addCollider: true);
            SpawnProps("Rock", _sproutRocks, _fallbackRockSprite, rockCount, 0.32f, 1.0f, addCollider: true);
            if (_sproutDecor.Count > 0)
            {
                SpawnProps("Decor", _sproutDecor, null, decorCount, 0.2f, 1.0f, addCollider: false);
            }

            // 3. Spawn enemies via infinite WaveManager2D in Play Mode (or static preview)
            if (useWaveSystem && Application.isPlaying)
            {
                var waveMgr = GetComponent<WaveManager2D>();
                if (waveMgr == null) waveMgr = gameObject.AddComponent<WaveManager2D>();
                waveMgr.Initialize(mapWidthTiles, mapHeightTiles, BossGladeCenter);
            }
            else if (!useWaveSystem)
            {
                SpawnSlimes(slimeCount);
                SpawnElementalMages(mageCount);
                if (spawnMinotaurBoss)
                {
                    SpawnMinotaurBoss();
                }
            }

            Random.state = prevState;
        }

        private void SpawnProps(
            string prefix,
            List<Sprite> spritePool,
            Sprite fallback,
            int count,
            float colliderRadius,
            float scale,
            bool addCollider)
        {
            int spawned = 0;
            int attempts = 0;
            float halfW = mapWidthTiles * 0.5f - 3f;
            float halfH = mapHeightTiles * 0.5f - 3f;

            while (spawned < count && attempts < count * 12)
            {
                attempts++;
                Vector2 pos = new Vector2(Random.Range(-halfW, halfW), Random.Range(-halfH, halfH));

                if (pos.magnitude < 4.0f || Mathf.Abs(pos.y) < 2.2f) continue;
                if (Vector2.Distance(pos, BossGladeCenter) < BossGladeRadius + 0.6f) continue;

                float riverCenterX = 9f + Mathf.Sin(pos.y * 0.18f) * 4.5f;
                if (Mathf.Abs(pos.x - riverCenterX) < 3.0f) continue;

                Sprite chosen = (spritePool != null && spritePool.Count > 0)
                    ? spritePool[Random.Range(0, spritePool.Count)]
                    : fallback;
                if (chosen == null) continue;

                var prop = new GameObject($"{prefix}_{spawned + 1}");
                prop.transform.SetParent(transform, false);
                prop.transform.position = new Vector3(pos.x, pos.y, 0f);
                prop.transform.localScale = Vector3.one * scale;

                var sr = prop.AddComponent<SpriteRenderer>();
                sr.sprite = chosen;
                sr.sortingOrder = addCollider ? Mathf.RoundToInt(-pos.y * 10f) : -9000;

                var col = prop.AddComponent<CircleCollider2D>();
                col.radius = colliderRadius;
                col.isTrigger = !addCollider;

                var interactable = prop.AddComponent<InteractableEntity2D>();
                bool isRock = prefix == "Rock";
                bool isTree = prefix == "Tree";
                interactable.isFlammable = !isRock;
                interactable.isShatterable = true;
                interactable.canBePushed = !isTree; // Rocks & small props can be thrown by Air! Trees sway in place.
                interactable.mass = isTree ? 4.5f : (isRock ? 1.6f : 0.75f);
                interactable.durability = isTree ? 75f : (isRock ? 45f : 25f);

                spawned++;
            }
        }

        private void SpawnSlimes(int count)
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

            int spawned = 0;
            int attempts = 0;
            float halfW = mapWidthTiles * 0.5f - 5f;
            float halfH = mapHeightTiles * 0.5f - 5f;

            while (spawned < count && attempts < count * 12)
            {
                attempts++;
                Vector2 pos = new Vector2(Random.Range(-halfW, halfW), Random.Range(-halfH, halfH));
                if (pos.magnitude < 5.5f) continue;
                if (Vector2.Distance(pos, BossGladeCenter) < BossGladeRadius + 0.8f) continue;

                float riverCenterX = 9f + Mathf.Sin(pos.y * 0.18f) * 4.5f;
                if (Mathf.Abs(pos.x - riverCenterX) < 3.0f) continue;

                var slimeGo = new GameObject($"Slime_{spawned + 1}");
                slimeGo.transform.SetParent(transform, false);
                slimeGo.transform.position = new Vector3(pos.x, pos.y, 0f);

                var slime = slimeGo.AddComponent<SlimeEnemy2D>();
                slime.animatorController = controllers[spawned % controllers.Count];
                var anim = slimeGo.GetComponent<Animator>();
                if (anim != null) anim.runtimeAnimatorController = slime.animatorController;

                spawned++;
            }
        }

        private void SpawnElementalMages(int count)
        {
            if (count <= 0) return;

            MageElement[] elements =
            {
                MageElement.Fire,
                MageElement.Lightning,
                MageElement.Water,
                MageElement.Darkness
            };

            int spawned = 0;
            int attempts = 0;
            float halfW = mapWidthTiles * 0.5f - 5f;
            float halfH = mapHeightTiles * 0.5f - 5f;

            while (spawned < count && attempts < count * 16)
            {
                attempts++;
                Vector2 pos = new Vector2(Random.Range(-halfW, halfW), Random.Range(-halfH, halfH));
                if (pos.magnitude < 6.5f) continue;
                if (Vector2.Distance(pos, BossGladeCenter) < BossGladeRadius + 1.2f) continue;

                float riverCenterX = 9f + Mathf.Sin(pos.y * 0.18f) * 4.5f;
                if (Mathf.Abs(pos.x - riverCenterX) < 3.0f) continue;

                MageElement elem = elements[spawned % elements.Length];
                var mageGo = new GameObject($"Mage_{elem}_{spawned + 1}");
                mageGo.transform.SetParent(transform, false);
                mageGo.transform.position = new Vector3(pos.x, pos.y, 0f);

                var mage = mageGo.AddComponent<ElementalMageEnemy2D>();
                mage.InitializeElement(elem);
                spawned++;
            }
        }

        private void SpawnMinotaurBoss()
        {
            var bossGo = new GameObject("MinotaurBoss");
            bossGo.transform.SetParent(transform, false);
            bossGo.transform.position = new Vector3(BossGladeCenter.x, BossGladeCenter.y, 0f);
            bossGo.AddComponent<MinotaurBossEnemy2D>();
        }

        private static Sprite CreatePixelTreeSprite()
        {
            const int w = 24;
            const int h = 32;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            Color trunk = new Color(0.42f, 0.26f, 0.14f, 1f);
            Color leafDark = new Color(0.14f, 0.36f, 0.16f, 1f);
            Color leafMid = new Color(0.20f, 0.48f, 0.21f, 1f);
            Color leafLight = new Color(0.28f, 0.60f, 0.26f, 1f);
            Color shadow = new Color(0f, 0f, 0f, 0.28f);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color c = Color.clear;
                    if (Vector2.Distance(new Vector2(x, y * 1.8f), new Vector2(11.5f, 6f)) <= 7.5f) c = shadow;
                    if (y >= 3 && y <= 12 && x >= 9 && x <= 14) c = trunk;
                    if (Vector2.Distance(new Vector2(x, y), new Vector2(11.5f, 19.5f)) <= 9.2f)
                    {
                        if (y > 22 && x < 14) c = leafLight;
                        else if (y > 16) c = leafMid;
                        else c = leafDark;
                    }
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.2f), 16f);
        }

        private static Sprite CreatePixelRockSprite()
        {
            const int size = 20;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            Color rockDark = new Color(0.36f, 0.38f, 0.42f, 1f);
            Color rockMid = new Color(0.50f, 0.53f, 0.58f, 1f);
            Color rockLight = new Color(0.66f, 0.70f, 0.75f, 1f);
            Vector2 center = new Vector2(9.5f, 8.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y * 1.25f), center);
                    if (d <= 7.2f)
                        tex.SetPixel(x, y, (y > 9 && x < 11) ? rockLight : (y > 5 ? rockMid : rockDark));
                    else
                        tex.SetPixel(x, y, Color.clear);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.35f), 16f);
        }
    }
}
