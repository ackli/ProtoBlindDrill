using UnityEngine;

/// <summary>
/// Gear de déplacement par défaut : marker inerte qui signale que le wall-jump actif
/// (implémenté dans <see cref="PlayerMovement2D"/>) est bien le comportement actuel.
/// Pas de logique propre : la mécanique vit déjà dans PlayerMovement2D. Les gears alternatifs
/// (Grappin, Jetpack) remplaceront ce composant via <see cref="EquipmentManager.EquipGear"/>.
/// </summary>
public class WallJumpGear : MonoBehaviour, IMovementGear { }
