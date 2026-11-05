using System.Collections;
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
        // Agent reste DÉSACTIVÉ jusqu'au Lazy Enable en coroutine — évite le crash natif
        // "Failed to create agent because it is not close enough to the NavMesh" qui se
        // déclenchait dans Instantiate avant même que nos scripts n'aient la main.
        // Les prefabs sont aussi désactivés par défaut (double verrou).
        agent.enabled = false;

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
        StartCoroutine(WaitForNavMeshAndStart());
    }

    /// <summary>Hook post-attachement NavMesh, appelé une fois que l'agent est bien accroché.</summary>
    protected virtual void OnStart() { }

    /// <summary>
    /// Attend que le NavMesh soit prêt (bake fait + agent accroché) avant d'appeler <see cref="OnStart"/>.
    /// Évite la race condition "Resume can only be called on an active agent that has been placed on a NavMesh"
    /// quand le TerrainGenerator fit+bake la surface au même frame que le mob se spawne.
    /// Tente un <see cref="NavMeshAgent.Warp"/> chaque frame vers le point NavMesh le plus proche.
    /// </summary>
    /// <summary>
    /// Séquence d'initialisation "Lazy Enable" du NavMeshAgent. L'agent reste désactivé au moment
    /// de Instantiate (prefab + Awake), on le place physiquement sur un point NavMesh valide,
    /// puis on l'active et on laisse une frame à Unity pour qu'il s'accroche.
    /// Évite complètement l'erreur native "Failed to create agent" qui se déclenchait avant
    /// que nos scripts n'aient la main.
    /// </summary>
    private IEnumerator WaitForNavMeshAndStart()
    {
        // 1. Laisse passer la frame d'instanciation.
        yield return null;

        // 2. Point NavMesh légal à proximité (rayon 10 unités — large, compense les trous locaux de bake).
        if (!NavMesh.SamplePosition(transform.position, out var hit, 10.0f, NavMesh.AllAreas))
        {
            Debug.LogError($"[MobBase] {name} : aucun point NavMesh trouvé dans un rayon de 10 autour de {transform.position}. Mob inactif.", this);
            yield break;
        }

        // 3. Déplace physiquement le GO sur le point légal.
        transform.position = hit.position;

        // 4. Active l'agent (c'est maintenant qu'il va créer son instance native et s'accrocher).
        agent.enabled = true;

        // Config post-enable : certaines props veulent un agent actif pour être propagées.
        agent.updateRotation = false;
        agent.autoBraking = true;

        // 5. Laisse Unity lier l'agent au NavMesh.
        yield return null;

        // 6. Vérification finale + démarrage comportemental.
        if (!agent.isOnNavMesh)
        {
            Debug.LogError($"[MobBase] {name} : agent activé mais isOnNavMesh=false après 1 frame à {transform.position}. Mob inactif.", this);
            yield break;
        }

        OnStart();
    }

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
        if (!agent.isOnNavMesh) return; // garde défensive : SetDestination impossible hors NavMesh.
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
