using Alchemy.Audio;
using Alchemy.SpellDrawing;
using UnityEngine;

namespace Alchemy.Gameplay
{
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CircleCollider2D))]
    public class MinotaurBossEnemy2D : LivingEntity2D
    {
        private enum BossState
        {
            Idle,
            Chase,
            CleaveWindup,
            CleaveSwing,
            SlamWindup,
            SlamSwing,
            ChargeWindup,
            Charging,
            HealChannel,
            SummonRoar
        }

        [Header("Minotaur Boss Tuning")]
        public bool isSummonedEcho;
        public float aggroRange = 15f;
        public float moveSpeed = 2.25f;
        public float cleaveRadius = 2.85f;
        public float cleaveDamage = 20f;
        public float slamRadius = 3.4f;
        public float slamDamage = 24f;
        public float chargeSpeed = 12.5f;
        public float chargeDamage = 22f;
        public float shockwaveDamage = 13f;

        private Rigidbody2D _rb;
        private SpriteRenderer _sr;
        private Animator _anim;
        private Transform _player;
        private PlayerController2D _playerCtrl;

        private SpriteRenderer _slamTelegraphSr;
        private SpriteRenderer _chargeLaneSr;

        private Texture2D _hbUnder;
        private Texture2D _hbProgress;
        private Texture2D _hbOver;

        private BossState _state = BossState.Idle;
        private float _stateTimer;
        private float _attackCooldown = 1.4f;
        private int _attackCycle;
        private Vector2 _lockedDir = Vector2.left;
        private Vector2 _moveVel;
        private Vector2 _knockbackVel;
        private bool _swingHitApplied;

        private bool _isPhase2;
        private bool _hasEngaged;
        private float _armorBrokenTimer;
        private float _hitFlashTimer;
        private float _burnTimer;
        private float _burnTickTimer;
        private float _wetTimer;
        private float _contactCooldown;

        private WaveManager2D _waveManager;
        private int _healsRemaining = 2;
        private float _healCooldown;
        private float _summonCooldown;
        private float _healVfxPulseTimer;
        private MinotaurBossEnemy2D _activeEcho;

        private GUIStyle _barStatusStyle;

        private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int MoveSpeedMultHash = Animator.StringToHash("MoveSpeedMult");

        private void Awake()
        {
            maxHealth = 720f;
            currentHealth = maxHealth;
            mass = 9.0f;
            headPopupOffsetY = 1.75f;

            transform.localScale = new Vector3(1.35f, 1.35f, 1f);

            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var col = GetComponent<CircleCollider2D>();
            col.radius = 0.68f;

            _sr = GetComponent<SpriteRenderer>();
            if (_sr == null) _sr = gameObject.AddComponent<SpriteRenderer>();
            _sr.sortingOrder = 12;

            _anim = GetComponent<Animator>();
            if (_anim == null) _anim = gameObject.AddComponent<Animator>();
            var ctrl = Resources.Load<RuntimeAnimatorController>("Animations/MinotaurBoss");
            if (ctrl != null) _anim.runtimeAnimatorController = ctrl;

            _hbUnder = Resources.Load<Texture2D>("UI/MinotaurHealthBar/mino_health_under");
            _hbProgress = Resources.Load<Texture2D>("UI/MinotaurHealthBar/mino_health_progress");
            _hbOver = Resources.Load<Texture2D>("UI/MinotaurHealthBar/mino_health_over");

            CreateTelegraphVisuals();
        }

        private void Start()
        {
            _playerCtrl = FindFirstObjectByType<PlayerController2D>();
            if (_playerCtrl != null) _player = _playerCtrl.transform;
        }

        public void ConfigureForWave(int waveNumber, WaveManager2D manager, bool asEcho = false)
        {
            _waveManager = manager;
            isSummonedEcho = asEcho;

            float dmgScale = 1f + Mathf.Max(0, waveNumber - 1) * 0.12f;
            if (asEcho)
            {
                maxHealth = Mathf.Round((600f + Mathf.Max(0, waveNumber - 1) * 180f) * 0.35f);
                currentHealth = maxHealth;
                transform.localScale = new Vector3(1.12f, 1.12f, 1f);
                cleaveDamage *= 0.72f * dmgScale;
                slamDamage *= 0.72f * dmgScale;
                chargeDamage *= 0.72f * dmgScale;
                shockwaveDamage *= 0.72f * dmgScale;
                _hasEngaged = true;
                _healsRemaining = 0;
            }
            else
            {
                maxHealth = 600f + Mathf.Max(0, waveNumber - 1) * 180f;
                currentHealth = maxHealth;
                cleaveDamage *= dmgScale;
                slamDamage *= dmgScale;
                chargeDamage *= dmgScale;
                shockwaveDamage *= dmgScale;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (_hitFlashTimer > 0f) _hitFlashTimer -= dt;
            if (_armorBrokenTimer > 0f) _armorBrokenTimer -= dt;
            if (_wetTimer > 0f) _wetTimer -= dt;
            if (_contactCooldown > 0f) _contactCooldown -= dt;
            if (_healCooldown > 0f) _healCooldown -= dt;
            if (_summonCooldown > 0f) _summonCooldown -= dt;

            if (_burnTimer > 0f)
            {
                _burnTimer -= dt;
                _burnTickTimer -= dt;
                if (_burnTickTimer <= 0f)
                {
                    _burnTickTimer = 0.6f;
                    TakeDamage(11f, Vector2.zero, 0f, bypassArmor: true, popupColor: new Color(1f, 0.45f, 0.15f));
                }
            }

            if (_knockbackVel.sqrMagnitude > 0.01f)
            {
                _knockbackVel = Vector2.Lerp(_knockbackVel, Vector2.zero, dt * 8f);
            }

            UpdateBossStateMachine(dt);
            UpdateVisualsAndAnimator();
        }

        private void UpdateBossStateMachine(float dt)
        {
            _moveVel = Vector2.zero;
            if (_player == null) return;

            Vector2 toPlayer = (Vector2)(_player.position - transform.position);
            float dist = toPlayer.magnitude;

            if (!_hasEngaged)
            {
                if (dist <= aggroRange)
                {
                    _hasEngaged = true;
                    SpawnHeadPopup("MINOTAUR AWAKENS!", new Color(1f, 0.4f, 0.25f), 17);
                }
                else
                {
                    return;
                }
            }

            // Minotaur sprite faces LEFT by default, so flipX = true when player is to the RIGHT.
            bool canFlip = _state == BossState.Idle || _state == BossState.Chase ||
                           _state == BossState.CleaveWindup || _state == BossState.SlamWindup;
            if (canFlip && Mathf.Abs(toPlayer.x) > 0.05f && _sr != null)
            {
                _sr.flipX = toPlayer.x > 0f;
            }

            switch (_state)
            {
                case BossState.Idle:
                case BossState.Chase:
                {
                    float speedMult = (_isPhase2 ? 1.32f : 1f) * (_wetTimer > 0f ? 0.68f : 1f);
                    if (dist > 1.75f)
                    {
                        _moveVel = (toPlayer / Mathf.Max(0.001f, dist)) * (moveSpeed * speedMult);
                    }

                    _attackCooldown -= dt;
                    if (_attackCooldown <= 0f)
                    {
                        PickNextAttack(dist, toPlayer);
                    }
                    break;
                }

                case BossState.CleaveWindup:
                {
                    _stateTimer -= dt;
                    if (_slamTelegraphSr != null)
                    {
                        _slamTelegraphSr.enabled = true;
                        _slamTelegraphSr.transform.localScale = Vector3.one * (cleaveRadius * 0.95f);
                    }
                    if (_stateTimer <= 0f)
                    {
                        HideTelegraphs();
                        _state = BossState.CleaveSwing;
                        _stateTimer = 0.95f;
                        _swingHitApplied = false;
                        if (_anim != null) _anim.SetTrigger(AttackHash);
                    }
                    break;
                }

                case BossState.CleaveSwing:
                {
                    _stateTimer -= dt;
                    // Apply cleave impact & shockwaves mid-swing (frame ~6 of 16)
                    if (!_swingHitApplied && _stateTimer <= 0.62f)
                    {
                        _swingHitApplied = true;
                        ExecuteAxeCleave();
                    }
                    if (_stateTimer <= 0f)
                    {
                        EnterChaseCooldown();
                    }
                    break;
                }

                case BossState.SlamWindup:
                {
                    _stateTimer -= dt;
                    if (_slamTelegraphSr != null)
                    {
                        _slamTelegraphSr.enabled = true;
                        float total = _isPhase2 ? 0.62f : 0.8f;
                        float t = 1f - Mathf.Clamp01(_stateTimer / total);
                        float scale = Mathf.Lerp(0.4f, slamRadius * 1.15f, t);
                        _slamTelegraphSr.transform.localScale = new Vector3(scale, scale, 1f);
                    }
                    if (_stateTimer <= 0f)
                    {
                        HideTelegraphs();
                        _state = BossState.SlamSwing;
                        _stateTimer = 0.95f;
                        _swingHitApplied = false;
                        if (_anim != null) _anim.SetTrigger(AttackHash);
                    }
                    break;
                }

                case BossState.SlamSwing:
                {
                    _stateTimer -= dt;
                    if (!_swingHitApplied && _stateTimer <= 0.58f)
                    {
                        _swingHitApplied = true;
                        ExecuteEarthshakerSlam();
                    }
                    if (_stateTimer <= 0f)
                    {
                        EnterChaseCooldown();
                    }
                    break;
                }

                case BossState.ChargeWindup:
                {
                    _stateTimer -= dt;
                    if (_chargeLaneSr != null)
                    {
                        _chargeLaneSr.enabled = true;
                        float angle = Mathf.Atan2(_lockedDir.y, _lockedDir.x) * Mathf.Rad2Deg;
                        _chargeLaneSr.transform.rotation = Quaternion.Euler(0f, 0f, angle);
                    }
                    if (_stateTimer <= 0f)
                    {
                        HideTelegraphs();
                        _state = BossState.Charging;
                        _stateTimer = 0.62f;
                        AudioManager.Instance.PlaySfx("player_dash", 0.9f, -0.18f);
                    }
                    break;
                }

                case BossState.Charging:
                {
                    _stateTimer -= dt;
                    _moveVel = _lockedDir * (chargeSpeed * (_isPhase2 ? 1.18f : 1f));
                    if (_stateTimer <= 0f)
                    {
                        if (_isPhase2)
                        {
                            // Phase 2 combo: Bull Charge chains directly into Axe Cleave!
                            _lockedDir = _player != null
                                ? ((Vector2)(_player.position - transform.position)).normalized
                                : _lockedDir;
                            _state = BossState.CleaveSwing;
                            _stateTimer = 0.9f;
                            _swingHitApplied = false;
                            if (_anim != null) _anim.SetTrigger(AttackHash);
                        }
                        else
                        {
                            EnterChaseCooldown();
                        }
                    }
                    break;
                }

                case BossState.HealChannel:
                {
                    _stateTimer -= dt;
                    _healVfxPulseTimer -= dt;
                    if (_healVfxPulseTimer <= 0f)
                    {
                        _healVfxPulseTimer = 0.55f;
                        var lib = PvfxVfxLibrary.Instance;
                        if (lib != null && lib.bossHealAura != null)
                        {
                            SpellImpactVfx.Spawn(transform.position, lib.bossHealAura, 14f, 1.9f);
                        }
                    }
                    if (_stateTimer <= 0f)
                    {
                        float healAmt = maxHealth * 0.15f;
                        currentHealth = Mathf.Min(maxHealth, currentHealth + healAmt);
                        _healsRemaining--;
                        _healCooldown = 16f;
                        SpawnHeadPopup($"+{Mathf.RoundToInt(healAmt)} HP!", new Color(0.4f, 1f, 0.5f), 17);
                        AudioManager.Instance.PlaySfx("player_respawn", 0.85f, 0.05f);
                        EnterChaseCooldown();
                    }
                    break;
                }

                case BossState.SummonRoar:
                {
                    _stateTimer -= dt;
                    if (_stateTimer <= 0f)
                    {
                        SpawnSummonedEcho();
                        _summonCooldown = 22f;
                        EnterChaseCooldown();
                    }
                    break;
                }
            }
        }

        private void PickNextAttack(float dist, Vector2 toPlayer)
        {
            _lockedDir = toPlayer.sqrMagnitude > 0.001f ? toPlayer.normalized : Vector2.left;

            if (!isSummonedEcho && _isPhase2)
            {
                if (_healsRemaining > 0 && _healCooldown <= 0f && HealthNormalized < 0.68f)
                {
                    _state = BossState.HealChannel;
                    _stateTimer = 1.8f;
                    _healVfxPulseTimer = 0f;
                    SpawnHeadPopup("HEALING RITUAL! (ZAP TO STOP)", new Color(0.45f, 1f, 0.55f), 15);
                    return;
                }

                if (_activeEcho == null && _summonCooldown <= 0f)
                {
                    _state = BossState.SummonRoar;
                    _stateTimer = 0.65f;
                    SpawnHeadPopup("SUMMONING ECHO!", new Color(0.85f, 0.4f, 1f), 16);
                    return;
                }
            }

            _attackCycle = (_attackCycle + 1) % 3;

            if (_attackCycle == 0 && dist <= cleaveRadius + 1.5f)
            {
                _state = BossState.CleaveWindup;
                _stateTimer = _isPhase2 ? 0.38f : 0.5f;
                SpawnHeadPopup("AXE CLEAVE!", new Color(1f, 0.45f, 0.25f), 15);
            }
            else if (_attackCycle == 1 || dist > 5.0f)
            {
                _state = BossState.ChargeWindup;
                _stateTimer = _isPhase2 ? 0.55f : 0.72f;
                SpawnHeadPopup("BULL CHARGE!", new Color(1f, 0.3f, 0.2f), 15);
            }
            else
            {
                _state = BossState.SlamWindup;
                _stateTimer = _isPhase2 ? 0.62f : 0.8f;
                SpawnHeadPopup("EARTHSHAKER!", new Color(1f, 0.65f, 0.2f), 15);
            }
        }

        private void EnterChaseCooldown()
        {
            _state = BossState.Chase;
            _attackCooldown = _isPhase2 ? 1.1f : 1.75f;
        }

        private void ExecuteAxeCleave()
        {
            AudioManager.Instance.PlaySfx("spell_air_cast", 0.9f, -0.22f);

            var lib = PvfxVfxLibrary.Instance;
            if (lib != null && lib.axeCleave != null)
            {
                SpellImpactVfx.Spawn(transform.position + (Vector3)(_lockedDir * 1.15f), lib.axeCleave, 18f, 2.2f);
            }

            if (_playerCtrl != null)
            {
                Vector2 diff = (Vector2)(_playerCtrl.transform.position - transform.position);
                if (diff.magnitude <= cleaveRadius)
                {
                    Vector2 kb = diff.sqrMagnitude > 0.001f ? diff.normalized : _lockedDir;
                    _playerCtrl.TakeDamage(cleaveDamage, kb);
                }
            }

            // Launch shockwave crescents forward (Air-deflectable!)
            int count = _isPhase2 ? 5 : 3;
            float spread = _isPhase2 ? 64f : 42f;
            float baseAng = Mathf.Atan2(_lockedDir.y, _lockedDir.x) * Mathf.Rad2Deg;
            Color waveColor = _isPhase2 ? new Color(1f, 0.25f, 0.18f) : new Color(1f, 0.55f, 0.22f);

            for (int i = 0; i < count; i++)
            {
                float t = count > 1 ? (i / (float)(count - 1)) - 0.5f : 0f;
                float rad = (baseAng + t * spread) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                SpellProjectile2D.SpawnEnemySpore(
                    transform.position + (Vector3)(dir * 1.1f),
                    dir,
                    waveColor,
                    _isPhase2 ? 8.5f : 7.0f,
                    shockwaveDamage,
                    0.82f);
            }
        }

        private void ExecuteEarthshakerSlam()
        {
            AudioManager.Instance.PlaySfx("env_rock_crack", 1.0f, -0.15f);

            var lib = PvfxVfxLibrary.Instance;
            if (lib != null)
            {
                if (lib.groundSlam != null) SpellImpactVfx.Spawn(transform.position, lib.groundSlam, 16f, 2.4f);
                if (lib.shockwaveRing != null) SpellImpactVfx.Spawn(transform.position, lib.shockwaveRing, 18f, 2.6f);
            }

            if (_playerCtrl != null)
            {
                Vector2 diff = (Vector2)(_playerCtrl.transform.position - transform.position);
                if (diff.magnitude <= slamRadius)
                {
                    Vector2 kb = diff.sqrMagnitude > 0.001f ? diff.normalized : Vector2.down;
                    _playerCtrl.TakeDamage(slamDamage, kb);
                }
            }

            int radialCount = _isPhase2 ? 8 : 4;
            Color waveColor = new Color(1f, 0.42f, 0.18f);
            for (int i = 0; i < radialCount; i++)
            {
                float rad = (i * (360f / radialCount)) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                SpellProjectile2D.SpawnEnemySpore(
                    transform.position + (Vector3)(dir * 0.9f),
                    dir,
                    waveColor,
                    6.2f,
                    shockwaveDamage,
                    0.75f);
            }
        }

        private void FixedUpdate()
        {
            _rb.MovePosition(_rb.position + (_moveVel + _knockbackVel) * Time.fixedDeltaTime);

            if (_playerCtrl != null && _contactCooldown <= 0f)
            {
                float dist = Vector2.Distance(_rb.position, _playerCtrl.transform.position);
                float hitRadius = _state == BossState.Charging ? 1.35f : 0.95f;
                if (dist <= hitRadius)
                {
                    Vector2 kb = ((Vector2)_playerCtrl.transform.position - _rb.position).normalized;
                    float dmg = _state == BossState.Charging ? chargeDamage : 14f;
                    if (_playerCtrl.TakeDamage(dmg, kb))
                    {
                        _contactCooldown = 0.65f;
                    }
                }
            }
        }

        public void ApplySpellHit(SpellData spell, Vector2 hitDirection, float boostMultiplier)
        {
            if (spell == null) return;
            _hasEngaged = true;

            SpellAttributeFlags attrs = spell.GetEffectiveAttributes();
            float boost = Mathf.Clamp(boostMultiplier, 0.8f, 2.0f);
            float dmg = spell.baseDamage * boost;

            if ((attrs & SpellAttributeFlags.WaterDrench) != 0)
            {
                _burnTimer = 0f;
                _wetTimer = 4.5f;
            }

            if ((attrs & SpellAttributeFlags.FireBurn) != 0 && (attrs & SpellAttributeFlags.WaterDrench) == 0)
            {
                _wetTimer = 0f;
                _burnTimer = Mathf.Max(_burnTimer, spell.burnDuration * boost);
                SpawnHeadPopup("IGNITED!", new Color(1f, 0.5f, 0.15f), 14);
            }

            bool isEarth = (attrs & SpellAttributeFlags.EarthHeavy) != 0;
            bool isLightning = (attrs & SpellAttributeFlags.LightningShock) != 0;

            if (isLightning && _state == BossState.HealChannel)
            {
                _healCooldown = 16f;
                EnterChaseCooldown();
                SpawnHeadPopup("HEAL INTERRUPTED!", new Color(0.35f, 0.92f, 1f), 17);
                AudioManager.Instance.PlaySfx("spell_lightning_impact", 0.9f, 0.05f);
                var libInt = PvfxVfxLibrary.Instance;
                if (libInt != null && libInt.stunInterrupt != null)
                {
                    SpellImpactVfx.Spawn(transform.position + Vector3.up * 0.9f, libInt.stunInterrupt, 14f, 1.5f);
                }
            }

            bool cracksArmor = isEarth || (isLightning && _wetTimer > 0f);

            if (cracksArmor)
            {
                dmg *= 1.6f;
                _armorBrokenTimer = 5.0f;
                SpawnHeadPopup("ARMOR CRACK!", new Color(1f, 0.85f, 0.25f), 16);
                AudioManager.Instance.PlaySfx("env_rock_crack", 0.85f, 0.05f);
                var libCrack = PvfxVfxLibrary.Instance;
                if (libCrack != null && libCrack.armorCrack != null)
                {
                    SpellImpactVfx.Spawn(transform.position, libCrack.armorCrack, 18f, 1.8f);
                }
            }
            else if (isLightning && _wetTimer > 0f)
            {
                dmg *= 1.35f;
            }

            TakeDamage(
                dmg,
                hitDirection,
                (spell.knockbackForce * boost) / Mathf.Max(1f, mass),
                bypassArmor: cracksArmor,
                popupColor: spell.spellColor);
        }

        public void TakeDamage(
            float amount,
            Vector2 hitDirection,
            float knockbackSpeed = 1.2f,
            bool bypassArmor = false,
            Color? popupColor = null)
        {
            bool armored = !bypassArmor && _armorBrokenTimer <= 0f;
            float finalDmg = armored ? amount * 0.6f : amount;

            currentHealth -= finalDmg;
            _hitFlashTimer = 0.14f;
            if (knockbackSpeed > 0.01f && hitDirection.sqrMagnitude > 0.001f)
            {
                _knockbackVel = hitDirection.normalized * knockbackSpeed;
            }

            SpawnDamagePopup(finalDmg, popupColor ?? new Color(1f, 0.88f, 0.32f), !armored);

            if (!_isPhase2 && currentHealth > 0f && HealthNormalized <= 0.5f)
            {
                EnterPhase2();
            }

            if (currentHealth <= 0f)
            {
                HideTelegraphs();
                if (_sr != null)
                {
                    PixelShatterVfx.Spawn(_sr, hitDirection.sqrMagnitude > 0.001f ? hitDirection.normalized : Vector2.up, false);
                }
                AudioManager.Instance.PlaySfx("slime_shatter", 1.0f, -0.2f);
                SpawnHeadPopup("MINOTAUR DEFEATED!", new Color(1f, 0.85f, 0.2f), 18);
                Destroy(gameObject);
            }
            else
            {
                AudioManager.Instance.PlaySfx(armored ? "env_rock_crack" : "slime_hit", 0.65f, 0.06f);
            }
        }

        private void EnterPhase2()
        {
            _isPhase2 = true;
            SpawnHeadPopup("PHASE 2: BERSERK!", new Color(1f, 0.22f, 0.18f), 18);
            AudioManager.Instance.PlaySfx("player_respawn", 0.9f, -0.25f);
            var lib = PvfxVfxLibrary.Instance;
            if (lib != null && lib.bossEnrage != null)
            {
                SpellImpactVfx.Spawn(transform.position, lib.bossEnrage, 15f, 2.2f);
            }
            if (!isSummonedEcho)
            {
                SummonMinionSlimes(2);
            }
        }

        private void SpawnSummonedEcho()
        {
            Vector3 spawnPos = transform.position + new Vector3(Random.value > 0.5f ? 2.6f : -2.6f, -1.2f, 0f);
            var lib = PvfxVfxLibrary.Instance;
            if (lib != null && lib.bossPortal != null)
            {
                SpellImpactVfx.Spawn(spawnPos, lib.bossPortal, 16f, 2.0f);
            }
            AudioManager.Instance.PlaySfx("spell_darkness_cast", 0.9f, -0.1f);

            var echoGo = new GameObject("MinotaurEcho");
            echoGo.transform.position = spawnPos;
            var echo = echoGo.AddComponent<MinotaurBossEnemy2D>();
            int wave = _waveManager != null ? _waveManager.CurrentWave : 1;
            echo.ConfigureForWave(wave, _waveManager, asEcho: true);
            _activeEcho = echo;
            _waveManager?.RegisterSpawnedEnemy(echoGo);
        }

        private void SummonMinionSlimes(int count)
        {
            var ctrl = Resources.Load<RuntimeAnimatorController>("Animations/Slime_Red")
                       ?? Resources.Load<RuntimeAnimatorController>("Animations/Slime_Green");
            if (ctrl == null) return;

            for (int i = 0; i < count; i++)
            {
                Vector2 offset = new Vector2(i == 0 ? -2.4f : 2.4f, -1.4f);
                var slimeGo = new GameObject($"BossMinionSlime_{i + 1}");
                slimeGo.transform.position = transform.position + (Vector3)offset;
                var slime = slimeGo.AddComponent<SlimeEnemy2D>();
                slime.animatorController = ctrl;
                var anim = slimeGo.GetComponent<Animator>();
                if (anim != null) anim.runtimeAnimatorController = ctrl;
                _waveManager?.RegisterSpawnedEnemy(slimeGo);
            }
        }

        private void UpdateVisualsAndAnimator()
        {
            if (_anim != null)
            {
                bool moving = (_state == BossState.Chase || _state == BossState.Charging) && _moveVel.sqrMagnitude > 0.05f;
                _anim.SetBool(IsMovingHash, moving);
                _anim.SetFloat(MoveSpeedMultHash, _state == BossState.Charging ? 2.3f : (_isPhase2 ? 1.25f : 1f));
            }

            if (_sr == null) return;
            _sr.sortingOrder = Mathf.RoundToInt(-transform.position.y * 10f);

            if (_hitFlashTimer > 0f)
            {
                _sr.color = new Color(1f, 0.45f, 0.45f, 1f);
            }
            else if (_armorBrokenTimer > 0f)
            {
                _sr.color = new Color(1f, 0.88f, 0.55f, 1f);
            }
            else if (_isPhase2)
            {
                _sr.color = new Color(1f, 0.72f, 0.72f, 1f);
            }
            else if (_wetTimer > 0f)
            {
                _sr.color = new Color(0.65f, 0.82f, 1f, 1f);
            }
            else if (isSummonedEcho)
            {
                _sr.color = new Color(0.75f, 0.55f, 0.95f, 0.92f);
            }
            else
            {
                _sr.color = Color.white;
            }
        }

        private void HideTelegraphs()
        {
            if (_slamTelegraphSr != null) _slamTelegraphSr.enabled = false;
            if (_chargeLaneSr != null) _chargeLaneSr.enabled = false;
        }

        private void CreateTelegraphVisuals()
        {
            var slamGo = new GameObject("SlamTelegraph");
            slamGo.transform.SetParent(transform, false);
            slamGo.transform.localPosition = new Vector3(0f, -0.35f, 0f);
            const int size = 48;
            var circleTex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            Vector2 c = new Vector2(23.5f, 23.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), c);
                    if (d >= 20f && d <= 23f) circleTex.SetPixel(x, y, new Color(1f, 0.22f, 0.15f, 0.85f));
                    else if (d < 20f) circleTex.SetPixel(x, y, new Color(1f, 0.25f, 0.15f, 0.22f));
                    else circleTex.SetPixel(x, y, Color.clear);
                }
            }
            circleTex.Apply();
            _slamTelegraphSr = slamGo.AddComponent<SpriteRenderer>();
            _slamTelegraphSr.sprite = Sprite.Create(circleTex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 24f);
            _slamTelegraphSr.sortingOrder = 2;
            _slamTelegraphSr.enabled = false;

            var laneGo = new GameObject("ChargeTelegraph");
            laneGo.transform.SetParent(transform, false);
            laneGo.transform.localPosition = Vector3.zero;
            var laneTex = new Texture2D(64, 16, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    bool edge = y <= 1 || y >= 14;
                    laneTex.SetPixel(x, y, edge ? new Color(1f, 0.25f, 0.15f, 0.8f) : new Color(1f, 0.35f, 0.15f, 0.28f));
                }
            }
            laneTex.Apply();
            _chargeLaneSr = laneGo.AddComponent<SpriteRenderer>();
            _chargeLaneSr.sprite = Sprite.Create(laneTex, new Rect(0, 0, 64, 16), new Vector2(0.05f, 0.5f), 16f);
            _chargeLaneSr.sortingOrder = 2;
            _chargeLaneSr.enabled = false;
        }

        private void OnGUI()
        {
            if (isSummonedEcho || !_hasEngaged || currentHealth <= 0f) return;

            if (_barStatusStyle == null)
            {
                _barStatusStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(1f, 0.86f, 0.52f) }
                };
            }

            string statusTag = _armorBrokenTimer > 0f
                ? "ARMOR CRACKED"
                : (_isPhase2 ? "PHASE 2 · BERSERK" : "ARMORED");

            if (_hbUnder != null && _hbProgress != null && _hbOver != null)
            {
                float barW = 360f;
                float barH = barW * (_hbUnder.height / (float)_hbUnder.width);
                float x = (Screen.width - barW) * 0.5f;
                float y = 8f;

                GUI.DrawTexture(new Rect(x, y, barW, barH), _hbUnder);

                GUI.BeginGroup(new Rect(x, y, barW * HealthNormalized, barH));
                GUI.DrawTexture(new Rect(0f, 0f, barW, barH), _hbProgress);
                GUI.EndGroup();

                GUI.DrawTexture(new Rect(x, y, barW, barH), _hbOver);
                GUI.Label(new Rect(x, y + barH - 22f, barW, 20f), $"{statusTag} · {Mathf.CeilToInt(currentHealth)}/{Mathf.CeilToInt(maxHealth)}", _barStatusStyle);
            }
            else
            {
                float barW = Mathf.Min(420f, Screen.width * 0.55f);
                float x = (Screen.width - barW) * 0.5f;
                GUI.Label(new Rect(x, 12f, barW, 20f), $"MINOTAUR [{statusTag}] · {Mathf.CeilToInt(currentHealth)}/{Mathf.CeilToInt(maxHealth)}", _barStatusStyle);
            }
        }
    }
}
