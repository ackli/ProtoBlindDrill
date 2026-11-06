/// <summary>
/// Interface marker pour un équipement de déplacement dans le slot "Movement" de <see cref="EquipmentManager"/>.
/// Même contrat que <see cref="IWeapon"/> : composant <see cref="UnityEngine.MonoBehaviour"/> posé sur
/// le joueur, autonome côté Input/Update. Le swap = Destroy ancien + AddComponent nouveau.
/// Gears prévus : WallJumpGear (défaut, le wall-jump est déjà dans PlayerMovement2D), Grappin, Jetpack…
/// Les gears futurs qui veulent couper le wall-jump pourront, dans leur Awake, agir sur
/// PlayerMovement2D (ex. flag à ajouter plus tard) ou désactiver des branches comportementales.
/// </summary>
public interface IMovementGear { }
