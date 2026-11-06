using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Gestionnaire de vie du joueur avec mécanique roguelite d'Excédent de Lumière.
/// - CurrentHealth : santé classique capée à MaxHealth. Draine passivement via healthDrainPerSecond
///   (mécanique "la torche consume la vie" — bypass volontaire du LightOverheal).
/// - LightOverheal : "bouclier" de lumière capé à maxOverheal. Draine séparément via
///   overhealDrainInterval, ne touche JAMAIS CurrentHealth.
///
/// Flux Heal : remplit d'abord CurrentHealth, le reliquat charge LightOverheal (capé).
/// Flux TakeDamage (externe) : absorbe d'abord via LightOverheal, le reliquat ampute CurrentHealth.
/// </summary>
public class PlayerStats : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private int maxHealth = 5;

    [Tooltip("Perte passive de CurrentHealth par seconde (ancienne mécanique du drain de torche). 0 = désactivé. 0.2 = 1 HP toutes les 5 s. Ne passe PAS par l'overheal, par design.")]
    [SerializeField] private float healthDrainPerSecond = 0.2f;

    [Header("Light Overheal")]
    [Tooltip("Plafond de l'excédent de lumière (bouclier dégénératif).")]
    [SerializeField] private int maxOverheal = 3;

    [Tooltip("Intervalle (s) entre deux pertes passives de 1 point de LightOverheal.")]
    [SerializeField] private float overhealDrainInterval = 10f;

    [Header("Respawn")]
    [Tooltip("Au Awake, capture la position courante comme spawn point.")]
    [SerializeField] private bool setSpawnOnStart = true;

    [Tooltip("Spawn point manuel (utilisé si setSpawnOnStart = false).")]
    [SerializeField] private Vector3 manualSpawnPoint;

    [Header("Telemetry")]
    [Tooltip("Intervalle (s) du rapport [PLAYER-STATUS]. 0 ou négatif = désactive le rapport périodique.")]
    [SerializeField] private float statusReportInterval = 5f;

    public int MaxHealth => maxHealth;
    public int CurrentHealth { get; private set; }
    public int LightOverheal { get; private set; }
    public int MaxOverheal => maxOverheal;
    public bool IsAlive => CurrentHealth > 0;

    /// <summary>Hook de triche : quand true, court-circuite TakeDamage et le drain passif. Piloté par DebugCheatController.</summary>
    public bool GodMode { get; set; }

    /// <summary>Levé après toute variation (Heal, TakeDamage, drain passif). Pour l'UI.</summary>
    public event Action OnStatsChanged;

    /// <summary>
    /// Levé uniquement quand <see cref="LightOverheal"/> change (gain via Heal, absorption via TakeDamage,
    /// drain passif via <see cref="OverhealDrainLoop"/>, reset via Respawn). Signature : (current, max).
    /// Pour l'UI dédiée de la jauge d'overheal (OverhealUI).
    /// </summary>
    public event Action<int, int> OnOverhealChanged;

    /// <summary>Levé juste avant le reset de position / stats dans Respawn().</summary>
    public event Action OnDeath;

    /// <summary>Levé juste après le reset complet dans Respawn().</summary>
    public event Action OnRespawn;

    /// <summary>
    /// Levé quand la santé tombe à 0 : plus de respawn automatique. Le joueur est figé
    /// (contrôles et physique désactivés) et l'UI Game Over peut s'y abonner pour afficher
    /// son panneau de recommencement.
    /// </summary>
    public event Action OnGameOver;

    private Rigidbody2D rb;
    private Vector3 spawnPoint;
    private Coroutine overhealDrainCoroutine;
    private float healthDrainAccumulator;
    private bool deathFired;

    // --- Télémétrie périodique ---
    private float nextStatusReportTime;
    private int recentDamageTaken;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spawnPoint = setSpawnOnStart ? transform.position : manualSpawnPoint;
        CurrentHealth = maxHealth;
        // Spawn avec l'Overheal plein : il sert de tempo-tampon qui verrouille CurrentHealth
        // (et donc la torche) tant qu'il n'est pas vide, cf. le drain passif dans Update().
        LightOverheal = maxOverheal;
        healthDrainAccumulator = 0f;
        deathFired = false;
        nextStatusReportTime = statusReportInterval > 0f ? Time.time + statusReportInterval : float.PositiveInfinity;
        recentDamageTaken = 0;
    }

    private void OnEnable()
    {
        if (overhealDrainInterval > 0f)
            overhealDrainCoroutine = StartCoroutine(OverhealDrainLoop());
    }

    private void OnDisable()
    {
        if (overhealDrainCoroutine != null)
        {
            StopCoroutine(overhealDrainCoroutine);
            overhealDrainCoroutine = null;
        }
    }

    private void Start()
    {
        // Propage l'état initial (HP=MaxHP, Overheal=0) à tous les listeners (TorchHealthLink, UI, ...).
        // Sans ça, la torche du joueur reste à son rayon de prefab jusqu'au premier dégât, ce qui donne
        // l'impression d'une "lumière qui s'allume en retard".
        OnStatsChanged?.Invoke();
        OnOverhealChanged?.Invoke(LightOverheal, maxOverheal);
    }

    private void Update()
    {
        TickStatusReport();

        // Drain passif de CurrentHealth ("la torche qui s'éteint"). VERROUILLÉ tant que
        // LightOverheal > 0 : l'overheal donne le tempo (via OverhealDrainLoop, 1 point / interval),
        // et c'est seulement quand ce tampon est entièrement vide que la santé — et donc le rayon
        // de la torche piloté par TorchHealthLink — recommence à décliner.
        if (!IsAlive) return;
        if (GodMode) return;
        if (healthDrainPerSecond <= 0f) return;
        if (LightOverheal > 0)
        {
            healthDrainAccumulator = 0f; // reset pour ne pas lâcher un gros chunk au moment où l'overheal tombe à 0
            return;
        }

        healthDrainAccumulator += healthDrainPerSecond * Time.deltaTime;
        while (healthDrainAccumulator >= 1f && IsAlive)
        {
            healthDrainAccumulator -= 1f;
            CurrentHealth = Mathf.Max(0, CurrentHealth - 1);
            recentDamageTaken += 1;
            OnStatsChanged?.Invoke();
            if (CurrentHealth <= 0) Die();
        }
    }

    private void TickStatusReport()
    {
        if (statusReportInterval <= 0f) return;
        if (Time.time < nextStatusReportTime) return;

        CombatTelemetry.PlayerStatus(Time.time, CurrentHealth, maxHealth, LightOverheal, recentDamageTaken);
        recentDamageTaken = 0;
        nextStatusReportTime = Time.time + statusReportInterval;
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
                OnOverhealChanged?.Invoke(LightOverheal, maxOverheal);
            }
        }
    }

    public void Heal(int amount)
    {
        if (amount <= 0 || !IsAlive) return;

        int prevOverheal = LightOverheal;

        int missing = maxHealth - CurrentHealth;
        int toHealth = Mathf.Min(amount, missing);
        if (toHealth > 0) CurrentHealth += toHealth;

        int overflow = amount - toHealth;
        if (overflow > 0)
            LightOverheal = Mathf.Min(maxOverheal, LightOverheal + overflow);

        OnStatsChanged?.Invoke();
        if (LightOverheal != prevOverheal)
            OnOverhealChanged?.Invoke(LightOverheal, maxOverheal);
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || !IsAlive) return;
        if (GodMode) return;

        int prevHealth = CurrentHealth;
        int absorbed = Mathf.Min(LightOverheal, amount);
        LightOverheal -= absorbed;
        int remaining = amount - absorbed;
        if (remaining > 0)
            CurrentHealth = Mathf.Max(0, CurrentHealth - remaining);

        int healthLoss = prevHealth - CurrentHealth;
        recentDamageTaken += amount;
        CombatTelemetry.PlayerHit(amount, absorbed, healthLoss, CurrentHealth, maxHealth, LightOverheal);

        OnStatsChanged?.Invoke();
        if (absorbed > 0)
            OnOverhealChanged?.Invoke(LightOverheal, maxOverheal);

        if (CurrentHealth <= 0) Die();
    }

    private void Die()
    {
        if (deathFired) return;    // garde contre doubles appels (Update + TakeDamage dans la même frame)
        deathFired = true;
        OnStatsChanged?.Invoke();
        OnDeath?.Invoke();
        Debug.Log("Game Over");
        FreezePlayer();
        OnGameOver?.Invoke();
    }

    /// <summary>
    /// Fige le joueur à la mort : coupe les scripts d'input/action et stoppe la simulation
    /// du Rigidbody2D. La scène reste active (UI Game Over cliquable) ; le reset complet
    /// vient de SceneManager.LoadScene côté GameOverUI.
    /// </summary>
    private void FreezePlayer()
    {
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.simulated = false;
        }

        // Désactive les contrôleurs présents sur le joueur (null-safe : si l'un n'existe pas, on ignore).
        var movement = GetComponent<PlayerMovement2D>(); if (movement != null) movement.enabled = false;
        var mining = GetComponent<MiningController>(); if (mining != null) mining.enabled = false;
        var shooting = GetComponent<ShootingController>(); if (shooting != null) shooting.enabled = false;
        var drill = GetComponent<DrillController>(); if (drill != null) drill.enabled = false;
    }

    /// <summary>Téléporte au spawn point et reset complet stats + accumulators + vélocité.</summary>
    public void Respawn()
    {
        transform.position = spawnPoint;
        if (rb != null) rb.linearVelocity = Vector2.zero;

        CurrentHealth = maxHealth;
        // Respawn : l'Overheal repart plein, même règle qu'Awake (tampon qui verrouille la torche).
        LightOverheal = maxOverheal;
        healthDrainAccumulator = 0f;
        deathFired = false;

        OnStatsChanged?.Invoke();
        OnOverhealChanged?.Invoke(LightOverheal, maxOverheal);
        OnRespawn?.Invoke();
    }

    public void SetSpawnPoint(Vector3 worldPos) => spawnPoint = worldPos;
}
