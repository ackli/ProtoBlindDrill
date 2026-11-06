using UnityEngine;

/// <summary>
/// Orbe de buff dropé par un bloc Relique. Au contact du joueur, déclenche
/// <see cref="PlayerStats.ApplyRandomBuff"/> (tire 1 des 3 buffs cumulatifs),
/// ajoute un peu de score, puis se détruit.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class BuffOrb : MonoBehaviour
{
    [Header("Buff")]
    [Tooltip("Points de pourcentage ajoutés au buff tiré. 10 = +10 % par relique ramassée.")]
    [SerializeField] private float buffPercentPerPick = 10f;

    [Header("Reward Side-Effect")]
    [Tooltip("Points de score ajoutés via ScoreManager (bonus de découverte).")]
    [SerializeField] private int scoreValue = 50;

    [Header("Juice")]
    [Tooltip("Vitesse de rotation de l'orbe (deg/s).")]
    [SerializeField] private float spinSpeed = 120f;

    private void Awake()
    {
        var col = GetComponent<Collider2D>();
        if (!col.isTrigger) Debug.LogWarning("[BuffOrb] Collider2D devrait être en isTrigger.", this);
    }

    private void Update()
    {
        if (spinSpeed != 0f)
            transform.Rotate(0f, 0f, spinSpeed * Time.deltaTime, Space.Self);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var stats = other.GetComponent<PlayerStats>();
        if (stats == null) stats = other.GetComponentInParent<PlayerStats>();
        if (stats == null) return;

        stats.ApplyRandomBuff(buffPercentPerPick);
        if (ScoreManager.Instance != null) ScoreManager.Instance.AddLight(scoreValue);

        Debug.Log($"[BUFF-ORB] Collected at ({transform.position.x:F1},{transform.position.y:F1}) → " +
                  $"stats: Overheal +{stats.OverhealBonusPercent}%, Damage +{stats.DamageBonusPercent}%, Defense +{stats.DefenseBonusPercent}% | +{scoreValue} score.");
        Destroy(gameObject);
    }
}
