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

    [Tooltip("Taille de la boîte d'overlap (X = largeur légèrement < collider, Y = fine).")]
    [SerializeField] private Vector2 groundCheckSize = new Vector2(0.9f, 0.1f);

    [Tooltip("Layer(s) considéré(s) comme sol — assigner Ground.")]
    [SerializeField] private LayerMask groundLayer;

    [Header("Input")]
    [Tooltip("Référencer PlayerInputActions.inputactions.")]
    [SerializeField] private InputActionAsset inputActions;

    private Rigidbody2D rb;
    private InputAction moveAction;
    private InputAction jumpAction;

    private float moveInputX;
    private bool jumpHeld;
    private float lastGroundedTime = -999f;
    private float lastJumpPressedTime = -999f;
    private bool isGrounded;

    private float customGravity;
    private float initialJumpVelocity;

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
        ClampFallSpeed();
    }

    private void UpdateGroundCheck()
    {
        isGrounded = Physics2D.OverlapBox(groundCheck.position, groundCheckSize, 0f, groundLayer);
        if (isGrounded) lastGroundedTime = Time.time;
    }

    private void ApplyHorizontalMovement()
    {
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

        if (withinCoyote && withinBuffer)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, initialJumpVelocity);
            lastJumpPressedTime = -999f; // consommer l'appui bufferisé
            lastGroundedTime = -999f;    // empêcher un double-saut via coyote
        }
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

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = isGrounded ? Color.green : Color.yellow;
        Gizmos.DrawWireCube(groundCheck.position, groundCheckSize);
    }
}
