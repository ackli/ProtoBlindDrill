using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD des buffs permanents de session (collectés via BuffOrb → PlayerStats.ApplyRandomBuff).
/// S'abonne à <see cref="PlayerStats.OnBuffsChanged"/> et reconstruit un Text listant
/// uniquement les stats > 0 %. À placer en bas-droite du Canvas.
/// </summary>
public class StatsUI : MonoBehaviour
{
    [Tooltip("Référence au PlayerStats. Auto-résolu par tag 'Player' si laissé vide.")]
    [SerializeField] private PlayerStats playerStats;

    [Tooltip("Text qui affiche la liste des buffs. Un par ligne, \"Buff: +X%\".")]
    [SerializeField] private Text statsText;

    [Tooltip("Texte de substitution affiché tant qu'aucun buff n'a été collecté — garantit que le composant reste visible et que le bug est diagnostiquable.")]
    [SerializeField] private string emptyPlaceholder = "Aucun buff";

    private readonly StringBuilder sb = new StringBuilder(128);

    private void Start()
    {
        if (playerStats == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) playerStats = go.GetComponent<PlayerStats>();
        }
        if (playerStats == null)
        {
            Debug.LogWarning("[StatsUI] PlayerStats introuvable.", this);
            enabled = false;
            return;
        }
        if (statsText == null)
        {
            Debug.LogWarning("[StatsUI] statsText non assigné — rien à peindre.", this);
            return;
        }
        playerStats.OnBuffsChanged += Refresh;
        Refresh();
    }

    private void OnDestroy()
    {
        if (playerStats != null) playerStats.OnBuffsChanged -= Refresh;
    }

    private void Refresh()
    {
        if (statsText == null) return;
        sb.Length = 0;
        if (playerStats.OverhealBonusPercent > 0f)
            sb.Append($"Overheal: +{playerStats.OverhealBonusPercent:0.#}%\n");
        if (playerStats.DamageBonusPercent > 0f)
            sb.Append($"Dégâts: +{playerStats.DamageBonusPercent:0.#}%\n");
        if (playerStats.DefenseBonusPercent > 0f)
            sb.Append($"Défense: +{playerStats.DefenseBonusPercent:0.#}%\n");

        // Trim trailing newline pour un rendu propre.
        if (sb.Length > 0 && sb[sb.Length - 1] == '\n') sb.Length--;

        // Rendu défensif : si aucun buff actif, on affiche un placeholder plutôt que de masquer le
        // Text. Le composant reste visible → un bug "UI invisible" devient immédiatement discernable
        // d'un bug "event jamais invoqué" (le placeholder resterait figé dans ce dernier cas).
        if (sb.Length == 0) sb.Append(emptyPlaceholder);

        statsText.text = sb.ToString();
        statsText.enabled = true;
    }
}
