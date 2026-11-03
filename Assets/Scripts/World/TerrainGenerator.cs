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

    [Tooltip("Tile incassable (optionnelle). Placée sur la rangée du bas.")]
    [SerializeField] private TileBase bedrockTile;

    [Header("Zone")]
    [Tooltip("Coin inférieur gauche (cellules) de la zone à remplir, inclus.")]
    [SerializeField] private Vector2Int bottomLeft = new Vector2Int(-20, -62);

    [Tooltip("Coin supérieur droit (cellules) de la zone à remplir, inclus.")]
    [SerializeField] private Vector2Int topRight = new Vector2Int(20, -3);

    [Header("Noise")]
    [Tooltip("Seuil du bruit de Perlin au-dessus duquel on place stoneTile. 1 = jamais, 0 = partout.")]
    [Range(0f, 1f)]
    [SerializeField] private float stoneThreshold = 0.6f;

    [Tooltip("Échelle du bruit de Perlin. Plus petit = patches plus larges.")]
    [SerializeField] private float noiseScale = 0.15f;

    [Tooltip("Décalage du bruit (change la seed).")]
    [SerializeField] private int noiseSeed = 0;

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
    /// Remplit la zone rectangulaire [bottomLeft, topRight] avec dirtTile en un seul SetTiles batch.
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
    /// Choix de tile par cellule :
    /// - bords (rangée du bas + colonnes gauche/droite) → bedrockTile (si fournie)
    /// - bruit de Perlin au-dessus de stoneThreshold → stoneTile (si fournie)
    /// - sinon → dirtTile
    /// </summary>
    private TileBase PickTile(int x, int y)
    {
        if (bedrockTile != null && (y == bottomLeft.y || x == bottomLeft.x || x == topRight.x))
            return bedrockTile;

        if (stoneTile != null)
        {
            float n = Mathf.PerlinNoise((x + noiseSeed) * noiseScale, (y + noiseSeed) * noiseScale);
            if (n > stoneThreshold) return stoneTile;
        }

        return dirtTile;
    }

    /// <summary>Vide toute la Tilemap (utile pour les tests).</summary>
    public void Clear()
    {
        tilemap.ClearAllTiles();
    }
}
