using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Salle d'arène : BoxCollider2D trigger qui, à la première entrée du joueur, spawne la vague
/// de mobs correspondant au <see cref="RoomTier"/>. Projette automatiquement les positions 2D
/// (centre + offset aléatoire) vers le plan NavMesh 3D (Y = navMeshY) pour respecter l'archi
/// Dual-Plane. Les mobs spawnés s'enregistrent via MobBase.Start → LevelObjectiveManager.
/// Quand tous les mobs de CETTE salle sont morts, drope une récompense PickupItem au centre-bas.
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

    [Header("Reward")]
    [Tooltip("Décalage vertical (unités) au-dessus du sol de la salle pour poser la récompense (évite de clipper dans le sol).")]
    [SerializeField] private float rewardBottomOffset = 1.0f;

    [Header("Camera")]
    [Tooltip("Orthographic size cible quand le joueur entre dans la salle. Dézoom = valeur plus grande que la baseline (7.5). Overridé par TerrainGenerator.Configure selon le tier.")]
    [SerializeField] private float arenaCameraZoom = 9f;

    private bool hasTriggered;
    private bool rewardDropped;
    private readonly List<MobBase> spawnedMobs = new List<MobBase>();

    private void Awake()
    {
        var box = GetComponent<BoxCollider2D>();
        box.isTrigger = true;
    }

    /// <summary>Configuration dynamique appelée par TerrainGenerator après Instantiate/AddComponent.</summary>
    public void Configure(RoomTier t, MobBase crawler, SpitterController spitter, TrapperController trapper,
                          Vector2 size, float ny, float cameraZoom)
    {
        tier = t;
        crawlerPrefab = crawler;
        spitterPrefab = spitter;
        trapperPrefab = trapper;
        navMeshY = ny;
        arenaCameraZoom = cameraZoom;

        var box = GetComponent<BoxCollider2D>();
        box.size = size;
        box.offset = Vector2.zero;
        box.isTrigger = true;

        // Les arènes sont créées dynamiquement par TerrainGenerator.SpawnArenaRooms(), appelé
        // juste avant MarkInitialGenerationComplete() dans la même frame. On enregistre ici
        // (et pas en Start) pour garantir l'incrément AVANT l'évaluation de victoire initiale.
        if (LevelObjectiveManager.Instance != null)
            LevelObjectiveManager.Instance.RegisterArena(this);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerStats>() == null) return;

        // Zoom caméra à chaque entrée (même si l'arène a déjà été déclenchée) pour que la
        // re-visite d'une salle vidée dézoome aussi — cohérence visuelle.
        if (CameraFollow2D.Instance != null)
            CameraFollow2D.Instance.SetZoom(arenaCameraZoom);

        if (hasTriggered) return;
        hasTriggered = true;
        Spawn();

        if (LevelObjectiveManager.Instance != null)
            LevelObjectiveManager.Instance.MarkArenaTriggered(this);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerStats>() == null) return;

        // Retour au zoom de parcours dès que le joueur sort de la salle.
        if (CameraFollow2D.Instance != null)
            CameraFollow2D.Instance.ResetZoom();
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

        var mob = Instantiate(prefab, spawnPos, Quaternion.identity);
        if (mob != null) spawnedMobs.Add(mob);
    }

    private void Update()
    {
        // Attend que l'arène ait été déclenchée ET que des mobs aient été spawnés ; sinon on saute.
        if (!hasTriggered || rewardDropped || spawnedMobs.Count == 0) return;

        // Les mobs détruits deviennent null côté Unity. Dès que la liste est vide → clear.
        bool stillAlive = false;
        for (int i = 0; i < spawnedMobs.Count; i++)
            if (spawnedMobs[i] != null) { stillAlive = true; break; }

        if (!stillAlive)
        {
            rewardDropped = true;
            spawnedMobs.Clear();
            DropReward();
        }
    }

    private void DropReward()
    {
        if (LevelObjectiveManager.Instance == null)
        {
            Debug.LogWarning("[ARENA] Pas de LevelObjectiveManager → pas de récompense dropée.", this);
            return;
        }
        var prefab = LevelObjectiveManager.Instance.GetRandomRewardPrefab();
        if (prefab == null)
        {
            Debug.Log("[ARENA] Clear mais aucun prefab de récompense configuré (LevelObjectiveManager.rewardPrefabs vide).", this);
            return;
        }

        // center.x, bottom.y + offset — le BoxCollider2D de l'arène porte la taille de la salle.
        var box = GetComponent<BoxCollider2D>();
        Vector3 c = transform.position;
        float halfH = box != null ? box.size.y * 0.5f : 0f;
        Vector3 spawnPos = new Vector3(c.x, c.y - halfH + rewardBottomOffset, 0f);

        var reward = Instantiate(prefab, spawnPos, Quaternion.identity);
        Debug.Log($"[ARENA] {tier} clear → récompense '{reward.DisplayName}' ({reward.Kind}) dropée à ({spawnPos.x:F1},{spawnPos.y:F1}).", this);
    }
}
