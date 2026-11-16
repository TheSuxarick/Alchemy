using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Button handlers for the Main Menu spellbook.</summary>
public class MainMenuController : MonoBehaviour
{
    [SerializeField] private string playScene = "SampleScene";
    [Tooltip("Leave empty until a practice scene exists.")]
    [SerializeField] private string spellPracticeScene = "";
    [Tooltip("Leave empty until an options scene exists.")]
    [SerializeField] private string optionsScene = "";

    public void Play() => Load(playScene);
    public void SpellPractice() => Load(spellPracticeScene);
    public void Options() => Load(optionsScene);

    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static void Load(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("MainMenuController: no scene assigned for this button yet.");
            return;
        }
        SceneManager.LoadScene(sceneName);
    }
}
