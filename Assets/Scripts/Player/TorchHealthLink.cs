using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Lie le rayon extérieur de la torche (Light2D Point) au ratio de santé du joueur.
/// Transition douce via MoveTowards, snap au premier Awake pour partir sur une
/// bonne valeur. Ignore le LightOverheal — c'est bien la santé classique qui pilote la vision.
/// </summary>
[RequireComponent(typeof(PlayerStats))]
public class TorchHealthLink : MonoBehaviour
{
    [Header("Torch")]
    [Tooltip("Light2D Point sur le joueur (le PlayerTorch enfant).")]
    [SerializeField] private Light2D torch;

    [Tooltip("Rayon à 0 HP (jamais atteint, le joueur meurt avant). Utilisé pour l'extrémité basse du lerp.")]
    [SerializeField] private float minOuterRadius = 2.25f;

    [Tooltip("Rayon à maxHealth. Au max de vie = pleine portée de vision.")]
    [SerializeField] private float maxOuterRadius = 10.5f;

    [Tooltip("Vitesse de transition du rayon (unités/s).")]
    [SerializeField] private float transitionSpeed = 4f;

    private PlayerStats stats;
    private float targetRadius;

    private void Awake()
    {
        stats = GetComponent<PlayerStats>();
    }

    private void OnEnable()
    {
        if (stats != null)
        {
            stats.OnStatsChanged += OnStatsChanged;
            UpdateTargetRadius();
        }
        if (torch != null) torch.pointLightOuterRadius = targetRadius;
    }

    private void OnDisable()
    {
        if (stats != null) stats.OnStatsChanged -= OnStatsChanged;
    }

    private void OnStatsChanged() => UpdateTargetRadius();

    private void UpdateTargetRadius()
    {
        if (stats == null) return;
        // Si le joueur est mort, on coupe la torche entièrement (radius 0) au lieu de rester à minOuterRadius.
        if (stats.CurrentHealth <= 0)
        {
            targetRadius = 0f;
            return;
        }
        float t = stats.MaxHealth > 0 ? (float)stats.CurrentHealth / stats.MaxHealth : 0f;
        targetRadius = Mathf.Lerp(minOuterRadius, maxOuterRadius, t);
    }

    private void Update()
    {
        if (torch == null) return;
        torch.pointLightOuterRadius = Mathf.MoveTowards(
            torch.pointLightOuterRadius, targetRadius, transitionSpeed * Time.deltaTime);
        if (torch.pointLightOuterRadius <= 0.01f) torch.pointLightOuterRadius = 0f; // snap au ras du zéro
    }
}
