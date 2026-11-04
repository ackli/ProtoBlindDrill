using UnityEngine;

/// <summary>
/// À poser sur un GameObject enfant portant un Collider2D en trigger.
/// Relaie les événements OnTriggerEnter2D au MobController racine (ou au target assigné).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class CollisionRelay : MonoBehaviour
{
    [Tooltip("Cible du relais. Si vide, résolu via GetComponentInParent<MobController>() au Awake.")]
    [SerializeField] private MobController target;

    private void Awake()
    {
        if (target == null) target = GetComponentInParent<MobController>();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (target != null) target.OnChildTriggerEnter(other);
    }
}
