using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Lie le rayon extérieur de la torche (Light2D Point) au ratio de santé du joueur.
/// Transition douce via MoveTowards, snap au premier Awake pour partir sur une
/// bonne valeur.
/// </summary>
[RequireComponent(typeof(PlayerHealth))]
public class TorchHealthLink : MonoBehaviour
{
    [Header("Torch")]
    [Tooltip("Light2D Point sur le joueur (le PlayerTorch enfant).")]
    [SerializeField] private Light2D torch;

    [Tooltip("Rayon à 0 HP (jamais atteint, le joueur meurt avant). Utilisé pour l'extrémité basse du lerp.")]
    [SerializeField] private float minOuterRadius = 1.5f;

    [Tooltip("Rayon à maxHealth. Au max de vie = pleine portée de vision.")]
    [SerializeField] private float maxOuterRadius = 7f;

    [Tooltip("Vitesse de transition du rayon (unités/s).")]
    [SerializeField] private float transitionSpeed = 4f;

    private PlayerHealth health;
    private float targetRadius;

    private void Awake()
    {
        health = GetComponent<PlayerHealth>();
    }

    private void OnEnable()
    {
        if (health != null)
        {
            health.OnHealthChanged += OnHealthChanged;
            UpdateTargetRadius(health.CurrentHealth, health.MaxHealth);
        }
        if (torch != null) torch.pointLightOuterRadius = targetRadius;
    }

    private void OnDisable()
    {
        if (health != null) health.OnHealthChanged -= OnHealthChanged;
    }

    private void OnHealthChanged(int current, int max) => UpdateTargetRadius(current, max);

    private void UpdateTargetRadius(int current, int max)
    {
        float t = max > 0 ? (float)current / max : 0f;
        targetRadius = Mathf.Lerp(minOuterRadius, maxOuterRadius, t);
    }

    private void Update()
    {
        if (torch == null) return;
        torch.pointLightOuterRadius = Mathf.MoveTowards(
            torch.pointLightOuterRadius, targetRadius, transitionSpeed * Time.deltaTime);
    }
}
