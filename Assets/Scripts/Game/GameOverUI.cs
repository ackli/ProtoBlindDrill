using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Panneau Game Over. S'abonne à <see cref="PlayerStats.OnGameOver"/> et :
///   1. active le panneau (root GameObject sur lequel il est posé)
///   2. le bouton <see cref="restartButton"/> recharge la scène courante.
/// </summary>
public class GameOverUI : MonoBehaviour
{
    [Tooltip("Référence au PlayerStats. Auto-résolu par tag 'Player' si laissé vide.")]
    [SerializeField] private PlayerStats playerStats;

    [Tooltip("Racine du panneau à activer au Game Over. Si vide, utilise ce GameObject.")]
    [SerializeField] private GameObject panelRoot;

    [Tooltip("Bouton qui recharge la scène. Son onClick est relié à RestartScene().")]
    [SerializeField] private Button restartButton;

    private void Start()
    {
        if (panelRoot == null) panelRoot = gameObject;
        panelRoot.SetActive(false);

        if (playerStats == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) playerStats = go.GetComponent<PlayerStats>();
        }
        if (playerStats == null)
        {
            Debug.LogWarning("[GameOverUI] PlayerStats introuvable — Game Over ne s'affichera pas.", this);
            return;
        }
        playerStats.OnGameOver += HandleGameOver;

        if (restartButton != null)
            restartButton.onClick.AddListener(RestartScene);
    }

    private void OnDestroy()
    {
        if (playerStats != null) playerStats.OnGameOver -= HandleGameOver;
        if (restartButton != null) restartButton.onClick.RemoveListener(RestartScene);
    }

    private void HandleGameOver()
    {
        if (panelRoot != null) panelRoot.SetActive(true);
    }

    public void RestartScene()
    {
        var scene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(scene.buildIndex);
    }
}
