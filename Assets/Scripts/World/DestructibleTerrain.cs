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

    [Header("NavMesh Blockers (3D)")]
    [Tooltip("Prefab de Blocker 3D optionnel. Laisse vide pour générer dynamiquement un GameObject avec BoxCollider + NavMeshObstacle (Carve).")]
    [SerializeField] private GameObject blockerPrefab;

    [Tooltip("Plan Y monde 3D où est posé le NavMesh horizontal (projection XZ). Le Y 2D devient Z 3D.")]
    [SerializeField] private float navMeshY = 100f;

    [Tooltip("Parent des Blockers 3D. Auto-créé (enfant 'Blockers') si vide.")]
    [SerializeField] private Transform blockersParent;

    [Header("Light Crystal")]
    [Tooltip("Prefab instancié sur chaque cellule de bloc LightCrystal lors de la génération. Doit porter un Light2D (Point) pour que le bloc soit visible dans le noir.")]
    [SerializeField] private GameObject lightCrystalPrefab;

    [Tooltip("Parent des Light2D de cristaux. Auto-créé (enfant 'CrystalLights') si vide.")]
    [SerializeField] private Transform crystalLightsParent;

    [Header("Upgrade Relic")]
    [Tooltip("Prefab instancié sur chaque cellule de bloc UpgradeRelic. Doit porter un SpriteRenderer (sprite 'révélé') + un RelicVisibility qui le masque hors Line-of-Sight. La tile de la Tilemap fournit le camouflage 'terre'.")]
    [SerializeField] private GameObject relicPrefab;

    [Tooltip("Parent des instances de reliques. Auto-créé (enfant 'Relics') si vide.")]
    [SerializeField] private Transform relicsParent;

    [Tooltip("Nombre de LightOrbs spawnés à la destruction d'une relique. La récompense justifie l'apparition rare du bloc. Ignoré si buffOrbPrefab est assigné (c'est alors un unique BuffOrb qui spawne).")]
    [SerializeField] private int relicOrbReward = 3;

    [Tooltip("Orbe de buff (prefab avec composant BuffOrb) dropé par la relique. Si assigné, remplace le drop de LightOrbs — la relique devient porteuse d'un bonus permanent de session.")]
    [SerializeField] private BuffOrb buffOrbPrefab;

    private Tilemap tilemap;
    private readonly Dictionary<Vector3Int, int> cellHP = new Dictionary<Vector3Int, int>();
    private readonly Dictionary<Vector3Int, GameObject> blockers = new Dictionary<Vector3Int, GameObject>();
    private readonly Dictionary<Vector3Int, GameObject> crystalLights = new Dictionary<Vector3Int, GameObject>();
    private readonly Dictionary<Vector3Int, GameObject> relics = new Dictionary<Vector3Int, GameObject>();

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
            DestroyCrystalLight(cell);
            DestroyRelic(cell);
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

        Vector3 cellCenter = tilemap.GetCellCenterWorld(cell);
        // Projection dual-plane : (x_2D, y_2D, 0) → (x, navMeshY, y) en 3D.
        Vector3 pos = new Vector3(cellCenter.x, navMeshY, cellCenter.y);

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

            // Pas de BoxCollider : le NavMeshObstacle Carve suffit et évite de polluer le bake.
            var obs = go.AddComponent<NavMeshObstacle>();
            obs.shape = NavMeshObstacleShape.Box;
            obs.size = new Vector3(1f, 4f, 1f); // épaisseur Y pour transpercer le plan NavMesh horizontal
            obs.carving = true;
            obs.carveOnlyStationary = false; // runtime-instancié : force le carving sans attente de validation d'immobilité
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
        if (blockersParent == null)
        {
            var existing = transform.Find("Blockers");
            if (existing != null) blockersParent = existing;
            else
            {
                var go = new GameObject("Blockers");
                go.transform.SetParent(transform, worldPositionStays: false);
                blockersParent = go.transform;
            }
        }

        // Garantit que 'Blockers' est à world Z = 0 : localPosition.z = 5 → world Z = 5 (aligné sur NavMeshPlane3D).
        var wp = blockersParent.position;
        if (!Mathf.Approximately(wp.z, 0f))
            blockersParent.position = new Vector3(wp.x, wp.y, 0f);
    }

    private void TrySpawnOrb(Vector3Int cell, DestructibleTile tile)
    {
        if (lightOrbPrefab == null) return;

        Vector3 pos = tilemap.GetCellCenterWorld(cell);

        // Relique : si un BuffOrb est configuré, il remplace les LightOrbs (récompense = buff permanent).
        // Sinon, fallback historique : relicOrbReward LightOrbs (pour le playtest quand le buff n'est pas câblé).
        if (tile.material == DestructibleTile.MaterialType.UpgradeRelic)
        {
            if (buffOrbPrefab != null)
            {
                Instantiate(buffOrbPrefab, pos, Quaternion.identity);
                Debug.Log($"[RELIC-MINED] cell=({cell.x},{cell.y}) worldPos=({pos.x:F1},{pos.y:F1}) → 1 BuffOrb spawné.");
            }
            else
            {
                int count = Mathf.Max(1, relicOrbReward);
                for (int i = 0; i < count; i++)
                    Instantiate(lightOrbPrefab, pos, Quaternion.identity);
                Debug.Log($"[RELIC-MINED] cell=({cell.x},{cell.y}) worldPos=({pos.x:F1},{pos.y:F1}) → {count} LightOrb(s) (fallback, BuffOrb non assigné).");
            }
            return;
        }

        // La terre ordinaire ne drop plus rien ; seuls les blocs LightCrystal donnent la lumière.
        if (tile.material != DestructibleTile.MaterialType.LightCrystal) return;
        // Crystal = source de lumière garantie : pas de tirage probabiliste ici.
        Instantiate(lightOrbPrefab, pos, Quaternion.identity);
        Debug.Log($"[CRYSTAL-MINED] cell=({cell.x},{cell.y}) worldPos=({pos.x:F1},{pos.y:F1}) → 1 LightOrb spawné.");
    }

    /// <summary>Instancie le prefab de lumière sur une cellule, si pas déjà présent. Appelé par TerrainGenerator après le pose-tile d'un bloc LightCrystal.</summary>
    public GameObject SpawnCrystalLight(Vector3Int cell)
    {
        if (lightCrystalPrefab == null) return null;
        if (tilemap == null) tilemap = GetComponent<Tilemap>();
        if (crystalLights.TryGetValue(cell, out var existing) && existing != null) return existing;

        EnsureCrystalLightsParent();
        Vector3 pos = tilemap.GetCellCenterWorld(cell);
        var go = Instantiate(lightCrystalPrefab, pos, Quaternion.identity, crystalLightsParent);
        crystalLights[cell] = go;
        return go;
    }

    /// <summary>Détruit la Light2D associée à la cellule (si elle existe). O(1).</summary>
    public bool DestroyCrystalLight(Vector3Int cell)
    {
        if (!crystalLights.TryGetValue(cell, out var go)) return false;
        if (go != null)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
        crystalLights.Remove(cell);
        return true;
    }

    /// <summary>Détruit toutes les Light2D de cristaux. Appelé avant une régénération complète.</summary>
    public void ClearCrystalLights()
    {
        foreach (var kv in crystalLights)
        {
            if (kv.Value == null) continue;
            if (Application.isPlaying) Destroy(kv.Value);
            else DestroyImmediate(kv.Value);
        }
        crystalLights.Clear();

        if (crystalLightsParent != null)
        {
            for (int i = crystalLightsParent.childCount - 1; i >= 0; i--)
            {
                var child = crystalLightsParent.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }
    }

    private void EnsureCrystalLightsParent()
    {
        if (crystalLightsParent == null)
        {
            var existing = transform.Find("CrystalLights");
            if (existing != null) crystalLightsParent = existing;
            else
            {
                var go = new GameObject("CrystalLights");
                go.transform.SetParent(transform, worldPositionStays: false);
                crystalLightsParent = go.transform;
            }
        }
    }

    // =========================================================================
    // Reliques camouflées (pattern miroir des Light Crystals)
    // =========================================================================

    /// <summary>Instancie le prefab de relique sur une cellule, si pas déjà présent. Appelé par TerrainGenerator après le pose-tile d'un bloc UpgradeRelic.</summary>
    public GameObject SpawnRelic(Vector3Int cell)
    {
        if (relicPrefab == null) return null;
        if (tilemap == null) tilemap = GetComponent<Tilemap>();
        if (relics.TryGetValue(cell, out var existing) && existing != null) return existing;

        EnsureRelicsParent();
        Vector3 pos = tilemap.GetCellCenterWorld(cell);
        var go = Instantiate(relicPrefab, pos, Quaternion.identity, relicsParent);
        relics[cell] = go;
        return go;
    }

    /// <summary>Détruit l'instance de relique associée à la cellule (si elle existe). O(1).</summary>
    public bool DestroyRelic(Vector3Int cell)
    {
        if (!relics.TryGetValue(cell, out var go)) return false;
        if (go != null)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
        relics.Remove(cell);
        return true;
    }

    /// <summary>Détruit toutes les reliques. Appelé avant une régénération complète.</summary>
    public void ClearRelics()
    {
        foreach (var kv in relics)
        {
            if (kv.Value == null) continue;
            if (Application.isPlaying) Destroy(kv.Value);
            else DestroyImmediate(kv.Value);
        }
        relics.Clear();

        if (relicsParent != null)
        {
            for (int i = relicsParent.childCount - 1; i >= 0; i--)
            {
                var child = relicsParent.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }
    }

    private void EnsureRelicsParent()
    {
        if (relicsParent == null)
        {
            var existing = transform.Find("Relics");
            if (existing != null) relicsParent = existing;
            else
            {
                var go = new GameObject("Relics");
                go.transform.SetParent(transform, worldPositionStays: false);
                relicsParent = go.transform;
            }
        }
    }
}
