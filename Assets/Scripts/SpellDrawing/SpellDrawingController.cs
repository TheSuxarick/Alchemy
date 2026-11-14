using System.Collections.Generic;
using Alchemy.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Alchemy.SpellDrawing
{
    public class SpellDrawingController : MonoBehaviour
    {
        [Header("Available Spells")]
        public List<SpellData> unlockedSpells = new List<SpellData>();

        [Header("Time Slow Settings")]
        [Range(0.05f, 0.5f)]
        public float slowTimeScale = 0.2f;
        [Range(0.3f, 0.85f)]
        public float overlayDarkness = 0.68f;

        [Header("Active Wand State (Read-Only Preview)")]
        public SpellData activeSpell;
        public int remainingCharges;
        public float activeBoostMultiplier = 1f;
        public float lastPrecisionPercent;
        public float lastDrawDuration;

        // Multi-stroke drawing runtime state
        private bool _isTimeSlowActive;
        private bool _isDrawingStroke;
        private readonly List<List<Vector2>> _completedGuiStrokes = new List<List<Vector2>>();
        private readonly List<Vector2> _currentStrokePoints = new List<Vector2>();
        private float _runeStartTimeUnscaled;
        private GlyphMatchResult _liveMatchResult;
        private PlayerController2D _player;

        // Feedback banner
        private string _statusBannerText = "WASD: Move | SHIFT: Dash | Hold RIGHT CLICK: Draw Rune";
        private Color _statusBannerColor = Color.white;
        private float _statusBannerTimer = 5f;

        private Texture2D _whiteTex;
        private Texture2D _circleTex;
        private GUIStyle _headerStyle;
        private GUIStyle _subHeaderStyle;
        private GUIStyle _cardTitleStyle;
        private GUIStyle _liveCursorStyle;
        private GUIStyle _chargeCountStyle;
        private GUIStyle _hpCountStyle;
        private GUIStyle _pillLabelStyle;

        private void Awake()
        {
            _whiteTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _whiteTex.SetPixel(0, 0, Color.white);
            _whiteTex.Apply();

            _circleTex = CreateCircleTexture(128);

            EnsureDefaultSpellsLoaded();
        }

        private void Start()
        {
            _player = Object.FindFirstObjectByType<PlayerController2D>();
        }

        public void EnsureDefaultSpellsLoaded()
        {
            if (unlockedSpells == null)
            {
                unlockedSpells = new List<SpellData>();
            }
            unlockedSpells.RemoveAll(s => s == null);

            if (unlockedSpells.Count == 0)
            {
                SpellData[] loaded = Resources.LoadAll<SpellData>("Spells");
                if (loaded != null && loaded.Length > 0)
                {
                    unlockedSpells.AddRange(loaded);
                }
            }

            if (unlockedSpells.Count == 0)
            {
                unlockedSpells.Add(CreateRuntimeSpell("fire", "Fire", new Color(1f, 0.35f, 0.15f)));
                unlockedSpells.Add(CreateRuntimeSpell("water", "Water", new Color(0.2f, 0.65f, 1f)));
                unlockedSpells.Add(CreateRuntimeSpell("air", "Air", new Color(0.75f, 0.95f, 1f)));
                unlockedSpells.Add(CreateRuntimeSpell("earth", "Earth", new Color(0.45f, 0.82f, 0.3f)));
            }
        }

        private static SpellData CreateRuntimeSpell(string id, string title, Color color)
        {
            var spell = ScriptableObject.CreateInstance<SpellData>();
            spell.name = title;
            spell.spellId = id;
            spell.displayName = title;
            spell.spellColor = color;
            spell.maxCharges = 10;
            spell.pinnedToOverlay = true;
            spell.SetSingleIdealTemplate(GlyphRecognizer.GenerateDefaultTemplate(id), "Default Ideal");
            return spell;
        }

        private void Update()
        {
            ReadMouseState(
                out Vector2 mouseGuiPos,
                out bool rmbHeld,
                out bool lmbDown,
                out bool lmbHeld,
                out bool lmbUp);

            // 1. Right-Click Time-Slow State (Multi-stroke rune drawing)
            if (rmbHeld)
            {
                if (!_isTimeSlowActive)
                {
                    EnterTimeSlowMode();
                }

                if (lmbDown)
                {
                    BeginStroke(mouseGuiPos);
                }
                else if (lmbHeld && _isDrawingStroke)
                {
                    ContinueStroke(mouseGuiPos);
                }
                else if (lmbUp && _isDrawingStroke)
                {
                    CommitCurrentStroke();
                }
            }
            else
            {
                if (_isTimeSlowActive)
                {
                    if (_isDrawingStroke)
                    {
                        CommitCurrentStroke();
                    }
                    EvaluateCompletedRune();
                    ExitTimeSlowMode();
                }

                // 2. Outside Time-Slow: Left Click shoots 1 charge of the loaded spell
                if (lmbDown)
                {
                    TryShootChargedSpell();
                }
            }

            if (_statusBannerTimer > 0f)
            {
                _statusBannerTimer -= Time.unscaledDeltaTime;
            }
        }

        private void EnterTimeSlowMode()
        {
            _isTimeSlowActive = true;
            _isDrawingStroke = false;
            _completedGuiStrokes.Clear();
            _currentStrokePoints.Clear();
            _liveMatchResult = null;
            _runeStartTimeUnscaled = Time.unscaledTime;

            Time.timeScale = slowTimeScale;
            Time.fixedDeltaTime = 0.02f * Time.timeScale;
        }

        private void ExitTimeSlowMode()
        {
            _isTimeSlowActive = false;
            _isDrawingStroke = false;
            _completedGuiStrokes.Clear();
            _currentStrokePoints.Clear();
            _liveMatchResult = null;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
        }

        private void OnDisable()
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
        }

        private void BeginStroke(Vector2 guiPos)
        {
            if (_completedGuiStrokes.Count == 0)
            {
                _runeStartTimeUnscaled = Time.unscaledTime;
            }

            _isDrawingStroke = true;
            _currentStrokePoints.Clear();
            _currentStrokePoints.Add(guiPos);
            UpdateLiveMatch();
        }

        private void ContinueStroke(Vector2 guiPos)
        {
            if (_currentStrokePoints.Count == 0 ||
                Vector2.Distance(_currentStrokePoints[_currentStrokePoints.Count - 1], guiPos) >= 3f)
            {
                _currentStrokePoints.Add(guiPos);
            }

            UpdateLiveMatch();
        }

        private void CommitCurrentStroke()
        {
            _isDrawingStroke = false;

            if (_currentStrokePoints.Count >= 2 && GlyphRecognizer.PathLength(_currentStrokePoints) >= 12f)
            {
                _completedGuiStrokes.Add(new List<Vector2>(_currentStrokePoints));
            }

            _currentStrokePoints.Clear();
            UpdateLiveMatch();
        }

        private void UpdateLiveMatch()
        {
            List<IReadOnlyList<Vector2>> allBottomUp = BuildBottomUpStrokeList(includeActiveStroke: true);
            if (allBottomUp.Count == 0)
            {
                _liveMatchResult = null;
                return;
            }

            float elapsed = Mathf.Max(0.05f, Time.unscaledTime - _runeStartTimeUnscaled);
            _liveMatchResult = GlyphRecognizer.Recognize(allBottomUp, unlockedSpells, elapsed);
        }

        private void EvaluateCompletedRune()
        {
            List<IReadOnlyList<Vector2>> allBottomUp = BuildBottomUpStrokeList(includeActiveStroke: false);
            if (allBottomUp.Count == 0)
            {
                return;
            }

            float duration = Mathf.Max(0.05f, Time.unscaledTime - _runeStartTimeUnscaled);
            GlyphMatchResult match = GlyphRecognizer.Recognize(allBottomUp, unlockedSpells, duration);

            if (match.isRecognized && match.bestSpell != null)
            {
                activeSpell = match.bestSpell;
                remainingCharges = activeSpell.maxCharges;
                activeBoostMultiplier = match.boostMultiplier;
                lastPrecisionPercent = match.precisionPercent;
                lastDrawDuration = match.drawDurationSeconds;

                ShowBanner(
                    $"+{remainingCharges} {activeSpell.displayName} ({match.boostMultiplier:F1}x)",
                    activeSpell.spellColor,
                    1.2f);
            }
            else
            {
                ShowBanner(
                    "Fizzle!",
                    new Color(1f, 0.35f, 0.35f),
                    1.0f);
            }
        }

        private List<IReadOnlyList<Vector2>> BuildBottomUpStrokeList(bool includeActiveStroke)
        {
            var list = new List<IReadOnlyList<Vector2>>(_completedGuiStrokes.Count + 1);
            for (int i = 0; i < _completedGuiStrokes.Count; i++)
            {
                list.Add(ConvertGuiPointsToBottomUp(_completedGuiStrokes[i]));
            }

            if (includeActiveStroke && _currentStrokePoints.Count >= 3 && GlyphRecognizer.PathLength(_currentStrokePoints) >= 12f)
            {
                list.Add(ConvertGuiPointsToBottomUp(_currentStrokePoints));
            }

            return list;
        }

        private void TryShootChargedSpell()
        {
            if (activeSpell == null || remainingCharges <= 0)
            {
                ShowBanner("0 Charges!", new Color(1f, 0.75f, 0.3f), 1.0f);
                return;
            }

            remainingCharges--;

            if (_player == null)
            {
                _player = Object.FindFirstObjectByType<PlayerController2D>();
            }

            Vector3 spawnPos = _player != null ? _player.WandTipWorldPosition : Vector3.zero;
            Vector3 mouseWorld = _player != null ? _player.GetMouseWorldPosition() : Vector3.right;
            Vector2 dir = (Vector2)(mouseWorld - spawnPos);

            SpellProjectile2D.SpawnFromSpell(
                spawnPos,
                dir,
                activeSpell,
                activeBoostMultiplier);

            if (remainingCharges == 0)
            {
                ShowBanner("0 Charges!", new Color(0.85f, 0.85f, 0.9f), 1.0f);
            }
        }

        private static List<Vector2> ConvertGuiPointsToBottomUp(IReadOnlyList<Vector2> guiPoints)
        {
            var list = new List<Vector2>(guiPoints.Count);
            float h = Screen.height;
            for (int i = 0; i < guiPoints.Count; i++)
            {
                list.Add(new Vector2(guiPoints[i].x, h - guiPoints[i].y));
            }
            return list;
        }

        private static void ReadMouseState(
            out Vector2 guiPos,
            out bool rmbHeld,
            out bool lmbDown,
            out bool lmbHeld,
            out bool lmbUp)
        {
            if (Mouse.current != null)
            {
                Vector2 screenPos = Mouse.current.position.ReadValue();
                guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
                rmbHeld = Mouse.current.rightButton.isPressed;
                lmbDown = Mouse.current.leftButton.wasPressedThisFrame;
                lmbHeld = Mouse.current.leftButton.isPressed;
                lmbUp = Mouse.current.leftButton.wasReleasedThisFrame;
                return;
            }

            Vector3 legacyPos = Input.mousePosition;
            guiPos = new Vector2(legacyPos.x, Screen.height - legacyPos.y);
            rmbHeld = Input.GetMouseButton(1);
            lmbDown = Input.GetMouseButtonDown(0);
            lmbHeld = Input.GetMouseButton(0);
            lmbUp = Input.GetMouseButtonUp(0);
        }

        private void ShowBanner(string text, Color color, float duration)
        {
            _statusBannerText = text;
            _statusBannerColor = color;
            _statusBannerTimer = duration;

            if (_player == null)
            {
                _player = Object.FindFirstObjectByType<PlayerController2D>();
            }
            if (_player != null)
            {
                _player.SpawnHeadPopup(text, color, 15);
            }
        }

        private void EnsureStyles()
        {
            if (_headerStyle != null) return;

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _headerStyle.normal.textColor = Color.white;

            _subHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _subHeaderStyle.normal.textColor = new Color(0.85f, 0.85f, 0.9f);

            _cardTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _cardTitleStyle.normal.textColor = Color.white;

            _liveCursorStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            _chargeCountStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _chargeCountStyle.normal.textColor = Color.white;

            _hpCountStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _hpCountStyle.normal.textColor = Color.white;

            _pillLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _pillLabelStyle.normal.textColor = Color.white;
        }

        private void OnGUI()
        {
            EnsureStyles();

            if (_isTimeSlowActive)
            {
                DrawRect(new Rect(0, 0, Screen.width, Screen.height), new Color(0.03f, 0.03f, 0.06f, overlayDarkness));
                DrawPinnedSpellsBar();
                DrawLiveRecognitionBreakdown();
                DrawAllUserStrokes();
            }

            DrawWandHud();
        }

        private void DrawPinnedSpellsBar()
        {
            const float cardW = 116f;
            const float cardH = 128f;
            const float spacing = 14f;

            int pinnedCount = 0;
            for (int i = 0; i < unlockedSpells.Count; i++)
            {
                if (unlockedSpells[i] != null && unlockedSpells[i].pinnedToOverlay) pinnedCount++;
            }
            if (pinnedCount == 0) return;

            float totalW = pinnedCount * cardW + (pinnedCount - 1) * spacing;
            float startX = (Screen.width - totalW) * 0.5f;
            float topY = 18f;

            int idx = 0;
            for (int i = 0; i < unlockedSpells.Count; i++)
            {
                SpellData spell = unlockedSpells[i];
                if (spell == null || !spell.pinnedToOverlay) continue;

                Rect cardRect = new Rect(startX + idx * (cardW + spacing), topY, cardW, cardH);
                bool isBestLive = _liveMatchResult != null &&
                                  _liveMatchResult.bestSpell == spell &&
                                  _liveMatchResult.precisionPercent >= GlyphRecognizer.RecognitionThresholdPercent;

                Color borderColor = isBestLive ? spell.spellColor : new Color(1f, 1f, 1f, 0.22f);

                DrawRect(cardRect, borderColor);
                DrawRect(new Rect(cardRect.x + 2, cardRect.y + 2, cardRect.width - 4, cardRect.height - 4),
                    new Color(0.08f, 0.09f, 0.13f, 0.94f));

                int strokeCount = spell.GetPrimaryStrokeCount();
                string titleLabel = strokeCount > 1 ? $"{spell.displayName} ({strokeCount}x)" : spell.displayName;
                _cardTitleStyle.normal.textColor = spell.spellColor;
                GUI.Label(new Rect(cardRect.x, cardRect.y + 4, cardRect.width, 22), titleLabel, _cardTitleStyle);

                Rect previewRect = new Rect(cardRect.x + 14, cardRect.y + 28, cardRect.width - 28, cardRect.width - 28);
                DrawRect(previewRect, new Color(0.03f, 0.03f, 0.05f, 0.9f));

                if (spell.HasValidTemplate())
                {
                    DrawTemplatePreviewGui(spell.idealTemplates[0].GetStrokes(), previewRect, spell.spellColor);
                }

                idx++;
            }
        }

        private void DrawTemplatePreviewGui(IReadOnlyList<List<Vector2>> normalizedBottomUpStrokes, Rect box, Color color)
        {
            if (normalizedBottomUpStrokes == null || normalizedBottomUpStrokes.Count == 0) return;

            for (int s = 0; s < normalizedBottomUpStrokes.Count; s++)
            {
                var pts = normalizedBottomUpStrokes[s];
                if (pts == null || pts.Count < 2) continue;

                Color strokeCol = s == 0 ? color : Color.Lerp(color, Color.white, Mathf.Min(0.45f, s * 0.18f));
                for (int i = 1; i < pts.Count; i++)
                {
                    Vector2 p0 = pts[i - 1];
                    Vector2 p1 = pts[i];

                    Vector2 gui0 = new Vector2(box.x + p0.x * box.width, box.y + (1f - p0.y) * box.height);
                    Vector2 gui1 = new Vector2(box.x + p1.x * box.width, box.y + (1f - p1.y) * box.height);

                    DrawLineGui(gui0, gui1, strokeCol, 2.5f);
                }

                Vector2 start = pts[0];
                Vector2 startGui = new Vector2(box.x + start.x * box.width, box.y + (1f - start.y) * box.height);
                DrawRect(new Rect(startGui.x - 2.5f, startGui.y - 2.5f, 5f, 5f), Color.white);
            }
        }

        private void DrawLiveRecognitionBreakdown()
        {
            int strokeCount = _completedGuiStrokes.Count + (_isDrawingStroke ? 1 : 0);
            _subHeaderStyle.normal.textColor = new Color(0.8f, 0.9f, 1f);
            GUI.Label(new Rect(0, 154, Screen.width, 26),
                $"TIME SLOWED (0.2x) — Draw with LEFT CLICK (Strokes: {strokeCount}) | Release RIGHT CLICK to Activate",
                _subHeaderStyle);

            if (_liveMatchResult == null || _liveMatchResult.allScores.Count == 0) return;

            var best = _liveMatchResult.bestSpell;
            float pct = _liveMatchResult.precisionPercent;
            bool passes = pct >= GlyphRecognizer.RecognitionThresholdPercent;

            string liveText = best != null
                ? $"Seeing: {best.displayName.ToUpper()} ({pct:F0}%)  |  Est. Boost: {(_liveMatchResult.boostMultiplier > 0 ? $"{_liveMatchResult.boostMultiplier:F2}x" : "Fizzle <60%")}"
                : $"Drawing ({_liveMatchResult.drawnStrokeCount}-stroke rune)...";

            _headerStyle.normal.textColor = (best != null && passes) ? best.spellColor : new Color(1f, 0.5f, 0.5f);
            GUI.Label(new Rect(0, 180, Screen.width, 32), liveText, _headerStyle);

            float panelW = 230f;
            float panelH = 36f + _liveMatchResult.allScores.Count * 28f;
            Rect panelRect = new Rect(Screen.width - panelW - 20f, 160f, panelW, panelH);
            DrawRect(panelRect, new Color(0.06f, 0.07f, 0.11f, 0.9f));

            _cardTitleStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(panelRect.x, panelRect.y + 6, panelRect.width, 22), "Live Match Scores", _cardTitleStyle);

            for (int i = 0; i < _liveMatchResult.allScores.Count; i++)
            {
                var entry = _liveMatchResult.allScores[i];
                float rowY = panelRect.y + 34f + i * 26f;

                Rect barBg = new Rect(panelRect.x + 12f, rowY, panelRect.width - 24f, 20f);
                DrawRect(barBg, new Color(0.15f, 0.16f, 0.22f, 1f));

                float fillW = barBg.width * Mathf.Clamp01(entry.precisionPercent / 100f);
                Color barColor = entry.precisionPercent >= GlyphRecognizer.RecognitionThresholdPercent
                    ? entry.spell.spellColor
                    : new Color(0.45f, 0.45f, 0.5f, 0.8f);
                DrawRect(new Rect(barBg.x, barBg.y, fillW, barBg.height), barColor);

                _cardTitleStyle.normal.textColor = Color.white;
                string strokeTag = entry.expectedStrokeCount > 1 ? $" ({entry.expectedStrokeCount}s)" : "";
                GUI.Label(barBg, $"{entry.spell.displayName}{strokeTag}: {entry.precisionPercent:F0}%", _cardTitleStyle);
            }
        }

        private void DrawAllUserStrokes()
        {
            Color strokeColor = Color.white;
            if (_liveMatchResult != null && _liveMatchResult.bestSpell != null &&
                _liveMatchResult.precisionPercent >= GlyphRecognizer.RecognitionThresholdPercent)
            {
                strokeColor = _liveMatchResult.bestSpell.spellColor;
            }

            // Draw all committed strokes so far
            for (int s = 0; s < _completedGuiStrokes.Count; s++)
            {
                var pts = _completedGuiStrokes[s];
                for (int i = 1; i < pts.Count; i++)
                {
                    DrawLineGui(pts[i - 1], pts[i], strokeColor, 5f);
                }
            }

            // Draw active stroke being dragged right now
            if (_currentStrokePoints.Count >= 2)
            {
                for (int i = 1; i < _currentStrokePoints.Count; i++)
                {
                    DrawLineGui(_currentStrokePoints[i - 1], _currentStrokePoints[i], strokeColor, 5f);
                }

                if (_isDrawingStroke && _liveMatchResult != null && _liveMatchResult.bestSpell != null)
                {
                    Vector2 tip = _currentStrokePoints[_currentStrokePoints.Count - 1];
                    _liveCursorStyle.normal.textColor = strokeColor;
                    GUI.Label(
                        new Rect(tip.x + 18f, tip.y - 14f, 260f, 28f),
                        $"{_liveMatchResult.bestSpell.displayName} {_liveMatchResult.precisionPercent:F0}%",
                        _liveCursorStyle);
                }
            }
        }

        private void DrawWandHud()
        {
            if (_player == null)
            {
                _player = Object.FindFirstObjectByType<PlayerController2D>();
            }

            const float orbSize = 74f;
            const float margin = 20f;
            const float orbSpacing = 14f;
            float baseY = Screen.height - orbSize - margin;

            // --------------------------------------------------------------
            // 1. LEFT ORB: Diablo-Style Red Health Orb (Vertical Liquid Fill)
            // --------------------------------------------------------------
            float hpPct = _player != null ? _player.HealthNormalized : 1f;
            int hpInt = _player != null ? Mathf.CeilToInt(_player.currentHealth) : 100;

            Rect hpOuterRect = new Rect(margin, baseY, orbSize, orbSize);
            Rect hpInnerRect = new Rect(hpOuterRect.x + 5f, hpOuterRect.y + 5f, orbSize - 10f, orbSize - 10f);

            // Outer dark-gold / bronze rim + dark empty glass interior
            DrawCircle(hpOuterRect, new Color(0.28f, 0.22f, 0.20f, 0.95f));
            DrawCircle(hpInnerRect, new Color(0.14f, 0.04f, 0.05f, 0.94f));

            // Clip vertically from bottom to top based on current HP %
            if (hpPct > 0.001f)
            {
                float fillH = hpInnerRect.height * hpPct;
                Rect clipRect = new Rect(hpInnerRect.x, hpInnerRect.y + (hpInnerRect.height - fillH), hpInnerRect.width, fillH);
                GUI.BeginGroup(clipRect);
                // Draw full circle shifted relative to clip group so only bottom hpPct% of circle appears!
                Rect shiftedCircle = new Rect(0f, -(hpInnerRect.height - fillH), hpInnerRect.width, hpInnerRect.height);
                DrawCircle(shiftedCircle, new Color(0.86f, 0.14f, 0.18f, 0.96f));
                // Subtle bright liquid surface line at the top of the blood level
                if (hpPct < 0.97f)
                {
                    DrawRect(new Rect(0f, 0f, hpInnerRect.width, 2f), new Color(1f, 0.52f, 0.52f, 0.85f));
                }
                GUI.EndGroup();
            }

            _hpCountStyle.normal.textColor = Color.white;
            GUI.Label(hpOuterRect, hpInt.ToString(), _hpCountStyle);

            // Pill label above HP Orb (dark background + constant white text)
            DrawPillTag(new Rect(hpOuterRect.x + 8f, hpOuterRect.y - 24f, orbSize - 16f, 20f), "HP");

            // --------------------------------------------------------------
            // 2. RIGHT ORB: Spell Charge Orb (Element Outer Ring + Dark Navy Inner)
            // --------------------------------------------------------------
            bool hasSpell = activeSpell != null && remainingCharges > 0;
            Color elemColor = hasSpell ? activeSpell.spellColor : new Color(0.42f, 0.48f, 0.58f, 1f);

            Rect spellOuterRect = new Rect(margin + orbSize + orbSpacing, baseY, orbSize, orbSize);
            Rect spellInnerRect = new Rect(spellOuterRect.x + 5f, spellOuterRect.y + 5f, orbSize - 10f, orbSize - 10f);

            // Outer ring changes to active element color; inner orb is rich dark navy-blue!
            DrawCircle(spellOuterRect, new Color(elemColor.r, elemColor.g, elemColor.b, 0.95f));
            DrawCircle(spellInnerRect, new Color(0.08f, 0.14f, 0.26f, 0.95f));

            _chargeCountStyle.normal.textColor = Color.white;
            GUI.Label(spellOuterRect, remainingCharges.ToString(), _chargeCountStyle);

            if (hasSpell)
            {
                string spellTag = activeSpell.displayName.ToUpper();
                float pillW = Mathf.Max(orbSize + 6f, spellTag.Length * 9f + 16f);
                Rect pillRect = new Rect(spellOuterRect.center.x - pillW * 0.5f, spellOuterRect.y - 24f, pillW, 20f);
                DrawPillTag(pillRect, spellTag);
            }
        }

        private void DrawPillTag(Rect rect, string text)
        {
            // High-contrast dark pill background with subtle light border and pure white text
            DrawRect(rect, new Color(1f, 1f, 1f, 0.25f));
            DrawRect(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f),
                new Color(0.06f, 0.08f, 0.12f, 0.92f));
            _pillLabelStyle.normal.textColor = Color.white;
            GUI.Label(rect, text, _pillLabelStyle);
        }

        private void DrawCircle(Rect rect, Color color)
        {
            if (_circleTex == null) return;
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _circleTex);
            GUI.color = prev;
        }

        private static Texture2D CreateCircleTexture(int diameter)
        {
            var tex = new Texture2D(diameter, diameter, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear
            };
            float r = diameter * 0.5f;
            Vector2 center = new Vector2(r - 0.5f, r - 0.5f);
            for (int y = 0; y < diameter; y++)
            {
                for (int x = 0; x < diameter; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    float alpha = Mathf.Clamp01((r - dist) * 0.85f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply();
            return tex;
        }

        private void DrawRect(Rect rect, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _whiteTex);
            GUI.color = prev;
        }

        private void DrawLineGui(Vector2 pointA, Vector2 pointB, Color color, float width)
        {
            Vector2 delta = pointB - pointA;
            float len = delta.magnitude;
            if (len < 0.01f) return;

            Matrix4x4 prevMatrix = GUI.matrix;
            Color prevColor = GUI.color;

            GUI.color = color;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(angle, pointA);
            GUI.DrawTexture(new Rect(pointA.x, pointA.y - width * 0.5f, len, width), _whiteTex);

            GUI.matrix = prevMatrix;
            GUI.color = prevColor;
        }
    }
}
