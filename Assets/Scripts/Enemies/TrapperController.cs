using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Mob de contrôle de zone : évite le contact frontal avec le joueur (patrouille ou recule à
/// <see cref="avoidDistance"/>) et dépose toutes les <see cref="dropInterval"/> secondes un
/// <see cref="MineTrap"/> à sa projection 2D.
/// </summary>
public class TrapperController : MobBase
{
    [Header("Trap Drop")]
    [SerializeField] private MineTrap trapPrefab;
    [SerializeField] private float dropInterval = 4f;

    [Header("Avoidance")]
    [Tooltip("Vitesse utilisée en mode évitement.")]
    [SerializeField] private float avoidSpeed = 2.5f;

    [Tooltip("Distance minimale à conserver vis-à-vis du joueur lorsqu'il est détecté.")]
    [SerializeField] private float avoidDistance = 4f;

    private enum State { Patrol, Avoid }
    private State state;
    private float dropTimer;

    protected override void OnStart()
    {
        EnterPatrol();
        dropTimer = dropInterval;
    }

    protected override void TickBehavior()
    {
        dropTimer -= Time.deltaTime;

        if (playerTarget != null && DistanceToPlayer2D() <= detectionRadius)
        {
            if (state != State.Avoid)
            {
                state = State.Avoid;
                agent.speed = avoidSpeed;
            }
            PickAvoidPoint();
        }
        else
        {
            if (state != State.Patrol) EnterPatrol();
            TickPatrol();
        }

        if (dropTimer <= 0f)
        {
            DropTrap();
            dropTimer = dropInterval;
        }
    }

    private void PickAvoidPoint()
    {
        if (agent.pathPending) return;
        if (agent.remainingDistance > agent.stoppingDistance + 0.1f) return;

        Vector2 selfXZ = new Vector2(transform.position.x, transform.position.z);
        Vector2 playerXZ = new Vector2(playerTarget.position.x, playerTarget.position.y);
        Vector2 away = selfXZ - playerXZ;
        if (away.sqrMagnitude < 0.01f) away = Random.insideUnitCircle.normalized;
        else away.Normalize();

        // Point à `avoidDistance` du joueur, dans la direction opposée + un léger offset latéral
        // pour que le Trapper contourne plutôt que reculer droit.
        Vector2 lateral = new Vector2(-away.y, away.x) * Random.Range(-1.5f, 1.5f);
        Vector2 target = playerXZ + away * avoidDistance + lateral;
        Vector3 candidate = new Vector3(target.x, navMeshY, target.y);

        if (NavMesh.SamplePosition(candidate, out var hit, sampleRadius, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
    }

    private void DropTrap()
    {
        if (trapPrefab == null) return;
        Vector2 pos = new Vector2(transform.position.x, transform.position.z);
        Instantiate(trapPrefab, new Vector3(pos.x, pos.y, 0f), Quaternion.identity);
    }

    private void EnterPatrol()
    {
        state = State.Patrol;
        agent.speed = patrolSpeed;
        waitTimer = 0f;
        PickPatrolPoint();
    }

    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();
        Gizmos.color = new Color(0.6f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, avoidDistance);
    }
}
