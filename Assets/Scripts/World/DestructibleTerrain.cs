using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Composant posé à côté d'une Tilemap. Centralise les HP par cellule et expose
/// une API TryMine. Garde la Tilemap comme simple stockage visuel/physique.
/// </summary>
[RequireComponent(typeof(Tilemap))]
public class DestructibleTerrain : MonoBehaviour
{
    public enum MineResult { NoTile, NonDestructible, Indestructible, Damaged, Destroyed }

    [Header("Loot")]
    [Tooltip("Prefab LightOrb spawnable à la destruction d'une tuile de terre (Dirt).")]
    [SerializeField] private LightOrb lightOrbPrefab;

    [Range(0f, 1f)]
    [Tooltip("Probabilité de spawner un LightOrb sur destruction d'un Dirt.")]
    [SerializeField] private float orbSpawnChance = 0.15f;

    private Tilemap tilemap;
    private readonly Dictionary<Vector3Int, int> cellHP = new Dictionary<Vector3Int, int>();

    private void Awake()
    {
        tilemap = GetComponent<Tilemap>();
    }

    /// <summary>
    /// Applique 1 point de dégât à la cellule visée. Détruit la tile si les HP tombent à 0.
    /// </summary>
    public MineResult TryMine(Vector3Int cell)
    {
        if (tilemap == null) tilemap = GetComponent<Tilemap>();

        var raw = tilemap.GetTile(cell);
        if (raw == null) return MineResult.NoTile;

        var destructible = raw as DestructibleTile;
        if (destructible == null) return MineResult.NonDestructible;
        if (destructible.IsIndestructible) return MineResult.Indestructible;

        int current = cellHP.TryGetValue(cell, out var hp) ? hp : destructible.maxHP;
        current -= 1;

        if (current <= 0)
        {
            tilemap.SetTile(cell, null);
            cellHP.Remove(cell);
            TrySpawnOrb(cell, destructible);
            return MineResult.Destroyed;
        }

        cellHP[cell] = current;
        // Autoriser la modification de couleur par cellule (verrouillée par défaut).
        tilemap.SetTileFlags(cell, TileFlags.None);
        tilemap.SetColor(cell, destructible.GetTintForHP(current));
        return MineResult.Damaged;
    }

    /// <summary>Remet à zéro les dégâts mémorisés (utile si tu régénères le terrain).</summary>
    public void ClearDamageState() => cellHP.Clear();

    private void TrySpawnOrb(Vector3Int cell, DestructibleTile tile)
    {
        if (lightOrbPrefab == null) return;
        if (tile.material != DestructibleTile.MaterialType.Dirt) return; // seulement sur dirt
        if (Random.value > orbSpawnChance) return;

        Vector3 pos = tilemap.GetCellCenterWorld(cell);
        Instantiate(lightOrbPrefab, pos, Quaternion.identity);
    }
}
