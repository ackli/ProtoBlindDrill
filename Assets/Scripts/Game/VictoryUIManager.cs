using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Écoute <see cref="LevelObjectiveManager.OnLevelCleared"/> pour afficher le panel de victoire,
/// figer le temps, libérer le curseur, puis permettre un relancement propre via <see cref="RestartGame"/>.
///
/// Attache ce composant à un manager de scène (ex: _LEVEL_OBJECTIVE). Branche le bouton "Recommencer"
/// sur <see cref="RestartGame"/> dans l'Inspector, et drag-drop le panel UI dans <see cref="victoryPanel"/>.
/// </summary>
public class VictoryUIManager : MonoBehaviour
{
    [Tooltip("Conteneur UI plein écran. Désactivé par défaut, activé à la victoire.")]
    [SerializeField] private GameObject victoryPanel;

    private void Awake()
    {
        // Jeu 2D / visée à la souris : le curseur doit TOUJOURS être visible et libre.
        // Posé ici pour couvrir le cas où Unity retient un lock d'une session Play précédente.
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    private void OnEnable()
    {
        LevelObjectiveManager.OnLevelCleared += HandleVictory;
        // Sécurité : s'assure que le panel est caché au démarrage, même si la scène le laisse actif.
        if (victoryPanel != null) victoryPanel.SetActive(false);
    }

    private void OnDisable()
    {
        LevelObjectiveManager.OnLevelCleared -= HandleVictory;
    }

    private void HandleVictory()
    {
        if (victoryPanel != null) victoryPanel.SetActive(true);
        Time.timeScale = 0f;

        // Libère le curseur — important si une logique de gameplay le verrouille.
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    /// <summary>Remet timeScale à 1 et recharge la scène courante. Branche-la sur le bouton "Recommencer".</summary>
    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
