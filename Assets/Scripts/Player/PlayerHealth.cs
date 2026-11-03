using System;
using UnityEngine;

/// <summary>
/// Points de vie du joueur + respawn. Le spawn point est capturé au Awake si
/// setSpawnOnStart est coché, sinon la valeur de l'Inspector est utilisée.
/// Expose des events UnityAction-like pour brancher plus tard une UI de vie
/// ou un screen shake sur la mort.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private int maxHealth = 3;

    [Header("Respawn")]
    [Tooltip("Au Awake, utiliser la position courante comme spawn point.")]
    [SerializeField] private bool setSpawnOnStart = true;

    [Tooltip("Spawn point manuel (ignoré si setSpawnOnStart = true).")]
    [SerializeField] private Vector3 manualSpawnPoint;

    public int CurrentHealth { get; private set; }
    public int MaxHealth => maxHealth;
    public bool IsAlive => CurrentHealth > 0;

    public event Action<int, int> OnHealthChanged;
    public event Action OnDeath;
    public event Action OnRespawn;

    private Rigidbody2D rb;
    private Vector3 spawnPoint;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spawnPoint = setSpawnOnStart ? transform.position : manualSpawnPoint;
        CurrentHealth = maxHealth;
    }

    public void TakeDamage(int amount)
    {
        if (!IsAlive) return;
        CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);

        if (CurrentHealth <= 0) Die();
    }

    public void Heal(int amount)
    {
        if (!IsAlive) return;
        CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
    }

    private void Die()
    {
        OnDeath?.Invoke();
        Respawn();
    }

    /// <summary>Replace le joueur au spawn point et reset HP + vélocité.</summary>
    public void Respawn()
    {
        transform.position = spawnPoint;
        if (rb != null) rb.linearVelocity = Vector2.zero;
        CurrentHealth = maxHealth;
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        OnRespawn?.Invoke();
    }

    public void SetSpawnPoint(Vector3 worldPos) => spawnPoint = worldPos;
}
