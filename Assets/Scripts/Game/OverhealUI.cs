using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Jauge UI de l'Overheal : <see cref="Image"/> en mode <c>Filled</c> dont le
/// <see cref="Image.fillAmount"/> reflète le ratio <c>LightOverheal / MaxOverheal</c>.
/// Écoute <see cref="PlayerStats.OnOverhealChanged"/> (abonnement Start / désabo OnDestroy).
/// </summary>
public class OverhealUI : MonoBehaviour
{
    [Tooltip("Référence au PlayerStats. Auto-résolu par tag 'Player' si laissé vide.")]
    [SerializeField] private PlayerStats playerStats;

    [Tooltip("Image en mode 'Filled' qui matérialise la jauge. Son fillAmount est piloté par ce script.")]
    [SerializeField] private Image overhealFill;

    [Tooltip("Masque automatiquement l'Image quand LightOverheal = 0 (évite d'afficher une jauge vide).")]
    [SerializeField] private bool hideWhenEmpty = true;

    private void Start()
    {
        if (playerStats == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) playerStats = go.GetComponent<PlayerStats>();
        }
        if (playerStats == null)
        {
            Debug.LogWarning("[OverhealUI] PlayerStats introuvable — jauge désactivée.", this);
            enabled = false;
            return;
        }
        if (overhealFill == null)
        {
            Debug.LogWarning("[OverhealUI] Image 'overhealFill' non assignée — rien à peindre.", this);
            return;
        }

        playerStats.OnOverhealChanged += HandleOverhealChanged;
        // Peinture initiale : PlayerStats.Start() lève déjà l'évènement, mais on couvre le cas
        // où OverhealUI est instancié / activé après le Start du joueur.
        HandleOverhealChanged(playerStats.LightOverheal, playerStats.MaxOverheal);
    }

    private void OnDestroy()
    {
        if (playerStats != null)
            playerStats.OnOverhealChanged -= HandleOverhealChanged;
    }

    private void HandleOverhealChanged(int current, int max)
    {
        if (overhealFill == null) return;
        float ratio = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;
        overhealFill.fillAmount = ratio;
        // Fallback robuste : pilote aussi localScale.x du RectTransform (pivot gauche) pour que
        // la jauge soit visible même si le sprite source n'a toujours pas de mesh de coupe.
        // Inoffensif si fillAmount suffit — les deux effets se superposent proprement.
        var rt = overhealFill.rectTransform;
        var scale = rt.localScale;
        scale.x = ratio;
        rt.localScale = scale;
        if (hideWhenEmpty)
            overhealFill.enabled = current > 0;
    }
}
