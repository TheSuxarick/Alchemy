using System;
using System.Collections.Generic;
using UnityEngine;

namespace Alchemy.SpellDrawing
{
    [Flags]
    public enum SpellAttributeFlags
    {
        None           = 0,
        FireBurn       = 1 << 0, // Ignites flammable objects (pixel-by-pixel burn) & burns enemies
        AirKnockback   = 1 << 1, // Throws enemies back & shakes trees/bushes
        WaterDrench    = 1 << 2, // Extinguishes fire & slows enemies
        EarthHeavy     = 1 << 3, // Heavy impact that can crack/shatter rocks
        LightningShock = 1 << 4, // Stuns enemies & zaps targets
        DarkVoid       = 1 << 5  // High damage / pulls inward
    }

    [Serializable]
    public class StrokeData
    {
        public List<Vector2> points = new List<Vector2>();
    }

    [Serializable]
    public class GlyphTemplate
    {
        public string label = "Primary";

        [Tooltip("Legacy single-stroke points (kept for backward compatibility).")]
        public List<Vector2> points = new List<Vector2>();

        [Tooltip("Multi-stroke rune data. Each stroke is normalized in shared [0..1] canvas space.")]
        public List<StrokeData> strokes = new List<StrokeData>();

        public List<List<Vector2>> GetStrokes()
        {
            var result = new List<List<Vector2>>();
            if (strokes != null && strokes.Count > 0)
            {
                for (int i = 0; i < strokes.Count; i++)
                {
                    if (strokes[i] != null && strokes[i].points != null && strokes[i].points.Count >= 2)
                    {
                        result.Add(strokes[i].points);
                    }
                }
            }

            if (result.Count == 0 && points != null && points.Count >= 4)
            {
                result.Add(points);
            }

            return result;
        }
    }

    [CreateAssetMenu(fileName = "NewSpell", menuName = "Alchemy/Spell Data", order = 1)]
    public class SpellData : ScriptableObject
    {
        [Header("Identity")]
        public string spellId = "fire";
        public string displayName = "Fire";
        [TextArea(2, 4)]
        public string description = "Base elemental spell.";
        public Color spellColor = new Color(1f, 0.35f, 0.15f, 1f);

        [Header("Wand & Casting")]
        public int maxCharges = 10;
        public bool pinnedToOverlay = true;

        [Header("Elemental Attributes & Effects")]
        public SpellAttributeFlags attributes = SpellAttributeFlags.None;
        [Range(0f, 30f)]
        public float knockbackForce = 5.5f;
        [Range(0f, 12f)]
        public float burnDuration = 4.0f;
        [Range(0f, 5f)]
        public float stunDuration = 0.0f;

        [Header("Spell Stats & Tuning")]
        [Range(3f, 35f)]
        public float projectileSpeed = 13.5f;
        [Range(0.25f, 3.0f)]
        public float projectileScale = 1.0f;
        [Range(0.3f, 5.0f)]
        public float projectileLifetime = 1.5f;
        [Range(1f, 200f)]
        public float baseDamage = 25f;

        [Header("Visuals & VFX (Flight + Impact)")]
        public Sprite icon;
        [Tooltip("Centered frames played while the projectile is flying.")]
        public Sprite[] projectileFrames;
        [Tooltip("One-shot explosion/splash/shatter frames played where the projectile hits or lands.")]
        public Sprite[] impactFrames;
        [Range(4f, 40f)]
        public float projectileFps = 14f;
        public bool tintProjectileBySpellColor = false;

        [Header("Alchemy Recipe (Optional Parents)")]
        public SpellData ingredientA;
        public SpellData ingredientB;

        [Header("Glyph Recognition")]
        [Tooltip("If true, drawing any stroke from end-to-start is also accepted.")]
        public bool allowReverseStroke = true;

        [Tooltip("If true, multi-stroke runes can be drawn in any stroke order.")]
        public bool allowAnyStrokeOrder = true;

        [Tooltip("One or more ideal rune examples for this spell. Each point is normalized in shared [0..1] canvas space.")]
        public List<GlyphTemplate> idealTemplates = new List<GlyphTemplate>();

        /// <summary>
        /// Optional virtual hook if you ever create a C# subclass of SpellData for unique spell logic.
        /// </summary>
        public virtual void OnCustomHit(GameObject target, Vector2 hitDirection, float boostMultiplier)
        {
        }

        public SpellAttributeFlags GetEffectiveAttributes()
        {
            if (attributes != SpellAttributeFlags.None) return attributes;

            // Auto-infer from spellId or ingredients if not explicitly set yet
            SpellAttributeFlags inferred = InferFromId(spellId);
            if (ingredientA != null) inferred |= InferFromId(ingredientA.spellId);
            if (ingredientB != null) inferred |= InferFromId(ingredientB.spellId);
            return inferred;
        }

        public static SpellAttributeFlags InferFromId(string id)
        {
            if (string.IsNullOrEmpty(id)) return SpellAttributeFlags.None;
            string lower = id.ToLowerInvariant();
            SpellAttributeFlags flags = SpellAttributeFlags.None;
            if (lower.Contains("fire") || lower.Contains("flame") || lower.Contains("lava"))
                flags |= SpellAttributeFlags.FireBurn;
            if (lower.Contains("air") || lower.Contains("wind") || lower.Contains("storm"))
                flags |= SpellAttributeFlags.AirKnockback;
            if (lower.Contains("water") || lower.Contains("ice") || lower.Contains("rain"))
                flags |= SpellAttributeFlags.WaterDrench;
            if (lower.Contains("earth") || lower.Contains("rock") || lower.Contains("boulder"))
                flags |= SpellAttributeFlags.EarthHeavy;
            if (lower.Contains("light") || lower.Contains("thunder") || lower.Contains("shock"))
                flags |= SpellAttributeFlags.LightningShock;
            if (lower.Contains("dark") || lower.Contains("shadow") || lower.Contains("void"))
                flags |= SpellAttributeFlags.DarkVoid;
            return flags;
        }

        public bool HasValidTemplate()
        {
            if (idealTemplates == null) return false;
            for (int i = 0; i < idealTemplates.Count; i++)
            {
                if (idealTemplates[i] != null && idealTemplates[i].GetStrokes().Count > 0)
                {
                    return true;
                }
            }
            return false;
        }

        public int GetPrimaryStrokeCount()
        {
            if (idealTemplates == null || idealTemplates.Count == 0 || idealTemplates[0] == null) return 0;
            return idealTemplates[0].GetStrokes().Count;
        }

        public void SetSingleIdealTemplate(List<Vector2> normalizedCanvasPoints, string templateLabel = "Ideal")
        {
            var multi = new List<List<Vector2>> { new List<Vector2>(normalizedCanvasPoints) };
            SetMultiStrokeIdealTemplate(multi, templateLabel);
        }

        public void SetMultiStrokeIdealTemplate(IReadOnlyList<List<Vector2>> normalizedCanvasStrokes, string templateLabel = "Ideal")
        {
            if (idealTemplates == null)
            {
                idealTemplates = new List<GlyphTemplate>();
            }
            idealTemplates.Clear();

            var template = new GlyphTemplate
            {
                label = templateLabel,
                strokes = new List<StrokeData>(),
                points = new List<Vector2>()
            };

            if (normalizedCanvasStrokes != null)
            {
                for (int i = 0; i < normalizedCanvasStrokes.Count; i++)
                {
                    if (normalizedCanvasStrokes[i] == null || normalizedCanvasStrokes[i].Count < 2) continue;
                    template.strokes.Add(new StrokeData { points = new List<Vector2>(normalizedCanvasStrokes[i]) });
                }

                if (template.strokes.Count == 1)
                {
                    template.points = new List<Vector2>(template.strokes[0].points);
                }
            }

            idealTemplates.Add(template);
        }
    }
}
