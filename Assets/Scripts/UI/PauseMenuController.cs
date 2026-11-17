using System;
using Alchemy.Audio;
using Alchemy.SpellDrawing;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// In-game pause "spellbook". Esc toggles it, freezes time, and offers Resume / Options / Main Menu / Quit.
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    [Serializable]
    public struct RuneSprite
    {
        public string spellId;
        public Sprite sprite;
    }

    [Header("Pages")]
    [SerializeField] private GameObject root;
    [SerializeField] private GameObject mainPage;
    [SerializeField] private GameObject optionsPage;
    [SerializeField] private GameObject confirmPage;

    [Header("Loaded spell card")]
    [SerializeField] private Image loadedIcon;
    [SerializeField] private TMP_Text loadedName;
    [SerializeField] private TMP_Text loadedInfo;
    [SerializeField] private Image[] chargePips;
    [SerializeField] private Color pipEmpty = new Color(0.42f, 0.29f, 0.17f, 0.3f);
    [SerializeField] private RuneSprite[] runeSprites;

    [Header("Confirm page")]
    [SerializeField] private TMP_Text confirmTitle;
    [SerializeField] private TMP_Text confirmLine1;
    [SerializeField] private TMP_Text confirmLine2;
    [SerializeField] private TMP_Text confirmHint;

    [Header("Options page")]
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private TMP_Text volumeValue;
    [SerializeField] private Toggle fullscreenToggle;

    [Header("Scenes")]
    [SerializeField] private string mainMenuScene = "MainMenu";

    private SpellDrawingController _spells;
    private bool _paused;
    private bool _confirmingQuit;

    public bool IsPaused => _paused;

    private void Awake()
    {
        _spells = FindFirstObjectByType<SpellDrawingController>();
        if (volumeSlider != null)
        {
            volumeSlider.SetValueWithoutNotify(AudioListener.volume);
            volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
            UpdateVolumeLabel(AudioListener.volume);
        }
        if (fullscreenToggle != null)
        {
            fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
            fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);
        }
        if (root != null) root.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_paused) ApplyTime(false);
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;

        if (!_paused)
        {
            // Don't pause in the middle of drawing a rune (right mouse held = time-slow drawing).
            if (Mouse.current != null && Mouse.current.rightButton.isPressed) return;
            Pause();
        }
        else if (mainPage.activeSelf) Resume();
        else ShowMain();
    }

    public void Pause()
    {
        _paused = true;
        root.SetActive(true);
        RefreshLoadedCard();
        SetPage(mainPage);
        ApplyTime(true);
        AudioManager.Instance.PlayUi("ui_pause");
    }

    public void Resume()
    {
        _paused = false;
        root.SetActive(false);
        ApplyTime(false);
        AudioManager.Instance.PlayUi("ui_resume");
    }

    public void ShowMain()
    {
        AudioManager.Instance.PlayUi("ui_click");
        SetPage(mainPage);
    }

    public void ShowOptions()
    {
        AudioManager.Instance.PlayUi("ui_click");
        SetPage(optionsPage);
    }

    public void AskMainMenu()
    {
        AudioManager.Instance.PlayUi("ui_click");
        _confirmingQuit = false;
        SetConfirmText("LEAVE?", "Return to the Main Menu?", "Progress since your last save\nwill be lost.", "Same page for Quit");
        SetPage(confirmPage);
    }

    public void AskQuit()
    {
        AudioManager.Instance.PlayUi("ui_click");
        _confirmingQuit = true;
        SetConfirmText("QUIT?", "Close the game?", "Progress since your last save\nwill be lost.", "Same page for Main Menu");
        SetPage(confirmPage);
    }

    public void ConfirmLeave()
    {
        AudioManager.Instance.PlayUi("ui_click");
        ApplyTime(false);
        _paused = false;
        if (_confirmingQuit)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
        else
        {
            SceneManager.LoadScene(mainMenuScene);
        }
    }

    private void SetPage(GameObject page)
    {
        mainPage.SetActive(page == mainPage);
        optionsPage.SetActive(page == optionsPage);
        confirmPage.SetActive(page == confirmPage);
    }

    private void SetConfirmText(string title, string line1, string line2, string hint)
    {
        confirmTitle.text = title;
        confirmLine1.text = line1;
        confirmLine2.text = line2;
        confirmHint.text = hint;
    }

    private static void ApplyTime(bool paused)
    {
        Time.timeScale = paused ? 0f : 1f;
        Time.fixedDeltaTime = 0.02f;
    }

    private void RefreshLoadedCard()
    {
        var spell = _spells != null ? _spells.activeSpell : null;
        int charges = _spells != null ? _spells.remainingCharges : 0;
        bool loaded = spell != null && charges > 0;

        loadedName.text = loaded ? spell.displayName : "Empty";
        loadedInfo.text = loaded ? $"{charges} left · {_spells.activeBoostMultiplier:0.0}x boost" : "Draw a rune to load";
        loadedIcon.enabled = loaded;
        if (loaded)
        {
            foreach (var r in runeSprites)
            {
                if (string.Equals(r.spellId, spell.spellId, StringComparison.OrdinalIgnoreCase))
                {
                    loadedIcon.sprite = r.sprite;
                    break;
                }
            }
        }

        Color pipOn = loaded ? spell.spellColor : pipEmpty;
        pipOn.a = 1f;
        for (int i = 0; i < chargePips.Length; i++)
            chargePips[i].color = loaded && i < charges ? pipOn : pipEmpty;
    }

    private void OnVolumeChanged(float v)
    {
        AudioManager.Instance.SetMasterVolume(v);
        UpdateVolumeLabel(v);
    }

    private void UpdateVolumeLabel(float v)
    {
        if (volumeValue != null) volumeValue.text = Mathf.RoundToInt(v * 100f).ToString();
    }

    private static void OnFullscreenChanged(bool on) => Screen.fullScreen = on;
}
