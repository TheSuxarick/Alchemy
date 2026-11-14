using System.Collections.Generic;
using System.IO;
using Alchemy.Gameplay;
using Alchemy.SpellDrawing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Alchemy.Editor
{
    public class SpellGlyphStudioWindow : EditorWindow
    {
        private List<SpellData> _allSpells = new List<SpellData>();
        private SpellData _selectedSpell;
        private readonly List<List<Vector2>> _pendingCanvasStrokes = new List<List<Vector2>>();
        private readonly List<Vector2> _activeCanvasStroke = new List<Vector2>();
        private bool _isDraggingCanvas;
        private string _newSpellName = "Lightning";
        private Color _newSpellColor = new Color(1f, 0.9f, 0.25f);
        private Vector2 _propsScrollPos;
        private string _statusMessage = "Select a spell, draw 1 or more strokes in the canvas, then click 'Save Multi-Stroke Ideal'!";

        private static readonly Color[] StrokePalette =
        {
            new Color(1f, 0.88f, 0.2f),
            new Color(0.35f, 0.95f, 1f),
            new Color(1f, 0.45f, 0.85f),
            new Color(0.55f, 1f, 0.45f)
        };

        [MenuItem("Tools/Alchemy/Spell Glyph Studio")]
        public static void OpenWindow()
        {
            var win = GetWindow<SpellGlyphStudioWindow>("Spell Glyph Studio");
            win.minSize = new Vector2(740, 500);
            win.RefreshSpellList();
        }

        public static void SetupDefaultSpellsAndScene()
        {
            EnsureDefaultSpellAssetsExist();

            var worldGen = Object.FindFirstObjectByType<SimpleWorldGenerator2D>();
            if (worldGen == null)
            {
                var worldGo = new GameObject("WorldMap");
                worldGen = worldGo.AddComponent<SimpleWorldGenerator2D>();
            }
            worldGen.GenerateWorld();
            EditorUtility.SetDirty(worldGen);

            var player = Object.FindFirstObjectByType<PlayerController2D>();
            if (player == null)
            {
                var playerGo = new GameObject("Player");
                playerGo.transform.position = Vector3.zero;
                player = playerGo.AddComponent<PlayerController2D>();
            }
            EditorUtility.SetDirty(player);

            var controller = Object.FindFirstObjectByType<SpellDrawingController>();
            if (controller == null)
            {
                var go = new GameObject("SpellDrawingSystem");
                controller = go.AddComponent<SpellDrawingController>();
            }

            controller.unlockedSpells.Clear();
            SpellData[] spells = Resources.LoadAll<SpellData>("Spells");
            controller.unlockedSpells.AddRange(spells);

            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = player.gameObject;
            Debug.Log("[Alchemy] Configured WorldMap, Player (WASD + Shift Dash), and SpellDrawingSystem in the scene!");
        }

        public static void EnsureDefaultSpellAssetsExist()
        {
            const string folderPath = "Assets/Resources/Spells";
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
                AssetDatabase.Refresh();
            }

            CreateOrLoadSpellAsset(folderPath, "fire", "Fire", new Color(1f, 0.35f, 0.15f), "Triangle (^) flame glyph.");
            CreateOrLoadSpellAsset(folderPath, "water", "Water", new Color(0.2f, 0.65f, 1f), "Horizontal wave (~) glyph.");
            CreateOrLoadSpellAsset(folderPath, "air", "Air", new Color(0.75f, 0.95f, 1f), "Outward spiral (@) glyph.");
            CreateOrLoadSpellAsset(folderPath, "earth", "Earth", new Color(0.45f, 0.82f, 0.3f), "Square box ([]) glyph.");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static SpellData CreateOrLoadSpellAsset(string folder, string id, string displayName, Color color, string desc)
        {
            string assetPath = $"{folder}/{displayName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<SpellData>(assetPath);
            if (existing != null)
            {
                if (!existing.HasValidTemplate())
                {
                    var def = GlyphRecognizer.GenerateDefaultTemplate(id);
                    if (def.Count >= 4)
                    {
                        existing.SetSingleIdealTemplate(def, "Default Ideal");
                        EditorUtility.SetDirty(existing);
                    }
                }
                return existing;
            }

            var spell = CreateInstance<SpellData>();
            spell.spellId = id;
            spell.displayName = displayName;
            spell.spellColor = color;
            spell.description = desc;
            spell.maxCharges = 10;
            spell.pinnedToOverlay = true;

            var defaultPts = GlyphRecognizer.GenerateDefaultTemplate(id);
            if (defaultPts.Count >= 4)
            {
                spell.SetSingleIdealTemplate(defaultPts, "Default Ideal");
            }

            AssetDatabase.CreateAsset(spell, assetPath);
            EditorUtility.SetDirty(spell);
            return spell;
        }

        private void OnEnable()
        {
            RefreshSpellList();
        }

        private void RefreshSpellList()
        {
            EnsureDefaultSpellAssetsExist();
            _allSpells.Clear();
            SpellData[] spells = Resources.LoadAll<SpellData>("Spells");
            if (spells != null)
            {
                _allSpells.AddRange(spells);
            }
            if (_selectedSpell == null && _allSpells.Count > 0)
            {
                _selectedSpell = _allSpells[0];
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();

            // Left Column: Spell List & Create New Spell
            EditorGUILayout.BeginVertical(GUILayout.Width(210));
            GUILayout.Label("Spells (Assets/Resources/Spells)", EditorStyles.boldLabel);

            if (GUILayout.Button("Sync All Spells to Scene", GUILayout.Height(26)))
            {
                SetupDefaultSpellsAndScene();
                RefreshSpellList();
                _statusMessage = "Synced all spells in Assets/Resources/Spells to the scene!";
            }

            GUILayout.Space(6);
            for (int i = 0; i < _allSpells.Count; i++)
            {
                var spell = _allSpells[i];
                if (spell == null) continue;

                Color prev = GUI.backgroundColor;
                if (_selectedSpell == spell) GUI.backgroundColor = spell.spellColor;

                int strokeCount = spell.GetPrimaryStrokeCount();
                string statusTag = spell.HasValidTemplate() ? $"{strokeCount} stroke{(strokeCount == 1 ? "" : "s")}" : "No Glyph";

                if (GUILayout.Button($"{spell.displayName} ({statusTag})", GUILayout.Height(28)))
                {
                    _selectedSpell = spell;
                    _pendingCanvasStrokes.Clear();
                    _activeCanvasStroke.Clear();
                    _statusMessage = $"Selected [{spell.displayName}]. Draw 1 or more strokes on the canvas, then click 'Save Multi-Stroke Ideal'.";
                }
                GUI.backgroundColor = prev;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Create New Spell", EditorStyles.boldLabel);
            _newSpellName = EditorGUILayout.TextField("Name", _newSpellName);
            _newSpellColor = EditorGUILayout.ColorField("Color", _newSpellColor);
            if (GUILayout.Button("+ Create New Spell Asset", GUILayout.Height(26)))
            {
                if (!string.IsNullOrWhiteSpace(_newSpellName))
                {
                    string cleanName = _newSpellName.Trim();
                    var created = CreateOrLoadSpellAsset(
                        "Assets/Resources/Spells",
                        cleanName.ToLowerInvariant().Replace(" ", "_"),
                        cleanName,
                        _newSpellColor,
                        "Custom alchemy spell.");
                    RefreshSpellList();
                    _selectedSpell = created;
                    _pendingCanvasStrokes.Clear();
                    _activeCanvasStroke.Clear();
                    _statusMessage = $"Created [{cleanName}]! Draw its multi-stroke rune on the canvas and click Save.";
                }
            }
            EditorGUILayout.EndVertical();

            // Middle Column: Scrollable Spell Properties & Stats
            if (_selectedSpell != null)
            {
                EditorGUILayout.BeginVertical(GUILayout.Width(290));
                _propsScrollPos = EditorGUILayout.BeginScrollView(_propsScrollPos);

                float prevLabelW = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 135f;

                EditorGUI.BeginChangeCheck();
                GUILayout.Label($"Properties — {_selectedSpell.displayName}", EditorStyles.boldLabel);
                _selectedSpell.displayName = EditorGUILayout.TextField("Display Name", _selectedSpell.displayName);
                _selectedSpell.spellColor = EditorGUILayout.ColorField("Spell Color", _selectedSpell.spellColor);
                _selectedSpell.pinnedToOverlay = EditorGUILayout.Toggle("Pinned to Overlay", _selectedSpell.pinnedToOverlay);
                _selectedSpell.allowReverseStroke = EditorGUILayout.Toggle("Allow Reverse Stroke", _selectedSpell.allowReverseStroke);
                _selectedSpell.allowAnyStrokeOrder = EditorGUILayout.Toggle("Allow Any Stroke Order", _selectedSpell.allowAnyStrokeOrder);

                _selectedSpell.ingredientA = (SpellData)EditorGUILayout.ObjectField("Ingredient A", _selectedSpell.ingredientA, typeof(SpellData), false);
                _selectedSpell.ingredientB = (SpellData)EditorGUILayout.ObjectField("Ingredient B", _selectedSpell.ingredientB, typeof(SpellData), false);

                GUILayout.Space(6);
                EditorGUILayout.LabelField("Elemental Attributes", EditorStyles.boldLabel);
                _selectedSpell.attributes = (SpellAttributeFlags)EditorGUILayout.EnumFlagsField("Attributes", _selectedSpell.attributes);
                _selectedSpell.knockbackForce = EditorGUILayout.Slider("Knockback Force", _selectedSpell.knockbackForce, 0f, 30f);
                _selectedSpell.burnDuration = EditorGUILayout.Slider("Burn Duration (s)", _selectedSpell.burnDuration, 0f, 12f);
                _selectedSpell.stunDuration = EditorGUILayout.Slider("Stun Duration (s)", _selectedSpell.stunDuration, 0f, 5f);

                GUILayout.Space(6);
                EditorGUILayout.LabelField("Stats & Animation", EditorStyles.boldLabel);
                _selectedSpell.maxCharges = EditorGUILayout.IntSlider("Max Charges", _selectedSpell.maxCharges, 1, 50);
                _selectedSpell.projectileSpeed = EditorGUILayout.Slider("Speed", _selectedSpell.projectileSpeed, 3f, 35f);
                _selectedSpell.projectileScale = EditorGUILayout.Slider("Size", _selectedSpell.projectileScale, 0.25f, 3.0f);
                _selectedSpell.projectileLifetime = EditorGUILayout.Slider("Range (Lifetime s)", _selectedSpell.projectileLifetime, 0.3f, 5.0f);
                _selectedSpell.baseDamage = EditorGUILayout.Slider("Base Damage", _selectedSpell.baseDamage, 1f, 200f);
                _selectedSpell.projectileFps = EditorGUILayout.Slider("Anim FPS", _selectedSpell.projectileFps, 4f, 40f);

                GUILayout.Space(4);
                if (GUILayout.Button("Inspect Asset (Frames / VFX)", GUILayout.Height(24)))
                {
                    Selection.activeObject = _selectedSpell;
                    EditorGUIUtility.PingObject(_selectedSpell);
                }

                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(_selectedSpell);
                }

                EditorGUIUtility.labelWidth = prevLabelW;
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();

                // Right Column: Full-Height Multi-Stroke Drawing Canvas
                EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                GUILayout.Label($"Multi-Stroke Rune Canvas — {_selectedSpell.displayName}", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                GUI.enabled = _pendingCanvasStrokes.Count > 0;
                Color prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.35f, 0.9f, 0.45f);
                if (GUILayout.Button($"Save Multi-Stroke Ideal ({_pendingCanvasStrokes.Count} strokes)", GUILayout.Height(28)))
                {
                    SavePendingStrokesToSelectedSpell();
                }
                GUI.backgroundColor = prevBg;

                if (GUILayout.Button("Undo Stroke", GUILayout.Height(28), GUILayout.Width(95)))
                {
                    if (_pendingCanvasStrokes.Count > 0)
                    {
                        _pendingCanvasStrokes.RemoveAt(_pendingCanvasStrokes.Count - 1);
                        _statusMessage = $"Removed last stroke. ({_pendingCanvasStrokes.Count} stroke(s) in draft)";
                    }
                }

                if (GUILayout.Button("Clear", GUILayout.Height(28), GUILayout.Width(65)))
                {
                    _pendingCanvasStrokes.Clear();
                    _activeCanvasStroke.Clear();
                    _statusMessage = "Cleared draft strokes. Showing saved template.";
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(4);
                Rect canvasRect = GUILayoutUtility.GetRect(260, 260, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                DrawInteractiveCanvas(canvasRect);

                EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void SavePendingStrokesToSelectedSpell()
        {
            if (_selectedSpell == null || _pendingCanvasStrokes.Count == 0) return;

            var bottomUpStrokes = new List<IReadOnlyList<Vector2>>(_pendingCanvasStrokes.Count);
            for (int s = 0; s < _pendingCanvasStrokes.Count; s++)
            {
                var raw = _pendingCanvasStrokes[s];
                if (raw.Count < 2) continue;

                // Convert GUI Y-down to standard bottom-up relative to the canvas
                var converted = new List<Vector2>(raw.Count);
                for (int i = 0; i < raw.Count; i++)
                {
                    converted.Add(new Vector2(raw[i].x, -raw[i].y));
                }
                bottomUpStrokes.Add(converted);
            }

            List<List<Vector2>> normalized = GlyphRecognizer.NormalizeMultiStrokesToUnitBox(bottomUpStrokes, 0.12f);
            _selectedSpell.SetMultiStrokeIdealTemplate(normalized, $"Ideal ({normalized.Count} strokes)");

            EditorUtility.SetDirty(_selectedSpell);
            AssetDatabase.SaveAssets();

            _statusMessage = $"Saved {normalized.Count}-stroke Ideal Rune for [{_selectedSpell.displayName}]!";
            _pendingCanvasStrokes.Clear();
            _activeCanvasStroke.Clear();
        }

        private void DrawInteractiveCanvas(Rect canvasRect)
        {
            EditorGUI.DrawRect(canvasRect, new Color(0.08f, 0.09f, 0.13f, 1f));

            EditorGUI.DrawRect(new Rect(canvasRect.x, canvasRect.center.y, canvasRect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            EditorGUI.DrawRect(new Rect(canvasRect.center.x, canvasRect.y, 1f, canvasRect.height), new Color(1f, 1f, 1f, 0.08f));

            bool hasDraft = _pendingCanvasStrokes.Count > 0 || _activeCanvasStroke.Count > 0;

            // 1. If no draft strokes are on the canvas, display the currently saved template
            if (!hasDraft && _selectedSpell.HasValidTemplate())
            {
                List<List<Vector2>> savedStrokes = _selectedSpell.idealTemplates[0].GetStrokes();
                Handles.BeginGUI();
                for (int s = 0; s < savedStrokes.Count; s++)
                {
                    var pts = savedStrokes[s];
                    if (pts.Count < 2) continue;
                    Color c = s == 0 ? _selectedSpell.spellColor : Color.Lerp(_selectedSpell.spellColor, Color.white, Mathf.Min(0.5f, s * 0.2f));
                    Handles.color = c;
                    for (int i = 1; i < pts.Count; i++)
                    {
                        Vector2 p0 = new Vector2(canvasRect.x + pts[i - 1].x * canvasRect.width, canvasRect.y + (1f - pts[i - 1].y) * canvasRect.height);
                        Vector2 p1 = new Vector2(canvasRect.x + pts[i].x * canvasRect.width, canvasRect.y + (1f - pts[i].y) * canvasRect.height);
                        Handles.DrawAAPolyLine(4f, p0, p1);
                    }

                    Vector2 start = new Vector2(canvasRect.x + pts[0].x * canvasRect.width, canvasRect.y + (1f - pts[0].y) * canvasRect.height);
                    DrawStrokeBadge(start, s + 1, c);
                }
                Handles.EndGUI();
            }

            // 2. Draw all pending draft strokes + active stroke
            if (hasDraft)
            {
                Handles.BeginGUI();
                for (int s = 0; s < _pendingCanvasStrokes.Count; s++)
                {
                    var pts = _pendingCanvasStrokes[s];
                    Color c = StrokePalette[s % StrokePalette.Length];
                    Handles.color = c;
                    for (int i = 1; i < pts.Count; i++)
                    {
                        Handles.DrawAAPolyLine(4.5f, pts[i - 1], pts[i]);
                    }
                    if (pts.Count > 0) DrawStrokeBadge(pts[0], s + 1, c);
                }

                if (_activeCanvasStroke.Count > 1)
                {
                    int nextIdx = _pendingCanvasStrokes.Count;
                    Color c = StrokePalette[nextIdx % StrokePalette.Length];
                    Handles.color = c;
                    for (int i = 1; i < _activeCanvasStroke.Count; i++)
                    {
                        Handles.DrawAAPolyLine(4.5f, _activeCanvasStroke[i - 1], _activeCanvasStroke[i]);
                    }
                    DrawStrokeBadge(_activeCanvasStroke[0], nextIdx + 1, c);
                }
                Handles.EndGUI();
            }

            // 3. Mouse Input for Multi-Stroke Drawing
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && canvasRect.Contains(e.mousePosition))
            {
                _isDraggingCanvas = true;
                _activeCanvasStroke.Clear();
                _activeCanvasStroke.Add(e.mousePosition);
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDrag && _isDraggingCanvas)
            {
                Vector2 clamped = new Vector2(
                    Mathf.Clamp(e.mousePosition.x, canvasRect.xMin + 2f, canvasRect.xMax - 2f),
                    Mathf.Clamp(e.mousePosition.y, canvasRect.yMin + 2f, canvasRect.yMax - 2f));

                if (_activeCanvasStroke.Count == 0 || Vector2.Distance(_activeCanvasStroke[_activeCanvasStroke.Count - 1], clamped) >= 3f)
                {
                    _activeCanvasStroke.Add(clamped);
                }
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseUp && _isDraggingCanvas)
            {
                _isDraggingCanvas = false;
                if (_activeCanvasStroke.Count >= 3)
                {
                    _pendingCanvasStrokes.Add(new List<Vector2>(_activeCanvasStroke));
                    _statusMessage = $"Added stroke #{_pendingCanvasStrokes.Count}. Draw another stroke or click 'Save Multi-Stroke Ideal'!";
                }
                _activeCanvasStroke.Clear();
                e.Use();
                Repaint();
            }
        }

        private static void DrawStrokeBadge(Vector2 pos, int number, Color color)
        {
            EditorGUI.DrawRect(new Rect(pos.x - 8, pos.y - 8, 16, 16), new Color(0.05f, 0.05f, 0.08f, 0.9f));
            var style = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            style.normal.textColor = color;
            GUI.Label(new Rect(pos.x - 8, pos.y - 8, 16, 16), number.ToString(), style);
        }
    }
}
