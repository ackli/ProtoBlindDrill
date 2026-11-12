using UnityEngine;

/// <summary>
/// Mob mêlée type "Crawler" : patrouille aléatoire, chasse et contact direct dès que le joueur
/// est en détection ET qu'un chemin NavMesh existe. Dégâts via contact (OnChildTriggerEnter hérité).
/// </summary>
public class MobController : MobBase
{
    [Header("Chase")]
    [SerializeField] private float chaseSpeed = 3f;

    private enum State { Patrol, Chase }
    private State state;

    protected override void OnStart()
    {
        EnterPatrol();
    }

    protected override void TickBehavior()
    {
        // isPlayerAggroed intègre déjà hystérésis + LOS + rétorsion (MobBase.UpdateAggroState).
        if (isPlayerAggroed && TryPathTo(PlayerOnNav(), out var hit))
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

    private void EnterPatrol()
    {
        if (!agent.isOnNavMesh) return;
        state = State.Patrol;
        agent.speed = patrolSpeed;
        waitTimer = 0f;
        PickPatrolPoint();
    }
}
