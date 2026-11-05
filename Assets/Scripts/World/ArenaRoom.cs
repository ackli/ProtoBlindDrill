using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Salle d'arène : BoxCollider2D trigger qui, à la première entrée du joueur, spawne la vague
/// de mobs correspondant au <see cref="RoomTier"/>. Projette automatiquement les positions 2D
/// (centre + offset aléatoire) vers le plan NavMesh 3D (Y = navMeshY) pour respecter l'archi
/// Dual-Plane. Les mobs spawnés s'enregistrent via MobBase.Start → LevelObjectiveManager.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class ArenaRoom : MonoBehaviour
{
    public enum RoomTier { Tier1, Tier2, Tier3 }

    [Header("Tier")]
    [SerializeField] private RoomTier tier = RoomTier.Tier1;

    [Header("Prefabs")]
    [SerializeField] private MobBase crawlerPrefab;
    [SerializeField] private SpitterController spitterPrefab;
    [SerializeField] private TrapperController trapperPrefab;

    [Header("Spawn")]
    [Tooltip("Plan Y monde 3D du NavMesh — doit matcher MobBase.navMeshY.")]
    [SerializeField] private float navMeshY = 100f;

    [Tooltip("Rayon d'offset aléatoire (unités 2D) autour du centre de la salle. Évite l'empilage des mobs.")]
    [SerializeField] private float spawnSpreadRadius = 2.5f;

    private bool hasTriggered;

    private void Awake()
    {
        var box = GetComponent<BoxCollider2D>();
        box.isTrigger = true;
    }

    /// <summary>Configuration dynamique appelée par TerrainGenerator après Instantiate/AddComponent.</summary>
    public void Configure(RoomTier t, MobBase crawler, SpitterController spitter, TrapperController trapper,
                          Vector2 size, float ny)
    {
        tier = t;
        crawlerPrefab = crawler;
        spitterPrefab = spitter;
        trapperPrefab = trapper;
        navMeshY = ny;

        var box = GetComponent<BoxCollider2D>();
        box.size = size;
        box.offset = Vector2.zero;
        box.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (hasTriggered) return;
        if (other.GetComponentInParent<PlayerStats>() == null) return;

        hasTriggered = true;
        Spawn();
    }

    private void Spawn()
    {
        int nCrawlers = 0, nSpitters = 0, nTrappers = 0;
        switch (tier)
        {
            case RoomTier.Tier1: nCrawlers = 3; nSpitters = 1; break;
            case RoomTier.Tier2: nCrawlers = 2; nSpitters = 2; nTrappers = 1; break;
            case RoomTier.Tier3: nCrawlers = 4; nSpitters = 3; nTrappers = 2; break;
        }

        for (int i = 0; i < nCrawlers; i++) SpawnMob(crawlerPrefab);
        for (int i = 0; i < nSpitters; i++) SpawnMob(spitterPrefab);
        for (int i = 0; i < nTrappers; i++) SpawnMob(trapperPrefab);

        Debug.Log($"[ARENA] {tier} déclenchée à {transform.position} — {nCrawlers + nSpitters + nTrappers} mobs spawnés.");
    }

    private void SpawnMob<T>(T prefab) where T : MobBase
    {
        if (prefab == null) return;
        Vector2 offset = Random.insideUnitCircle * spawnSpreadRadius;
        Vector3 c = transform.position;
        // Position idéale (c.x + offX, Y=navMeshY, c.y + offY) : projection 2D → NavMesh 3D.
        Vector3 idealPos = new Vector3(c.x + offset.x, navMeshY, c.y + offset.y);

        // Snap sur NavMesh : évite de spawner à l'intérieur d'un NavMeshObstacle Carve (bloc de plateforme).
        Vector3 spawnPos;
        if (NavMesh.SamplePosition(idealPos, out var hit, 4.0f, NavMesh.AllAreas))
            spawnPos = hit.position;
        else
            spawnPos = new Vector3(c.x, navMeshY, c.y); // fallback : centre strict de la salle

        Instantiate(prefab, spawnPos, Quaternion.identity);
    }
}
