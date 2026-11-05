using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Contrôleur du menu principal. Les deux méthodes publiques sont destinées à être branchées
/// sur les boutons "Jouer" et "Quitter" via UnityEvent.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    /// <summary>Charge la scène de jeu (SampleScene, build index 1).</summary>
    public void PlayGame()
    {
        SceneManager.LoadScene(1);
    }

    /// <summary>Quitte l'application. En Editor, arrête le Play mode.</summary>
    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
