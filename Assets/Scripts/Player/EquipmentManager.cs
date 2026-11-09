using System;
using UnityEngine;

/// <summary>
/// Gère les deux slots d'équipement du joueur : Arme (IWeapon) et Déplacement (IMovementGear).
/// Au Start, résout le composant courant de chaque slot (déjà présent = défaut), ou en instancie
/// un via AddComponent à partir des <see cref="defaultWeaponType"/>/<see cref="defaultGearType"/>.
/// Swap : Destroy ancien + AddComponent nouveau (Type passé par <see cref="PickupItem"/>).
/// </summary>
public class EquipmentManager : MonoBehaviour
{
    [Header("Defaults (noms de type C#)")]
    [Tooltip("Nom de type de l'arme par défaut (ex: 'ShootingController'). Si un composant du même type est déjà sur le joueur, il est réutilisé — pas de doublon.")]
    [SerializeField] private string defaultWeaponType = "ShootingController";

    [Tooltip("Nom de type du gear par défaut (ex: 'WallJumpGear'). Marker pour le wall-jump déjà porté par PlayerMovement2D.")]
    [SerializeField] private string defaultGearType = "WallJumpGear";

    public event Action OnEquipmentChanged;

    private MonoBehaviour currentWeapon;
    private MonoBehaviour currentGear;

    private string currentWeaponDisplayName;
    private string currentGearDisplayName;

    public MonoBehaviour CurrentWeapon => currentWeapon;
    public MonoBehaviour CurrentGear => currentGear;
    public string WeaponName => currentWeapon != null ? currentWeapon.GetType().Name : "(aucune)";
    public string GearName => currentGear != null ? currentGear.GetType().Name : "(aucun)";

    /// <summary>Nom lisible de l'arme équipée (fourni par le PickupItem via displayName).
    /// Fallback sur <see cref="WeaponName"/> (nom de type C#) si aucun displayName n'a été passé
    /// — typiquement pour l'arme initiale résolue par <see cref="EnsureSlot"/>.</summary>
    public string WeaponDisplayName => string.IsNullOrEmpty(currentWeaponDisplayName) ? WeaponName : currentWeaponDisplayName;

    /// <summary>Nom lisible du gear équipé. Fallback sur <see cref="GearName"/> si aucun displayName.</summary>
    public string GearDisplayName => string.IsNullOrEmpty(currentGearDisplayName) ? GearName : currentGearDisplayName;

    /// <summary>Nom simple du type C# de l'arme équipée (ex: "ShotgunWeapon"), ou chaîne vide si aucune.
    /// Utilisé par <see cref="LevelObjectiveManager.GetRandomRewardPrefab"/> pour exclure du loot
    /// ce que le joueur a déjà.</summary>
    public string CurrentWeaponTypeName => currentWeapon != null ? currentWeapon.GetType().Name : string.Empty;

    /// <summary>Nom simple du type C# du gear équipé (ex: "JetpackGear"), ou chaîne vide si aucun.</summary>
    public string CurrentGearTypeName => currentGear != null ? currentGear.GetType().Name : string.Empty;

    private void Start()
    {
        currentWeapon = EnsureSlot(defaultWeaponType, typeof(IWeapon));
        currentGear = EnsureSlot(defaultGearType, typeof(IMovementGear));
        // Pas de displayName à l'init : les getters Display tomberont en fallback sur WeaponName/GearName.
        currentWeaponDisplayName = null;
        currentGearDisplayName = null;
        Debug.Log($"[EQUIP] Init → Arme: {WeaponName} | Gear: {GearName}");
        OnEquipmentChanged?.Invoke();
    }

    /// <summary>Remplace l'arme actuelle par une instance du <paramref name="newType"/>.</summary>
    public bool EquipWeapon(Type newType, string displayName = null)
    {
        bool ok = Swap(ref currentWeapon, newType, typeof(IWeapon), "Arme", displayName);
        if (ok) currentWeaponDisplayName = displayName;
        return ok;
    }

    /// <summary>Remplace le gear actuel par une instance du <paramref name="newType"/>.</summary>
    public bool EquipGear(Type newType, string displayName = null)
    {
        bool ok = Swap(ref currentGear, newType, typeof(IMovementGear), "Gear", displayName);
        if (ok) currentGearDisplayName = displayName;
        return ok;
    }

    private bool Swap(ref MonoBehaviour slot, Type newType, Type markerInterface, string slotLabel, string displayName)
    {
        if (newType == null)
        {
            Debug.LogWarning($"[EQUIP] {slotLabel} : type null — pickup ignoré.");
            return false;
        }
        if (!markerInterface.IsAssignableFrom(newType))
        {
            Debug.LogWarning($"[EQUIP] {slotLabel} : {newType.Name} n'implémente pas {markerInterface.Name} — pickup ignoré.");
            return false;
        }

        // Scan EXHAUSTIF : détruit TOUS les composants qui implémentent le marker du slot (pas uniquement
        // la ref trackée `slot`). Évite qu'un composant parasite (ajouté hors du manager, ou survivant
        // d'un précédent bug) reste actif en parallèle du nouveau — c'est CE scan qui tue l'écrasement
        // observé où ramasser un IMovementGear laissait un ancien IWeapon survivre et vice-versa.
        // IMPORTANT : on ne touche QUE aux composants du bon marker — l'autre slot est intact.
        var existing = GetComponents(markerInterface);
        for (int i = 0; i < existing.Length; i++)
        {
            var c = existing[i] as MonoBehaviour;
            if (c == null) continue;
            Debug.Log($"[EQUIP] {slotLabel} : destroy {c.GetType().Name}");
            Destroy(c);
        }
        slot = null;

        var comp = gameObject.AddComponent(newType) as MonoBehaviour;
        slot = comp;
        Debug.Log($"[EQUIP] {slotLabel} équipé : {displayName ?? newType.Name}");
        OnEquipmentChanged?.Invoke();
        return true;
    }

    /// <summary>Résout le composant d'un slot : réutilise celui déjà présent, sinon AddComponent du type nommé.</summary>
    private MonoBehaviour EnsureSlot(string typeName, Type markerInterface)
    {
        if (string.IsNullOrEmpty(typeName)) return null;
        var t = ResolveType(typeName);
        if (t == null) { Debug.LogWarning($"[EQUIP] Type '{typeName}' introuvable."); return null; }
        if (!markerInterface.IsAssignableFrom(t))
        {
            Debug.LogWarning($"[EQUIP] Type '{typeName}' n'implémente pas {markerInterface.Name}.");
            return null;
        }
        var existing = GetComponent(t) as MonoBehaviour;
        if (existing != null) return existing;
        return gameObject.AddComponent(t) as MonoBehaviour;
    }

    /// <summary>Résout un type C# par nom simple ou nom qualifié, en scannant les assemblies chargées.</summary>
    public static Type ResolveType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return null;
        var direct = Type.GetType(typeName);
        if (direct != null) return direct;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(typeName);
            if (t != null) return t;
        }
        return null;
    }
}
