using Alchemy.Audio;
using Alchemy.SpellDrawing;
using UnityEngine;

namespace Alchemy.Gameplay
{
    [RequireComponent(typeof(CircleCollider2D))]
    public class SpellProjectile2D : MonoBehaviour
    {
        private Vector2 _direction;
        private float _speed;
        private float _maxLifetime = 1.5f;
        private float _elapsed;
        private float _damage = 25f;
        private float _boostMultiplier = 1f;
        private float _finalScale = 1f;
        private SpellData _sourceSpell;
        private bool _isEnemyProjectile;
        private bool _canBeDeflectedByAir;

        private SpriteRenderer _sr;
        private Sprite[] _flightFrames;
        private Sprite[] _impactFrames;
        private float _fps = 14f;
        private float _animTimer;
        private int _frameIndex;
        private bool _hasImpacted;

        private static Sprite _fallbackSprite;

        public static void SpawnFromSpell(
            Vector3 spawnPos,
            Vector2 direction,
            SpellData spell,
            float boostMultiplier)
        {
            if (spell == null) return;

            var proj = Spawn(
                spawnPos,
                direction,
                spell.spellColor,
                boostMultiplier,
                spell.projectileFrames,
                spell.impactFrames,
                spell.projectileFps,
                spell.projectileScale,
                spell.projectileSpeed,
                spell.projectileLifetime,
                spell.baseDamage,
                spell.tintProjectileBySpellColor);

            if (proj != null)
            {
                proj._sourceSpell = spell;
                proj._boostMultiplier = boostMultiplier;
            }
        }

        public static void SpawnEnemyFromSpell(
            Vector3 spawnPos,
            Vector2 direction,
            SpellData spell,
            float damageOverride = 12f,
            float speedScale = 0.7f)
        {
            if (spell == null) return;

            var proj = Spawn(
                spawnPos,
                direction,
                spell.spellColor,
                1f,
                spell.projectileFrames,
                spell.impactFrames,
                spell.projectileFps,
                spell.projectileScale * 0.9f,
                spell.projectileSpeed * speedScale,
                spell.projectileLifetime * 1.15f,
                damageOverride,
                spell.tintProjectileBySpellColor);

            if (proj != null)
            {
                proj._sourceSpell = spell;
                proj._isEnemyProjectile = true;
                proj._damage = damageOverride;
            }
        }

        public static void SpawnEnemySpore(
            Vector3 spawnPos,
            Vector2 direction,
            Color color,
            float speed = 6.5f,
            float damage = 14f,
            float scale = 0.72f)
        {
            var proj = Spawn(
                spawnPos,
                direction,
                color,
                1f,
                flightFrames: null,
                impactFrames: null,
                fps: 12f,
                baseScale: scale,
                baseSpeed: speed,
                lifetime: 2.4f,
                baseDamage: damage,
                tintBySpellColor: true);

            if (proj != null)
            {
                proj._isEnemyProjectile = true;
                proj._canBeDeflectedByAir = true;
                proj._damage = damage;
            }
        }

        public static SpellProjectile2D Spawn(
            Vector3 spawnPos,
            Vector2 direction,
            Color color,
            float boostMultiplier,
            Sprite[] flightFrames = null,
            Sprite[] impactFrames = null,
            float fps = 14f,
            float baseScale = 1f,
            float baseSpeed = 13.5f,
            float lifetime = 1.5f,
            float baseDamage = 25f,
            bool tintBySpellColor = false)
        {
            var go = new GameObject("SpellProjectile");
            go.transform.position = spawnPos;

            float boost = Mathf.Clamp(boostMultiplier, 0.8f, 2.0f);
            bool hasAnimatedFrames = flightFrames != null && flightFrames.Length > 0;

            float finalScale = hasAnimatedFrames
                ? baseScale * Mathf.Lerp(0.95f, 1.3f, Mathf.InverseLerp(1f, 1.75f, boost))
                : 0.45f * baseScale * boost;
            go.transform.localScale = new Vector3(finalScale, finalScale, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            if (hasAnimatedFrames)
            {
                sr.sprite = flightFrames[0];
                sr.color = tintBySpellColor ? Color.Lerp(Color.white, color, 0.65f) : Color.white;
            }
            else
            {
                EnsureFallbackSprite();
                sr.sprite = _fallbackSprite;
                sr.color = color;
            }
            sr.sortingOrder = 15;

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.28f;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;

            var proj = go.AddComponent<SpellProjectile2D>();
            proj._sr = sr;
            proj._flightFrames = flightFrames;
            proj._impactFrames = impactFrames;
            proj._fps = Mathf.Max(4f, fps);
            proj._finalScale = finalScale;
            proj._direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
            proj._speed = Mathf.Max(1f, baseSpeed) * Mathf.Lerp(1f, 1.25f, Mathf.InverseLerp(1f, 1.75f, boostMultiplier));
            proj._maxLifetime = Mathf.Max(0.15f, lifetime);
            proj._damage = Mathf.Max(1f, baseDamage) * boost;
            proj._boostMultiplier = boostMultiplier;

            float angle = Mathf.Atan2(proj._direction.y, proj._direction.x) * Mathf.Rad2Deg;
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            sr.flipY = Mathf.Abs(angle) > 90f;
            return proj;
        }

        private static void EnsureFallbackSprite()
        {
            if (_fallbackSprite != null) return;

            const int size = 12;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };

            Vector2 center = new Vector2(5.5f, 5.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), center);
                    if (d <= 3.5f)
                        tex.SetPixel(x, y, Color.white);
                    else if (d <= 5.2f)
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, 0.55f));
                    else
                        tex.SetPixel(x, y, Color.clear);
                }
            }
            tex.Apply();
            _fallbackSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
        }

        private void Update()
        {
            if (_hasImpacted) return;

            float dt = Time.deltaTime;
            transform.position += (Vector3)(_direction * (_speed * dt));

            if (_flightFrames != null && _flightFrames.Length > 1 && _sr != null)
            {
                _animTimer += dt;
                float frameDuration = 1f / _fps;
                while (_animTimer >= frameDuration)
                {
                    _animTimer -= frameDuration;
                    _frameIndex = (_frameIndex + 1) % _flightFrames.Length;
                    _sr.sprite = _flightFrames[_frameIndex];
                }
            }

            _elapsed += dt;
            if (_elapsed >= _maxLifetime)
            {
                TriggerImpactAndDestroy();
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_hasImpacted || other == null) return;

            if (_isEnemyProjectile)
            {
                if (_canBeDeflectedByAir)
                {
                    var otherProj = other.GetComponent<SpellProjectile2D>();
                    if (otherProj != null && !otherProj._isEnemyProjectile && otherProj._sourceSpell != null &&
                        (otherProj._sourceSpell.GetEffectiveAttributes() & SpellAttributeFlags.AirKnockback) != 0)
                    {
                        AudioManager.Instance.PlaySfx("env_bush_rustle", 0.7f, 0.1f);
                        TriggerImpactAndDestroy();
                        return;
                    }
                }

                var player = other.GetComponent<PlayerController2D>();
                if (player != null)
                {
                    player.TakeDamage(_damage, _direction);
                    TriggerImpactAndDestroy();
                }
                return;
            }

            var slime = other.GetComponent<SlimeEnemy2D>();
            if (slime != null)
            {
                slime.ApplySpellHit(_sourceSpell, _direction, _boostMultiplier);
                TriggerImpactAndDestroy();
                return;
            }

            var mage = other.GetComponent<ElementalMageEnemy2D>();
            if (mage != null)
            {
                mage.ApplySpellHit(_sourceSpell, _direction, _boostMultiplier);
                TriggerImpactAndDestroy();
                return;
            }

            var boss = other.GetComponent<MinotaurBossEnemy2D>();
            if (boss != null)
            {
                boss.ApplySpellHit(_sourceSpell, _direction, _boostMultiplier);
                TriggerImpactAndDestroy();
                return;
            }

            var interactable = other.GetComponent<InteractableEntity2D>();
            if (interactable != null)
            {
                interactable.ApplySpellHit(_sourceSpell, _direction, _boostMultiplier);
                TriggerImpactAndDestroy();
            }
        }

        private void TriggerImpactAndDestroy()
        {
            if (_hasImpacted) return;
            _hasImpacted = true;

            if (_sourceSpell != null && _sourceSpell.impactClip != null)
            {
                AudioManager.Instance.PlaySfx(_sourceSpell.impactClip, 0.85f, 0.08f);
            }
            else if (_sourceSpell != null && !string.IsNullOrEmpty(_sourceSpell.spellId))
            {
                AudioManager.Instance.PlaySfx($"spell_{_sourceSpell.spellId.Trim().ToLowerInvariant()}_impact", 0.85f, 0.08f);
            }

            if (_impactFrames != null && _impactFrames.Length > 0)
            {
                SpellImpactVfx.Spawn(transform.position, _impactFrames, _fps * 1.15f, _finalScale);
            }

            Destroy(gameObject);
        }
    }

    public class SpellImpactVfx : MonoBehaviour
    {
        private SpriteRenderer _sr;
        private Sprite[] _frames;
        private float _fps;
        private float _timer;
        private int _index;

        public static void Spawn(Vector3 position, Sprite[] frames, float fps, float scale)
        {
            if (frames == null || frames.Length == 0) return;

            var go = new GameObject("SpellImpactVFX");
            go.transform.position = position;
            go.transform.localScale = new Vector3(scale, scale, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = frames[0];
            sr.sortingOrder = 16;

            var vfx = go.AddComponent<SpellImpactVfx>();
            vfx._sr = sr;
            vfx._frames = frames;
            vfx._fps = Mathf.Max(6f, fps);
        }

        private void Update()
        {
            if (_frames == null || _frames.Length == 0)
            {
                Destroy(gameObject);
                return;
            }

            _timer += Time.deltaTime;
            float frameDuration = 1f / _fps;
            while (_timer >= frameDuration)
            {
                _timer -= frameDuration;
                _index++;
                if (_index >= _frames.Length)
                {
                    Destroy(gameObject);
                    return;
                }
                if (_sr != null) _sr.sprite = _frames[_index];
            }
        }
    }
}
