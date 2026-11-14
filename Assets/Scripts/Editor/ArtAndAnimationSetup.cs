using System;
using System.Collections.Generic;
using System.IO;
using Alchemy.Gameplay;
using Alchemy.SpellDrawing;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Alchemy.EditorTools
{
    [InitializeOnLoad]
    public static class ArtAndAnimationSetup
    {
        private const string SproutCharSheetPath = "Assets/Art/Sprout Lands - Sprites - Basic pack/Sprout Lands - Sprites - Basic pack/Characters/Basic Charakter Spritesheet.png";
        private const string SlimeFolder = "Assets/Art/Slimes";
        private const string PvfxEffectsFolder = "Assets/Art/pvfx-foundry-thirteen-spritesheets/effects";
        private const string SproutObjectsSource = "Assets/Art/Sprout Lands - Sprites - Basic pack/Sprout Lands - Sprites - Basic pack/Objects/Basic_Grass_Biom_things.png";
        private const string SproutObjectsDest = "Assets/Resources/Environment/SproutProps.png";
        private const string SetupMarkerPath = "Assets/Resources/Animations/setup_v7.txt";

        static ArtAndAnimationSetup()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            if (!File.Exists(SetupMarkerPath))
            {
                EditorApplication.delayCall += TryAutoRunInEditMode;
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode && !File.Exists(SetupMarkerPath))
            {
                EditorApplication.delayCall += TryAutoRunInEditMode;
            }
        }

        private static void TryAutoRunInEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.Exists(SetupMarkerPath)) return;
            RunFullSetup();
        }

        public static void RunFullSetup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Alchemy] Please exit Play Mode first before running Setup Art & Animations!");
                return;
            }

            EnsureDirectories();

            SetupSproutPlayer();
            SetupSlimes();
            SetupPvfxSpells();
            SetupSproutEnvironment();

            File.WriteAllText(SetupMarkerPath, "v7");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            RefreshActiveSceneVisuals();
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("[Alchemy] Art, Animations, Water Projectile & Overhead Popups updated (v7)!");
        }

        private static void RefreshActiveSceneVisuals()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            var worldGen = UnityEngine.Object.FindFirstObjectByType<SimpleWorldGenerator2D>();
            if (worldGen != null)
            {
                worldGen.GenerateWorld();
                EditorUtility.SetDirty(worldGen);
            }

            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController2D>();
            if (player != null)
            {
                player.BuildCharacterAndWandVisuals();
                EditorUtility.SetDirty(player);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            SceneView.RepaintAll();
        }

        private static void EnsureDirectories()
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Resources/Animations");
            EnsureFolder("Assets/Resources/Animations/Clips");
            EnsureFolder("Assets/Resources/Environment");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            string name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(name))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }

        // ------------------------------------------------------------------
        // 1. SPROUT LANDS PLAYER (192x192 -> 48x48 grid, 4 cols x 4 rows)
        // ------------------------------------------------------------------
        private static void SetupSproutPlayer()
        {
            if (!File.Exists(SproutCharSheetPath))
            {
                Debug.LogWarning($"[Alchemy] Sprout character sheet not found at {SproutCharSheetPath}");
                return;
            }

            var rects = BuildGridRects("SproutPlayer", 192, 192, 48, 48, new Vector2(0.5f, 0.35f));
            SliceTexture(SproutCharSheetPath, rects, ppu: 16f);

            var sprites = LoadOrderedSprites(SproutCharSheetPath, "SproutPlayer_");
            if (sprites.Count < 16)
            {
                Debug.LogWarning($"[Alchemy] Expected 16 Sprout character frames, got {sprites.Count}");
                return;
            }

            // Row 0: Down, Row 1: Up, Row 2: Left, Row 3: Right (cols 0-1 Idle, cols 2-3 Walk)
            var idleDown = CreateClip("Sprout_Idle_Down", sprites, 0, 2, 4f, true);
            var idleUp = CreateClip("Sprout_Idle_Up", sprites, 4, 2, 4f, true);
            var idleLeft = CreateClip("Sprout_Idle_Left", sprites, 8, 2, 4f, true);
            var idleRight = CreateClip("Sprout_Idle_Right", sprites, 12, 2, 4f, true);

            var walkDown = CreateClip("Sprout_Walk_Down", sprites, 2, 2, 8f, true);
            var walkUp = CreateClip("Sprout_Walk_Up", sprites, 6, 2, 8f, true);
            var walkLeft = CreateClip("Sprout_Walk_Left", sprites, 10, 2, 8f, true);
            var walkRight = CreateClip("Sprout_Walk_Right", sprites, 14, 2, 8f, true);

            string ctrlPath = "Assets/Resources/Animations/Wizard.controller";
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            ctrl.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("MoveY", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);

            var root = ctrl.layers[0].stateMachine;
            var idleState = CreateDirectionalBlendState(ctrl, root, "Idle", idleDown, idleUp, idleLeft, idleRight);
            var walkState = CreateDirectionalBlendState(ctrl, root, "Walk", walkDown, walkUp, walkLeft, walkRight);
            root.defaultState = idleState;

            var toWalk = idleState.AddTransition(walkState);
            toWalk.hasExitTime = false;
            toWalk.duration = 0f;
            toWalk.AddCondition(AnimatorConditionMode.If, 0f, "IsMoving");

            var toIdle = walkState.AddTransition(idleState);
            toIdle.hasExitTime = false;
            toIdle.duration = 0f;
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsMoving");

            EditorUtility.SetDirty(ctrl);
        }

        // ------------------------------------------------------------------
        // 2. SLIMES (128x128 -> 32x32 grid, 4 cols x 4 rows)
        // ------------------------------------------------------------------
        private static void SetupSlimes()
        {
            SetupSingleSlime("Slime_Green", $"{SlimeFolder}/Slime_Medium_Green.png");
            SetupSingleSlime("Slime_Blue", $"{SlimeFolder}/Slime_Medium_Blue.png");
            SetupSingleSlime("Slime_Red", $"{SlimeFolder}/Slime_Medium_Red.png");
        }

        private static void SetupSingleSlime(string key, string texPath)
        {
            if (!File.Exists(texPath)) return;

            var rects = BuildGridRects(key, 128, 128, 32, 32, new Vector2(0.5f, 0.2f));
            SliceTexture(texPath, rects, ppu: 24f, makeReadable: true);

            var sprites = LoadOrderedSprites(texPath, $"{key}_");
            if (sprites.Count < 16) return;

            // Row 0: Down, Row 1: Right, Row 2: Up, Row 3: Left
            var down = CreateClip($"{key}_Down", sprites, 0, 4, 8f, true);
            var right = CreateClip($"{key}_Right", sprites, 4, 4, 8f, true);
            var up = CreateClip($"{key}_Up", sprites, 8, 4, 8f, true);
            var left = CreateClip($"{key}_Left", sprites, 12, 4, 8f, true);

            string ctrlPath = $"Assets/Resources/Animations/{key}.controller";
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            ctrl.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("MoveY", AnimatorControllerParameterType.Float);

            var root = ctrl.layers[0].stateMachine;
            root.defaultState = CreateDirectionalBlendState(ctrl, root, "Move", down, up, left, right);
            EditorUtility.SetDirty(ctrl);
        }

        // ------------------------------------------------------------------
        // 3. PVFX FOUNDRY SPELLS
        // ------------------------------------------------------------------
        [Serializable]
        private class PvfxManifest
        {
            public int[] source_size;
            public PvfxFrame[] frames;
        }

        [Serializable]
        private class PvfxFrame
        {
            public int frame;
            public int[] trim_bounds;
            public PvfxSheet sheet;
        }

        [Serializable]
        private class PvfxSheet
        {
            public int x;
            public int y;
            public int width;
            public int height;
        }

        private static void SetupPvfxSpells()
        {
            // Tightly trimmed & centered flight frames + one-shot impact frames + elemental attributes
            WireSpellVfx("Fire",      "fire/fireball-impact",      "lit",    15f, 1.0f, flightStart: 1, flightCount: 3, impactStart: 4, impactCount: 9,
                SpellAttributeFlags.FireBurn, knockback: 4.5f, burnSec: 4.5f, stunSec: 0f, tintByColor: false);
            // Use "projectiles/ice-shard" ("strict" tier) for Water: animated horizontal water/aqua bolt with trailing droplets + splash burst!
            WireSpellVfx("Water",     "projectiles/ice-shard",     "strict", 16f, 1.0f, flightStart: 1, flightCount: 4, impactStart: 6, impactCount: 8,
                SpellAttributeFlags.WaterDrench, knockback: 5.0f, burnSec: 0f, stunSec: 0f, tintByColor: false);
            WireSpellVfx("Air",       "wind/wind-blade",           "lit",    16f, 1.0f, flightStart: 1, flightCount: 3, impactStart: 4, impactCount: 8,
                SpellAttributeFlags.AirKnockback, knockback: 18.0f, burnSec: 0f, stunSec: 0f, tintByColor: false);
            WireSpellVfx("Earth",     "nature/boulder-toss",       "lit",    14f, 1.0f, flightStart: 1, flightCount: 2, impactStart: 3, impactCount: 9,
                SpellAttributeFlags.EarthHeavy | SpellAttributeFlags.AirKnockback, knockback: 12.0f, burnSec: 0f, stunSec: 0.35f, tintByColor: false);
            WireSpellVfx("Lightning", "lightning/ball-lightning",  "lit",    16f, 1.0f, flightStart: 2, flightCount: 3, impactStart: 5, impactCount: 9,
                SpellAttributeFlags.LightningShock, knockback: 5.5f, burnSec: 0f, stunSec: 1.2f, tintByColor: false);
            WireSpellVfx("Darkness",  "shadow/shadow-bolt",        "lit",    16f, 1.0f, flightStart: 2, flightCount: 3, impactStart: 5, impactCount: 9,
                SpellAttributeFlags.DarkVoid, knockback: 6.0f, burnSec: 2.0f, stunSec: 0.5f, tintByColor: false);
        }

        private static void WireSpellVfx(
            string spellResourceName,
            string effectRelPath,
            string tier,
            float fps,
            float scale,
            int flightStart,
            int flightCount,
            int impactStart,
            int impactCount,
            SpellAttributeFlags defaultAttrs,
            float knockback,
            float burnSec,
            float stunSec,
            bool tintByColor)
        {
            string sheetPath = $"{PvfxEffectsFolder}/{effectRelPath}/{tier}/grid/sprite-sheet.png";
            string manifestPath = $"{PvfxEffectsFolder}/{effectRelPath}/{tier}/grid/manifest.json";
            if (!File.Exists(sheetPath) || !File.Exists(manifestPath))
            {
                Debug.LogWarning($"[Alchemy] Missing PVFX asset: {sheetPath}");
                return;
            }

            var manifest = JsonUtility.FromJson<PvfxManifest>(File.ReadAllText(manifestPath));
            if (manifest == null || manifest.frames == null || manifest.frames.Length == 0) return;

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(sheetPath);
            if (tex == null) return;

            int texH = tex.height;
            string prefix = spellResourceName + "_vfx_";

            var rects = new List<SpriteRect>(manifest.frames.Length);
            for (int i = 0; i < manifest.frames.Length; i++)
            {
                var f = manifest.frames[i];
                if (f.sheet == null) continue;

                // PVFX manifest trim_bounds is [minX, minY, maxX, maxY] inside the frame cell!
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

            SliceTexture(sheetPath, rects, ppu: 24f);
            var sprites = LoadOrderedSprites(sheetPath, prefix);

            var spell = Resources.Load<SpellData>($"Spells/{spellResourceName}");
            if (spell != null && sprites.Count > 0)
            {
                int fStart = Mathf.Clamp(flightStart, 0, sprites.Count - 1);
                int fCount = Mathf.Clamp(flightCount, 1, sprites.Count - fStart);
                spell.projectileFrames = sprites.GetRange(fStart, fCount).ToArray();

                int iStart = Mathf.Clamp(impactStart, 0, sprites.Count - 1);
                int iCount = Mathf.Clamp(impactCount, 1, sprites.Count - iStart);
                spell.impactFrames = sprites.GetRange(iStart, iCount).ToArray();

                spell.projectileFps = fps;
                spell.projectileScale = scale;
                spell.tintProjectileBySpellColor = tintByColor;
                spell.attributes = defaultAttrs;
                spell.knockbackForce = knockback;
                spell.burnDuration = burnSec;
                spell.stunDuration = stunSec;
                EditorUtility.SetDirty(spell);
            }
        }

        // ------------------------------------------------------------------
        // 4. SPROUT LANDS ENVIRONMENT PROPS (Exact pixel-island bounds + readable for burning!)
        // ------------------------------------------------------------------
        private static void SetupSproutEnvironment()
        {
            if (!File.Exists(SproutObjectsSource)) return;

            File.Copy(SproutObjectsSource, SproutObjectsDest, overwrite: true);
            AssetDatabase.ImportAsset(SproutObjectsDest, ImportAssetOptions.ForceUpdate);

            // Exact connected-pixel bounding boxes in Basic_Grass_Biom_things.png (144x80, Unity Y=0 is bottom)
            var rects = new List<SpriteRect>
            {
                MakeRect("sprout_tree_tall",   1,  51, 14, 29, new Vector2(0.5f, 0.15f)),
                MakeRect("sprout_tree_oak",   20,  48, 24, 31, new Vector2(0.5f, 0.15f)),
                MakeRect("sprout_tree_apple", 52,  48, 24, 31, new Vector2(0.5f, 0.15f)),
                MakeRect("sprout_rock_small", 114, 54, 10,  8, new Vector2(0.5f, 0.25f)),
                MakeRect("sprout_rock_large", 128, 50, 16, 12, new Vector2(0.5f, 0.25f)),
                MakeRect("sprout_rock_big2",   80,  1, 16, 12, new Vector2(0.5f, 0.25f)),
                MakeRect("sprout_mushroom_1",  82, 65, 13, 13, new Vector2(0.5f, 0.2f)),
                MakeRect("sprout_mushroom_2", 100, 65, 10, 11, new Vector2(0.5f, 0.2f)),
                MakeRect("sprout_bush_1",       0, 16, 16, 16, new Vector2(0.5f, 0.2f)),
                MakeRect("sprout_bush_2",      16, 16, 16, 16, new Vector2(0.5f, 0.2f)),
                MakeRect("sprout_flower_y",   114, 34, 11,  9, new Vector2(0.5f, 0.2f)),
                MakeRect("sprout_flower_p",   114, 19, 11,  9, new Vector2(0.5f, 0.2f)),
                MakeRect("sprout_stump",       52, 34,  8, 10, new Vector2(0.5f, 0.2f)),
            };

            SliceTexture(SproutObjectsDest, rects, ppu: 16f, makeReadable: true);
        }

        // ------------------------------------------------------------------
        // HELPERS
        // ------------------------------------------------------------------
        private static SpriteRect MakeRect(string name, float x, float y, float w, float h, Vector2 pivot)
        {
            return new SpriteRect
            {
                name = name,
                rect = new Rect(x, y, w, h),
                pivot = pivot,
                alignment = SpriteAlignment.Custom,
                spriteID = GUID.Generate()
            };
        }

        private static List<SpriteRect> BuildGridRects(string prefix, int texW, int texH, int cellW, int cellH, Vector2 pivot)
        {
            int cols = texW / cellW;
            int rows = texH / cellH;
            var list = new List<SpriteRect>(cols * rows);
            int idx = 0;

            for (int r = 0; r < rows; r++)
            {
                int unityY = texH - (r + 1) * cellH;
                for (int c = 0; c < cols; c++)
                {
                    list.Add(new SpriteRect
                    {
                        name = $"{prefix}_{idx:D3}",
                        rect = new Rect(c * cellW, unityY, cellW, cellH),
                        pivot = pivot,
                        alignment = SpriteAlignment.Custom,
                        spriteID = GUID.Generate()
                    });
                    idx++;
                }
            }
            return list;
        }

        private static void SliceTexture(string assetPath, List<SpriteRect> rects, float ppu, bool makeReadable = false)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = ppu;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.isReadable = makeReadable;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
            dataProvider.InitSpriteEditorDataProvider();
            dataProvider.SetSpriteRects(rects.ToArray());
            dataProvider.Apply();

            importer.SaveAndReimport();
        }

        private static List<Sprite> LoadOrderedSprites(string assetPath, string namePrefix)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            var sprites = new List<Sprite>();
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite s && s.name.StartsWith(namePrefix, StringComparison.Ordinal))
                {
                    sprites.Add(s);
                }
            }
            sprites.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return sprites;
        }

        private static AnimationClip CreateClip(string clipName, List<Sprite> allSprites, int startIndex, int count, float fps, bool loop)
        {
            string clipPath = $"Assets/Resources/Animations/Clips/{clipName}.anim";
            var clip = new AnimationClip { frameRate = fps };

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var binding = EditorCurveBinding.PPtrCurve(string.Empty, typeof(SpriteRenderer), "m_Sprite");
            var keys = new ObjectReferenceKeyframe[count];
            for (int i = 0; i < count; i++)
            {
                keys[i] = new ObjectReferenceKeyframe
                {
                    time = i / fps,
                    value = allSprites[startIndex + i]
                };
            }
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

            if (File.Exists(clipPath))
            {
                AssetDatabase.DeleteAsset(clipPath);
            }
            AssetDatabase.CreateAsset(clip, clipPath);
            return clip;
        }

        private static AnimatorState CreateDirectionalBlendState(
            AnimatorController ctrl,
            AnimatorStateMachine sm,
            string stateName,
            AnimationClip down,
            AnimationClip up,
            AnimationClip left,
            AnimationClip right)
        {
            var state = sm.AddState(stateName);
            var tree = new BlendTree
            {
                name = stateName + "_Tree",
                blendType = BlendTreeType.SimpleDirectional2D,
                blendParameter = "MoveX",
                blendParameterY = "MoveY"
            };
            AssetDatabase.AddObjectToAsset(tree, ctrl);

            tree.AddChild(down, new Vector2(0f, -1f));
            tree.AddChild(up, new Vector2(0f, 1f));
            tree.AddChild(left, new Vector2(-1f, 0f));
            tree.AddChild(right, new Vector2(1f, 0f));

            state.motion = tree;
            return state;
        }
    }
}
