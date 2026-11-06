using UnityEngine;

/// <summary>
/// Objet au sol à ramasser : déclare le slot ciblé (Arme / Déplacement) et le nom du type C# du
/// composant à équiper via <see cref="EquipmentManager"/>. Au trigger joueur, swap l'équipement
/// correspondant puis se détruit. Prévu pour les récompenses de fin d'arène.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class PickupItem : MonoBehaviour
{
    public enum EquipmentKind { Weapon, Movement }

    [Header("Équipement")]
    [Tooltip("Slot cible du swap.")]
    [SerializeField] private EquipmentKind kind = EquipmentKind.Weapon;

    [Tooltip("Nom du type C# du composant à AddComponent sur le joueur (ex: 'ShootingController', 'WallJumpGear'). Le type doit implémenter IWeapon ou IMovementGear selon le slot.")]
    [SerializeField] private string componentTypeName = "";

    [Tooltip("Nom affiché dans les logs/UI (ex: 'Fusil à pompe'). Cosmétique.")]
    [SerializeField] private string displayName = "Pickup";

    [Header("Juice")]
    [Tooltip("Vitesse de rotation visuelle (deg/s).")]
    [SerializeField] private float spinSpeed = 45f;

    public EquipmentKind Kind => kind;
    public string ComponentTypeName => componentTypeName;
    public string DisplayName => displayName;

    private void Awake()
    {
        var col = GetComponent<Collider2D>();
        if (!col.isTrigger) Debug.LogWarning("[PickupItem] Collider2D devrait être en isTrigger.", this);
    }

    private void Update()
    {
        if (spinSpeed != 0f) transform.Rotate(0f, 0f, spinSpeed * Time.deltaTime, Space.Self);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var eq = other.GetComponent<EquipmentManager>();
        if (eq == null) eq = other.GetComponentInParent<EquipmentManager>();
        if (eq == null) return;

        var type = EquipmentManager.ResolveType(componentTypeName);
        if (type == null)
        {
            Debug.LogWarning($"[PickupItem] Type '{componentTypeName}' introuvable — pickup ignoré.", this);
            return;
        }

        bool ok = kind == EquipmentKind.Weapon
            ? eq.EquipWeapon(type, displayName)
            : eq.EquipGear(type, displayName);

        if (ok) Destroy(gameObject);
    }
}
