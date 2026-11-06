/// <summary>
/// Interface marker pour une arme équipable dans le slot "Weapon" de <see cref="EquipmentManager"/>.
/// Un script d'arme est un <see cref="UnityEngine.MonoBehaviour"/> posé sur le joueur : il gère
/// lui-même son Input et son Update. Le swap d'arme = Destroy de l'ancien composant + AddComponent
/// du nouveau type. Pas de méthode obligatoire ici — on garde l'interface vide pour que
/// chaque arme reste maître de son cycle de vie (fire rate, input bindings, VFX…).
/// Armes prévues : ShootingController (lance-terre, défaut), Shotgun, Flamethrower, Sabre…
/// </summary>
public interface IWeapon { }
