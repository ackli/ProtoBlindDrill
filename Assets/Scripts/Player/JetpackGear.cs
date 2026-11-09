using UnityEngine;

/// <summary>
/// Gear de déplacement "Jetpack" : tant que la touche Jump est maintenue ET qu'il reste du
/// carburant, applique une poussée vers le haut capée. Le carburant se consomme en vol
/// (<see cref="drainRate"/>) et se régénère uniquement au contact du sol
/// (<see cref="rechargeRate"/>) — empêche le vol infini et la sortie de map.
///
/// Un latch <see cref="outOfFuel"/> + un palier <see cref="minFuelToLiftoff"/> évitent le
/// "sputtering" (alternance drain/recharge au millième de seconde) quand la jauge est à sec.
///
/// Désactive le wall-jump natif de <see cref="PlayerMovement2D"/> le temps d'être équipé
/// (le gear rétablit le flag au OnDestroy, de sorte qu'un swap vers WallJumpGear réactive
/// naturellement le wall-jump).
///
/// L'input Jump et l'état "au sol" sont lus via <see cref="PlayerMovement2D.JumpHeld"/>
/// et <see cref="PlayerMovement2D.IsGrounded"/> — pas de double abonnement d'InputAction
/// ni de ground-check dupliqué.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class JetpackGear : MonoBehaviour, IMovementGear
{
    [Header("Thrust")]
    [Tooltip("Accélération verticale (u/s²) appliquée tant que Jump est maintenu. Grandir = montée plus nerveuse.")]
    [SerializeField] private float thrust = 60f;

    [Tooltip("Vitesse verticale maximale sous poussée (u/s). Évite la montée infinie vers l'orbite.")]
    [SerializeField] private float maxUpSpeed = 14f;

    [Header("Fuel")]
    [Tooltip("Capacité maximale de carburant (unités abstraites ≈ secondes de poussée à drainRate=1).")]
    [SerializeField] private float maxFuel = 2.5f;

    [Tooltip("Consommation de carburant en vol (u/s). À 1 avec maxFuel=2.5, ≈ 2.5 s de vol continu.")]
    [SerializeField] private float drainRate = 1f;

    [Tooltip("Régénération de carburant au sol (u/s). À 4 avec maxFuel=2.5, plein refait en ≈ 0.6 s.")]
    [SerializeField] private float rechargeRate = 4f;

    [Tooltip("Carburant minimum requis pour redécoller après panne sèche. Palier anti-sputtering (micro-poussées en boucle).")]
    [SerializeField] private float minFuelToLiftoff = 0.15f;

    private Rigidbody2D rb;
    private PlayerMovement2D movement;

    private float currentFuel;
    private bool outOfFuel;

    /// <summary>Niveau de carburant normalisé [0,1] — exposé pour un futur UI de jauge.</summary>
    public float FuelNormalized => maxFuel > 0f ? Mathf.Clamp01(currentFuel / maxFuel) : 0f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        movement = GetComponent<PlayerMovement2D>();
        currentFuel = maxFuel;
        outOfFuel = false;
        if (movement != null)
        {
            movement.WallJumpEnabled = false;
            Debug.Log("[JETPACK] Équipé → wall-jump désactivé.");
        }
    }

    private void OnDestroy()
    {
        // Le swap vers un autre gear (ou le simple retrait) doit rendre le wall-jump au joueur.
        if (movement != null) movement.WallJumpEnabled = true;
    }

    private void FixedUpdate()
    {
        if (movement == null || rb == null) return;

        bool wantsThrust = movement.JumpHeld && !outOfFuel && currentFuel > 0f;

        if (wantsThrust)
        {
            // Poussée additive : on incrémente la vY jusqu'à maxUpSpeed. Ne touche pas vX.
            float newY = rb.linearVelocity.y + thrust * Time.fixedDeltaTime;
            newY = Mathf.Min(newY, maxUpSpeed);
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, newY);

            currentFuel -= drainRate * Time.fixedDeltaTime;
            if (currentFuel <= 0f)
            {
                currentFuel = 0f;
                outOfFuel = true;
            }
        }
        else if (movement.IsGrounded)
        {
            // Recharge uniquement au contact du sol — hors map = pas de recharge.
            currentFuel = Mathf.Min(maxFuel, currentFuel + rechargeRate * Time.fixedDeltaTime);
            if (outOfFuel && currentFuel >= minFuelToLiftoff)
                outOfFuel = false;
        }
        // En l'air sans input (ou en panne sèche en l'air) : fuel figé, chute naturelle via la gravité de PlayerMovement2D.
    }
}
