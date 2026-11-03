using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class DebrisProjectile : MonoBehaviour
{
    [Header("Lifetime")]
    [Tooltip("Durée (s) avant auto-destruction si le projectile n'a rien touché.")]
    [SerializeField] private float lifeTime = 2.5f;

    [Header("Collision")]
    [Tooltip("Layer(s) considéré(s) comme mur — à la collision, le projectile est détruit.")]
    [SerializeField] private LayerMask wallLayer;

    private Rigidbody2D rb;
    private float spawnTime;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;                                 // vol en ligne droite
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

        // Oriente le sprite dans la direction du vol (utile si tu mets une forme non symétrique).
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
        // On ne détruit que sur contact d'un mur (Layer Ground / wallLayer).
        if (((1 << other.gameObject.layer) & wallLayer.value) != 0)
            Destroy(gameObject);
    }
}
