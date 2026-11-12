using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Arme "Lance-flammes" : tant que la touche Tir est maintenue (phasing <see cref="shootAction"/>.IsPressed),
/// crache en flux continu de petits <see cref="DebrisProjectile"/> à très courte durée de vie.
/// Portée limitée (lifetime court × vitesse = portée effective), dispersion naturelle via
/// <see cref="jitterDegrees"/>. Dégâts par particule faibles mais tick-rate élevé → DPS global convenable.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class FlamethrowerWeapon : MonoBehaviour, IWeapon
{
    [Header("Projectile (flamme)")]
    [SerializeField] private DebrisProjectile projectilePrefab;
    [SerializeField] private float projectileSpeed = 14f;
    [SerializeField] private float muzzleOffset = 0.5f;
    [Tooltip("Portée effective de la flamme (unités monde). particleLifetime est dérivé en Awake par la formule : lifetime = effectiveRange / projectileSpeed. Tune ici en u pour avoir une lecture directe de la portée, pas en secondes.")]
    [SerializeField] private float effectiveRange = 6f;
    private float particleLifetime;

    [Header("Flux")]
    [Tooltip("Intervalle (s) entre deux particules spawnées tant que la touche est maintenue.")]
    [SerializeField] private float tickInterval = 0.05f;
    [Tooltip("Particules émises à chaque tick (petit cone).")]
    [SerializeField] private int particlesPerTick = 2;
    [Tooltip("Dispersion angulaire par particule (°). Donne l'effet 'flamme qui s'écarte'.")]
    [SerializeField] private float jitterDegrees = 12f;

    [Header("Damage")]
    [SerializeField] private int baseDamagePerParticle = 1;

    [Header("Aim")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float gamepadDeadzone = 0.3f;

    [Header("Input")]
    [SerializeField] private InputActionAsset inputActions;

    private PlayerStats stats;
    private InputAction shootAction;
    private InputAction aimAction;
    private float nextTickTime;

    private void Awake()
    {
        stats = GetComponent<PlayerStats>();
        if (targetCamera == null) targetCamera = Camera.main;

        // Portée = vitesse × durée de vie. On dérive la durée depuis effectiveRange pour que
        // le champ Inspector reste lisible (en u, pas en s). Fallback 0.25s si speed = 0.
        particleLifetime = projectileSpeed > 0.01f ? effectiveRange / projectileSpeed : 0.25f;

        if (projectilePrefab == null) projectilePrefab = DebrisProjectile.DefaultPrefab;
        if (inputActions == null)
        {
            var movement = GetComponent<PlayerMovement2D>();
            if (movement != null) inputActions = movement.InputActions;
        }
        if (projectilePrefab == null)
        {
            Debug.LogError("[FlamethrowerWeapon] DebrisProjectile.DefaultPrefab indisponible — arme inopérante.", this);
            enabled = false;
            return;
        }
        if (inputActions == null)
        {
            Debug.LogError("[FlamethrowerWeapon] InputActionAsset introuvable.", this);
            enabled = false;
            return;
        }

        var map = inputActions.FindActionMap("Player", throwIfNotFound: true);
        shootAction = map.FindAction("Shoot", throwIfNotFound: true);
        aimAction = map.FindAction("Aim", throwIfNotFound: true);
    }

    private void OnEnable()
    {
        shootAction?.Enable();
        aimAction?.Enable();
    }

    private void OnDisable()
    {
        shootAction?.Disable();
        aimAction?.Disable();
    }

    private void Update()
    {
        if (shootAction == null || aimAction == null) return;
        if (!shootAction.IsPressed()) return;
        if (Time.time < nextTickTime) return;
        nextTickTime = Time.time + Mathf.Max(0.01f, tickInterval);

        Vector2 dir = ComputeAimDirection();
        if (dir.sqrMagnitude < 0.0001f) return;

        float baseAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        float mult = stats != null ? stats.DamageMultiplier : 1f;
        int dmg = Mathf.Max(1, Mathf.RoundToInt(baseDamagePerParticle * mult));

        int n = Mathf.Max(1, particlesPerTick);
        for (int i = 0; i < n; i++)
        {
            float angle = baseAngle + Random.Range(-jitterDegrees, jitterDegrees);
            float rad = angle * Mathf.Deg2Rad;
            Vector2 pDir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

            Vector3 spawnPos = transform.position + (Vector3)(pDir * muzzleOffset);
            var proj = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
            proj.SetDamage(dmg);
            proj.Launch(pDir, projectileSpeed);
            // Vie courte : la portée effective = lifetime × speed. On Destroy manuellement pour
            // être sûr de couper, même si le prefab a un lifeTime plus long par défaut.
            Destroy(proj.gameObject, particleLifetime);
        }
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
}
