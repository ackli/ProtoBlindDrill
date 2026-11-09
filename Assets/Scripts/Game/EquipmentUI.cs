using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD d'équipement : affiche l'arme et le gear de déplacement actuellement portés par le joueur.
/// S'abonne à <see cref="EquipmentManager.OnEquipmentChanged"/> et reconstruit un Text à deux lignes
/// via <see cref="EquipmentManager.WeaponDisplayName"/> / <see cref="EquipmentManager.GearDisplayName"/>
/// (fallback automatique sur le nom de type C# si aucun displayName n'a été fourni par le pickup).
/// Pattern miroir de <see cref="StatsUI"/> pour cohérence (UnityEngine.UI.Text + StringBuilder).
/// </summary>
public class EquipmentUI : MonoBehaviour
{
    [Tooltip("Référence à l'EquipmentManager du joueur. Auto-résolu par tag 'Player' si laissé vide.")]
    [SerializeField] private EquipmentManager equipmentManager;

    [Tooltip("Text qui affiche l'équipement courant. Deux lignes : \"Arme: …\" / \"Gear: …\".")]
    [SerializeField] private Text equipmentText;

    [Tooltip("Masque le Text quand aucun équipement n'est actif (très rare — slot vide des deux côtés).")]
    [SerializeField] private bool hideWhenEmpty = false;

    private readonly StringBuilder sb = new StringBuilder(128);

    private void Start()
    {
        if (equipmentManager == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) equipmentManager = go.GetComponent<EquipmentManager>();
        }
        if (equipmentManager == null)
        {
            Debug.LogWarning("[EquipmentUI] EquipmentManager introuvable.", this);
            enabled = false;
            return;
        }
        if (equipmentText == null)
        {
            Debug.LogWarning("[EquipmentUI] equipmentText non assigné — rien à peindre.", this);
            return;
        }
        equipmentManager.OnEquipmentChanged += Refresh;
        Refresh();
    }

    private void OnDestroy()
    {
        if (equipmentManager != null) equipmentManager.OnEquipmentChanged -= Refresh;
    }

    private void Refresh()
    {
        if (equipmentText == null) return;
        sb.Length = 0;
        sb.Append("Arme: ").Append(equipmentManager.WeaponDisplayName).Append('\n');
        sb.Append("Gear: ").Append(equipmentManager.GearDisplayName);

        equipmentText.text = sb.ToString();
        if (hideWhenEmpty) equipmentText.enabled = sb.Length > 0;
    }
}
