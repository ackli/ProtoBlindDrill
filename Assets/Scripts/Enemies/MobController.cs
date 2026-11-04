using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// IA d'un mob type "Crawler". Projection 3D : le NavMesh est horizontal à Y = navMeshY.
/// L'axe Y du monde 2D est projeté sur l'axe Z du monde 3D. L'enfant Visuals vit, lui, en 2D (Z=0).
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class MobController : MonoBehaviour
{
    [Header("Projection")]
    [Tooltip("Plan Y monde 3D où est posé le NavMesh horizontal.")]
    [SerializeField] private float navMeshY = 100f;

    [Header("Stats")]
    [SerializeField] private int maxHP = 3;
    [SerializeField] private int damageToPlayer = 1;
    [Tooltip("Rayon de détection du joueur (unités monde, mesuré en projection 2D).")]
    [SerializeField] private float detectionRadius = 8f;

    [Header("Patrol")]
    [SerializeField] private float patrolRadius = 5f;
    [SerializeField] private float patrolWait = 1f;
    [SerializeField] private float patrolSpeed = 2f;

    [Header("Chase")]
    [SerializeField] private float chaseSpeed = 3f;
    [SerializeField] private float sampleRadius = 2f;

    [Header("Player")]
    [Tooltip("Si vide, résolu au Awake via FindObjectOfType<PlayerHealth>().")]
    [SerializeField] private Transform playerTarget;

    [Header("Visuals")]
    [Tooltip("Enfant visuel 2D (SpriteRenderer + Collider). Si vide, résolu au Awake via GetComponentInChildren<SpriteRenderer>().")]
    [SerializeField] private Transform visuals;

    private enum State { Patrol, Chase }

    private NavMeshAgent agent;
    private int currentHP;
    private State state;
    private float waitTimer;
    private NavMeshPath reusablePath;

    private void Awake()
    {
        reusablePath = new NavMeshPath();
        agent = GetComponent<NavMeshAgent>();
        agent.updateRotation = false;
        agent.autoBraking = true;

        currentHP = maxHP;

        if (playerTarget == null)
        {
            var ph = FindObjectOfType<PlayerHealth>();
            if (ph != null) playerTarget = ph.transform;
        }

        if (visuals == null)
        {
            var sr = GetComponentInChildren<SpriteRenderer>();
            if (sr != null) visuals = sr.transform;
        }
    }

    private void Start()
    {
        // Détache le visuel 2D : le NavMeshAgent pilote la racine sur le plan XZ,
        // le visuel doit rester dans le plan 2D (Z = 0).
        if (visuals != null) visuals.parent = null;

        EnterPatrol();
    }

    private void Update()
    {
        if (!agent.isOnNavMesh) return;

        if (playerTarget != null)
        {
            // Distance en espace 2D (agent XZ ↔ joueur XY).
            float dist = Vector2.Distance(
                new Vector2(transform.position.x, transform.position.z),
                new Vector2(playerTarget.position.x, playerTarget.position.y));

            Vector3 playerOnNav = new Vector3(playerTarget.position.x, navMeshY, playerTarget.position.y);

            if (dist <= detectionRadius && TryPathTo(playerOnNav, out var hit))
            {
                if (state != State.Chase)
                {
                    state = State.Chase;
                    agent.speed = chaseSpeed;
                }
                agent.SetDestination(hit.position);
            }
            else
            {
                if (state != State.Patrol) EnterPatrol();
                TickPatrol();
            }
        }
        else
        {
            if (state != State.Patrol) EnterPatrol();
            TickPatrol();
        }

        // Sync du visuel 2D sur la position XZ de l'agent.
        if (visuals != null)
        {
            Vector3 ap = transform.position;
            visuals.position = new Vector3(ap.x, ap.z, 0f);
        }
    }

    private bool TryPathTo(Vector3 worldPosOnNav, out NavMeshHit hit)
    {
        if (!NavMesh.SamplePosition(worldPosOnNav, out hit, sampleRadius, NavMesh.AllAreas)) return false;
        if (!agent.CalculatePath(hit.position, reusablePath)) return false;
        return reusablePath.status == NavMeshPathStatus.PathComplete;
    }

    private void EnterPatrol()
    {
        state = State.Patrol;
        agent.speed = patrolSpeed;
        waitTimer = 0f;
        PickPatrolPoint();
    }

    private void TickPatrol()
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

    private void PickPatrolPoint()
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
    public void OnChildTriggerEnter(Collider2D other)
    {
        var ph = other.GetComponent<PlayerHealth>();
        if (ph == null) ph = other.GetComponentInParent<PlayerHealth>();
        if (ph != null)
        {
            ph.TakeDamage(damageToPlayer);
            return;
        }

        var proj = other.GetComponent<DebrisProjectile>();
        if (proj == null) proj = other.GetComponentInParent<DebrisProjectile>();
        if (proj != null)
        {
            TakeDamage(1);
            Destroy(proj.gameObject);
        }
    }

    public void TakeDamage(int amount)
    {
        if (currentHP <= 0) return;
        currentHP = Mathf.Max(0, currentHP - amount);
        if (currentHP <= 0)
        {
            if (visuals != null) Destroy(visuals.gameObject);
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        // Sécurité : Visuals a été détaché → ne sera pas nettoyé avec la racine. On le détruit ici si pas déjà fait.
        if (!gameObject.scene.isLoaded) return;
        if (visuals != null) Destroy(visuals.gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, patrolRadius);
    }
}
