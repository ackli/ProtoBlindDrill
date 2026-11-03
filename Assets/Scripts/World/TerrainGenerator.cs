using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(Tilemap))]
public class TerrainGenerator : MonoBehaviour
{
    [Tooltip("Tile placée dans la zone définie par bottomLeft/topRight.")]
    [SerializeField] private TileBase dirtTile;

    [Tooltip("Coin inférieur gauche (cellules) de la zone à remplir, inclus.")]
    [SerializeField] private Vector2Int bottomLeft = new Vector2Int(-20, -20);

    [Tooltip("Coin supérieur droit (cellules) de la zone à remplir, inclus.")]
    [SerializeField] private Vector2Int topRight = new Vector2Int(20, -3);

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
                tiles[i] = dirtTile;
                i++;
            }
        }

        tilemap.SetTiles(positions, tiles);
    }

    /// <summary>Vide toute la Tilemap (utile pour les tests).</summary>
    public void Clear()
    {
        tilemap.ClearAllTiles();
    }
}
