using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Alchemy.Gameplay;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Alchemy.EditorTools
{
    [InitializeOnLoad]
    public static class EnemySetupUtility
    {
        private const string MarkerPath = "Assets/Resources/Animations/enemies_v3.txt";
        private const string AnimOutDir = "Assets/Resources/Animations";
        private const string ClipsOutDir = "Assets/Resources/Animations/Clips";
        private const string PvfxEffectsFolder = "Assets/Art/pvfx-foundry-thirteen-spritesheets/effects";
        private const string VfxAssetDir = "Assets/Resources/VFX";
        private const string VfxAssetPath = "Assets/Resources/VFX/PvfxLibrary.asset";

        static EnemySetupUtility()
        {
            if (!File.Exists(MarkerPath))
            {
                EditorApplication.delayCall += TryAutoSetup;
            }
        }

        private static void TryAutoSetup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.Exists(MarkerPath)) return;
            SetupAll();
        }

        [MenuItem("Tools/Alchemy/Setup Mages and Minotaur Boss")]
        public static void SetupAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            Directory.CreateDirectory(AnimOutDir);
            Directory.CreateDirectory(ClipsOutDir);
            AssetDatabase.Refresh();

            string[] mageElements = { "Fire", "Lightning", "Water", "Darkness" };
            foreach (string elem in mageElements)
            {
                SetupMageElement(elem);
            }

            SetupMinotaurBoss();
            SetupPvfxLibrary();

            File.WriteAllText(MarkerPath, "v3");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Alchemy] Elemental Mages, Minotaur Boss & PVFX Library configured.");
        }

        private static void SetupMageElement(string element)
        {
            string baseArt = $"Assets/Art/Enemies/Mage/{element}";
            if (!Directory.Exists(baseArt)) return;

            AnimationClip idleClip = BuildClipFromFolder($"{baseArt}/Idle", $"Mage_{element}_Idle", 7f, loop: true, ppu: 42f);
            AnimationClip walkClip = BuildClipFromFolder($"{baseArt}/Walk", $"Mage_{element}_Walk", 9f, loop: true, ppu: 42f);
            AnimationClip attackClip = BuildClipFromFolder($"{baseArt}/Attack", $"Mage_{element}_Attack", 10f, loop: false, ppu: 42f);

            string ctrlPath = $"{AnimOutDir}/Mage_{element}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;
            var idleState = sm.AddState("Idle");
            idleState.motion = idleClip;
            sm.defaultState = idleState;

            var walkState = sm.AddState("Walk");
            walkState.motion = walkClip;

            var attackState = sm.AddState("Attack");
            attackState.motion = attackClip;

            var toWalk = idleState.AddTransition(walkState);
            toWalk.AddCondition(AnimatorConditionMode.If, 0f, "IsMoving");
            toWalk.hasExitTime = false;
            toWalk.duration = 0.05f;

            var toIdle = walkState.AddTransition(idleState);
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsMoving");
            toIdle.hasExitTime = false;
            toIdle.duration = 0.05f;

            var anyToAttack = sm.AddAnyStateTransition(attackState);
            anyToAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            anyToAttack.hasExitTime = false;
            anyToAttack.duration = 0.04f;

            var attackToIdle = attackState.AddTransition(idleState);
            attackToIdle.hasExitTime = true;
            attackToIdle.exitTime = 0.95f;
            attackToIdle.duration = 0.05f;
        }

        private static void SetupMinotaurBoss()
        {
            const string minoArt = "Assets/Art/Enemies/Minotaur";
            if (!Directory.Exists(minoArt)) return;

            AnimationClip idleClip = BuildClipFromFolder($"{minoArt}/Idle", "Minotaur_Idle", 12f, loop: true, ppu: 48f);
            AnimationClip walkClip = BuildClipFromFolder($"{minoArt}/Walk", "Minotaur_Walk", 12f, loop: true, ppu: 48f);
            AnimationClip attackClip = BuildClipFromFolder($"{minoArt}/Attack", "Minotaur_Attack", 16f, loop: false, ppu: 48f);

            string ctrlPath = $"{AnimOutDir}/MinotaurBoss.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("MoveSpeedMult", AnimatorControllerParameterType.Float);

            var sm = controller.layers[0].stateMachine;
            var idleState = sm.AddState("Idle");
            idleState.motion = idleClip;
            sm.defaultState = idleState;

            var walkState = sm.AddState("Walk");
            walkState.motion = walkClip;
            walkState.speedParameterActive = true;
            walkState.speedParameter = "MoveSpeedMult";

            var attackState = sm.AddState("Attack");
            attackState.motion = attackClip;

            var toWalk = idleState.AddTransition(walkState);
            toWalk.AddCondition(AnimatorConditionMode.If, 0f, "IsMoving");
            toWalk.hasExitTime = false;
            toWalk.duration = 0.05f;

            var toIdle = walkState.AddTransition(idleState);
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsMoving");
            toIdle.hasExitTime = false;
            toIdle.duration = 0.05f;

            var anyToAttack = sm.AddAnyStateTransition(attackState);
            anyToAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            anyToAttack.hasExitTime = false;
            anyToAttack.duration = 0.04f;

            var attackToIdle = attackState.AddTransition(idleState);
            attackToIdle.hasExitTime = true;
            attackToIdle.exitTime = 0.96f;
            attackToIdle.duration = 0.05f;
        }

        private static AnimationClip BuildClipFromFolder(string folderPath, string clipName, float fps, bool loop, float ppu)
        {
            string[] files = Directory.GetFiles(folderPath, "*.png");
            System.Array.Sort(files, CompareByTrailingNumber);

            var sprites = new List<Sprite>(files.Length);
            foreach (string file in files)
            {
                string assetPath = file.Replace('\\', '/');
                ConfigurePixelSprite(assetPath, ppu);
                Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (s != null) sprites.Add(s);
            }

            var clip = new AnimationClip { frameRate = fps };
            if (sprites.Count > 0)
            {
                var binding = new EditorCurveBinding
                {
                    type = typeof(SpriteRenderer),
                    path = "",
                    propertyName = "m_Sprite"
                };

                var keyframes = new ObjectReferenceKeyframe[sprites.Count];
                for (int i = 0; i < sprites.Count; i++)
                {
                    keyframes[i] = new ObjectReferenceKeyframe
                    {
                        time = i / fps,
                        value = sprites[i]
                    };
                }
                AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);
            }

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            string clipPath = $"{ClipsOutDir}/{clipName}.anim";
            if (File.Exists(clipPath)) AssetDatabase.DeleteAsset(clipPath);
            AssetDatabase.CreateAsset(clip, clipPath);
            return clip;
        }

        private static int CompareByTrailingNumber(string a, string b)
        {
            int na = ExtractTrailingNumber(a);
            int nb = ExtractTrailingNumber(b);
            if (na != nb) return na.CompareTo(nb);
            return string.Compare(a, b, System.StringComparison.OrdinalIgnoreCase);
        }

        private static int ExtractTrailingNumber(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            Match m = Regex.Match(name, @"(\d+)$");
            return m.Success && int.TryParse(m.Groups[1].Value, out int val) ? val : 0;
        }

        private static void ConfigurePixelSprite(string assetPath, float ppu)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;

            bool dirty = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                dirty = true;
            }
            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                dirty = true;
            }
            if (!Mathf.Approximately(importer.spritePixelsPerUnit, ppu))
            {
                importer.spritePixelsPerUnit = ppu;
                dirty = true;
            }
            if (importer.filterMode != FilterMode.Point)
            {
                importer.filterMode = FilterMode.Point;
                dirty = true;
            }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                dirty = true;
            }

            if (dirty)
            {
                importer.SaveAndReimport();
            }
        }

        [System.Serializable]
        private class PvfxManifest
        {
            public PvfxFrame[] frames;
        }

        [System.Serializable]
        private class PvfxFrame
        {
            public PvfxRect sheet;
            public int[] trim_bounds;
        }

        [System.Serializable]
        private class PvfxRect
        {
            public int x;
            public int y;
            public int width;
            public int height;
        }

        private static void SetupPvfxLibrary()
        {
            Directory.CreateDirectory(VfxAssetDir);

            PvfxVfxLibrary lib = AssetDatabase.LoadAssetAtPath<PvfxVfxLibrary>(VfxAssetPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<PvfxVfxLibrary>();
                AssetDatabase.CreateAsset(lib, VfxAssetPath);
            }

            lib.waveStart = SlicePvfx("ui/level-up", "lit", "pvfx_wave_start_");
            lib.bossAnnounce = SlicePvfx("explosions/fire-nova", "lit", "pvfx_boss_nova_");
            lib.bossPortal = SlicePvfx("shadow/void-implosion", "lit", "pvfx_boss_portal_");
            lib.playerHeal = SlicePvfx("holy/holy-heal", "lit", "pvfx_player_heal_");

            lib.axeCleave = SlicePvfx("melee/axe-smear", "lit", "pvfx_axe_cleave_");
            lib.groundSlam = SlicePvfx("impacts/ground-slam", "lit", "pvfx_ground_slam_");
            lib.shockwaveRing = SlicePvfx("impacts/shockwave-ring", "lit", "pvfx_shockwave_ring_");
            lib.armorCrack = SlicePvfx("impacts/rock-shatter", "lit", "pvfx_armor_crack_");
            lib.bossHealAura = SlicePvfx("holy/regen-aura", "lit", "pvfx_boss_heal_");
            lib.bossEnrage = SlicePvfx("status/power-up-aura", "lit", "pvfx_boss_enrage_");
            lib.stunInterrupt = SlicePvfx("status/stun-stars", "lit", "pvfx_stun_stars_");

            EditorUtility.SetDirty(lib);
            PvfxVfxLibrary.ClearCache();
        }

        private static Sprite[] SlicePvfx(string effectRelPath, string tier, string prefix)
        {
            string sheetPath = $"{PvfxEffectsFolder}/{effectRelPath}/{tier}/grid/sprite-sheet.png";
            string manifestPath = $"{PvfxEffectsFolder}/{effectRelPath}/{tier}/grid/manifest.json";
            if (!File.Exists(sheetPath) || !File.Exists(manifestPath))
            {
                // Fallback to strict tier if lit is unavailable
                sheetPath = $"{PvfxEffectsFolder}/{effectRelPath}/strict/grid/sprite-sheet.png";
                manifestPath = $"{PvfxEffectsFolder}/{effectRelPath}/strict/grid/manifest.json";
                if (!File.Exists(sheetPath) || !File.Exists(manifestPath))
                {
                    Debug.LogWarning($"[Alchemy] Missing PVFX asset: {effectRelPath}");
                    return System.Array.Empty<Sprite>();
                }
            }

            var manifest = JsonUtility.FromJson<PvfxManifest>(File.ReadAllText(manifestPath));
            if (manifest == null || manifest.frames == null || manifest.frames.Length == 0)
            {
                return System.Array.Empty<Sprite>();
            }

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(sheetPath);
            if (tex == null) return System.Array.Empty<Sprite>();

            int texH = tex.height;
            var rects = new List<SpriteRect>(manifest.frames.Length);
            for (int i = 0; i < manifest.frames.Length; i++)
            {
                var f = manifest.frames[i];
                if (f.sheet == null) continue;

                int rx = f.sheet.x;
                int ry = f.sheet.y;
                int rw = f.sheet.width;
                int rh = f.sheet.height;

                if (f.trim_bounds != null && f.trim_bounds.Length == 4)
                {
                    int minX = Mathf.Clamp(f.trim_bounds[0], 0, f.sheet.width - 1);
                    int minY = Mathf.Clamp(f.trim_bounds[1], 0, f.sheet.height - 1);
                    int maxX = Mathf.Clamp(f.trim_bounds[2], minX + 1, f.sheet.width);
                    int maxY = Mathf.Clamp(f.trim_bounds[3], minY + 1, f.sheet.height);
                    int tw = maxX - minX;
                    int th = maxY - minY;
                    if (tw >= 2 && th >= 2)
                    {
                        rx = f.sheet.x + minX;
                        ry = f.sheet.y + minY;
                        rw = tw;
                        rh = th;
                    }
                }

                rects.Add(new SpriteRect
                {
                    name = $"{prefix}{i:D3}",
                    rect = new Rect(rx, texH - ry - rh, rw, rh),
                    pivot = new Vector2(0.5f, 0.5f),
                    alignment = SpriteAlignment.Center,
                    spriteID = GUID.Generate()
                });
            }

            var importer = AssetImporter.GetAtPath(sheetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.spritePixelsPerUnit = 24f;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;

                var factory = new SpriteDataProviderFactories();
                factory.Init();
                var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
                provider.InitSpriteEditorDataProvider();
                provider.SetSpriteRects(rects.ToArray());
                provider.Apply();
                importer.SaveAndReimport();
            }

            Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(sheetPath);
            var sprites = new List<Sprite>();
            foreach (Object obj in subAssets)
            {
                if (obj is Sprite s && s.name.StartsWith(prefix))
                {
                    sprites.Add(s);
                }
            }
            sprites.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
            return sprites.ToArray();
        }
    }
}
