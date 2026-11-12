using UnityEngine;

public class CameraFollow2D : MonoBehaviour
{
    /// <summary>Référence singleton pour que les ArenaRoom (instanciées runtime) puissent déclencher
    /// SetZoom/ResetZoom sans ref sérialisée. Posé/retiré en Awake/OnDestroy.</summary>
    public static CameraFollow2D Instance { get; private set; }

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

    [Header("Zoom")]
    [Tooltip("Orthographic size par défaut (hors salle de combat). La caméra converge vers cette valeur après un ResetZoom().")]
    [SerializeField] private float defaultOrthoSize = 7.5f;

    [Tooltip("Durée approximative du SmoothDamp de zoom (secondes). Plus bas = zoom plus nerveux, plus haut = transition cinématographique.")]
    [SerializeField] private float zoomSmoothTime = 0.6f;

    private Camera cam;
    private Vector2 velocity;
    private float targetOrthoSize;
    private float orthoSizeVelocity;

    private void Awake()
    {
        Instance = this;
        cam = GetComponent<Camera>();
        if (cam == null)
            Debug.LogError("[CameraFollow2D] Aucun composant Camera sur ce GameObject.", this);
        else
            targetOrthoSize = cam.orthographicSize;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Appelé par ArenaRoom.OnTriggerEnter2D pour dézoomer en salle de combat.</summary>
    public void SetZoom(float orthoSize)
    {
        targetOrthoSize = orthoSize;
    }

    /// <summary>Appelé par ArenaRoom.OnTriggerExit2D pour revenir au zoom de parcours.</summary>
    public void ResetZoom()
    {
        targetOrthoSize = defaultOrthoSize;
    }

    private void LateUpdate()
    {
        if (target != null)
        {
            Vector2 desired = (Vector2)target.position + offset;
            Vector2 current = transform.position;

            Vector2 next = smoothTime <= 0f
                ? desired
                : Vector2.SmoothDamp(current, desired, ref velocity, smoothTime, maxSpeed);

            // Préserver le Z d'origine (caméra 2D orthographique à Z = -10 par défaut).
            transform.position = new Vector3(next.x, next.y, transform.position.z);
        }

        // Zoom : SmoothDamp monotonique vers targetOrthoSize. Exécuté même sans target
        // pour que ResetZoom fonctionne en menu / pause / mort du joueur.
        if (cam != null)
            cam.orthographicSize = Mathf.SmoothDamp(
                cam.orthographicSize, targetOrthoSize, ref orthoSizeVelocity, zoomSmoothTime);
    }
}
