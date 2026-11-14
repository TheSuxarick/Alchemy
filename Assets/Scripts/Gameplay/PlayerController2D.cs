using Alchemy.SpellDrawing;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Alchemy.Gameplay
{
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CircleCollider2D))]
    public class PlayerController2D : LivingEntity2D
    {
        [Header("Combat")]
        public float invincibilityDuration = 0.85f;

        [Header("Movement")]
        public float moveSpeed = 5.5f;
        public float mapBoundsHalfExtent = 22.5f;

        [Header("Dash (Left Shift)")]
        public float dashSpeed = 16.5f;
        public float dashDuration = 0.16f;
        public float dashCooldown = 0.75f;

        // Runtime state
        private Rigidbody2D _rb;
        private SpriteRenderer _bodyRenderer;
        private Animator _bodyAnimator;
        private bool _usingAnimatedWizard;
        private Transform _visualRoot;
        private Transform _wandPivot;
        private Transform _wandTip;
        private SpriteRenderer _wandTipRenderer;
        private SpriteRenderer _wandGlowRenderer;
        private SpellDrawingController _spellController;

        private static readonly int MoveXHash = Animator.StringToHash("MoveX");
        private static readonly int MoveYHash = Animator.StringToHash("MoveY");
        private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

        private Vector2 _moveInput;
        private Vector2 _dashDirection = Vector2.right;
        private float _dashTimeRemaining;
        private float _dashCooldownRemaining;
        private float _ghostSpawnTimer;
        private float _walkBobTimer;
        private float _invincibilityTimer;
        private Vector2 _damageKnockbackVel;

        public bool IsDashing => _dashTimeRemaining > 0f;
        public bool IsInvincible => IsDashing || _invincibilityTimer > 0f;
        public float DashCooldownNormalized => dashCooldown > 0f ? Mathf.Clamp01(_dashCooldownRemaining / dashCooldown) : 0f;
        public Vector3 WandTipWorldPosition => _wandTip != null ? _wandTip.position : transform.position;

        private void Awake()
        {
            currentHealth = maxHealth;
            headPopupOffsetY = 0.75f;

            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            var col = GetComponent<CircleCollider2D>();
            col.radius = 0.35f;

            BuildCharacterAndWandVisuals();
        }

        private void Start()
        {
            _spellController = Object.FindFirstObjectByType<SpellDrawingController>();
        }

        public void BuildCharacterAndWandVisuals()
        {
            // Clear old procedural children if re-initialized
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(transform.GetChild(i).gameObject);
            }

            // 1. Visual Root (Sprout Lands Animator or fallback procedural sprite)
            var visualGo = new GameObject("VisualRoot");
            visualGo.transform.SetParent(transform, false);
            _visualRoot = visualGo.transform;

            _bodyRenderer = visualGo.AddComponent<SpriteRenderer>();
            _bodyRenderer.sortingOrder = 5;

            var wizardController = Resources.Load<RuntimeAnimatorController>("Animations/Wizard");
            if (wizardController != null)
            {
                _bodyAnimator = visualGo.AddComponent<Animator>();
                _bodyAnimator.runtimeAnimatorController = wizardController;
                _usingAnimatedWizard = true;

                if (wizardController.animationClips != null && wizardController.animationClips.Length > 0)
                {
                    // Sample frame 0 so character sprite is visible even in Edit Mode
                    wizardController.animationClips[0].SampleAnimation(visualGo, 0f);
                }
            }
            else
            {
                _bodyRenderer.sprite = CreateAlchemistPixelSprite();
                _usingAnimatedWizard = false;
            }

            // 2. Cute Sprout-Lands-Style Pixel Wand & Tiny 2x2 Crystal Tip
            var wandPivotGo = new GameObject("WandPivot");
            wandPivotGo.transform.SetParent(transform, false);
            wandPivotGo.transform.localPosition = new Vector3(0f, 0.22f, 0f);
            _wandPivot = wandPivotGo.transform;

            var wandShaftGo = new GameObject("WandShaft");
            wandShaftGo.transform.SetParent(_wandPivot, false);
            wandShaftGo.transform.localPosition = new Vector3(0.32f, 0f, 0f);
            var shaftSr = wandShaftGo.AddComponent<SpriteRenderer>();
            shaftSr.sprite = CreateWandShaftSprite();
            shaftSr.sortingOrder = 10;

            var wandTipGo = new GameObject("WandTip");
            wandTipGo.transform.SetParent(_wandPivot, false);
            wandTipGo.transform.localPosition = new Vector3(0.47f, 0f, 0f);
            _wandTip = wandTipGo.transform;

            _wandTipRenderer = wandTipGo.AddComponent<SpriteRenderer>();
            _wandTipRenderer.sprite = CreateTinyCrystalTipSprite();
            _wandTipRenderer.sortingOrder = 12;

            var glowGo = new GameObject("WandElementalGlow");
            glowGo.transform.SetParent(_wandTip, false);
            glowGo.transform.localPosition = Vector3.zero;
            _wandGlowRenderer = glowGo.AddComponent<SpriteRenderer>();
            _wandGlowRenderer.sprite = CreateSoftGlowSprite(10);
            _wandGlowRenderer.sortingOrder = 11;
        }

        private void Update()
        {
            ReadMovementAndDashInput(out Vector2 move, out bool dashPressed);
            _moveInput = move.sqrMagnitude > 1f ? move.normalized : move;

            float dt = Time.deltaTime;

            if (_dashCooldownRemaining > 0f)
            {
                _dashCooldownRemaining -= dt;
            }

            if (_invincibilityTimer > 0f)
            {
                _invincibilityTimer -= dt;
            }

            if (_damageKnockbackVel.sqrMagnitude > 0.01f)
            {
                _damageKnockbackVel = Vector2.Lerp(_damageKnockbackVel, Vector2.zero, dt * 10f);
            }

            // Trigger Dash on Left Shift
            if (dashPressed && _dashTimeRemaining <= 0f && _dashCooldownRemaining <= 0f)
            {
                StartDash();
            }

            if (_dashTimeRemaining > 0f)
            {
                _dashTimeRemaining -= dt;
                _ghostSpawnTimer -= dt;
                if (_ghostSpawnTimer <= 0f)
                {
                    _ghostSpawnTimer = 0.035f;
                    SpawnDashGhost();
                }
            }

            UpdateVisualsAndWandAim(dt);
        }

        private void FixedUpdate()
        {
            Vector2 velocity = _dashTimeRemaining > 0f
                ? _dashDirection * dashSpeed
                : (_moveInput * moveSpeed + _damageKnockbackVel);

            Vector2 nextPos = _rb.position + velocity * Time.fixedDeltaTime;
            nextPos.x = Mathf.Clamp(nextPos.x, -mapBoundsHalfExtent, mapBoundsHalfExtent);
            nextPos.y = Mathf.Clamp(nextPos.x == nextPos.x ? nextPos.y : 0f, -mapBoundsHalfExtent, mapBoundsHalfExtent);

            _rb.MovePosition(nextPos);
        }

        public bool TakeDamage(float damage, Vector2 hitDirection)
        {
            if (IsInvincible || damage <= 0f) return false;

            currentHealth = Mathf.Max(0f, currentHealth - damage);
            _invincibilityTimer = invincibilityDuration;

            SpawnDamagePopup(damage, new Color(1f, 0.32f, 0.35f));

            Vector2 kbDir = hitDirection.sqrMagnitude > 0.001f ? hitDirection.normalized : Vector2.down;
            _damageKnockbackVel = kbDir * 7.5f;

            if (currentHealth <= 0f)
            {
                currentHealth = maxHealth;
                _rb.position = Vector2.zero;
                transform.position = Vector3.zero;
                _invincibilityTimer = 1.5f;
                SpawnHeadPopup("Respawned!", new Color(0.55f, 1f, 0.65f), 15);
            }
            return true;
        }

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 targetCamPos = new Vector3(transform.position.x, transform.position.y, cam.transform.position.z);
                cam.transform.position = Vector3.Lerp(cam.transform.position, targetCamPos, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
            }
        }

        private void StartDash()
        {
            if (_moveInput.sqrMagnitude > 0.01f)
            {
                _dashDirection = _moveInput.normalized;
            }
            else
            {
                Vector3 mouseWorld = GetMouseWorldPosition();
                Vector2 toMouse = (Vector2)(mouseWorld - transform.position);
                _dashDirection = toMouse.sqrMagnitude > 0.001f ? toMouse.normalized : Vector2.right;
            }

            _dashTimeRemaining = dashDuration;
            _dashCooldownRemaining = dashCooldown;
            _ghostSpawnTimer = 0f;
            SpawnDashGhost();
        }

        private void SpawnDashGhost()
        {
            if (_bodyRenderer == null || _bodyRenderer.sprite == null) return;

            var ghostGo = new GameObject("DashGhost");
            ghostGo.transform.position = _visualRoot.position;
            ghostGo.transform.localScale = _visualRoot.lossyScale;

            var sr = ghostGo.AddComponent<SpriteRenderer>();
            sr.sprite = _bodyRenderer.sprite;
            sr.flipX = _bodyRenderer.flipX;
            sr.sortingOrder = _bodyRenderer.sortingOrder - 1;

            Color tint = (_spellController != null && _spellController.activeSpell != null && _spellController.remainingCharges > 0)
                ? _spellController.activeSpell.spellColor
                : new Color(0.55f, 0.85f, 1f, 1f);
            sr.color = new Color(tint.r, tint.g, tint.b, 0.55f);

            Destroy(ghostGo, 0.18f);
        }

        private void UpdateVisualsAndWandAim(float dt)
        {
            // Dynamic depth sorting by Y position so player walks behind/in front of trees naturally
            int baseSort = Mathf.RoundToInt(-transform.position.y * 10f);
            if (_bodyRenderer != null)
            {
                _bodyRenderer.sortingOrder = baseSort;
                if (_invincibilityTimer > 0f)
                {
                    float flash = Mathf.PingPong(Time.unscaledTime * 16f, 1f);
                    _bodyRenderer.color = Color.Lerp(Color.white, new Color(1f, 0.35f, 0.35f, 0.75f), flash);
                }
                else
                {
                    _bodyRenderer.color = Color.white;
                }
            }

            // Walk bobbing (only needed for static fallback sprite)
            if (!_usingAnimatedWizard)
            {
                if (_moveInput.sqrMagnitude > 0.01f && _dashTimeRemaining <= 0f)
                {
                    _walkBobTimer += dt * 12f;
                    float bobY = Mathf.Abs(Mathf.Sin(_walkBobTimer)) * 0.07f;
                    _visualRoot.localPosition = new Vector3(0f, bobY, 0f);
                }
                else
                {
                    _visualRoot.localPosition = Vector3.Lerp(_visualRoot.localPosition, Vector3.zero, dt * 12f);
                }
            }

            // Aim wand toward mouse cursor
            Vector3 mouseWorld = GetMouseWorldPosition();
            Vector2 aimDir = (Vector2)(mouseWorld - transform.position);
            if (aimDir.sqrMagnitude > 0.0001f)
            {
                float angle = Mathf.Atan2(aimDir.y, aimDir.x) * Mathf.Rad2Deg;
                _wandPivot.rotation = Quaternion.Euler(0f, 0f, angle);

                if (!_usingAnimatedWizard && _bodyRenderer != null && Mathf.Abs(aimDir.x) > 0.05f)
                {
                    _bodyRenderer.flipX = aimDir.x < 0f;
                }
            }

            if (_usingAnimatedWizard && _bodyAnimator != null)
            {
                bool isMoving = _moveInput.sqrMagnitude > 0.01f;
                Vector2 facing = isMoving ? _moveInput.normalized : (aimDir.sqrMagnitude > 0.001f ? aimDir.normalized : Vector2.down);
                _bodyAnimator.SetBool(IsMovingHash, isMoving);
                _bodyAnimator.SetFloat(MoveXHash, facing.x);
                _bodyAnimator.SetFloat(MoveYHash, facing.y);
            }

            // Update Wand Elemental Glow based on charged spell
            if (_spellController == null)
            {
                _spellController = Object.FindFirstObjectByType<SpellDrawingController>();
            }

            bool isCharged = _spellController != null &&
                             _spellController.activeSpell != null &&
                             _spellController.remainingCharges > 0;

            if (isCharged)
            {
                Color raw = _spellController.activeSpell.spellColor;
                // Ensure dark spells (like Darkness) still glow vibrant violet instead of pitch black
                float lum = 0.299f * raw.r + 0.587f * raw.g + 0.114f * raw.b;
                Color elemColor = lum < 0.25f
                    ? Color.Lerp(raw, new Color(0.68f, 0.38f, 1f, 1f), 0.75f)
                    : raw;

                _wandTipRenderer.color = elemColor;

                float pulse = 0.8f + Mathf.Sin(Time.unscaledTime * 7f) * 0.12f;
                float boostScale = Mathf.Clamp(_spellController.activeBoostMultiplier, 1f, 1.35f);
                _wandGlowRenderer.transform.localScale = Vector3.one * (pulse * boostScale);
                _wandGlowRenderer.color = new Color(elemColor.r, elemColor.g, elemColor.b, 0.28f);
            }
            else
            {
                _wandTipRenderer.color = new Color(0.88f, 0.92f, 0.98f, 1f);
                _wandGlowRenderer.color = new Color(1f, 1f, 1f, 0f);
            }
        }

        public Vector3 GetMouseWorldPosition()
        {
            Vector2 screenPos;
            if (Mouse.current != null)
            {
                screenPos = Mouse.current.position.ReadValue();
            }
            else
            {
                screenPos = Input.mousePosition;
            }

            Camera cam = Camera.main;
            if (cam == null) return transform.position + Vector3.right;

            Vector3 world = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -cam.transform.position.z));
            world.z = 0f;
            return world;
        }

        private static void ReadMovementAndDashInput(out Vector2 move, out bool dashPressed)
        {
            move = Vector2.zero;
            dashPressed = false;

            if (Keyboard.current != null)
            {
                var kb = Keyboard.current;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;

                dashPressed = kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame;
                return;
            }

            move.x = Input.GetAxisRaw("Horizontal");
            move.y = Input.GetAxisRaw("Vertical");
            dashPressed = Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift);
        }

        private static Sprite CreateAlchemistPixelSprite()
        {
            const int w = 20;
            const int h = 24;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };

            Color robeDark = new Color(0.18f, 0.20f, 0.34f, 1f);
            Color robeMain = new Color(0.25f, 0.28f, 0.46f, 1f);
            Color trimGold = new Color(0.88f, 0.72f, 0.28f, 1f);
            Color skin = new Color(0.95f, 0.80f, 0.66f, 1f);
            Color hatDark = new Color(0.14f, 0.15f, 0.26f, 1f);
            Color shadow = new Color(0f, 0f, 0f, 0.3f);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color c = Color.clear;

                    // Ground shadow
                    if (y >= 1 && y <= 3 && x >= 5 && x <= 14)
                    {
                        c = shadow;
                    }
                    // Robe body (RimWorld-like rounded pawn silhouette)
                    else if (y >= 3 && y <= 13 && x >= 5 && x <= 14)
                    {
                        bool edge = (x == 5 || x == 14 || y == 3);
                        c = edge ? robeDark : robeMain;
                        if (x == 9 || x == 10) c = trimGold;
                    }
                    // Head
                    else if (y >= 13 && y <= 17 && x >= 6 && x <= 13)
                    {
                        c = skin;
                        // Eyes
                        if (y == 15 && (x == 8 || x == 11)) c = new Color(0.12f, 0.12f, 0.16f, 1f);
                    }
                    // Wizard Hat brim & peak
                    else if (y == 18 && x >= 4 && x <= 15)
                    {
                        c = hatDark;
                    }
                    else if (y == 19 && x >= 6 && x <= 13)
                    {
                        c = trimGold;
                    }
                    else if (y >= 20 && y <= 22 && x >= 7 + (y - 20) && x <= 12 - (y - 20))
                    {
                        c = robeMain;
                    }

                    tex.SetPixel(x, y, c);
                }
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.35f), 16f);
        }

        private static Sprite CreateWandShaftSprite()
        {
            // 8x4 pixel wand with Sprout Lands purple-slate outline (#584563) + warm wood core
            const int w = 8;
            const int h = 4;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };

            Color outline = new Color(0.345f, 0.271f, 0.388f, 1f); // #584563 Sprout Lands outline
            Color woodLight = new Color(0.82f, 0.60f, 0.42f, 1f);
            Color woodMid = new Color(0.68f, 0.46f, 0.30f, 1f);
            Color goldBand = new Color(0.95f, 0.84f, 0.48f, 1f);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (y == 0 || y == h - 1 || x == 0)
                    {
                        tex.SetPixel(x, y, outline);
                    }
                    else if (x >= w - 2)
                    {
                        tex.SetPixel(x, y, goldBand);
                    }
                    else
                    {
                        tex.SetPixel(x, y, y == 2 ? woodLight : woodMid);
                    }
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f);
        }

        private static Sprite CreateTinyCrystalTipSprite()
        {
            // 4x4 pixel crystal tip (2x2 glowing center + soft Sprout Lands outline corners)
            const int size = 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };
            Color outline = new Color(0.345f, 0.271f, 0.388f, 0.85f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool isCorner = (x == 0 || x == size - 1) && (y == 0 || y == size - 1);
                    bool isCenter = x >= 1 && x <= 2 && y >= 1 && y <= 2;
                    if (isCenter) tex.SetPixel(x, y, Color.white);
                    else if (!isCorner) tex.SetPixel(x, y, outline);
                    else tex.SetPixel(x, y, Color.clear);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
        }

        private static Sprite CreateCircleSprite(int size, float alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float radius = size * 0.45f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), center);
                    tex.SetPixel(x, y, d <= radius ? new Color(1f, 1f, 1f, alpha) : Color.clear);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
        }

        private static Sprite CreateSoftGlowSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear
            };
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float maxR = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), center);
                    float a = Mathf.Clamp01(1f - (d / maxR));
                    a = a * a;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
        }
    }
}
