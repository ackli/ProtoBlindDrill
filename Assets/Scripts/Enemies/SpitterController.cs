using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Mob à distance : s'approche jusqu'à <see cref="attackRange"/>, tire un <see cref="AcidProjectile"/>
/// si la ligne de vue vers le joueur est libre (Physics2D.Linecast), fuit en direction opposée si le
/// joueur passe sous <see cref="fleeRange"/>.
/// </summary>
public class SpitterController : MobBase
{
    [Header("Combat (Ranged)")]
    [Tooltip("Distance d'arrêt pour tirer (unités 2D).")]
    [SerializeField] private float attackRange = 5f;

    [Tooltip("Distance en-dessous de laquelle le Spitter fuit (unités 2D).")]
    [SerializeField] private float fleeRange = 2.5f;

    [Tooltip("Délai entre deux tirs (secondes).")]
    [SerializeField] private float fireCooldown = 2f;

    [Tooltip("Projectile tiré.")]
    [SerializeField] private AcidProjectile projectilePrefab;

    [SerializeField] private float projectileSpeed = 6f;

    [Tooltip("Décalage (unités 2D) du point de spawn depuis la position du mob, dans la direction du tir.")]
    [SerializeField] private float muzzleOffset = 0.6f;

    [Header("Line of Sight")]
    [Tooltip("Layer(s) considérés comme bloquants pour le linecast.")]
    [SerializeField] private LayerMask wallMask;

    [Header("Movement")]
    [SerializeField] private float chaseSpeed = 3f;

    [Tooltip("Distance de recul utilisée quand on entre en mode fuite.")]
    [SerializeField] private float fleeDistance = 6f;

    private enum State { Patrol, Approach, Attack, Flee }
    private State state;
    private float fireTimer;

    protected override void OnStart()
    {
        EnterPatrol();
    }

    protected override void TickBehavior()
    {
        fireTimer -= Time.deltaTime;

        if (playerTarget == null)
        {
            if (state != State.Patrol) EnterPatrol();
            TickPatrol();
            return;
        }

        float dist = DistanceToPlayer2D();

        // isPlayerAggroed intègre hystérésis + LOS + rétorsion (défini dans MobBase).
        if (!isPlayerAggroed)
        {
            if (state != State.Patrol) EnterPatrol();
            TickPatrol();
            return;
        }

        if (dist < fleeRange)
        {
            EnterFlee();
            return;
        }

        bool hasLos = HasLineOfSight();

        if (dist <= attackRange && hasLos)
        {
            if (state != State.Attack)
            {
                state = State.Attack;
                agent.isStopped = true;
                agent.ResetPath();
            }
            if (fireTimer <= 0f)
            {
                Fire();
                fireTimer = fireCooldown;
            }
        }
        else
        {
            if (state != State.Approach)
            {
                state = State.Approach;
                agent.isStopped = false;
                agent.speed = chaseSpeed;
            }
            if (TryPathTo(PlayerOnNav(), out var hit))
                agent.SetDestination(hit.position);
        }
    }

    private void EnterFlee()
    {
        if (!agent.isOnNavMesh) return;
        if (state != State.Flee)
        {
            state = State.Flee;
            agent.isStopped = false;
            agent.speed = chaseSpeed;
        }

        Vector2 selfXZ = new Vector2(transform.position.x, transform.position.z);
        Vector2 playerXZ = new Vector2(playerTarget.position.x, playerTarget.position.y);
        Vector2 away = selfXZ - playerXZ;
        if (away.sqrMagnitude < 0.01f) away = Random.insideUnitCircle.normalized;
        else away.Normalize();

        Vector3 candidate = new Vector3(
            selfXZ.x + away.x * fleeDistance, navMeshY, selfXZ.y + away.y * fleeDistance);

        if (NavMesh.SamplePosition(candidate, out var hit, sampleRadius, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
    }

    private bool HasLineOfSight()
    {
        Vector2 from = new Vector2(transform.position.x, transform.position.z);
        Vector2 to = new Vector2(playerTarget.position.x, playerTarget.position.y);
        return Physics2D.Linecast(from, to, wallMask).collider == null;
    }

    private void Fire()
    {
        if (projectilePrefab == null) return;

        Vector2 from = new Vector2(transform.position.x, transform.position.z);
        Vector2 to = new Vector2(playerTarget.position.x, playerTarget.position.y);
        Vector2 dir = to - from;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;
        dir.Normalize();

        Vector2 spawn = from + dir * muzzleOffset;
        var proj = Instantiate(projectilePrefab, new Vector3(spawn.x, spawn.y, 0f), Quaternion.identity);
        proj.Launch(dir, projectileSpeed);
    }

    private void EnterPatrol()
    {
        if (!agent.isOnNavMesh) return;
        state = State.Patrol;
        agent.isStopped = false;
        agent.speed = patrolSpeed;
        waitTimer = 0f;
        PickPatrolPoint();
    }

    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, attackRange);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, fleeRange);
    }
}
