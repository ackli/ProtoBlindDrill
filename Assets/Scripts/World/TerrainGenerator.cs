using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(Tilemap))]
public class TerrainGenerator : MonoBehaviour
{
    [Header("Tiles")]
    [Tooltip("Tile par défaut (la plus commune, HP = 1 en principe).")]
    [SerializeField] private TileBase dirtTile;

    [Tooltip("Tile plus résistante (optionnelle). Placée en patches via bruit de Perlin.")]
    [SerializeField] private TileBase stoneTile;

    [Tooltip("Tile incassable (optionnelle). Placée sur la rangée du bas et les colonnes gauche/droite.")]
    [SerializeField] private TileBase bedrockTile;

    [Header("Zone")]
    [Tooltip("Coin inférieur gauche (cellules) de la zone à remplir, inclus.")]
    [SerializeField] private Vector2Int bottomLeft = new Vector2Int(-20, -62);

    [Tooltip("Coin supérieur droit (cellules) de la zone à remplir, inclus.")]
    [SerializeField] private Vector2Int topRight = new Vector2Int(20, -3);

    [Header("Stone Noise")]
    [Tooltip("Seuil du bruit de Perlin au-dessus duquel on place stoneTile. 1 = jamais, 0 = partout.")]
    [Range(0f, 1f)]
    [SerializeField] private float stoneThreshold = 0.6f;

    [Tooltip("Échelle du bruit de Perlin (pierre). Plus petit = patches plus larges.")]
    [SerializeField] private float noiseScale = 0.15f;

    [Tooltip("Décalage du bruit (change la seed) pour la pierre.")]
    [SerializeField] private int noiseSeed = 0;

    [Header("Cavity Noise (poches d'air)")]
    [Tooltip("Seuil du second bruit de Perlin au-dessus duquel la cellule devient vide. 1 = jamais, 0 = partout.")]
    [Range(0f, 1f)]
    [SerializeField] private float cavityThreshold = 0.58f;

    [Tooltip("Échelle du bruit des cavités. Plus petit = poches plus larges et plus connectées.")]
    [SerializeField] private float cavityScale = 0.12f;

    [Tooltip("Seed indépendante du bruit des cavités.")]
    [SerializeField] private int cavitySeed = 1337;

    [Header("Safe Spawn")]
    [Tooltip("Largeur (en cellules) de la zone vide garantie au haut-centre pour le spawn du joueur.")]
    [SerializeField] private int safeSpawnWidth = 7;

    [Tooltip("Hauteur (en cellules) de la zone vide garantie au haut-centre pour le spawn du joueur.")]
    [SerializeField] private int safeSpawnHeight = 5;

    [Header("Behavior")]
    [Tooltip("Générer automatiquement au Start. Décocher si tu peins la Tilemap à la main.")]
    [SerializeField] private bool generateOnStart = true;

    [Header("Arenas")]
    [Tooltip("Nombre de salles Tier 1 à essayer de placer.")]
    [SerializeField] private int tier1Count = 2;
    [Tooltip("Nombre de salles Tier 2 à essayer de placer.")]
    [SerializeField] private int tier2Count = 1;
    [Tooltip("Nombre de salles Tier 3 à essayer de placer.")]
    [SerializeField] private int tier3Count = 1;

    [SerializeField] private Vector2Int tier1Size = new Vector2Int(8, 8);
    [SerializeField] private Vector2Int tier2Size = new Vector2Int(14, 14);
    [SerializeField] private Vector2Int tier3Size = new Vector2Int(20, 20);

    [Tooltip("Tentatives de placement par salle avant d'abandonner (évite overlap + spawn safe).")]
    [SerializeField] private int arenaPlacementAttempts = 30;

    [Header("Arena Mob Prefabs")]
    [SerializeField] private MobBase crawlerPrefab;
    [SerializeField] private SpitterController spitterPrefab;
    [SerializeField] private TrapperController trapperPrefab;

    [Tooltip("Plan Y monde 3D du NavMesh — propagé à chaque ArenaRoom spawnée.")]
    [SerializeField] private float navMeshY = 100f;

    [Tooltip("Parent facultatif des GameObjects d'arènes. Auto-créé (enfant 'Arenas') si vide.")]
    [SerializeField] private Transform arenasParent;

    private Tilemap tilemap;

    // Rect en coordonnées cellules (xMin/yMin inclus, xMax/yMax exclusif via RectInt).
    private readonly List<(RectInt rect, ArenaRoom.RoomTier tier)> arenaRects =
        new List<(RectInt, ArenaRoom.RoomTier)>();

    private void Awake()
    {
        tilemap = GetComponent<Tilemap>();
    }

    private void Start()
    {
        if (generateOnStart) Generate();
    }

    /// <summary>
    /// Remplit la zone rectangulaire [bottomLeft, topRight] en un seul SetTiles batch.
    /// Sculpte des poches d'air via un second bruit de Perlin, tout en préservant la bordure Bedrock et la zone de spawn.
    /// </summary>
    public void Generate()
    {
        if (dirtTile == null)
        {
            Debug.LogWarning("[TerrainGenerator] dirtTile non assignée — rien à générer.", this);
            return;
        }

        // On repart d'une Tilemap propre pour éviter les restes d'une génération précédente.
        tilemap.ClearAllTiles();
        ClearArenaInstances();

        int w = topRight.x - bottomLeft.x + 1;
        int h = topRight.y - bottomLeft.y + 1;
        if (w <= 0 || h <= 0)
        {
            Debug.LogWarning("[TerrainGenerator] Zone invalide (bottomLeft doit être inférieur à topRight).", this);
            return;
        }

        // 1. Place les salles AVANT PickTile : les rects viendront court-circuiter PickTile pour les évider.
        PlaceArenas();

        var positions = new Vector3Int[w * h];
        var tiles = new TileBase[w * h];

        int i = 0;
        for (int y = bottomLeft.y; y <= topRight.y; y++)
        {
            for (int x = bottomLeft.x; x <= topRight.x; x++)
            {
                positions[i] = new Vector3Int(x, y, 0);
                tiles[i] = PickTile(x, y);
                i++;
            }
        }

        tilemap.SetTiles(positions, tiles);

        SyncBlockers(positions, tiles);

        // 2. Instancie les ArenaRoom au centre de chaque rect.
        SpawnArenaRooms();

        // 3. Signale à l'objectif de niveau que la génération initiale est OK.
        if (LevelObjectiveManager.Instance != null)
            LevelObjectiveManager.Instance.MarkInitialGenerationComplete();
    }

    /// <summary>
    /// Synchronise les Blockers 3D (NavMeshObstacle Carve) avec les tuiles pleines.
    /// Sans DestructibleTerrain sur le même GO, c'est un no-op.
    /// </summary>
    private void SyncBlockers(Vector3Int[] positions, TileBase[] tiles)
    {
        var destructible = GetComponent<DestructibleTerrain>();
        if (destructible == null) return;

        destructible.ClearBlockers();
        for (int k = 0; k < positions.Length; k++)
        {
            if (tiles[k] != null)
                destructible.SpawnBlocker(positions[k]);
        }
    }

    /// <summary>
    /// Choix de tile par cellule (ordre de priorité strict) :
    /// 1. Bordure (bas + colonnes gauche/droite) → bedrockTile, intacte, jamais vide.
    /// 2. Zone de spawn haut-centre → null (vide).
    /// 3. Cellule à l'intérieur d'une arène → null (salle évidée).
    /// 4. Bruit de Perlin des cavités au-dessus de cavityThreshold → null (poche d'air).
    /// 5. Bruit de Perlin de la pierre au-dessus de stoneThreshold → stoneTile.
    /// 6. Sinon → dirtTile.
    /// </summary>
    private TileBase PickTile(int x, int y)
    {
        // 1. Bordure Bedrock : prioritaire absolue, aucune cavité ne la touche.
        if (bedrockTile != null && (y == bottomLeft.y || x == bottomLeft.x || x == topRight.x))
            return bedrockTile;

        // 2. Zone de spawn au haut-centre : toujours vide.
        if (IsInSafeSpawn(x, y))
            return null;

        // 3. Arènes carved out.
        if (IsInArena(x, y))
            return null;

        // 4. Cavités via second bruit de Perlin.
        float cavity = Mathf.PerlinNoise((x + cavitySeed) * cavityScale, (y + cavitySeed) * cavityScale);
        if (cavity > cavityThreshold)
            return null;

        // 5. Pierre (comportement d'origine, inchangé).
        if (stoneTile != null)
        {
            float n = Mathf.PerlinNoise((x + noiseSeed) * noiseScale, (y + noiseSeed) * noiseScale);
            if (n > stoneThreshold) return stoneTile;
        }

        // 6. Dirt par défaut (le LightOrb-spawn-chance côté DestructibleTerrain reste inchangé).
        return dirtTile;
    }

    private bool IsInArena(int x, int y)
    {
        for (int i = 0; i < arenaRects.Count; i++)
        {
            var r = arenaRects[i].rect;
            if (x >= r.xMin && x < r.xMax && y >= r.yMin && y < r.yMax) return true;
        }
        return false;
    }

    /// <summary>
    /// Vrai si (x, y) est dans la bande vide garantie en haut-centre pour le spawn du joueur.
    /// </summary>
    private bool IsInSafeSpawn(int x, int y)
    {
        if (safeSpawnWidth <= 0 || safeSpawnHeight <= 0) return false;

        int centerX = (bottomLeft.x + topRight.x) / 2;
        int halfW = safeSpawnWidth / 2;
        int minX = centerX - halfW;
        int maxX = centerX + (safeSpawnWidth - halfW - 1);
        int minY = topRight.y - safeSpawnHeight + 1;
        int maxY = topRight.y;

        return x >= minX && x <= maxX && y >= minY && y <= maxY;
    }

    /// <summary>Vide toute la Tilemap (utile pour les tests).</summary>
    public void Clear()
    {
        tilemap.ClearAllTiles();
        ClearArenaInstances();
        arenaRects.Clear();
    }

    // =========================================================================
    // Arènes
    // =========================================================================

    private void PlaceArenas()
    {
        arenaRects.Clear();
        // Du plus gros au plus petit : les grandes salles sont plus dures à caser, on les sert d'abord.
        PlaceTier(ArenaRoom.RoomTier.Tier3, tier3Count, tier3Size);
        PlaceTier(ArenaRoom.RoomTier.Tier2, tier2Count, tier2Size);
        PlaceTier(ArenaRoom.RoomTier.Tier1, tier1Count, tier1Size);
    }

    private void PlaceTier(ArenaRoom.RoomTier tier, int count, Vector2Int size)
    {
        if (count <= 0 || size.x <= 0 || size.y <= 0) return;

        // On évite la bordure Bedrock (1 cellule) et on veut que le rect tienne entièrement dans la zone.
        int minX = bottomLeft.x + 1;
        int minY = bottomLeft.y + 1;
        int maxX = topRight.x - size.x;    // xMax exclu = minX + size → xMax ≤ topRight.x
        int maxY = topRight.y - size.y;

        if (maxX < minX || maxY < minY)
        {
            Debug.LogWarning($"[TerrainGenerator] Salle {tier} ({size.x}x{size.y}) trop grande pour la zone.", this);
            return;
        }

        int placed = 0;
        for (int i = 0; i < count; i++)
        {
            bool ok = false;
            for (int attempt = 0; attempt < arenaPlacementAttempts; attempt++)
            {
                int rx = Random.Range(minX, maxX + 1);
                int ry = Random.Range(minY, maxY + 1);
                var rect = new RectInt(rx, ry, size.x, size.y);
                if (OverlapsAnyArena(rect)) continue;
                if (OverlapsSafeSpawn(rect)) continue;
                arenaRects.Add((rect, tier));
                placed++;
                ok = true;
                break;
            }
            if (!ok)
                Debug.LogWarning($"[TerrainGenerator] Impossible de placer une salle {tier} après {arenaPlacementAttempts} essais.", this);
        }
    }

    private bool OverlapsAnyArena(RectInt r)
    {
        for (int i = 0; i < arenaRects.Count; i++)
            if (arenaRects[i].rect.Overlaps(r)) return true;
        return false;
    }

    private bool OverlapsSafeSpawn(RectInt r)
    {
        if (safeSpawnWidth <= 0 || safeSpawnHeight <= 0) return false;
        int centerX = (bottomLeft.x + topRight.x) / 2;
        int halfW = safeSpawnWidth / 2;
        int sMinX = centerX - halfW;
        int sMinY = topRight.y - safeSpawnHeight + 1;
        var safe = new RectInt(sMinX, sMinY, safeSpawnWidth, safeSpawnHeight);
        return r.Overlaps(safe);
    }

    private void EnsureArenasParent()
    {
        if (arenasParent != null) return;
        var existing = transform.Find("Arenas");
        if (existing != null) { arenasParent = existing; return; }
        var go = new GameObject("Arenas");
        go.transform.SetParent(transform, worldPositionStays: false);
        arenasParent = go.transform;
    }

    private void ClearArenaInstances()
    {
        if (arenasParent == null)
        {
            var existing = transform.Find("Arenas");
            if (existing != null) arenasParent = existing;
            else return;
        }
        for (int i = arenasParent.childCount - 1; i >= 0; i--)
        {
            var child = arenasParent.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
    }

    private void SpawnArenaRooms()
    {
        if (arenaRects.Count == 0) return;
        EnsureArenasParent();

        foreach (var entry in arenaRects)
        {
            var rect = entry.rect;
            // RectInt.xMax/yMax sont exclusifs → CellToWorld renvoie la position du coin opposé.
            Vector3 wMin = tilemap.CellToWorld(new Vector3Int(rect.xMin, rect.yMin, 0));
            Vector3 wMax = tilemap.CellToWorld(new Vector3Int(rect.xMax, rect.yMax, 0));
            Vector3 center = (wMin + wMax) * 0.5f;
            center.z = 0f;
            Vector2 worldSize = new Vector2(wMax.x - wMin.x, wMax.y - wMin.y);

            var go = new GameObject($"Arena_{entry.tier}");
            go.transform.SetParent(arenasParent, worldPositionStays: false);
            go.transform.position = center;

            var arena = go.AddComponent<ArenaRoom>();
            arena.Configure(entry.tier, crawlerPrefab, spitterPrefab, trapperPrefab, worldSize, navMeshY);
        }
    }
}
