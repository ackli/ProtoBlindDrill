using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Arme "Fusil à pompe" : à chaque tir, envoie <see cref="pelletCount"/> projectiles
/// <see cref="DebrisProjectile"/> dans un cône de <see cref="spreadDegrees"/>° devant la direction
/// de visée. Applique un knockback plus marqué que l'arme de base.
///
/// Résolution runtime des dépendances (quand AddComponent via Pickup) : l'InputActionAsset est
/// repris de <see cref="PlayerMovement2D.InputActions"/>, et le prefab de projectile de
/// <see cref="DebrisProjectile.DefaultPrefab"/> (mis en place par ShootingController au Awake).
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class ShotgunWeapon : MonoBehaviour, IWeapon
{
    [Header("Projectile")]
    [SerializeField] private DebrisProjectile projectilePrefab;
    [SerializeField] private float projectileSpeed = 18f;
    [SerializeField] private float muzzleOffset = 0.6f;

    [Header("Spread")]
    [Tooltip("Nombre de projectiles par salve.")]
    [SerializeField] private int pelletCount = 5;
    [Tooltip("Angle total du cône de dispersion (°). Les pellets sont répartis uniformément de -spread/2 à +spread/2.")]
    [SerializeField] private float spreadDegrees = 30f;

    [Header("Fire Rate & Damage")]
    [Tooltip("Délai strict entre deux salves. 0.6s empêche tout spam, même sur un clic très rapide.")]
    [SerializeField] private float fireCooldown = 0.6f;
    [SerializeField] private int baseDamagePerPellet = 1;

    [Header("Knockback")]
    [SerializeField] private float knockbackImpulse = 14f;
    [SerializeField] private float verticalKnockbackMultiplier = 1.15f;

    [Header("Aim")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float gamepadDeadzone = 0.3f;

    [Header("Input")]
    [SerializeField] private InputActionAsset inputActions;

    private Rigidbody2D rb;
    private PlayerStats stats;
    private InputAction shootAction;
    private InputAction aimAction;
    private float nextFireTime;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        stats = GetComponent<PlayerStats>();
        if (targetCamera == null) targetCamera = Camera.main;

        if (projectilePrefab == null) projectilePrefab = DebrisProjectile.DefaultPrefab;
        if (inputActions == null)
        {
            var movement = GetComponent<PlayerMovement2D>();
            if (movement != null) inputActions = movement.InputActions;
        }
        if (projectilePrefab == null)
        {
            Debug.LogError("[ShotgunWeapon] Aucun DebrisProjectile.DefaultPrefab disponible — arme inopérante.", this);
            enabled = false;
            return;
        }
        if (inputActions == null)
        {
            Debug.LogError("[ShotgunWeapon] InputActionAsset introuvable.", this);
            enabled = false;
            return;
        }

        var map = inputActions.FindActionMap("Player", throwIfNotFound: true);
        shootAction = map.FindAction("Shoot", throwIfNotFound: true);
        aimAction = map.FindAction("Aim", throwIfNotFound: true);
    }

    private void OnEnable()
    {
        if (shootAction == null || aimAction == null) return;
        shootAction.Enable();
        aimAction.Enable();
    }

    private void OnDisable()
    {
        if (shootAction == null || aimAction == null) return;
        shootAction.Disable();
        aimAction.Disable();
    }

    private void Update()
    {
        // Déclenchement SUR LA FRAME du clic uniquement (WasPerformedThisFrame) + cooldown strict.
        // Deux garanties contre le spam : (1) pas de IsPressed donc pas de tir continu,
        // (2) même un re-clic ultra-rapide est refusé tant que nextFireTime n'est pas atteint.
        if (shootAction == null || aimAction == null) return;
        if (!shootAction.WasPerformedThisFrame()) return;
        if (Time.time < nextFireTime) return;
        FireVolley();
    }

    private void FireVolley()
    {
        Vector2 dir = ComputeAimDirection();
        if (dir.sqrMagnitude < 0.0001f) return;
        nextFireTime = Time.time + fireCooldown;

        float mult = stats != null ? stats.DamageMultiplier : 1f;
        int dmg = Mathf.Max(1, Mathf.RoundToInt(baseDamagePerPellet * mult));

        float baseAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        float half = spreadDegrees * 0.5f;
        int n = Mathf.Max(1, pelletCount);

        for (int i = 0; i < n; i++)
        {
            // Répartition uniforme : -half, …, +half. Si n=1, le pellet part tout droit.
            float t = n == 1 ? 0.5f : (float)i / (n - 1);
            float angle = baseAngle - half + t * spreadDegrees;
            float rad = angle * Mathf.Deg2Rad;
            Vector2 pelletDir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

            Vector3 spawnPos = transform.position + (Vector3)(pelletDir * muzzleOffset);
            var proj = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
            proj.SetDamage(dmg);
            proj.Launch(pelletDir, projectileSpeed);
        }

        ApplyKnockback(-dir);
    }

    private Vector2 ComputeAimDirection()
    {
        Vector2 aim = aimAction.ReadValue<Vector2>();
        var active = aimAction.activeControl;
        bool usingGamepad = active != null && active.device is Gamepad && aim.magnitude > gamepadDeadzone;
        if (usingGamepad) return aim.normalized;
        if (targetCamera == null) return Vector2.zero;

        float planeDistance = Mathf.Abs(targetCamera.transform.position.z - transform.position.z);
        Vector3 screen = new Vector3(aim.x, aim.y, planeDistance);
        Vector3 world = targetCamera.ScreenToWorldPoint(screen);
        Vector2 d = (Vector2)world - (Vector2)transform.position;
        return d.sqrMagnitude < 0.0001f ? Vector2.right : d.normalized;
    }

    private void ApplyKnockback(Vector2 reverseDirection)
    {
        Vector2 impulse = new Vector2(reverseDirection.x, reverseDirection.y * verticalKnockbackMultiplier).normalized * knockbackImpulse;
        rb.AddForce(impulse, ForceMode2D.Impulse);
    }
}
