using Alchemy.SpellDrawing;
using UnityEditor;
using UnityEngine;

namespace Alchemy.Editor
{
    public static class AudioSetupUtility
    {
        [MenuItem("Tools/Alchemy/Assign Default Spell Audio")]
        public static void AssignDefaultSpellAudio()
        {
            AssetDatabase.Refresh();

            string[] guids = AssetDatabase.FindAssets("t:SpellData", new[] { "Assets/Resources/Spells" });
            int updated = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                SpellData spell = AssetDatabase.LoadAssetAtPath<SpellData>(path);
                if (spell == null || string.IsNullOrEmpty(spell.spellId)) continue;

                string id = spell.spellId.Trim().ToLowerInvariant();
                AudioClip cast = Resources.Load<AudioClip>($"Audio/SFX/spell_{id}_cast");
                AudioClip impact = Resources.Load<AudioClip>($"Audio/SFX/spell_{id}_impact");

                bool changed = false;
                if (cast != null && spell.castClip != cast)
                {
                    spell.castClip = cast;
                    changed = true;
                }
                if (impact != null && spell.impactClip != impact)
                {
                    spell.impactClip = impact;
                    changed = true;
                }

                if (changed)
                {
                    EditorUtility.SetDirty(spell);
                    updated++;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"AudioSetupUtility: Assigned audio clips to {updated} SpellData asset(s).");
        }
    }
}
