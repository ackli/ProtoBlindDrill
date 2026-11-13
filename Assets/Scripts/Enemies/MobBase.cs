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
    [Tooltip("Rayon de détection du joueur (unités monde, mesuré en projection 2D). Seuil d'ENTRÉE en aggro.")]
    [SerializeField] protected float detectionRadius = 8f;

    [Tooltip("Leash : distance au-delà de laquelle un mob déjà aggroé lâche IMMÉDIATEMENT, peu importe mémoire ou rétorsion. Doit être > detectionRadius (hystérésis) et assez grand pour que le mob reste engagé pendant un chase long.")]
    [SerializeField] protected float aggroLostRadius = 20f;

    [Tooltip("Layers considérés comme obstacles bloquant la ligne de vue (Physics2D.Linecast). Un mob n'aggrote pas à travers un mur. Si laissé à 0, Awake tente d'y mettre le layer 'Ground'.")]
    [SerializeField] protected LayerMask losMask;

    [Tooltip("Mémoire (s) : temps de maintien de l'aggro après perte du LOS. Pendant cette fenêtre le mob continue à pathfinder vers la dernière position connue du joueur — c'est ce qui lui fait contourner le petit bloc derrière lequel le joueur s'est caché.")]
    [SerializeField] protected float aggroMemoryDuration = 3f;

    [Tooltip("Durée (s) d'aggro forcé après que le mob a reçu des dégâts, même sans LOS. Rétorsion : un mob tapé à travers un mur va contourner pour venir au contact.")]
    [SerializeField] protected float retaliationDuration = 4f;

    [Header("Patrol")]
    [SerializeField] protected float patrolRadius = 5f;
    [SerializeField] protected float patrolWait = 1f;
    [SerializeField] protected float patrolSpeed = 2f;

    [Header("Pathfinding")]
    [Tooltip("Rayon de SamplePosition pour projeter une cible sur le NavMesh.")]
    [SerializeField] protected float sampleRadius = 2f;

    [Tooltip("Distance d'arrêt du NavMeshAgent. Mêlée ~0.5 pour coller au joueur. Ranged peut mettre plus haut si besoin — le Spitter gère son propre isStopped indépendamment.")]
    [SerializeField] protected float stoppingDistance = 0.5f;

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

    // --- Aggro state ---
    /// <summary>État "mob engagé" exposé aux dérivés. Remplace le test direct DistanceToPlayer2D() <= detectionRadius
    /// dans les TickBehavior. Mis à jour par UpdateAggroState avec hystérésis + LOS + rétorsion.</summary>
    protected bool isPlayerAggroed;
    private bool previousAggroState;
    private float retaliationUntil = -1f;
    /// <summary>Deadline de la mémoire d'aggro : tant que Time.time &lt; aggroMemoryUntil, le mob
    /// reste aggroé même sans LOS/hors detectionRadius. Rafraîchi à chaque rafraîchissement
    /// (LOS+portée OU rétorsion). Le leash (aggroLostRadius) reste prioritaire sur la mémoire.</summary>
    private float aggroMemoryUntil = -1f;
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

        // Fallback losMask si non assigné sur le prefab : tente le layer "Ground". Si absent,
        // reste à 0 → linecast retourne toujours null → aggro sans LOS (ancien comportement).
        if (losMask.value == 0) losMask = LayerMask.GetMask("Ground");
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
        agent.stoppingDistance = stoppingDistance;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        // Priorité aléatoire : évite les deadlocks quand deux mobs ont la même priorité.
        agent.avoidancePriority = Random.Range(30, 70);

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
        // UpdateAggroState avant TickBehavior : les dérivés lisent isPlayerAggroed dans leur tick.
        UpdateAggroState();
        TickBehavior();
        SyncVisuals();
    }

    /// <summary>
    /// Évalue isPlayerAggroed avec quatre mécaniques imbriquées, évaluées dans cet ordre :
    /// 1. Leash (priorité absolue) : si distance &gt; aggroLostRadius → perte immédiate, même si
    ///    mémoire/rétorsion actives. Évite que le joueur emmène le mob à l'autre bout du niveau.
    /// 2. Rétorsion : mob touché il y a &lt; retaliationDuration → aggro forcé + rafraîchit la mémoire.
    ///    Permet le revenge push à travers les murs.
    /// 3. Rafraîchissement : LOS libre ET distance &lt;= detectionRadius → aggro + rafraîchit la mémoire.
    ///    Seul chemin d'entrée en aggro depuis l'état "patrouille".
    /// 4. Maintien par mémoire : déjà aggroé et Time.time &lt; aggroMemoryUntil → on reste aggroé,
    ///    même sans LOS et même hors detectionRadius (mais toujours à ≤ aggroLostRadius, cf. 1).
    ///    C'est cette fenêtre qui force le mob à continuer de pathfinder vers le joueur et à
    ///    contourner un petit bloc derrière lequel le joueur vient de se cacher.
    /// </summary>
    private void UpdateAggroState()
    {
        bool forcedByRetaliation = Time.time < retaliationUntil;
        bool now;
        string lostReason = null;

        if (playerTarget == null)
        {
            now = false;
            lostReason = "player null";
        }
        else
        {
            float d = DistanceToPlayer2D();

            if (d > aggroLostRadius)
            {
                // 1. Leash : coupe tout, y compris rétorsion/mémoire.
                now = false;
                lostReason = "leash (hors de portée)";
            }
            else if (forcedByRetaliation)
            {
                // 2. Rétorsion : aggro forcé + on prolonge la mémoire pour que la perte de la
                // fenêtre de rétorsion ne retombe pas en patrol direct s'il n'y a pas de LOS.
                now = true;
                aggroMemoryUntil = Time.time + aggroMemoryDuration;
            }
            else if (d <= detectionRadius && HasLineOfSight())
            {
                // 3. Rafraîchissement standard : LOS + portée.
                now = true;
                aggroMemoryUntil = Time.time + aggroMemoryDuration;
            }
            else if (isPlayerAggroed && Time.time < aggroMemoryUntil)
            {
                // 4. Maintien par mémoire : on garde l'aggro le temps de contourner.
                now = true;
            }
            else
            {
                now = false;
                if (isPlayerAggroed) lostReason = "mémoire expirée";
            }
        }

        isPlayerAggroed = now;
        // Boussole anti-frustration : tant qu'un mob est aggro, le joueur est "en combat".
        if (now) LevelObjectiveManager.SafeNotifyPlayerAction();

        if (now != previousAggroState)
        {
            previousAggroState = now;
            float dist = DistanceToPlayer2D();
            if (now)
                CombatTelemetry.AggroGained(GetType().Name, GetInstanceID(), dist);
            else
                CombatTelemetry.AggroLost(GetType().Name, GetInstanceID(), dist, lostReason ?? "inconnu");
        }
    }

    /// <summary>Linecast 2D du mob vers le joueur. Convention Dual-Plane : le mob est en (x, z) sur
    /// le plan NavMesh, le joueur en (x, y). Retourne true si aucun collider du losMask ne bloque.</summary>
    protected bool HasLineOfSight()
    {
        if (playerTarget == null) return false;
        Vector2 from = new Vector2(transform.position.x, transform.position.z);
        Vector2 to = new Vector2(playerTarget.position.x, playerTarget.position.y);
        return Physics2D.Linecast(from, to, losMask).collider == null;
    }

    /// <summary>Boucle comportementale propre à chaque mob (chase, attaque, patrouille...).</summary>
    protected abstract void TickBehavior();

    protected void SyncVisuals()
    {
        if (visuals == null) return;
        Vector3 ap = transform.position;
        visuals.position = new Vector3(ap.x, ap.z, 0f);
    }

    /// <summary>Position du mob dans le plan 2D du joueur (projection XZ → XY, comme le visuel).</summary>
    public Vector3 Position2D => new Vector3(transform.position.x, transform.position.z, 0f);

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
            // Le damage porté par le projectile intègre déjà le multiplicateur de buff côté ShootingController.
            int dmg = DebugCheatController.OneShotKillEnabled ? 9999 : Mathf.Max(1, proj.Damage);
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
        // Boussole anti-frustration : infliger des dégâts à un mob compte comme un combat.
        LevelObjectiveManager.SafeNotifyPlayerAction();

        // Rétorsion : aggro forcé pendant retaliationDuration pour permettre à un mob frappé
        // à travers un mur / hors LOS de contourner et venir au contact.
        retaliationUntil = Time.time + retaliationDuration;

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
