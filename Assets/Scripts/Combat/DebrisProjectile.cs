using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class DebrisProjectile : MonoBehaviour
{
    /// <summary>Prefab de référence partagé, set par le premier <see cref="ShootingController"/> au Awake.
    /// Utilisé par les armes ajoutées en runtime via AddComponent (Shotgun, Flamethrower…) pour éviter
    /// de resérialiser la ref sur chaque instance.</summary>
    public static DebrisProjectile DefaultPrefab { get; set; }

    [Header("Damage")]
    [Tooltip("Dégâts infligés aux mobs touchés. Set par ShootingController au spawn, multiplié par PlayerStats.DamageMultiplier pour intégrer le buff Damage des reliques.")]
    [SerializeField] private int damage = 1;

    /// <summary>Dégâts que ce projectile applique au MobBase touché. Set via <see cref="SetDamage"/>.</summary>
    public int Damage => damage;

    /// <summary>Appelé par l'émetteur (ShootingController) juste après Instantiate pour intégrer le multiplicateur de dégâts du joueur.</summary>
    public void SetDamage(int value) => damage = Mathf.Max(1, value);

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
