using System.Collections.Generic;
using Unity.AI.Navigation;
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

    [Tooltip("Tile 'bloc de lumière'. DestructibleTile dont MaterialType = LightCrystal. Placée aléatoirement à la place de dirtTile et dropera un LightOrb à destruction.")]
    [SerializeField] private TileBase lightCrystalTile;

    [Range(0f, 1f)]
    [Tooltip("Probabilité qu'un bloc de terre (dirt) soit remplacé par un bloc de lumière lors de la génération. Recommandé : 0.05 à 0.15.")]
    [SerializeField] private float crystalSpawnChance = 0.14f;

    [Tooltip("Seed indépendante du tirage de remplacement par les blocs de lumière, pour une génération reproductible.")]
    [SerializeField] private int crystalSeed = 7777;

    [Header("Zone")]
    [Tooltip("Coin inférieur gauche (cellules) de la zone à remplir, inclus.")]
    [SerializeField] private Vector2Int bottomLeft = new Vector2Int(-40, -92);

    [Tooltip("Coin supérieur droit (cellules) de la zone à remplir, inclus.")]
    [SerializeField] private Vector2Int topRight = new Vector2Int(40, -3);

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
    [Tooltip("Largeur (en cellules) de la zone vide garantie au haut-centre pour le spawn du joueur. Joueur ≈ 1 cellule de large → 2 suffit pour ne pas être coincé.")]
    [SerializeField] private int safeSpawnWidth = 2;

    [Tooltip("Hauteur (en cellules) de la zone vide garantie au haut-centre pour le spawn du joueur. Joueur ≈ 1.5 cellule de haut → 3 donne une case de headroom.")]
    [SerializeField] private int safeSpawnHeight = 3;

    [Tooltip("Nombre de cellules de Bedrock ajoutées AU-DESSUS de topRight.y sur les colonnes gauche/droite, pour empêcher le joueur de retomber à côté du spawn depuis la surface. 0 = pas de mur.")]
    [SerializeField] private int bedrockWallExtension = 20;

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
    [SerializeField] private int arenaPlacementAttempts = 100;

    [Header("Arena Platforms (Tier 2 & 3)")]
    [Tooltip("Nombre de cellules à partir du sol de la salle à laisser vides avant la première rangée de plateformes.")]
    [SerializeField] private int platformFloorClearance = 2;

    [Tooltip("Écart vertical (cellules) entre deux rangées de plateformes. Doit laisser ≥ 3 cases libres pour laisser passer le joueur (2 cases de haut + headroom).")]
    [SerializeField] private int platformRowSpacing = 4;

    [Tooltip("Fréquence du bruit de Perlin le long de X pour les plateformes. ~0.3–0.5 donne des grappes de 3–5 cellules.")]
    [SerializeField] private float platformNoiseScale = 0.35f;

    [Tooltip("Seuil au-dessus duquel une cellule de rangée-plateforme reçoit un bloc.")]
    [Range(0f, 1f)]
    [SerializeField] private float platformNoiseThreshold = 0.55f;

    [Tooltip("Seed indépendante du bruit des plateformes.")]
    [SerializeField] private int platformSeed = 42;

    [Header("Arena Mob Prefabs")]
    [SerializeField] private MobBase crawlerPrefab;
    [SerializeField] private SpitterController spitterPrefab;
    [SerializeField] private TrapperController trapperPrefab;

    [Tooltip("Plan Y monde 3D du NavMesh — propagé à chaque ArenaRoom spawnée.")]
    [SerializeField] private float navMeshY = 100f;

    [Tooltip("Parent facultatif des GameObjects d'arènes. Auto-créé (enfant 'Arenas') si vide.")]
    [SerializeField] private Transform arenasParent;

    [Header("NavMesh")]
    [Tooltip("Transform du plan 3D (NavMeshPlane3D) qui sera repositionné + redimensionné pour épouser la grille à chaque Generate.")]
    public Transform navMeshPlaneTransform;

    [Tooltip("NavMeshSurface associée — re-bake automatiquement en fin de redimensionnement et AVANT que les Blockers ne viennent carver.")]
    public NavMeshSurface navMeshSurface;

    private Tilemap tilemap;

    // Rect en coordonnées cellules (xMin/yMin inclus, xMax/yMax exclusif via RectInt).
    private readonly List<(RectInt rect, ArenaRoom.RoomTier tier)> arenaRects =
        new List<(RectInt, ArenaRoom.RoomTier)>();

    /// <summary>Coin inférieur gauche (cellules, inclus) de la zone générée.</summary>
    public Vector2Int BottomLeftCell => bottomLeft;

    /// <summary>Coin supérieur droit (cellules, inclus) de la zone générée.</summary>
    public Vector2Int TopRightCell => topRight;

    /// <summary>
    /// Bounds monde de la zone de terrain générée. Utilise la Tilemap pour convertir
    /// les cellules en coordonnées monde (bords xMax/yMax passés à <c>+1</c> car
    /// <see cref="topRight"/> est inclusif). Ne dépend pas des tuiles posées, seulement
    /// de la taille de cellule du Grid.
    /// </summary>
    public Bounds GetWorldBounds()
    {
        if (tilemap == null) tilemap = GetComponent<Tilemap>();
        Vector3 min = tilemap.CellToWorld(new Vector3Int(bottomLeft.x, bottomLeft.y, 0));
        Vector3 max = tilemap.CellToWorld(new Vector3Int(topRight.x + 1, topRight.y + 1, 0));
        Vector3 size = max - min;
        Vector3 center = (min + max) * 0.5f;
        return new Bounds(center, size);
    }

    private void Awake()
    {
        tilemap = GetComponent<Tilemap>();
        // Fit + bake NavMesh dès Awake — AVANT que n'importe quel mob (pré-placé ou instancié par une arène)
        // ne tente d'attacher son NavMeshAgent. Supprime la race condition "Resume can only be called on
        // an active agent that has been placed on a NavMesh".
        FitAndBakeNavMesh();
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

        int minX = bottomLeft.x, maxX = topRight.x;
        int minY = bottomLeft.y, maxY = topRight.y;
        int w = maxX - minX + 1;
        int h = maxY - minY + 1;
        if (w <= 0 || h <= 0)
        {
            Debug.LogWarning("[TerrainGenerator] Zone invalide (bottomLeft doit être inférieur à topRight).", this);
            return;
        }

        // NavMesh : Awake a déjà fait le fit + bake. On n'y retouche pas ici (évite une double-bake).
        // Si les bornes ont changé au runtime, rappeler FitAndBakeNavMesh() manuellement avant Generate().

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

        // Prolonge les colonnes Bedrock gauche/droite au-dessus de topRight.y pour empêcher
        // le joueur de retomber à côté de la safe spawn depuis la surface (canyon vertical).
        ExtendBedrockWalls();

        SyncBlockers(positions, tiles);

        // 2. Instancie les ArenaRoom au centre de chaque rect.
        SpawnArenaRooms();

        // 3. Signale à l'objectif de niveau que la génération initiale est OK.
        if (LevelObjectiveManager.Instance != null)
            LevelObjectiveManager.Instance.MarkInitialGenerationComplete();
    }

    /// <summary>
    /// Pose du Bedrock sur les colonnes gauche/droite AU-DESSUS de topRight.y, sur
    /// <see cref="bedrockWallExtension"/> cellules. Objectif : empêcher le joueur
    /// de retomber à côté de la safe spawn depuis la surface — les deux colonnes
    /// forment un canyon vertical qui canalise la chute vers la zone de spawn.
    /// Pas de SpawnBlocker ici : le NavMesh ne couvre pas cette bande, et le
    /// TilemapCollider2D suffit pour arrêter le Rigidbody2D du joueur.
    /// </summary>
    private void ExtendBedrockWalls()
    {
        if (bedrockTile == null || bedrockWallExtension <= 0) return;

        int count = bedrockWallExtension * 2;
        var wallPos = new Vector3Int[count];
        var wallTiles = new TileBase[count];
        int k = 0;
        for (int y = topRight.y + 1; y <= topRight.y + bedrockWallExtension; y++)
        {
            wallPos[k] = new Vector3Int(bottomLeft.x, y, 0);
            wallTiles[k] = bedrockTile;
            k++;
            wallPos[k] = new Vector3Int(topRight.x, y, 0);
            wallTiles[k] = bedrockTile;
            k++;
        }
        tilemap.SetTiles(wallPos, wallTiles);
    }

    /// <summary>
    /// Centre monde de la safe spawn — utile pour positionner le joueur par script
    /// plutôt que de dépendre du placement manuel dans la scène.
    /// </summary>
    public Vector3 GetSafeSpawnWorldCenter()
    {
        if (tilemap == null) tilemap = GetComponent<Tilemap>();
        int centerX = (bottomLeft.x + topRight.x) / 2;
        int halfW = safeSpawnWidth / 2;
        int sMinX = centerX - halfW;
        int sMaxX = centerX + (safeSpawnWidth - halfW - 1);
        int sMinY = topRight.y - safeSpawnHeight + 1;
        int sMaxY = topRight.y;
        Vector3 min = tilemap.CellToWorld(new Vector3Int(sMinX, sMinY, 0));
        Vector3 max = tilemap.CellToWorld(new Vector3Int(sMaxX + 1, sMaxY + 1, 0));
        return (min + max) * 0.5f;
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
        destructible.ClearCrystalLights();

        for (int k = 0; k < positions.Length; k++)
        {
            if (tiles[k] == null) continue;

            destructible.SpawnBlocker(positions[k]);

            // Allumer une Light2D si la cellule reçoit un bloc de lumière.
            var dt = tiles[k] as DestructibleTile;
            if (dt != null && dt.material == DestructibleTile.MaterialType.LightCrystal)
                destructible.SpawnCrystalLight(positions[k]);
        }
    }

    /// <summary>
    /// Choix de tile par cellule (ordre de priorité strict) :
    /// 1. Bordure (bas + colonnes gauche/droite) → bedrockTile, intacte, jamais vide.
    /// 2. Zone de spawn haut-centre → null (vide).
    /// 3. Cellule dans une arène :
    ///    - Tier 1 : null (salle totalement évidée).
    ///    - Tier 2 / Tier 3 : dirtTile si la cellule tombe sur une plateforme Perlin, sinon null.
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

        // 3. Arènes : vide pour Tier 1, plateformes Perlin pour Tier 2/3.
        if (TryGetArenaCell(x, y, out var rect, out var tier))
        {
            if (tier == ArenaRoom.RoomTier.Tier1) return null;
            if (IsArenaPlatformCell(x, y, rect)) return dirtTile;
            return null;
        }

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

        // 6. Dirt par défaut. Tirage indépendant : une fraction crystalSpawnChance des dirts
        //    devient un bloc de lumière (LightCrystal) — remplacement 1:1, préserve la densité
        //    de collisions / navmesh ; la Light2D est instanciée par DestructibleTerrain.SpawnCrystalLight.
        if (lightCrystalTile != null && crystalSpawnChance > 0f)
        {
            // Hash déterministe par cellule via Perlin pour que la répartition ne bouge pas entre runs
            // (même seed = mêmes crystals). Évite aussi les grappes trop fortes sur un simple Random.value.
            float r = Mathf.PerlinNoise((x + crystalSeed) * 0.9f, (y + crystalSeed) * 0.9f);
            if (r > 1f - crystalSpawnChance) return lightCrystalTile;
        }
        return dirtTile;
    }

    private bool TryGetArenaCell(int x, int y, out RectInt rect, out ArenaRoom.RoomTier tier)
    {
        for (int i = 0; i < arenaRects.Count; i++)
        {
            var r = arenaRects[i].rect;
            if (x >= r.xMin && x < r.xMax && y >= r.yMin && y < r.yMax)
            {
                rect = r;
                tier = arenaRects[i].tier;
                return true;
            }
        }
        rect = default; tier = default; return false;
    }

    /// <summary>
    /// Vrai si (x,y) tombe sur une rangée-plateforme ET passe le test de bruit Perlin.
    /// Laisse 1 cellule de contour vide sur chaque bord + platformFloorClearance au-dessus du sol
    /// pour ne pas gêner les triggers / spawn / mobilité des mobs.
    /// </summary>
    private bool IsArenaPlatformCell(int x, int y, RectInt rect)
    {
        // 1 cellule de contour vide sur chaque bord (pas de plateforme collée au mur).
        if (x <= rect.xMin || x >= rect.xMax - 1) return false;
        if (y <= rect.yMin || y >= rect.yMax - 1) return false;

        // Sol dégagé : les N premières cellules au-dessus du sol sont vides.
        int localY = y - rect.yMin;
        if (localY < platformFloorClearance) return false;

        // Rangée-plateforme : une rangée tous les `platformRowSpacing` cellules à partir du sol+clearance.
        int rowMod = localY - platformFloorClearance;
        if (platformRowSpacing <= 0) return false;
        if (rowMod % platformRowSpacing != 0) return false;

        // Grappes horizontales via Perlin 1D sur X, indexées par rangée (pour varier entre les niveaux).
        int rowIndex = rowMod / platformRowSpacing;
        float n = Mathf.PerlinNoise(
            (x + platformSeed) * platformNoiseScale,
            (rowIndex + platformSeed) * 0.9f);
        return n > platformNoiseThreshold;
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
    // NavMesh
    // =========================================================================

    /// <summary>
    /// Repositionne et rescale le plan 3D pour qu'il épouse exactement la zone de génération.
    /// Détecte automatiquement la taille unitaire du mesh (Cube=1, Plane=10) pour calculer la scale,
    /// préserve la localScale.y pour garder l'épaisseur voulue du slab.
    /// </summary>
    private void FitNavMeshPlane(int minX, int maxX, int minY, int maxY)
    {
        if (navMeshPlaneTransform == null) return;
        float width = maxX - minX;
        float depth = maxY - minY;
        navMeshPlaneTransform.position = new Vector3((minX + maxX) / 2f, navMeshY, (minY + maxY) / 2f);

        // Taille unitaire du mesh : Cube Unity = 1, Plane Unity = 10. On divise par cette taille
        // pour que la scale reflète directement les dimensions monde attendues.
        var mf = navMeshPlaneTransform.GetComponent<MeshFilter>();
        float meshSizeX = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.bounds.size.x : 10f;
        float meshSizeZ = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.bounds.size.z : 10f;
        if (meshSizeX < 0.001f) meshSizeX = 10f;
        if (meshSizeZ < 0.001f) meshSizeZ = 10f;

        float yScale = navMeshPlaneTransform.localScale.y;
        if (yScale < 0.001f) yScale = 0.1f;

        navMeshPlaneTransform.localScale = new Vector3(width / meshSizeX, yScale, depth / meshSizeZ);
    }

    /// <summary>
    /// Fit + bake complet, synchrone. Appelé en Awake pour éviter la race avec les mobs.
    /// Également appelable manuellement si tu changes bottomLeft/topRight au runtime.
    /// Loggue le nombre de triangles post-bake pour vérifier côté console que le NavMesh n'est pas vide.
    /// </summary>
    [ContextMenu("Fit and Bake NavMesh")]
    public void FitAndBakeNavMesh()
    {
        FitNavMeshPlane(bottomLeft.x, topRight.x, bottomLeft.y, topRight.y);
        if (navMeshSurface == null) return;

        navMeshSurface.BuildNavMesh();
        var tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
        int triCount = tri.indices.Length / 3;
        if (triCount == 0)
            Debug.LogError($"[TerrainGenerator] NavMesh bake VIDE (0 triangles) — vérifie CollectObjects + Navigation Static du plane.", this);
        else
            Debug.Log($"[TerrainGenerator] NavMesh baké : {tri.vertices.Length} vertices, {triCount} triangles.", this);
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
