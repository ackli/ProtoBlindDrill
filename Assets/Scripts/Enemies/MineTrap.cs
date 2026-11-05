using UnityEngine;

/// <summary>
/// Piège déposé par le Trapper. S'arme après <see cref="armTime"/> secondes, puis explose au
/// premier contact du joueur ou dès qu'il entre dans un rayon de <see cref="triggerRadius"/>.
/// L'explosion inflige <see cref="damage"/> point(s), joue un flash de couleur et se détruit.
/// </summary>
[RequireComponent(typeof(CircleCollider2D))]
public class MineTrap : MonoBehaviour
{
    [Header("Arming")]
    [Tooltip("Délai (s) après spawn avant que le piège devienne actif.")]
    [SerializeField] private float armTime = 1f;

    [Header("Detonation")]
    [SerializeField] private int damage = 2;
    [Tooltip("Rayon de détection du joueur (OverlapCircle). Aussi utilisé pour le CircleCollider2D en trigger.")]
    [SerializeField] private float triggerRadius = 1f;

    [Header("Feedback")]
    [SerializeField] private float flashDuration = 0.1f;
    [SerializeField] private Color flashColor = Color.white;
    [Tooltip("Teinte quand le piège n'est pas encore armé.")]
    [SerializeField] private Color unarmedColor = new Color(1f, 1f, 1f, 0.6f);
    [Tooltip("Teinte à l'armement (prête à exploser).")]
    [SerializeField] private Color armedColor = new Color(1f, 0.3f, 0.3f, 1f);

    private CircleCollider2D col;
    private SpriteRenderer sr;
    private float armedAt;
    private bool armed;
    private bool exploded;

    private void Awake()
    {
        col = GetComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = triggerRadius;

        sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null) sr.color = unarmedColor;

        armedAt = Time.time + armTime;
    }

    private void Update()
    {
        if (exploded) return;

        if (!armed)
        {
            if (Time.time >= armedAt)
            {
                armed = true;
                if (sr != null) sr.color = armedColor;
            }
            else return;
        }

        // Scan radius : explose dès que le joueur entre dans la zone (même s'il n'a pas franchi le trigger).
        var hit = Physics2D.OverlapCircle(transform.position, triggerRadius);
        if (hit == null) return;

        var ph = hit.GetComponent<PlayerStats>();
        if (ph == null) ph = hit.GetComponentInParent<PlayerStats>();
        if (ph != null) Explode(ph);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!armed || exploded) return;
        var ph = other.GetComponent<PlayerStats>();
        if (ph == null) ph = other.GetComponentInParent<PlayerStats>();
        if (ph != null) Explode(ph);
    }

    private void Explode(PlayerStats ph)
    {
        if (exploded) return;
        exploded = true;
        ph.TakeDamage(damage);

        if (sr != null) sr.color = flashColor;
        Destroy(gameObject, flashDuration);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = armed ? Color.red : Color.gray;
        Gizmos.DrawWireSphere(transform.position, triggerRadius);
    }
}
