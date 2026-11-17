using Alchemy.Audio;
using Alchemy.SpellDrawing;
using UnityEngine;

namespace Alchemy.Gameplay
{
    public class InteractableEntity2D : MonoBehaviour
    {
        [Header("Physics & Mass")]
        public float mass = 1.5f;
        public bool canBePushed = true;

        [Header("Material & Durability")]
        public bool isFlammable = true;
        public bool isShatterable = true;
        public float durability = 45f;

        private SpriteRenderer _sr;
        private Texture2D _runtimeTex;
        private Color[] _originalPixels;
        private Color[] _workingPixels;
        private float[] _pixelBurnStage;
        private int _texW;
        private int _texH;

        private bool _isBurning;
        private float _burnTimeRemaining;
        private float _pixelStepTimer;

        // Stacking Wetness (0..1+) -> deeper blue tint & buffs Lightning damage/shatter
        private float _wetLevel;

        // Knockback velocity (for throwing rocks/props with Air) & wind sway
        private Vector2 _knockbackVel;
        private float _swayAngle;
        private float _swayVelocity;

        public float WetLevel => _wetLevel;

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
        }

        public void ApplySpellHit(SpellData spell, Vector2 hitDir, float boostMultiplier)
        {
            if (spell == null) return;
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();

            SpellAttributeFlags attrs = spell.GetEffectiveAttributes();
            float boost = Mathf.Clamp(boostMultiplier, 0.8f, 2.0f);
            Vector2 normDir = hitDir.sqrMagnitude > 0.0001f ? hitDir.normalized : Vector2.right;

            // 1. Mass-based Knockback (Air throws rocks & movable props; rooted trees sway!)
            float safeMass = Mathf.Max(0.25f, mass);
            if ((attrs & SpellAttributeFlags.AirKnockback) != 0)
            {
                float pushSpeed = (Mathf.Max(12f, spell.knockbackForce) * boost) / safeMass;
                if (canBePushed)
                {
                    _knockbackVel += normDir * pushSpeed;
                }

                float dirSign = Mathf.Abs(normDir.x) > 0.05f ? -Mathf.Sign(normDir.x) : 1f;
                _swayVelocity += (dirSign * Mathf.Max(10f, spell.knockbackForce) * 32f * boost) / safeMass;
            }
            else
            {
                if (canBePushed)
                {
                    _knockbackVel += normDir * ((spell.knockbackForce * 0.45f * boost) / safeMass);
                }
                float dirSign = Mathf.Abs(normDir.x) > 0.05f ? -Mathf.Sign(normDir.x) : 1f;
                _swayVelocity += (dirSign * 45f) / safeMass;
            }

            // 2. Water -> Extinguishes fire & stacks Wetness (bluish tint that buffs Lightning!)
            if ((attrs & SpellAttributeFlags.WaterDrench) != 0)
            {
                _isBurning = false;
                _burnTimeRemaining = 0f;
                _wetLevel = Mathf.Clamp(_wetLevel + 0.42f * boost, 0f, 1.5f);
            }

            // 3. Fire -> Dries wetness first; if dry and flammable, ignites pixel-by-pixel!
            if ((attrs & SpellAttributeFlags.FireBurn) != 0 && (attrs & SpellAttributeFlags.WaterDrench) == 0)
            {
                if (_wetLevel > 0.25f)
                {
                    _wetLevel = Mathf.Max(0f, _wetLevel - 0.55f);
                }
                else if (isFlammable)
                {
                    _wetLevel = 0f;
                    EnsureRuntimeBurnTexture();
                    _isBurning = true;
                    _burnTimeRemaining = Mathf.Max(_burnTimeRemaining, Mathf.Max(3.5f, spell.burnDuration) * boost);
                    SeedInitialFirePixels();
                    AudioManager.Instance.PlaySfx("env_ignite", 0.7f, 0.08f);
                }
            }

            // 4. Lightning / Earth -> Shatter objects! (Wetness heavily amplifies Lightning shatter!)
            bool isLightning = (attrs & SpellAttributeFlags.LightningShock) != 0;
            bool isEarth = (attrs & SpellAttributeFlags.EarthHeavy) != 0;
            if (isShatterable && (isLightning || isEarth))
            {
                float wetLightningBonus = isLightning ? (1f + _wetLevel * 1.75f) : 1f;
                float shatterDamage = spell.baseDamage * boost * 1.4f * wetLightningBonus;
                durability -= shatterDamage;

                if (durability <= 0f)
                {
                    AudioManager.Instance.PlaySfx("env_rock_crack", 0.85f, 0.08f);
                    PixelShatterVfx.Spawn(_sr, normDir, isLightning);
                    Destroy(gameObject);
                    return;
                }
                AudioManager.Instance.PlaySfx("env_rock_crack", 0.55f, 0.1f);
            }
            else if ((attrs & SpellAttributeFlags.AirKnockback) != 0)
            {
                AudioManager.Instance.PlaySfx("env_bush_rustle", 0.65f, 0.1f);
            }

            spell.OnCustomHit(gameObject, normDir, boost);
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // 1. Sliding knockback physics for thrown rocks / movable props
            if (canBePushed && _knockbackVel.sqrMagnitude > 0.01f)
            {
                Vector3 delta = (Vector3)(_knockbackVel * dt);
                transform.position += delta;
                if (_sr != null)
                {
                    _sr.sortingOrder = Mathf.RoundToInt(-transform.position.y * 10f);
                }

                // If a fast-flying thrown rock hits a Slime, damage & knock back the Slime!
                if (_knockbackVel.sqrMagnitude > 6.0f)
                {
                    Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, 0.45f);
                    for (int i = 0; i < hits.Length; i++)
                    {
                        var slime = hits[i].GetComponent<SlimeEnemy2D>();
                        if (slime != null)
                        {
                            slime.TakeDamage(18f * mass, _knockbackVel.normalized, _knockbackVel.magnitude * 0.8f);
                            _knockbackVel *= 0.4f;
                            break;
                        }
                    }
                }

                // Ground friction
                _knockbackVel = Vector2.Lerp(_knockbackVel, Vector2.zero, dt * 6.5f);
            }

            // 2. Spring sway recovery
            if (Mathf.Abs(_swayAngle) > 0.01f || Mathf.Abs(_swayVelocity) > 0.01f)
            {
                _swayVelocity += (-_swayAngle * 180f - _swayVelocity * 10f) * dt;
                _swayAngle = Mathf.Clamp(_swayAngle + _swayVelocity * dt, -28f, 28f);
                transform.localRotation = Quaternion.Euler(0f, 0f, _swayAngle);
            }
            else if (_swayAngle != 0f)
            {
                _swayAngle = 0f;
                transform.localRotation = Quaternion.identity;
            }

            // 3. Gradual drying of Wetness & bluish tint
            if (_wetLevel > 0f)
            {
                _wetLevel = Mathf.Max(0f, _wetLevel - dt * 0.07f);
                if (_sr != null)
                {
                    float blueBlend = Mathf.Clamp01(_wetLevel) * 0.65f;
                    _sr.color = Color.Lerp(Color.white, new Color(0.35f, 0.68f, 1f, 1f), blueBlend);
                }
            }
            else if (_sr != null && _sr.color != Color.white)
            {
                _sr.color = Color.white;
            }

            // 4. Pixel-by-pixel burning simulation
            if (_isBurning)
            {
                _burnTimeRemaining -= dt;
                if (_burnTimeRemaining <= 0f)
                {
                    _isBurning = false;
                }

                _pixelStepTimer -= dt;
                if (_pixelStepTimer <= 0f)
                {
                    _pixelStepTimer = 0.08f;
                    StepPixelBurn();
                }
            }
        }

        private void EnsureRuntimeBurnTexture()
        {
            if (_runtimeTex != null || _sr == null || _sr.sprite == null) return;

            Sprite s = _sr.sprite;
            Texture2D srcTex = s.texture;
            if (srcTex == null || !srcTex.isReadable) return;

            Rect r = s.rect;
            _texW = Mathf.RoundToInt(r.width);
            _texH = Mathf.RoundToInt(r.height);
            int rx = Mathf.RoundToInt(r.x);
            int ry = Mathf.RoundToInt(r.y);

            _originalPixels = srcTex.GetPixels(rx, ry, _texW, _texH);
            _workingPixels = new Color[_originalPixels.Length];
            _pixelBurnStage = new float[_originalPixels.Length];
            System.Array.Copy(_originalPixels, _workingPixels, _originalPixels.Length);

            _runtimeTex = new Texture2D(_texW, _texH, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _runtimeTex.SetPixels(_workingPixels);
            _runtimeTex.Apply();

            Vector2 normalizedPivot = new Vector2(s.pivot.x / r.width, s.pivot.y / r.height);
            _sr.sprite = Sprite.Create(_runtimeTex, new Rect(0, 0, _texW, _texH), normalizedPivot, s.pixelsPerUnit);
        }

        private void SeedInitialFirePixels()
        {
            if (_pixelBurnStage == null) return;

            int ignited = 0;
            for (int attempt = 0; attempt < 60 && ignited < 6; attempt++)
            {
                int idx = Random.Range(0, _pixelBurnStage.Length);
                if (_originalPixels[idx].a > 0.1f && _pixelBurnStage[idx] <= 0.01f)
                {
                    _pixelBurnStage[idx] = 0.08f;
                    ignited++;
                }
            }
        }

        private void StepPixelBurn()
        {
            if (_runtimeTex == null || _pixelBurnStage == null) return;

            int remainingAlivePixels = 0;

            for (int y = 0; y < _texH; y++)
            {
                for (int x = 0; x < _texW; x++)
                {
                    int idx = y * _texW + x;
                    Color orig = _originalPixels[idx];
                    if (orig.a <= 0.05f) continue;

                    float stage = _pixelBurnStage[idx];
                    if (stage > 0f && stage < 1f)
                    {
                        stage += Random.Range(0.045f, 0.11f);
                        _pixelBurnStage[idx] = stage;

                        TrySpreadToNeighbor(x + 1, y, 0.30f);
                        TrySpreadToNeighbor(x - 1, y, 0.30f);
                        TrySpreadToNeighbor(x, y + 1, 0.48f);
                        TrySpreadToNeighbor(x, y - 1, 0.18f);
                    }

                    if (stage <= 0f)
                    {
                        _workingPixels[idx] = orig;
                        remainingAlivePixels++;
                    }
                    else if (stage < 0.35f)
                    {
                        float t = stage / 0.35f;
                        _workingPixels[idx] = Color.Lerp(orig, new Color(1f, 0.65f, 0.15f, orig.a), t * 0.85f);
                        remainingAlivePixels++;
                    }
                    else if (stage < 0.68f)
                    {
                        float t = (stage - 0.35f) / 0.33f;
                        _workingPixels[idx] = Color.Lerp(
                            new Color(1f, 0.65f, 0.15f, orig.a),
                            new Color(0.82f, 0.16f, 0.08f, orig.a),
                            t);
                        remainingAlivePixels++;
                    }
                    else if (stage < 0.94f)
                    {
                        float t = (stage - 0.68f) / 0.26f;
                        _workingPixels[idx] = Color.Lerp(
                            new Color(0.82f, 0.16f, 0.08f, orig.a),
                            new Color(0.14f, 0.11f, 0.10f, orig.a),
                            t);
                        remainingAlivePixels++;
                    }
                    else
                    {
                        _workingPixels[idx] = Color.clear;
                    }
                }
            }

            _runtimeTex.SetPixels(_workingPixels);
            _runtimeTex.Apply();

            if (remainingAlivePixels <= 3)
            {
                Destroy(gameObject);
            }
        }

        private void TrySpreadToNeighbor(int nx, int ny, float chance)
        {
            if (nx < 0 || nx >= _texW || ny < 0 || ny >= _texH) return;
            int nIdx = ny * _texW + nx;
            if (_originalPixels[nIdx].a > 0.1f && _pixelBurnStage[nIdx] <= 0f && Random.value < chance)
            {
                _pixelBurnStage[nIdx] = 0.05f;
            }
        }
    }

    /// <summary>
    /// Splits any sprite into flying, spinning pixel-art shards when shattered by Lightning or Earth!
    /// </summary>
    public class PixelShatterVfx : MonoBehaviour
    {
        private SpriteRenderer _sr;
        private Vector2 _vel;
        private float _zHeight;
        private float _zVel;
        private float _spinSpeed;
        private float _elapsed;
        private float _lifetime;
        private Color _initialTint;

        public static void Spawn(SpriteRenderer sourceRenderer, Vector2 hitDir, bool isElectric)
        {
            if (sourceRenderer == null || sourceRenderer.sprite == null) return;

            Sprite s = sourceRenderer.sprite;
            Texture2D tex = s.texture;
            if (tex == null) return;

            Rect r = s.rect;
            const int gridX = 3;
            const int gridY = 3;
            float cellW = r.width / gridX;
            float cellH = r.height / gridY;
            if (cellW < 2f || cellH < 2f) return;

            Vector3 basePos = sourceRenderer.transform.position;
            Vector3 scale = sourceRenderer.transform.lossyScale;
            float ppu = s.pixelsPerUnit;

            for (int gy = 0; gy < gridY; gy++)
            {
                for (int gx = 0; gx < gridX; gx++)
                {
                    Rect subRect = new Rect(r.x + gx * cellW, r.y + gy * cellH, cellW, cellH);
                    Sprite shardSprite = Sprite.Create(tex, subRect, new Vector2(0.5f, 0.5f), ppu);

                    var shardGo = new GameObject("PixelShard");
                    Vector2 localOffset = new Vector2((gx - 1f) * (cellW / ppu), (gy - 0.5f) * (cellH / ppu));
                    shardGo.transform.position = basePos + new Vector3(localOffset.x * scale.x, localOffset.y * scale.y, 0f);
                    shardGo.transform.localScale = scale;

                    var sr = shardGo.AddComponent<SpriteRenderer>();
                    sr.sprite = shardSprite;
                    sr.sortingOrder = sourceRenderer.sortingOrder + 2;

                    var vfx = shardGo.AddComponent<PixelShatterVfx>();
                    vfx._sr = sr;
                    Vector2 scatter = (localOffset.normalized + Random.insideUnitCircle * 0.75f + hitDir * 0.65f).normalized;
                    vfx._vel = scatter * Random.Range(2.2f, 5.2f);
                    vfx._zHeight = Mathf.Max(0.05f, localOffset.y);
                    vfx._zVel = Random.Range(2.5f, 5.0f);
                    vfx._spinSpeed = Random.Range(-540f, 540f);
                    vfx._lifetime = Random.Range(0.55f, 0.9f);
                    vfx._initialTint = isElectric ? new Color(0.75f, 0.96f, 1f, 1f) : Color.white;
                    sr.color = vfx._initialTint;
                }
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _elapsed += dt;
            if (_elapsed >= _lifetime)
            {
                Destroy(gameObject);
                return;
            }

            // Horizontal scatter + vertical bounce
            _zVel -= 14f * dt;
            _zHeight += _zVel * dt;
            if (_zHeight < 0f)
            {
                _zHeight = 0f;
                _zVel = -_zVel * 0.45f;
                _vel *= 0.65f;
            }

            transform.position += new Vector3(_vel.x * dt, (_vel.y + _zVel * 0.35f) * dt, 0f);
            transform.Rotate(0f, 0f, _spinSpeed * dt);

            if (_sr != null)
            {
                float alpha = 1f - Mathf.Clamp01((_elapsed - _lifetime * 0.5f) / (_lifetime * 0.5f));
                _sr.color = new Color(_initialTint.r, _initialTint.g, _initialTint.b, alpha);
            }
        }
    }
}
