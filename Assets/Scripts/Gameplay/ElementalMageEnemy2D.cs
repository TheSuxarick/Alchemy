using Alchemy.Audio;
using Alchemy.SpellDrawing;
using UnityEngine;

namespace Alchemy.Gameplay
{
    public enum MageElement
    {
        Fire,
        Lightning,
        Water,
        Darkness
    }

    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CircleCollider2D))]
    public class ElementalMageEnemy2D : LivingEntity2D
    {
        [Header("Elemental Identity")]
        public MageElement element = MageElement.Fire;
        public SpellData castSpell;

        [Header("Combat & AI")]
        public float moveSpeed = 2.0f;
        public float aggroRange = 12f;
        public float preferredMinRange = 5.0f;
        public float preferredMaxRange = 8.5f;
        public float castCooldown = 2.4f;
        public float castWindup = 0.55f;
        public float projectileDamage = 12f;

        private Rigidbody2D _rb;
        private SpriteRenderer _sr;
        private Animator _anim;
        private Transform _player;
        private SpriteRenderer _telegraphRing;

        private float _cooldownTimer;
        private float _windupTimer;
        private bool _isWindingUp;
        private Vector2 _lockedAimDir = Vector2.right;
        private Vector2 _moveVel;
        private Vector2 _knockbackVel;
        private float _hitFlashTimer;
        private float _burnTimer;
        private float _burnTickTimer;
        private float _stunTimer;
        private float _wetTimer;

        private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
        private static readonly int AttackHash = Animator.StringToHash("Attack");

        private void Awake()
        {
            maxHealth = 110f;
            currentHealth = maxHealth;
            mass = 1.2f;
            headPopupOffsetY = 0.9f;

            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var col = GetComponent<CircleCollider2D>();
            col.radius = 0.36f;

            _sr = GetComponent<SpriteRenderer>();
            if (_sr == null) _sr = gameObject.AddComponent<SpriteRenderer>();
            _sr.sortingOrder = 10;

            _anim = GetComponent<Animator>();
            if (_anim == null) _anim = gameObject.AddComponent<Animator>();

            CreateTelegraphRing();
            ConfigureElementVisualsAndSpell();
        }

        private void Start()
        {
            var playerCtrl = FindFirstObjectByType<PlayerController2D>();
            if (playerCtrl != null) _player = playerCtrl.transform;
            _cooldownTimer = Random.Range(0.6f, 1.4f);
        }

        public void InitializeElement(MageElement targetElement)
        {
            element = targetElement;
            ConfigureElementVisualsAndSpell();
        }

        private void ConfigureElementVisualsAndSpell()
        {
            string elemName = element.ToString();
            if (castSpell == null)
            {
                castSpell = Resources.Load<SpellData>($"Spells/{elemName}");
            }

            var ctrl = Resources.Load<RuntimeAnimatorController>($"Animations/Mage_{elemName}");
            if (ctrl != null && _anim != null)
            {
                _anim.runtimeAnimatorController = ctrl;
            }

            if (_telegraphRing != null)
            {
                Color c = GetElementColor();
                c.a = 0.65f;
                _telegraphRing.color = c;
            }
        }

        private Color GetElementColor()
        {
            if (castSpell != null) return castSpell.spellColor;
            return element switch
            {
                MageElement.Fire => new Color(1f, 0.35f, 0.12f),
                MageElement.Lightning => new Color(0.3f, 0.9f, 1f),
                MageElement.Water => new Color(0.2f, 0.6f, 1f),
                _ => new Color(0.7f, 0.3f, 1f)
            };
        }

        private SpellAttributeFlags GetOwnElementFlag()
        {
            return element switch
            {
                MageElement.Fire => SpellAttributeFlags.FireBurn,
                MageElement.Lightning => SpellAttributeFlags.LightningShock,
                MageElement.Water => SpellAttributeFlags.WaterDrench,
                MageElement.Darkness => SpellAttributeFlags.DarkVoid,
                _ => SpellAttributeFlags.None
            };
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (_hitFlashTimer > 0f) _hitFlashTimer -= dt;
            if (_stunTimer > 0f) _stunTimer -= dt;
            if (_wetTimer > 0f) _wetTimer -= dt;

            if (_burnTimer > 0f)
            {
                _burnTimer -= dt;
                _burnTickTimer -= dt;
                if (_burnTickTimer <= 0f)
                {
                    _burnTickTimer = 0.55f;
                    TakeDamage(7f, Vector2.zero, 0f, popupColor: new Color(1f, 0.45f, 0.15f));
                }
            }

            if (_knockbackVel.sqrMagnitude > 0.01f)
            {
                _knockbackVel = Vector2.Lerp(_knockbackVel, Vector2.zero, dt * 7f);
            }

            UpdateAi(dt);
            UpdateVisuals();
        }

        private void UpdateAi(float dt)
        {
            _moveVel = Vector2.zero;
            if (_player == null || _stunTimer > 0f)
            {
                CancelWindup();
                return;
            }

            Vector2 toPlayer = (Vector2)(_player.position - transform.position);
            float dist = toPlayer.magnitude;

            if (dist > aggroRange)
            {
                CancelWindup();
                return;
            }

            if (toPlayer.x != 0f && _sr != null)
            {
                _sr.flipX = toPlayer.x < 0f;
            }

            if (_isWindingUp)
            {
                _windupTimer -= dt;
                if (_telegraphRing != null)
                {
                    _telegraphRing.enabled = true;
                    float pulse = 0.85f + Mathf.Sin(Time.time * 22f) * 0.2f;
                    _telegraphRing.transform.localScale = Vector3.one * pulse;
                }

                if (_windupTimer <= 0f)
                {
                    FireElementalSpell();
                    _isWindingUp = false;
                    _cooldownTimer = castCooldown;
                    if (_telegraphRing != null) _telegraphRing.enabled = false;
                }
                return;
            }

            Vector2 dir = dist > 0.001f ? toPlayer / dist : Vector2.right;
            float speedMult = _wetTimer > 0f ? 0.65f : 1f;

            if (dist < preferredMinRange)
            {
                _moveVel = -dir * (moveSpeed * 0.9f * speedMult);
            }
            else if (dist > preferredMaxRange)
            {
                _moveVel = dir * (moveSpeed * speedMult);
            }

            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= dt;
            }
            else if (dist <= aggroRange * 0.92f)
            {
                _isWindingUp = true;
                _windupTimer = castWindup;
                _lockedAimDir = dir;
                if (_anim != null) _anim.SetTrigger(AttackHash);
            }
        }

        private void CancelWindup()
        {
            _isWindingUp = false;
            if (_telegraphRing != null) _telegraphRing.enabled = false;
        }

        private void FireElementalSpell()
        {
            Vector2 aimDir = _player != null
                ? ((Vector2)(_player.position - transform.position)).normalized
                : _lockedAimDir;
            if (aimDir.sqrMagnitude < 0.001f) aimDir = Vector2.right;

            Vector3 spawnPos = transform.position + (Vector3)(aimDir * 0.55f);

            if (castSpell != null)
            {
                SpellProjectile2D.SpawnEnemyFromSpell(spawnPos, aimDir, castSpell, projectileDamage, 0.72f);
                if (castSpell.castClip != null)
                {
                    AudioManager.Instance.PlaySfx(castSpell.castClip, 0.65f, 0.08f);
                }
                else
                {
                    AudioManager.Instance.PlaySfx($"spell_{castSpell.spellId.Trim().ToLowerInvariant()}_cast", 0.65f, 0.08f);
                }
            }
            else
            {
                SpellProjectile2D.SpawnEnemySpore(spawnPos, aimDir, GetElementColor(), 7.5f, projectileDamage, 0.65f);
            }
        }

        private void FixedUpdate()
        {
            _rb.MovePosition(_rb.position + (_moveVel + _knockbackVel) * Time.fixedDeltaTime);
        }

        private void UpdateVisuals()
        {
            if (_anim != null)
            {
                _anim.SetBool(IsMovingHash, !_isWindingUp && _moveVel.sqrMagnitude > 0.05f);
            }

            if (_sr == null) return;
            _sr.sortingOrder = Mathf.RoundToInt(-transform.position.y * 10f);

            if (_hitFlashTimer > 0f)
            {
                _sr.color = new Color(1f, 0.45f, 0.45f, 1f);
            }
            else if (_stunTimer > 0f)
            {
                _sr.color = new Color(0.65f, 0.92f, 1f, 1f);
            }
            else if (_burnTimer > 0f)
            {
                _sr.color = new Color(1f, 0.7f, 0.45f, 1f);
            }
            else if (_wetTimer > 0f)
            {
                _sr.color = new Color(0.6f, 0.78f, 1f, 1f);
            }
            else
            {
                _sr.color = Color.white;
            }
        }

        public void ApplySpellHit(SpellData spell, Vector2 hitDirection, float boostMultiplier)
        {
            if (spell == null) return;

            SpellAttributeFlags attrs = spell.GetEffectiveAttributes();
            SpellAttributeFlags ownFlag = GetOwnElementFlag();

            // Elemental Immunity: Mages are completely immune to their own element!
            if (ownFlag != SpellAttributeFlags.None && (attrs & ownFlag) != 0)
            {
                SpawnHeadPopup("IMMUNE!", GetElementColor(), 15);
                AudioManager.Instance.PlaySfx("rune_fizzle", 0.65f, 0.08f);
                return;
            }

            float boost = Mathf.Clamp(boostMultiplier, 0.8f, 2.0f);
            float dmg = spell.baseDamage * boost;

            if ((attrs & SpellAttributeFlags.WaterDrench) != 0)
            {
                _burnTimer = 0f;
                _wetTimer = 4.0f;
            }

            if ((attrs & SpellAttributeFlags.FireBurn) != 0 && (attrs & SpellAttributeFlags.WaterDrench) == 0)
            {
                _wetTimer = 0f;
                _burnTimer = Mathf.Max(_burnTimer, spell.burnDuration * boost);
            }

            bool isLightning = (attrs & SpellAttributeFlags.LightningShock) != 0;
            bool isEarth = (attrs & SpellAttributeFlags.EarthHeavy) != 0;
            if (isLightning)
            {
                _stunTimer = Mathf.Max(_stunTimer, Mathf.Max(0.75f, spell.stunDuration));
                if (_wetTimer > 0f) dmg *= 1.35f;
            }

            float safeMass = Mathf.Max(0.35f, mass);
            TakeDamage(
                dmg,
                hitDirection,
                (spell.knockbackForce * boost) / safeMass,
                shatterOnDeath: isLightning || isEarth,
                isElectric: isLightning,
                popupColor: spell.spellColor);
        }

        public void TakeDamage(
            float amount,
            Vector2 hitDirection,
            float knockbackSpeed = 4.5f,
            bool shatterOnDeath = false,
            bool isElectric = false,
            Color? popupColor = null)
        {
            currentHealth -= amount;
            _hitFlashTimer = 0.14f;
            if (knockbackSpeed > 0.01f && hitDirection.sqrMagnitude > 0.001f)
            {
                _knockbackVel = hitDirection.normalized * knockbackSpeed;
            }

            SpawnDamagePopup(amount, popupColor ?? new Color(1f, 0.85f, 0.35f));

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
                AudioManager.Instance.PlaySfx("slime_hit", 0.65f, 0.1f);
            }
        }

        private void CreateTelegraphRing()
        {
            var ringGo = new GameObject("CastTelegraph");
            ringGo.transform.SetParent(transform, false);
            ringGo.transform.localPosition = new Vector3(0f, -0.1f, 0f);

            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            Vector2 c = new Vector2(15.5f, 15.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), c);
                    bool ring = d >= 11.5f && d <= 14.5f;
                    tex.SetPixel(x, y, ring ? Color.white : Color.clear);
                }
            }
            tex.Apply();

            _telegraphRing = ringGo.AddComponent<SpriteRenderer>();
            _telegraphRing.sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 24f);
            _telegraphRing.sortingOrder = 5;
            _telegraphRing.enabled = false;
        }
    }
}
