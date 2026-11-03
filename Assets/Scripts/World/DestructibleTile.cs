using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tile avec des points de vie et un type de matériau. Instancier via le menu
/// "Assets > Create > BlindDrill > DestructibleTile", puis assigner un sprite
/// et régler maxHP (−1 = incassable).
/// </summary>
[CreateAssetMenu(menuName = "BlindDrill/DestructibleTile", fileName = "NewDestructibleTile")]
public class DestructibleTile : Tile
{
    public enum MaterialType { Dirt, Stone, Bedrock }

    [Header("Material")]
    public MaterialType material = MaterialType.Dirt;

    [Header("Mining")]
    [Tooltip("Points de vie maximum. -1 = incassable.")]
    public int maxHP = 1;

    [Header("Damage Feedback")]
    [Tooltip("Teinte quand la tile est intacte.")]
    public Color freshColor = Color.white;

    [Tooltip("Teinte quand la tile est juste avant destruction. Interpolée depuis freshColor selon les HP restants.")]
    public Color damagedColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    public bool IsIndestructible => maxHP < 0;

    public Color GetTintForHP(int currentHP)
    {
        if (IsIndestructible || maxHP <= 1) return freshColor;
        // t: 0 quand fraîche, 1 quand presque détruite
        float t = 1f - Mathf.Clamp01((float)currentHP / maxHP);
        return Color.Lerp(freshColor, damagedColor, t);
    }
}
