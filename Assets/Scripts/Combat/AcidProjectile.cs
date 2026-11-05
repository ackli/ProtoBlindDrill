using UnityEngine;

/// <summary>
/// Projectile du Spitter. Vole à vitesse constante en 2D. Inflige <see cref="damage"/> point(s)
/// au joueur à l'impact (via PlayerStats.TakeDamage) puis se détruit. Se détruit également au
/// contact d'un mur du <see cref="wallLayer"/> ou après <see cref="lifeTime"/> secondes.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class AcidProjectile : MonoBehaviour
{
    [Header("Damage")]
    [SerializeField] private int damage = 1;

    [Header("Lifetime")]
    [SerializeField] private float lifeTime = 3f;

    [Header("Collision")]
    [Tooltip("Layer(s) considérés comme mur bloquant — détruit le projectile au contact.")]
    [SerializeField] private LayerMask wallLayer;

    private Rigidbody2D rb;
    private float spawnTime;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        var col = GetComponent<CircleCollider2D>();
        col.isTrigger = true;

        spawnTime = Time.time;
    }

    /// <summary>Lance le projectile dans la direction donnée à la vitesse donnée.</summary>
    public void Launch(Vector2 direction, float speed)
    {
        if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;
        direction.Normalize();
        rb.linearVelocity = direction * speed;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
    }

    private void Update()
    {
        if (Time.time - spawnTime >= lifeTime)
            Destroy(gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var ph = other.GetComponent<PlayerStats>();
        if (ph == null) ph = other.GetComponentInParent<PlayerStats>();
        if (ph != null)
        {
            ph.TakeDamage(damage);
            Destroy(gameObject);
            return;
        }

        if (((1 << other.gameObject.layer) & wallLayer.value) != 0)
            Destroy(gameObject);
    }
}
