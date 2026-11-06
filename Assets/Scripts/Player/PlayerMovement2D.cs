using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement2D : MonoBehaviour
{
    [Header("Horizontal Movement")]
    [Tooltip("Vitesse horizontale cible (unités Unity / seconde).")]
    [SerializeField] private float moveSpeed = 8f;

    [Tooltip("Accélération au sol pour atteindre moveSpeed (u/s²).")]
    [SerializeField] private float accelerationGrounded = 70f;

    [Tooltip("Décélération au sol quand l'input est relâché (u/s²).")]
    [SerializeField] private float decelerationGrounded = 90f;

    [Tooltip("Accélération en l'air (u/s²).")]
    [SerializeField] private float accelerationAirborne = 40f;

    [Tooltip("Décélération en l'air quand l'input est relâché (u/s²).")]
    [SerializeField] private float decelerationAirborne = 30f;

    [Header("Jump")]
    [Tooltip("Hauteur maximale d'un saut maintenu (unités Unity).")]
    [SerializeField] private float jumpHeight = 3.2f;

    [Tooltip("Temps (s) pour atteindre le sommet d'un saut maintenu.")]
    [SerializeField] private float timeToApex = 0.35f;

    [Tooltip("Multiplicateur de gravité quand le joueur retombe (>1 = chute plus pêchue).")]
    [SerializeField] private float fallGravityMultiplier = 1.6f;

    [Tooltip("Multiplicateur de gravité quand le saut est relâché tôt (petit saut).")]
    [SerializeField] private float lowJumpMultiplier = 2.4f;

    [Tooltip("Fenêtre après avoir quitté le sol où le saut reste permis (s).")]
    [SerializeField] private float coyoteTime = 0.1f;

    [Tooltip("Fenêtre avant d'atterrir où un appui Jump sera consommé à l'atterrissage (s).")]
    [SerializeField] private float jumpBufferTime = 0.12f;

    [Tooltip("Vitesse verticale maximale en chute (clamp, u/s). Évite l'accélération infinie.")]
    [SerializeField] private float maxFallSpeed = 24f;

    [Header("Ground Check")]
    [Tooltip("Pivot placé aux pieds du joueur pour l'OverlapBox de détection du sol.")]
    [SerializeField] private Transform groundCheck;

    [Tooltip("Taille de la boîte d'overlap. X doit être STRICTEMENT < largeur du collider pour ne pas détecter les murs latéraux. Y très fin.")]
    [SerializeField] private Vector2 groundCheckSize = new Vector2(0.5f, 0.08f);

    [Tooltip("Layer(s) considéré(s) comme sol — assigner Ground.")]
    [SerializeField] private LayerMask groundLayer;

    [Header("Wall Jump")]
    [Tooltip("Offset horizontal (depuis le centre du joueur) des boîtes de détection de mur. ~ demi-largeur du collider + petite marge.")]
    [SerializeField] private float wallCheckOffset = 0.5f;

    [Tooltip("Taille de la boîte wall-check. X suffisamment large pour englober le bord du collider ET pénétrer dans la tuile (sinon la détection est aléatoire). Y ≈ hauteur du collider − marge pour ne pas déborder dans le sol/plafond.")]
    [SerializeField] private Vector2 wallCheckSize = new Vector2(0.25f, 1.0f);

    [Tooltip("Force appliquée au wall jump. X = répulsion du mur (plus bas = trajectoire plus verticale), Y = impulsion verticale.")]
    [SerializeField] private Vector2 wallJumpForce = new Vector2(6f, 15f);

    [Tooltip("Durée (s) pendant laquelle l'input horizontal est ignoré après un wall jump — évite le recollage immédiat au mur.")]
    [SerializeField] private float wallJumpDuration = 0.2f;

    [Tooltip("Vitesse maximale de chute (unités absolues) tant que le joueur est collé à un mur : simule l'accroche + glissade. Mettre 0 pour désactiver.")]
    [SerializeField] private float wallSlideFallSpeed = 3f;

    [Tooltip("Seuil minimal d'input horizontal pour qu'un wall jump se déclenche (l'input DOIT pointer à l'opposé du mur — ex. mur à droite → appuyer à gauche).")]
    [Range(0f, 1f)]
    [SerializeField] private float wallJumpInputThreshold = 0.2f;

    [Header("Input")]
    [Tooltip("Référencer PlayerInputActions.inputactions.")]
    [SerializeField] private InputActionAsset inputActions;

    private Rigidbody2D rb;
    private InputAction moveAction;
    private InputAction jumpAction;

    private float moveInputX;
    private bool jumpHeld;

    /// <summary>Flag contrôlable par un IMovementGear (ex: JetpackGear) pour couper le wall-jump
    /// natif. True par défaut (comportement actuel inchangé).</summary>
    public bool WallJumpEnabled { get; set; } = true;

    /// <summary>État "touche Jump maintenue" exposé pour les gears (JetpackGear l'utilise pour la poussée).</summary>
    public bool JumpHeld => jumpHeld;

    /// <summary>InputActionAsset partagé — utilisé par les armes ajoutées en runtime (AddComponent) pour éviter d'avoir à le resérialiser.</summary>
    public InputActionAsset InputActions => inputActions;
    private float lastGroundedTime = -999f;
    private float lastJumpPressedTime = -999f;
    private bool isGrounded;

    private float customGravity;
    private float initialJumpVelocity;

    // Verrou input horizontal post-wall-jump : tant que Time.time < wallJumpLockUntil,
    // ApplyHorizontalMovement est court-circuité pour laisser la répulsion s'exprimer.
    private float wallJumpLockUntil = -999f;
    private int lastWallDir;        // -1 = mur à gauche, +1 = mur à droite, 0 = aucun (debug Gizmos)

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        // Dérivation cinématique :
        //   h = 0.5 * g * t²  →  g = 2h / t²
        //   v = g * t         →  v = 2h / t
        customGravity = (2f * jumpHeight) / (timeToApex * timeToApex);
        initialJumpVelocity = customGravity * timeToApex;

        // Physics2D.gravity.y est négatif (-9.81 par défaut). On règle gravityScale
        // pour que la gravité effective sur ce Rigidbody vaille customGravity.
        rb.gravityScale = customGravity / Mathf.Abs(Physics2D.gravity.y);
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        if (inputActions == null)
        {
            Debug.LogError("[PlayerMovement2D] InputActionAsset non assigné.", this);
            enabled = false;
            return;
        }

        var map = inputActions.FindActionMap("Player", throwIfNotFound: true);
        moveAction = map.FindAction("Move", throwIfNotFound: true);
        jumpAction = map.FindAction("Jump", throwIfNotFound: true);
    }

    private void OnEnable()
    {
        if (moveAction == null || jumpAction == null) return;
        moveAction.Enable();
        jumpAction.Enable();
        jumpAction.started += OnJumpStarted;
        jumpAction.canceled += OnJumpCanceled;
    }

    private void OnDisable()
    {
        if (moveAction == null || jumpAction == null) return;
        jumpAction.started -= OnJumpStarted;
        jumpAction.canceled -= OnJumpCanceled;
        moveAction.Disable();
        jumpAction.Disable();
    }

    private void OnJumpStarted(InputAction.CallbackContext _)
    {
        lastJumpPressedTime = Time.time;
        jumpHeld = true;
    }

    private void OnJumpCanceled(InputAction.CallbackContext _)
    {
        jumpHeld = false;
    }

    private void Update()
    {
        // Lecture d'input dans Update : plus réactif que FixedUpdate pour un platformer.
        moveInputX = moveAction.ReadValue<Vector2>().x;
    }

    private void FixedUpdate()
    {
        UpdateGroundCheck();
        ApplyHorizontalMovement();
        TryConsumeBufferedJump();
        ApplyGravityTweaks();
        ApplyWallSlide();
        ClampFallSpeed();
    }

    private void UpdateGroundCheck()
    {
        isGrounded = Physics2D.OverlapBox(groundCheck.position, groundCheckSize, 0f, groundLayer);
        if (isGrounded) lastGroundedTime = Time.time;
    }

    private void ApplyHorizontalMovement()
    {
        // Pendant la fenêtre post-wall-jump : l'input horizontal est ignoré pour que la
        // force de répulsion diagonale ne soit pas annulée par un input vers le mur.
        if (Time.time < wallJumpLockUntil) return;

        float target = moveInputX * moveSpeed;
        float accel = isGrounded ? accelerationGrounded : accelerationAirborne;
        float decel = isGrounded ? decelerationGrounded : decelerationAirborne;
        float rate = Mathf.Abs(moveInputX) > 0.01f ? accel : decel;

        float newX = Mathf.MoveTowards(rb.linearVelocity.x, target, rate * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(newX, rb.linearVelocity.y);
    }

    private void TryConsumeBufferedJump()
    {
        bool withinCoyote = (Time.time - lastGroundedTime) <= coyoteTime;
        bool withinBuffer = (Time.time - lastJumpPressedTime) <= jumpBufferTime;

        // 1. Saut au sol (ou coyote).
        if (withinCoyote && withinBuffer)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, initialJumpVelocity);
            lastJumpPressedTime = -999f; // consommer l'appui bufferisé
            lastGroundedTime = -999f;    // empêcher un double-saut via coyote
            return;
        }

        // 2. Wall jump : input bufferisé + en l'air (hors coyote) + mur à portée
        //    + input horizontal VERS le mur.
        //    → Mur à droite  (+1) : il faut tenir D (moveInputX > 0)
        //    → Mur à gauche (-1) : il faut tenir Q (moveInputX < 0)
        //    La propulsion part quand même du côté opposé au mur ("Mario style").
        //    Désactivé si WallJumpEnabled = false (ex: JetpackGear équipé).
        if (withinBuffer && !isGrounded && WallJumpEnabled)
        {
            int wallDir = GetWallDirection();
            if (wallDir != 0)
            {
                bool inputTowardWall =
                    Mathf.Abs(moveInputX) > wallJumpInputThreshold
                    && Mathf.Sign(moveInputX) == wallDir;

                if (inputTowardWall)
                {
                    // Annule la vélocité Y existante, applique Y vers le haut + X opposé au mur.
                    rb.linearVelocity = new Vector2(-wallDir * wallJumpForce.x, wallJumpForce.y);
                    lastJumpPressedTime = -999f;
                    wallJumpLockUntil = Time.time + wallJumpDuration;
                }
            }
        }
    }

    /// <summary>
    /// Retourne le signe du mur en contact (+1 à droite, −1 à gauche, 0 aucun).
    /// Utilise deux OverlapBox verticaux de chaque côté du joueur sur le groundLayer.
    /// </summary>
    private int GetWallDirection()
    {
        Vector2 center = transform.position;
        Vector2 rightCenter = center + Vector2.right * wallCheckOffset;
        Vector2 leftCenter = center + Vector2.left * wallCheckOffset;

        bool wallRight = Physics2D.OverlapBox(rightCenter, wallCheckSize, 0f, groundLayer);
        bool wallLeft = Physics2D.OverlapBox(leftCenter, wallCheckSize, 0f, groundLayer);

        if (wallRight && !wallLeft) { lastWallDir = +1; return +1; }
        if (wallLeft && !wallRight) { lastWallDir = -1; return -1; }
        lastWallDir = 0;
        return 0;
    }

    private void ApplyGravityTweaks()
    {
        // Chute : gravité renforcée pour une descente nerveuse.
        if (rb.linearVelocity.y < 0f)
        {
            float extra = customGravity * (fallGravityMultiplier - 1f);
            rb.linearVelocity += Vector2.down * (extra * Time.fixedDeltaTime);
        }
        // Montée mais Jump relâché : petit saut (coupe l'ascension plus tôt).
        else if (rb.linearVelocity.y > 0f && !jumpHeld)
        {
            float extra = customGravity * (lowJumpMultiplier - 1f);
            rb.linearVelocity += Vector2.down * (extra * Time.fixedDeltaTime);
        }
    }

    private void ClampFallSpeed()
    {
        if (rb.linearVelocity.y < -maxFallSpeed)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, -maxFallSpeed);
    }

    /// <summary>
    /// Si le joueur est en l'air, touche un mur et retombe, bride la vitesse de chute à
    /// <see cref="wallSlideFallSpeed"/>. Donne la sensation d'accrocher le mur et de glisser.
    /// Ne s'applique pas pendant le verrou post-wall-jump pour laisser la trajectoire s'exprimer.
    /// </summary>
    private void ApplyWallSlide()
    {
        if (!WallJumpEnabled) return;              // cohérence avec le wall-jump : si le gear l'a coupé, pas de slide non plus
        if (wallSlideFallSpeed <= 0f) return;
        if (isGrounded) return;
        if (rb.linearVelocity.y >= 0f) return;         // en montée : pas de slide
        if (Time.time < wallJumpLockUntil) return;     // on vient de se propulser, laisser filer
        if (GetWallDirection() == 0) return;

        if (rb.linearVelocity.y < -wallSlideFallSpeed)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, -wallSlideFallSpeed);
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = isGrounded ? Color.green : Color.yellow;
            Gizmos.DrawWireCube(groundCheck.position, groundCheckSize);
        }

        // Wall-check gizmos : rouge si touche, cyan si vide. Visible même hors Play.
        Vector3 c = transform.position;
        Vector3 right = c + Vector3.right * wallCheckOffset;
        Vector3 left = c + Vector3.left * wallCheckOffset;
        Gizmos.color = lastWallDir == +1 ? Color.red : Color.cyan;
        Gizmos.DrawWireCube(right, wallCheckSize);
        Gizmos.color = lastWallDir == -1 ? Color.red : Color.cyan;
        Gizmos.DrawWireCube(left, wallCheckSize);
    }
}
