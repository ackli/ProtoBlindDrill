using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Base commune aux mobs Dual-Plane : racine pilotée par NavMeshAgent sur un plan XZ à Y=navMeshY,
/// enfant Visuals détaché au Start() et projeté en permanence à (agent.x, agent.z, 0) pour vivre en 2D.
/// Encapsule aussi HP, prise de dégâts via CollisionRelay, patrouille et helpers de pathfinding.
/// Les sous-classes implémentent <see cref="TickBehavior"/> pour leur comportement de combat.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public abstract class MobBase : MonoBehaviour
{
    [Header("Projection")]
    [Tooltip("Plan Y monde 3D où est posé le NavMesh horizontal.")]
    [SerializeField] protected float navMeshY = 100f;

    [Header("Stats")]
    [SerializeField] protected int maxHP = 3;
    [SerializeField] protected int damageToPlayer = 1;
    [Tooltip("Rayon de détection du joueur (unités monde, mesuré en projection 2D).")]
    [SerializeField] protected float detectionRadius = 8f;

    [Header("Patrol")]
    [SerializeField] protected float patrolRadius = 5f;
    [SerializeField] protected float patrolWait = 1f;
    [SerializeField] protected float patrolSpeed = 2f;

    [Header("Pathfinding")]
    [Tooltip("Rayon de SamplePosition pour projeter une cible sur le NavMesh.")]
    [SerializeField] protected float sampleRadius = 2f;

    [Header("Player")]
    [Tooltip("Si vide, résolu au Awake via FindObjectOfType<PlayerStats>().")]
    [SerializeField] protected Transform playerTarget;

    [Header("Visuals")]
    [Tooltip("Enfant visuel 2D (SpriteRenderer + Collider). Si vide, résolu au Awake via GetComponentInChildren<SpriteRenderer>().")]
    [SerializeField] protected Transform visuals;

    protected NavMeshAgent agent;
    protected NavMeshPath reusablePath;
    protected int currentHP;
    protected float waitTimer;

    // --- Télémétrie ---
    private bool isAggroed;
    private float spawnTime;

    protected virtual void Awake()
    {
        reusablePath = new NavMeshPath();
        agent = GetComponent<NavMeshAgent>();
        agent.updateRotation = false;
        agent.autoBraking = true;

        currentHP = maxHP;
        spawnTime = Time.time;

        if (playerTarget == null)
        {
            var ph = FindFirstObjectByType<PlayerStats>();
            if (ph != null) playerTarget = ph.transform;
        }

        if (visuals == null)
        {
            var sr = GetComponentInChildren<SpriteRenderer>();
            if (sr != null) visuals = sr.transform;
        }
    }

    protected virtual void Start()
    {
        // Détache le visuel 2D : l'agent pilote la racine sur XZ, le visuel doit rester en Z=0.
        if (visuals != null) visuals.parent = null;
        LevelObjectiveManager.SafeRegister(this);
        OnStart();
    }

    /// <summary>Hook post-détachement, après Start de la base.</summary>
    protected virtual void OnStart() { }

    protected virtual void Update()
    {
        if (!agent.isOnNavMesh) return;
        TickBehavior();
        UpdateAggroState();
        SyncVisuals();
    }

    /// <summary>Évalue "mob engagé" = player présent ET dans detectionRadius. Loggue uniquement les transitions.</summary>
    private void UpdateAggroState()
    {
        bool nowAggroed = playerTarget != null && DistanceToPlayer2D() <= detectionRadius;
        if (nowAggroed == isAggroed) return;

        isAggroed = nowAggroed;
        float dist = DistanceToPlayer2D();
        if (nowAggroed)
            CombatTelemetry.AggroGained(GetType().Name, GetInstanceID(), dist);
        else
            CombatTelemetry.AggroLost(GetType().Name, GetInstanceID(), dist, "hors de portée");
    }

    /// <summary>Boucle comportementale propre à chaque mob (chase, attaque, patrouille...).</summary>
    protected abstract void TickBehavior();

    protected void SyncVisuals()
    {
        if (visuals == null) return;
        Vector3 ap = transform.position;
        visuals.position = new Vector3(ap.x, ap.z, 0f);
    }

    protected float DistanceToPlayer2D()
    {
        if (playerTarget == null) return float.PositiveInfinity;
        return Vector2.Distance(
            new Vector2(transform.position.x, transform.position.z),
            new Vector2(playerTarget.position.x, playerTarget.position.y));
    }

    protected Vector3 PlayerOnNav()
    {
        if (playerTarget == null) return transform.position;
        return new Vector3(playerTarget.position.x, navMeshY, playerTarget.position.y);
    }

    protected bool TryPathTo(Vector3 worldPosOnNav, out NavMeshHit hit)
    {
        if (!NavMesh.SamplePosition(worldPosOnNav, out hit, sampleRadius, NavMesh.AllAreas)) return false;
        if (!agent.CalculatePath(hit.position, reusablePath)) return false;
        return reusablePath.status == NavMeshPathStatus.PathComplete;
    }

    protected void TickPatrol()
    {
        if (agent.pathPending) return;
        if (agent.remainingDistance > agent.stoppingDistance + 0.1f) return;

        waitTimer += Time.deltaTime;
        if (waitTimer >= patrolWait)
        {
            waitTimer = 0f;
            PickPatrolPoint();
        }
    }

    protected void PickPatrolPoint()
    {
        Vector3 center = new Vector3(transform.position.x, navMeshY, transform.position.z);
        for (int i = 0; i < 10; i++)
        {
            Vector2 rnd = Random.insideUnitCircle * patrolRadius;
            Vector3 candidate = center + new Vector3(rnd.x, 0f, rnd.y);
            if (NavMesh.SamplePosition(candidate, out var hit, sampleRadius, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
                return;
            }
        }
    }

    /// <summary>Hook appelé par CollisionRelay depuis l'enfant Visuals (détaché au Start).</summary>
    public virtual void OnChildTriggerEnter(Collider2D other)
    {
        var ph = other.GetComponent<PlayerStats>();
        if (ph == null) ph = other.GetComponentInParent<PlayerStats>();
        if (ph != null)
        {
            ph.TakeDamage(damageToPlayer);
            return;
        }

        var proj = other.GetComponent<DebrisProjectile>();
        if (proj == null) proj = other.GetComponentInParent<DebrisProjectile>();
        if (proj != null)
        {
            int dmg = DebugCheatController.OneShotKillEnabled ? 9999 : 1;
            TakeDamage(dmg, proj.gameObject.name);
            Destroy(proj.gameObject);
        }
    }

    public virtual void TakeDamage(int amount) => TakeDamage(amount, "Unknown");

    /// <summary>Encaisse des dégâts et loggue l'impact avec la source (nom de GameObject usuel).</summary>
    public virtual void TakeDamage(int amount, string source)
    {
        if (currentHP <= 0 || amount <= 0) return;
        currentHP = Mathf.Max(0, currentHP - amount);
        CombatTelemetry.MobHit(GetType().Name, GetInstanceID(), amount, source, currentHP, maxHP);

        if (currentHP <= 0)
        {
            CombatTelemetry.MobDeath(GetType().Name, GetInstanceID(), Time.time - spawnTime);
            OnDeath();
            if (visuals != null) Destroy(visuals.gameObject);
            Destroy(gameObject);
        }
    }

    /// <summary>Hook de mort (drop, FX, score...). Appelé juste avant la destruction.</summary>
    protected virtual void OnDeath() { }

    protected virtual void OnDestroy()
    {
        // Sécurité : Visuals a été détaché, il ne sera pas nettoyé avec la racine.
        if (!gameObject.scene.isLoaded) return;
        LevelObjectiveManager.SafeUnregister(this);
        if (visuals != null) Destroy(visuals.gameObject);
    }

    protected virtual void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, patrolRadius);
    }
}
