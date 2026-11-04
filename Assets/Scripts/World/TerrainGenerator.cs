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

    private Tilemap tilemap;

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

        int w = topRight.x - bottomLeft.x + 1;
        int h = topRight.y - bottomLeft.y + 1;
        if (w <= 0 || h <= 0)
        {
            Debug.LogWarning("[TerrainGenerator] Zone invalide (bottomLeft doit être inférieur à topRight).", this);
            return;
        }

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
    }

    /// <summary>
    /// Choix de tile par cellule (ordre de priorité strict) :
    /// 1. Bordure (bas + colonnes gauche/droite) → bedrockTile, intacte, jamais vide.
    /// 2. Zone de spawn haut-centre → null (vide), pour que le joueur ne spawn pas dans un mur.
    /// 3. Bruit de Perlin des cavités au-dessus de cavityThreshold → null (poche d'air).
    /// 4. Bruit de Perlin de la pierre au-dessus de stoneThreshold → stoneTile.
    /// 5. Sinon → dirtTile.
    /// </summary>
    private TileBase PickTile(int x, int y)
    {
        // 1. Bordure Bedrock : prioritaire absolue, aucune cavité ne la touche.
        if (bedrockTile != null && (y == bottomLeft.y || x == bottomLeft.x || x == topRight.x))
            return bedrockTile;

        // 2. Zone de spawn au haut-centre : toujours vide.
        if (IsInSafeSpawn(x, y))
            return null;

        // 3. Cavités via second bruit de Perlin.
        float cavity = Mathf.PerlinNoise((x + cavitySeed) * cavityScale, (y + cavitySeed) * cavityScale);
        if (cavity > cavityThreshold)
            return null;

        // 4. Pierre (comportement d'origine, inchangé).
        if (stoneTile != null)
        {
            float n = Mathf.PerlinNoise((x + noiseSeed) * noiseScale, (y + noiseSeed) * noiseScale);
            if (n > stoneThreshold) return stoneTile;
        }

        // 5. Dirt par défaut (le LightOrb-spawn-chance côté DestructibleTerrain reste inchangé).
        return dirtTile;
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
    }
}
