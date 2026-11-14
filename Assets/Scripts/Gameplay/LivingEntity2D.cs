using System.Collections.Generic;
using UnityEngine;

namespace Alchemy.Gameplay
{
    /// <summary>
    /// Base class for all living entities on the map (Player, Enemies, etc.).
    /// Provides shared health, mass, and floating overhead damage/status popups.
    /// </summary>
    public abstract class LivingEntity2D : MonoBehaviour
    {
        [Header("Living Entity Base")]
        public float maxHealth = 100f;
        public float currentHealth = 100f;
        public float mass = 1.0f;
        public float headPopupOffsetY = 0.68f;

        public float HealthNormalized => maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;

        public void SpawnDamagePopup(float damage, Color color, bool isCritOrBoosted = false)
        {
            int rounded = Mathf.Max(1, Mathf.RoundToInt(damage));
            string label = isCritOrBoosted ? $"-{rounded}!" : $"-{rounded}";
            int size = isCritOrBoosted ? 17 : 14;
            SpawnHeadPopup(label, color, size);
        }

        public void SpawnHeadPopup(string text, Color color, int fontSize = 14)
        {
            Vector3 worldPos = transform.position + new Vector3(Random.Range(-0.18f, 0.18f), headPopupOffsetY, 0f);
            FloatingPopupManager2D.Spawn(worldPos, text, color, fontSize);
        }
    }

    /// <summary>
    /// Renders crisp, lightweight floating numbers & short tags over entity heads in world space.
    /// </summary>
    public class FloatingPopupManager2D : MonoBehaviour
    {
        private class PopupEntry
        {
            public Vector3 worldPos;
            public Vector2 velocity;
            public string text;
            public Color color;
            public int fontSize;
            public float elapsed;
            public float duration;
        }

        private static FloatingPopupManager2D _instance;
        private readonly List<PopupEntry> _popups = new List<PopupEntry>();
        private GUIStyle _style;

        public static void Spawn(Vector3 worldPos, string text, Color color, int fontSize = 14, float duration = 0.75f)
        {
            if (_instance == null)
            {
                var go = new GameObject("FloatingPopupManager2D");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<FloatingPopupManager2D>();
            }

            _instance._popups.Add(new PopupEntry
            {
                worldPos = worldPos,
                velocity = new Vector2(Random.Range(-0.25f, 0.25f), 1.15f),
                text = text,
                color = color,
                fontSize = fontSize,
                elapsed = 0f,
                duration = Mathf.Max(0.25f, duration)
            });
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                var p = _popups[i];
                p.elapsed += dt;
                if (p.elapsed >= p.duration)
                {
                    _popups.RemoveAt(i);
                    continue;
                }

                p.worldPos += (Vector3)(p.velocity * dt);
                p.velocity = Vector2.Lerp(p.velocity, new Vector2(0f, 0.35f), dt * 4f);
            }
        }

        private void OnGUI()
        {
            if (_popups.Count == 0) return;
            Camera cam = Camera.main;
            if (cam == null) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            for (int i = 0; i < _popups.Count; i++)
            {
                var p = _popups[i];
                Vector3 screen = cam.WorldToScreenPoint(p.worldPos);
                if (screen.z <= 0f) continue;

                float guiX = screen.x;
                float guiY = Screen.height - screen.y;

                float t = p.elapsed / p.duration;
                float alpha = t < 0.6f ? 1f : Mathf.Clamp01(1f - (t - 0.6f) / 0.4f);

                _style.fontSize = p.fontSize;
                Rect rect = new Rect(guiX - 90f, guiY - 16f, 180f, 32f);

                // Crisp 1px dark outline so numbers pop clearly on any background
                _style.normal.textColor = new Color(0.04f, 0.05f, 0.08f, alpha * 0.95f);
                GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), p.text, _style);
                GUI.Label(new Rect(rect.x - 1f, rect.y + 1f, rect.width, rect.height), p.text, _style);
                GUI.Label(new Rect(rect.x + 1f, rect.y - 1f, rect.width, rect.height), p.text, _style);
                GUI.Label(new Rect(rect.x - 1f, rect.y - 1f, rect.width, rect.height), p.text, _style);

                _style.normal.textColor = new Color(p.color.r, p.color.g, p.color.b, alpha);
                GUI.Label(rect, p.text, _style);
            }
        }
    }
}
