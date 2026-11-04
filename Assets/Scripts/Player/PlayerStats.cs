using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Gestionnaire de vie du joueur avec mécanique roguelite d'Excédent de Lumière.
/// - CurrentHealth : santé classique capée à MaxHealth.
/// - LightOverheal : "bouclier" de lumière capé à maxOverheal, drainé passivement.
///
/// Flux Heal : remplit d'abord CurrentHealth, le reliquat charge LightOverheal (capé).
/// Flux TakeDamage : absorbe d'abord via LightOverheal, le reliquat ampute CurrentHealth.
/// La dégénérescence ne touche JAMAIS CurrentHealth.
/// </summary>
public class PlayerStats : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private int maxHealth = 5;

    [Header("Light Overheal")]
    [Tooltip("Plafond de l'excédent de lumière (bouclier dégénératif).")]
    [SerializeField] private int maxOverheal = 3;

    [Tooltip("Intervalle (s) entre deux pertes passives de 1 point de LightOverheal.")]
    [SerializeField] private float overhealDrainInterval = 10f;

    public int MaxHealth => maxHealth;
    public int CurrentHealth { get; private set; }
    public int LightOverheal { get; private set; }
    public int MaxOverheal => maxOverheal;
    public bool IsAlive => CurrentHealth > 0;

    /// <summary>Levé après toute variation (Heal, TakeDamage, drain passif). Pour l'UI.</summary>
    public event Action OnStatsChanged;

    private Coroutine drainCoroutine;

    private void Awake()
    {
        CurrentHealth = maxHealth;
        LightOverheal = 0;
    }

    private void OnEnable()
    {
        if (overhealDrainInterval > 0f)
            drainCoroutine = StartCoroutine(OverhealDrainLoop());
    }

    private void OnDisable()
    {
        if (drainCoroutine != null)
        {
            StopCoroutine(drainCoroutine);
            drainCoroutine = null;
        }
    }

    private IEnumerator OverhealDrainLoop()
    {
        var wait = new WaitForSeconds(overhealDrainInterval);
        while (true)
        {
            yield return wait;
            if (LightOverheal > 0)
            {
                LightOverheal--;
                OnStatsChanged?.Invoke();
            }
        }
    }

    public void Heal(int amount)
    {
        if (amount <= 0 || !IsAlive) return;

        int missing = maxHealth - CurrentHealth;
        int toHealth = Mathf.Min(amount, missing);
        if (toHealth > 0) CurrentHealth += toHealth;

        int overflow = amount - toHealth;
        if (overflow > 0)
            LightOverheal = Mathf.Min(maxOverheal, LightOverheal + overflow);

        OnStatsChanged?.Invoke();
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || !IsAlive) return;

        int absorbed = Mathf.Min(LightOverheal, amount);
        LightOverheal -= absorbed;
        int remaining = amount - absorbed;
        if (remaining > 0)
            CurrentHealth = Mathf.Max(0, CurrentHealth - remaining);

        OnStatsChanged?.Invoke();

        if (CurrentHealth <= 0) Die();
    }

    private void Die()
    {
        Debug.Log("Game Over");
    }
}
