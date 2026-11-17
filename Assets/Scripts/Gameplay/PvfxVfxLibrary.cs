using UnityEngine;

namespace Alchemy.Gameplay
{
    [CreateAssetMenu(fileName = "PvfxLibrary", menuName = "Alchemy/PVFX Library")]
    public class PvfxVfxLibrary : ScriptableObject
    {
        private static PvfxVfxLibrary _cached;
        private static bool _searched;

        public static PvfxVfxLibrary Instance
        {
            get
            {
                if (!_searched)
                {
                    _searched = true;
                    _cached = Resources.Load<PvfxVfxLibrary>("VFX/PvfxLibrary");
                }
                return _cached;
            }
        }

        public static void ClearCache()
        {
            _searched = false;
            _cached = null;
        }

        [Header("Wave & Boss Announcements")]
        public Sprite[] waveStart;
        public Sprite[] bossAnnounce;
        public Sprite[] bossPortal;
        public Sprite[] playerHeal;

        [Header("Minotaur & Combat VFX")]
        public Sprite[] axeCleave;
        public Sprite[] groundSlam;
        public Sprite[] shockwaveRing;
        public Sprite[] armorCrack;
        public Sprite[] bossHealAura;
        public Sprite[] bossEnrage;
        public Sprite[] stunInterrupt;

        public static void DrawSpriteInGui(Rect targetRect, Sprite sprite, Color? tint = null)
        {
            if (sprite == null || sprite.texture == null) return;

            Texture2D tex = sprite.texture;
            Rect r = sprite.textureRect;
            Rect uv = new Rect(
                r.x / tex.width,
                r.y / tex.height,
                r.width / tex.width,
                r.height / tex.height);

            Color prev = GUI.color;
            if (tint.HasValue) GUI.color = tint.Value;
            GUI.DrawTextureWithTexCoords(targetRect, tex, uv, alphaBlend: true);
            GUI.color = prev;
        }
    }
}
