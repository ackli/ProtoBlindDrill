using UnityEngine;

public class CameraFollow2D : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Transform suivi (en général le Player).")]
    [SerializeField] private Transform target;

    [Tooltip("Décalage appliqué à la cible (ex. caméra légèrement au-dessus du joueur).")]
    [SerializeField] private Vector2 offset = Vector2.zero;

    [Header("Smoothing")]
    [Tooltip("Temps approximatif pour atteindre la cible (SmoothDamp). 0 = snap instantané.")]
    [SerializeField] private float smoothTime = 0.15f;

    [Tooltip("Vitesse max du smoothing (unités/seconde). Infini = pas de clamp.")]
    [SerializeField] private float maxSpeed = Mathf.Infinity;

    private Vector2 velocity;

    private void LateUpdate()
    {
        if (target == null) return;

        Vector2 desired = (Vector2)target.position + offset;
        Vector2 current = transform.position;

        Vector2 next = smoothTime <= 0f
            ? desired
            : Vector2.SmoothDamp(current, desired, ref velocity, smoothTime, maxSpeed);

        // Préserver le Z d'origine (caméra 2D orthographique à Z = -10 par défaut).
        transform.position = new Vector3(next.x, next.y, transform.position.z);
    }
}
