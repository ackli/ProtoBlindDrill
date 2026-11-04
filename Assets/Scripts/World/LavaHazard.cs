using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Zone dangereuse (lac de lave) : tue le joueur à l'entrée.
/// Au Start, peut vider automatiquement les tuiles de la Tilemap qui sont sous son
/// collider pour s'intégrer dans un terrain généré programmatiquement.
///
/// Requiert un Collider2D en mode isTrigger (BoxCollider2D de préférence).
/// </summary>
[RequireComponent(typeof(Collider2D))]
[DefaultExecutionOrder(100)] // s'exécute APRÈS TerrainGenerator pour que le clear soit visible
public class LavaHazard : MonoBehaviour
{
    [Header("Damage")]
    [Tooltip("Dégâts infligés au joueur à l'entrée dans le trigger. Mettre très haut pour tuer en 1 contact.")]
    [SerializeField] private int damageOnContact = 999;

    [Header("Terrain integration")]
    [Tooltip("Tilemap à vider sous le lac au Start (optionnel). Laisser null si le lac est déjà hors terrain.")]
    [SerializeField] private Tilemap terrainTilemap;

    [Tooltip("Au Start, vider les tuiles qui sont sous le collider du lac.")]
    [SerializeField] private bool clearTilesOnStart = true;

    private Collider2D col;

    private void Awake()
    {
        col = GetComponent<Collider2D>();
        if (!col.isTrigger)
            Debug.LogWarning("[LavaHazard] Collider2D doit être en mode isTrigger.", this);
    }

    private void Start()
    {
        if (clearTilesOnStart) ClearTilesUnderCollider();
    }

    private void ClearTilesUnderCollider()
    {
        if (terrainTilemap == null) return;

        Bounds b = col.bounds;
        Vector3Int bl = terrainTilemap.WorldToCell(b.min);
        Vector3Int tr = terrainTilemap.WorldToCell(b.max);

        for (int y = bl.y; y <= tr.y; y++)
        {
            for (int x = bl.x; x <= tr.x; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                var tile = terrainTilemap.GetTile(cell) as DestructibleTile;
                // On ne vide pas les bedrocks (incassables) pour garder le niveau clos.
                if (tile != null && tile.IsIndestructible) continue;
                terrainTilemap.SetTile(cell, null);
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var hp = other.GetComponent<PlayerStats>();
        if (hp == null) hp = other.GetComponentInParent<PlayerStats>();
        if (hp == null) return;

        hp.TakeDamage(damageOnContact);
    }
}
