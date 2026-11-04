using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Tilemaps;

/// <summary>
/// Composant posé à côté d'une Tilemap. Centralise les HP par cellule, le loot,
/// et la synchro vers les Blockers 3D (NavMeshObstacle Carve) pour le pathfinding.
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

    [Header("NavMesh Blockers (3D)")]
    [Tooltip("Prefab de Blocker 3D optionnel. Laisse vide pour générer dynamiquement un GameObject avec BoxCollider + NavMeshObstacle (Carve).")]
    [SerializeField] private GameObject blockerPrefab;

    [Tooltip("Profondeur Z où les Blockers 3D sont placés. Doit être aligné avec NavMeshPlane3D.")]
    [SerializeField] private float blockerZ = 5f;

    [Tooltip("Parent des Blockers 3D. Auto-créé (enfant 'Blockers') si vide.")]
    [SerializeField] private Transform blockersParent;

    private Tilemap tilemap;
    private readonly Dictionary<Vector3Int, int> cellHP = new Dictionary<Vector3Int, int>();
    private readonly Dictionary<Vector3Int, GameObject> blockers = new Dictionary<Vector3Int, GameObject>();

    private void Awake()
    {
        tilemap = GetComponent<Tilemap>();
    }

    /// <summary>
    /// Applique 1 point de dégât à la cellule visée. Détruit la tile ET son Blocker 3D si les HP tombent à 0.
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
            DestroyBlocker(cell);
            TrySpawnOrb(cell, destructible);
            return MineResult.Destroyed;
        }

        cellHP[cell] = current;
        tilemap.SetTileFlags(cell, TileFlags.None);
        tilemap.SetColor(cell, destructible.GetTintForHP(current));
        return MineResult.Damaged;
    }

    /// <summary>Remet à zéro les dégâts mémorisés (utile si tu régénères le terrain).</summary>
    public void ClearDamageState() => cellHP.Clear();

    /// <summary>
    /// Crée (ou réutilise) un Blocker 3D pour la cellule donnée. Appelé par TerrainGenerator après SetTiles.
    /// Retourne le GameObject créé/existant.
    /// </summary>
    public GameObject SpawnBlocker(Vector3Int cell)
    {
        if (tilemap == null) tilemap = GetComponent<Tilemap>();
        if (blockers.TryGetValue(cell, out var existing) && existing != null) return existing;

        EnsureBlockersParent();

        Vector3 pos = tilemap.GetCellCenterWorld(cell);
        pos.z = blockerZ;

        GameObject go;
        if (blockerPrefab != null)
        {
            go = Instantiate(blockerPrefab, pos, Quaternion.identity, blockersParent);
        }
        else
        {
            go = new GameObject($"Blocker_{cell.x}_{cell.y}");
            go.transform.SetParent(blockersParent, worldPositionStays: false);
            go.transform.position = pos;

            var col = go.AddComponent<BoxCollider>();
            col.size = Vector3.one;

            var obs = go.AddComponent<NavMeshObstacle>();
            obs.shape = NavMeshObstacleShape.Box;
            obs.size = Vector3.one;
            obs.carving = true;
            obs.carveOnlyStationary = true;
        }

        blockers[cell] = go;
        return go;
    }

    /// <summary>Détruit le Blocker 3D de la cellule, s'il existe. O(1).</summary>
    public bool DestroyBlocker(Vector3Int cell)
    {
        if (!blockers.TryGetValue(cell, out var go)) return false;
        if (go != null)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
        blockers.Remove(cell);
        return true;
    }

    /// <summary>Détruit tous les Blockers 3D. Appelé avant une régénération complète du terrain.</summary>
    public void ClearBlockers()
    {
        foreach (var kv in blockers)
        {
            if (kv.Value == null) continue;
            if (Application.isPlaying) Destroy(kv.Value);
            else DestroyImmediate(kv.Value);
        }
        blockers.Clear();

        if (blockersParent != null)
        {
            for (int i = blockersParent.childCount - 1; i >= 0; i--)
            {
                var child = blockersParent.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }
    }

    private void EnsureBlockersParent()
    {
        if (blockersParent != null) return;
        var existing = transform.Find("Blockers");
        if (existing != null) { blockersParent = existing; return; }
        var go = new GameObject("Blockers");
        go.transform.SetParent(transform, worldPositionStays: false);
        blockersParent = go.transform;
    }

    private void TrySpawnOrb(Vector3Int cell, DestructibleTile tile)
    {
        if (lightOrbPrefab == null) return;
        if (tile.material != DestructibleTile.MaterialType.Dirt) return;
        if (Random.value > orbSpawnChance) return;

        Vector3 pos = tilemap.GetCellCenterWorld(cell);
        Instantiate(lightOrbPrefab, pos, Quaternion.identity);
    }
}
