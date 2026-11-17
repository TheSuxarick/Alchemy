using Alchemy.Audio;
using Alchemy.SpellDrawing;
using UnityEngine;

namespace Alchemy.Gameplay
{
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CircleCollider2D))]
    public class SlimeEnemy2D : LivingEntity2D
    {
        [Header("Slime Movement & Combat")]
        public float moveSpeed = 1.8f;
        public float aggroRange = 9f;
        public float contactDamage = 15f;

        [Header("Hopping Rhythm")]
        public float hopPauseDuration = 0.42f;
        public float hopLeapDuration = 0.34f;

        [Header("Optional Animator")]
        public RuntimeAnimatorController animatorController;

        private Rigidbody2D _rb;
        private SpriteRenderer _sr;
        private Animator _animator;
        private Transform _player;
        private PlayerController2D _playerCtrl;
        private Vector2 _knockbackVel;
        private float _hitFlashTimer;
        private float _burnTimeRemaining;
        private float _burnTickTimer;
        private float _wetLevel; // 0..1.5 stacking wetness (visual blue + Lightning buff)
        private float _stunTimeRemaining;
        private float _hopCycleTimer;
        private Vector2 _lockedHopDir;
        private Color _baseColor = Color.white;

        private static readonly int MoveXHash = Animator.StringToHash("MoveX");
        private static readonly int MoveYHash = Animator.StringToHash("MoveY");

        private void Awake()
        {
            if (maxHealth <= 0f || Mathf.Approximately(maxHealth, 100f)) maxHealth = 60f;
            currentHealth = maxHealth;
            headPopupOffsetY = 0.55f;

            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            var col = GetComponent<CircleCollider2D>();
            col.radius = 0.35f;

            _sr = GetComponent<SpriteRenderer>();
            if (_sr == null) _sr = gameObject.AddComponent<SpriteRenderer>();
            _baseColor = _sr.color;

            _animator = GetComponent<Animator>();
            if (_animator == null) _animator = gameObject.AddComponent<Animator>();

            if (animatorController == null)
            {
                animatorController = Resources.Load<RuntimeAnimatorController>("Animations/Slime_Green");
            }
            if (animatorController != null)
            {
                _animator.runtimeAnimatorController = animatorController;
            }
        }

        private void Start()
        {
            _playerCtrl = Object.FindFirstObjectByType<PlayerController2D>();
            if (_playerCtrl != null) _player = _playerCtrl.transform;
            _hopCycleTimer = Random.Range(0f, hopPauseDuration + hopLeapDuration);
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (_stunTimeRemaining > 0f) _stunTimeRemaining -= dt;
            if (_wetLevel > 0f) _wetLevel = Mathf.Max(0f, _wetLevel - dt * 0.08f);

            if (_burnTimeRemaining > 0f)
            {
                _burnTimeRemaining -= dt;
                _burnTickTimer -= dt;
                if (_burnTickTimer <= 0f)
                {
                    _burnTickTimer = 0.45f;
                    const float burnDmg = 5f;
                    currentHealth -= burnDmg;
                    SpawnDamagePopup(burnDmg, new Color(1f, 0.52f, 0.16f));
                    if (currentHealth <= 0f)
                    {
                        Destroy(gameObject);
                        return;
                    }
                }
            }

            if (_sr != null)
            {
                _sr.sortingOrder = Mathf.RoundToInt(-transform.position.y * 10f);

                if (_hitFlashTimer > 0f)
                {
                    _hitFlashTimer -= dt;
                    _sr.color = Color.Lerp(_baseColor, new Color(1f, 0.35f, 0.35f, 1f), _hitFlashTimer / 0.14f);
                }
                else if (_stunTimeRemaining > 0f)
                {
                    float flash = Mathf.PingPong(Time.time * 14f, 1f);
                    _sr.color = Color.Lerp(_baseColor, new Color(1f, 0.95f, 0.35f, 1f), flash);
                }
                else if (_burnTimeRemaining > 0f)
                {
                    float flicker = 0.55f + Mathf.Sin(Time.time * 18f) * 0.25f;
                    _sr.color = Color.Lerp(_baseColor, new Color(1f, 0.42f, 0.12f, 1f), flicker);
                }
                else if (_wetLevel > 0.01f)
                {
                    float wetBlend = Mathf.Clamp01(_wetLevel) * 0.72f;
                    _sr.color = Color.Lerp(_baseColor, new Color(0.28f, 0.62f, 1f, 1f), wetBlend);
                }
                else
                {
                    _sr.color = _baseColor;
                }
            }

            if (_knockbackVel.sqrMagnitude > 0.01f)
            {
                _knockbackVel = Vector2.Lerp(_knockbackVel, Vector2.zero, dt * 7.5f);
            }
        }

        private void FixedUpdate()
        {
            float fdt = Time.fixedDeltaTime;
            Vector2 moveVel = Vector2.zero;

            if (_stunTimeRemaining <= 0f && _player != null)
            {
                Vector2 toPlayer = (Vector2)(_player.position - transform.position);
                float dist = toPlayer.magnitude;

                // Contact damage check when close to the player
                if (dist <= 0.72f && _playerCtrl != null)
                {
                    Vector2 hitDir = dist > 0.001f ? (toPlayer / dist) : Vector2.down;
                    if (_playerCtrl.TakeDamage(contactDamage, hitDir))
                    {
                        // Bounce the slime back slightly after landing a hit
                        _knockbackVel = -hitDir * 4.5f;
                    }
                }

                if (dist > 0.55f && dist <= aggroRange)
                {
                    float totalCycle = Mathf.Max(0.2f, hopPauseDuration + hopLeapDuration);
                    float speedMult = _wetLevel > 0.05f ? Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(_wetLevel)) : 1f;
                    _hopCycleTimer += fdt * speedMult;

                    if (_hopCycleTimer >= totalCycle)
                    {
                        _hopCycleTimer -= totalCycle;
                        _lockedHopDir = toPlayer / dist;
                    }

                    if (_hopCycleTimer < hopPauseDuration)
                    {
                        // Wind-up squish on the ground before jumping
                        float squishT = _hopCycleTimer / Mathf.Max(0.05f, hopPauseDuration);
                        float sx = 1f + Mathf.Sin(squishT * Mathf.PI) * 0.14f;
                        float sy = 1f - Mathf.Sin(squishT * Mathf.PI) * 0.14f;
                        transform.localScale = new Vector3(sx, sy, 1f);
                        _lockedHopDir = toPlayer / dist;
                    }
                    else
                    {
                        // Airborne hop burst!
                        float leapT = (_hopCycleTimer - hopPauseDuration) / Mathf.Max(0.05f, hopLeapDuration);
                        float arc = Mathf.Sin(leapT * Mathf.PI);
                        transform.localScale = new Vector3(1f - arc * 0.10f, 1f + arc * 0.18f, 1f);

                        if (_lockedHopDir.sqrMagnitude < 0.001f) _lockedHopDir = toPlayer / dist;
                        float burstSpeed = moveSpeed * 2.25f * speedMult * Mathf.Sin(leapT * Mathf.PI);
                        moveVel = _lockedHopDir * burstSpeed;
                    }

                    if (_animator != null && _animator.runtimeAnimatorController != null)
                    {
                        _animator.SetFloat(MoveXHash, _lockedHopDir.x);
                        _animator.SetFloat(MoveYHash, _lockedHopDir.y);
                    }
                }
                else
                {
                    transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one, fdt * 10f);
                }
            }
            else
            {
                transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one, fdt * 10f);
            }

            Vector2 velocity = moveVel + _knockbackVel;
            _rb.MovePosition(_rb.position + velocity * fdt);
        }

        public void ApplySpellHit(SpellData spell, Vector2 hitDirection, float boostMultiplier)
        {
            float boost = Mathf.Clamp(boostMultiplier, 0.8f, 2.0f);
            float safeMass = Mathf.Max(0.25f, mass);

            if (spell == null)
            {
                TakeDamage(25f * boost, hitDirection, 5.5f / safeMass);
                return;
            }

            SpellAttributeFlags attrs = spell.GetEffectiveAttributes();
            bool isLightning = (attrs & SpellAttributeFlags.LightningShock) != 0;
            bool isEarth = (attrs & SpellAttributeFlags.EarthHeavy) != 0;

            // 1. Water stacks Wetness (bluish tint + slows + buffs Lightning!)
            if ((attrs & SpellAttributeFlags.WaterDrench) != 0)
            {
                _burnTimeRemaining = 0f;
                _wetLevel = Mathf.Clamp(_wetLevel + 0.45f * boost, 0f, 1.5f);
            }

            // 2. Fire dries Wetness or ignites enemy
            if ((attrs & SpellAttributeFlags.FireBurn) != 0 && (attrs & SpellAttributeFlags.WaterDrench) == 0)
            {
                if (_wetLevel > 0.25f)
                {
                    _wetLevel = Mathf.Max(0f, _wetLevel - 0.55f);
                }
                else
                {
                    _wetLevel = 0f;
                    _burnTimeRemaining = Mathf.Max(_burnTimeRemaining, Mathf.Max(3.0f, spell.burnDuration) * boost);
                }
            }

            // 3. Lightning is buffed up to 2.5x if enemy is Wet!
            float wetLightningMult = isLightning ? (1f + _wetLevel * 1.5f) : 1f;
            if (isLightning)
            {
                _stunTimeRemaining = Mathf.Max(_stunTimeRemaining, Mathf.Max(0.8f, spell.stunDuration) * boost * wetLightningMult);
            }

            // 4. Mass-based Knockback (Air throws enemies far back!)
            float rawKb = (attrs & SpellAttributeFlags.AirKnockback) != 0
                ? Mathf.Max(14f, spell.knockbackForce) * boost
                : spell.knockbackForce * 0.6f * boost;

            float dmg = spell.baseDamage * boost * wetLightningMult;
            Color popupCol = isLightning && _wetLevel > 0.05f
                ? new Color(0.45f, 0.95f, 1f)
                : new Color(1f, 0.88f, 0.32f);

            TakeDamage(
                dmg,
                hitDirection,
                rawKb / safeMass,
                shatterOnDeath: isLightning || isEarth,
                isElectric: isLightning,
                popupColor: popupCol,
                isBoostedHit: wetLightningMult > 1.15f || boost >= 1.35f);

            spell.OnCustomHit(gameObject, hitDirection, boost);
        }

        public void TakeDamage(
            float amount,
            Vector2 hitDirection,
            float knockbackSpeed = 5.5f,
            bool shatterOnDeath = false,
            bool isElectric = false,
            Color? popupColor = null,
            bool isBoostedHit = false)
        {
            currentHealth -= amount;
            _hitFlashTimer = 0.14f;
            _knockbackVel = hitDirection.normalized * knockbackSpeed;

            SpawnDamagePopup(amount, popupColor ?? new Color(1f, 0.88f, 0.32f), isBoostedHit);

            if (currentHealth <= 0f)
            {
                if (shatterOnDeath && _sr != null)
                {
                    PixelShatterVfx.Spawn(_sr, hitDirection.normalized, isElectric);
                    AudioManager.Instance.PlaySfx("slime_shatter", 0.85f, 0.08f);
                }
                else
                {
                    AudioManager.Instance.PlaySfx("slime_death", 0.8f, 0.08f);
                }
                Destroy(gameObject);
            }
            else
            {
                AudioManager.Instance.PlaySfx("slime_hit", 0.7f, 0.1f);
            }
        }
    }
}
