using UnityEngine;

/// <summary>
/// Piège déposé par le Trapper. S'arme après <see cref="armTime"/> secondes, puis explose au
/// premier contact du joueur ou dès qu'il entre dans un rayon de <see cref="triggerRadius"/>.
/// Si personne n'a déclenché la mine après <see cref="lifetime"/> secondes, elle s'auto-détonne
/// (dégâts si un joueur se trouve dans la zone au moment du scan final, sinon flash + destroy
/// sans dégâts) pour éviter de polluer durablement l'arène.
/// L'explosion inflige <see cref="damage"/> point(s), joue un flash de couleur et se détruit.
/// </summary>
[RequireComponent(typeof(CircleCollider2D))]
public class MineTrap : MonoBehaviour
{
    [Header("Arming")]
    [Tooltip("Délai (s) après spawn avant que le piège devienne actif.")]
    [SerializeField] private float armTime = 1f;

    [Header("Lifetime")]
    [Tooltip("Durée de vie (s) après spawn. À l'expiration, la mine s'auto-détonne (voir SelfDetonate) pour éviter de polluer l'arène. Doit être > armTime pour avoir du sens.")]
    [SerializeField] private float lifetime = 5f;

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
    private float expiresAt;
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
        expiresAt = Time.time + lifetime;
    }

    private void Update()
    {
        if (exploded) return;

        // Expiration : auto-détonation même si pas encore armée (cas défensif où lifetime < armTime).
        // On force armed=true + teinte armedColor avant SelfDetonate pour rester cohérent avec les
        // autres chemins d'explosion (flashColor appliqué juste après, mais la sémantique est nette).
        if (Time.time >= expiresAt)
        {
            if (!armed)
            {
                armed = true;
                if (sr != null) sr.color = armedColor;
            }
            SelfDetonate();
            return;
        }

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

    /// <summary>Déclenche l'explosion. <paramref name="ph"/> peut être null (auto-détonation
    /// sans cible dans la zone) : dans ce cas, pas de dégâts mais flash + destroy identiques
    /// à un déclenchement normal — l'explosion reste visible et audible pour le joueur.</summary>
    private void Explode(PlayerStats ph)
    {
        if (exploded) return;
        exploded = true;
        if (ph != null) ph.TakeDamage(damage);

        if (sr != null) sr.color = flashColor;
        Destroy(gameObject, flashDuration);
    }

    /// <summary>Auto-explosion sur expiration de lifetime. Refait un <see cref="Physics2D.OverlapCircle"/>
    /// pour attraper un joueur qui se trouverait dans le radius au moment pile — cohérent avec
    /// "comme si le joueur avait marché dessus". Sinon Explode(null) : flash+destroy sans dégâts.</summary>
    private void SelfDetonate()
    {
        if (exploded) return;
        var hit = Physics2D.OverlapCircle(transform.position, triggerRadius);
        PlayerStats ph = null;
        if (hit != null)
        {
            ph = hit.GetComponent<PlayerStats>();
            if (ph == null) ph = hit.GetComponentInParent<PlayerStats>();
        }
        Explode(ph);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = armed ? Color.red : Color.gray;
        Gizmos.DrawWireSphere(transform.position, triggerRadius);
    }
}
