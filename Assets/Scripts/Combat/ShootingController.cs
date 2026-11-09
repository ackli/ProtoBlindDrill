using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class ShootingController : MonoBehaviour, IWeapon
{
    [Header("Projectile")]
    [Tooltip("Prefab DebrisProjectile instancié à chaque tir.")]
    [SerializeField] private DebrisProjectile projectilePrefab;

    [Tooltip("Vitesse du projectile (unités/s).")]
    [SerializeField] private float projectileSpeed = 18f;

    [Tooltip("Offset de spawn depuis la position du joueur, dans la direction du tir (évite collision immédiate avec le Player).")]
    [SerializeField] private float muzzleOffset = 0.6f;

    [Header("Fire Rate")]
    [Tooltip("Délai minimum entre deux tirs (s). Avec 'Fire Mode = Hold', c'est la cadence du tir continu tant que le bouton est maintenu.")]
    [SerializeField] private float fireCooldown = 0.35f;

    [Header("Knockback")]
    [Tooltip("Impulsion appliquée au joueur dans la direction opposée au tir.")]
    [SerializeField] private float knockbackImpulse = 7f;

    [Tooltip("Multiplicateur de la composante verticale du knockback (>1 = recul plus pêchu si tir vers le bas).")]
    [SerializeField] private float verticalKnockbackMultiplier = 1.1f;

    [Header("Aiming")]
    [Tooltip("Caméra pour convertir la position souris en monde. Si null, Camera.main.")]
    [SerializeField] private Camera targetCamera;

    [Tooltip("À la manette, distance virtuelle du réticule depuis le joueur dans la direction du stick droit.")]
    [SerializeField] private float gamepadAimDistance = 2.5f;

    [Tooltip("Deadzone du stick droit avant de considérer qu'on vise à la manette.")]
    [SerializeField] private float gamepadDeadzone = 0.3f;

    [Header("Input")]
    [Tooltip("Référencer PlayerInputActions.inputactions.")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("Damage")]
    [Tooltip("Dégâts de base d'un tir. Multipliés par PlayerStats.DamageMultiplier au spawn pour intégrer les buffs reliques.")]
    [SerializeField] private int baseDamage = 1;

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

        // Si AddComponent via Pickup → projectilePrefab serialized est null. On le récupère
        // depuis le DefaultPrefab static, initialisé par la toute première instance posée sur le prefab Player.
        if (projectilePrefab == null) projectilePrefab = DebrisProjectile.DefaultPrefab;

        // Et on (re)partage le prefab pour les armes ajoutées en runtime (Shotgun, Flamethrower, et les futurs ShootingController).
        if (projectilePrefab != null) DebrisProjectile.DefaultPrefab = projectilePrefab;
        else Debug.LogError("[ShootingController] projectilePrefab introuvable (ni serialized, ni DefaultPrefab static). L'arme ne pourra pas tirer.", this);

        if (inputActions == null)
        {
            // Fallback runtime : l'AddComponent via Pickup n'a pas la ref serialized → on la pique à PlayerMovement2D.
            var movement = GetComponent<PlayerMovement2D>();
            if (movement != null) inputActions = movement.InputActions;
        }
        if (inputActions == null)
        {
            Debug.LogError("[ShootingController] InputActionAsset non assigné.", this);
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
        // Tir continu : tant que la touche est maintenue, on tire à la cadence fireCooldown.
        // Timer strict : si on relâche puis réappuie plus tôt, le délai persiste — pas de burst infini.
        if (shootAction == null || aimAction == null) return;
        if (!shootAction.IsPressed()) return;
        if (Time.time < nextFireTime) return;
        Fire();
    }

    private void Fire()
    {
        if (projectilePrefab == null) return;

        Vector2 aim = aimAction.ReadValue<Vector2>();
        Vector2 direction = ComputeFireDirection(aim);
        if (direction.sqrMagnitude < 0.0001f) return;

        // Spawn au bord du collider (muzzleOffset) pour éviter que le projectile touche le joueur.
        Vector3 spawnPos = transform.position + (Vector3)(direction * muzzleOffset);
        var projectile = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
        float mult = stats != null ? stats.DamageMultiplier : 1f;
        projectile.SetDamage(Mathf.Max(1, Mathf.RoundToInt(baseDamage * mult)));
        projectile.Launch(direction, projectileSpeed);

        ApplyKnockback(-direction);

        nextFireTime = Time.time + fireCooldown;
    }

    private Vector2 ComputeFireDirection(Vector2 aim)
    {
        var active = aimAction.activeControl;
        bool usingGamepad = active != null && active.device is Gamepad && aim.magnitude > gamepadDeadzone;

        if (usingGamepad)
            return aim.normalized;

        if (targetCamera == null) return Vector2.zero;

        float planeDistance = Mathf.Abs(targetCamera.transform.position.z - transform.position.z);
        Vector3 screen = new Vector3(aim.x, aim.y, planeDistance);
        Vector3 world = targetCamera.ScreenToWorldPoint(screen);
        Vector2 dir = (Vector2)world - (Vector2)transform.position;
        return dir.sqrMagnitude < 0.0001f ? Vector2.right : dir.normalized;
    }

    private void ApplyKnockback(Vector2 reverseDirection)
    {
        // Boost léger de la composante verticale (feel Downwell : tirer vers le bas te propulse en l'air).
        Vector2 impulse = new Vector2(
            reverseDirection.x,
            reverseDirection.y * verticalKnockbackMultiplier
        ).normalized * knockbackImpulse;

        rb.AddForce(impulse, ForceMode2D.Impulse);
    }
}
