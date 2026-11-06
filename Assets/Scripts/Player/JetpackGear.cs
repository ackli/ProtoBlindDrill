using UnityEngine;

/// <summary>
/// Gear de déplacement "Jetpack" : tant que la touche Jump est maintenue, applique une poussée
/// vers le haut capée. Désactive le wall-jump natif de <see cref="PlayerMovement2D"/> le temps
/// d'être équipé (le gear rétablit le flag au OnDestroy, de sorte qu'un swap vers WallJumpGear
/// réactive naturellement le wall-jump).
///
/// L'input Jump est lu via <see cref="PlayerMovement2D.JumpHeld"/> — pas de double abonnement
/// d'InputAction qui dupliquerait des événements.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class JetpackGear : MonoBehaviour, IMovementGear
{
    [Header("Thrust")]
    [Tooltip("Accélération verticale (u/s²) appliquée tant que Jump est maintenu. Grandir = montée plus nerveuse.")]
    [SerializeField] private float thrust = 60f;

    [Tooltip("Vitesse verticale maximale sous poussée (u/s). Évite la montée infinie vers l'orbite.")]
    [SerializeField] private float maxUpSpeed = 14f;

    private Rigidbody2D rb;
    private PlayerMovement2D movement;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        movement = GetComponent<PlayerMovement2D>();
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
        if (!movement.JumpHeld) return;

        // Poussée additive : on incrémente la vY jusqu'à maxUpSpeed. Ne touche pas vX.
        float newY = rb.linearVelocity.y + thrust * Time.fixedDeltaTime;
        newY = Mathf.Min(newY, maxUpSpeed);
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, newY);
    }
}
