using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton de scène qui suit le compteur d'ennemis vivants et déclenche l'événement
/// <see cref="OnLevelCleared"/> quand la condition "Clear All Enemies" est remplie :
///   - la génération initiale du terrain est terminée (<see cref="MarkInitialGenerationComplete"/>),
///   - au moins un mob a été enregistré (on ne déclenche pas la victoire sur un état initial à 0),
///   - le compteur retombe à 0.
/// Les helpers statiques <see cref="SafeRegister"/> / <see cref="SafeUnregister"/> ne font rien
/// si aucune Instance n'est présente — safe à appeler depuis tout MobBase.
/// </summary>
public class LevelObjectiveManager : MonoBehaviour
{
    public static LevelObjectiveManager Instance { get; private set; }

    /// <summary>Levé une seule fois quand tous les ennemis ont été purgés après init complète.</summary>
    public static event Action OnLevelCleared;

    [Header("Arena Rewards")]
    [Tooltip("Pool de récompenses (prefabs de PickupItem) tirée au hasard par ArenaRoom au clear.")]
    [SerializeField] private PickupItem[] rewardPrefabs;

    [Header("Boussole anti-frustration")]
    [Tooltip("Secondes sans découverte de salle ni combat avant que la boussole n'apparaisse.")]
    [SerializeField] private float compassInactivityDelay = 120f;

    // Chrono d'inactivité : remis à 0 par NotifyPlayerAction (nouvelle arène, dégâts infligés, aggro).
    private float timeSinceLastAction;
    // Mobs vivants enregistrés (MobBase.Start → OnDestroy), cibles potentielles de la boussole.
    private readonly List<MobBase> aliveMobs = new List<MobBase>();

    private int totalEnemiesRemaining;
    private readonly List<ArenaRoom> untriggeredRooms = new List<ArenaRoom>();
    private bool hasEverRegistered;
    private bool initialGenerationComplete;
    private bool victoryFired;

    /// <summary>
    /// Tire un prefab de récompense au hasard, en excluant l'arme ET le gear actuellement équipés
    /// par le joueur (le pickup est un doublon ⇒ frustrant). Si le filtrage vide totalement la pool
    /// (cas pathologique où tout ce qui reste équivaut à ce que le joueur a déjà), fallback sur un
    /// tirage brut dans la pool complète pour toujours droper quelque chose.
    /// </summary>
    public PickupItem GetRandomRewardPrefab()
    {
        if (rewardPrefabs == null || rewardPrefabs.Length == 0) return null;

        // Résolution de l'équipement actif : on cherche l'EquipmentManager via le GO tagué "Player".
        // On tolère son absence (early boot) — dans ce cas, pas de filtre.
        string equippedWeapon = string.Empty;
        string equippedGear = string.Empty;
        var playerGO = GameObject.FindGameObjectWithTag("Player");
        if (playerGO != null)
        {
            var eq = playerGO.GetComponent<EquipmentManager>();
            if (eq == null) eq = playerGO.GetComponentInParent<EquipmentManager>();
            if (eq != null)
            {
                equippedWeapon = eq.CurrentWeaponTypeName;
                equippedGear = eq.CurrentGearTypeName;
            }
        }

        // Construit la liste des choix valides : tout prefab dont le componentTypeName du PickupItem
        // ne matche NI l'arme NI le gear actuellement équipé. Comparaison par nom simple, cohérente
        // avec CurrentWeaponTypeName / CurrentGearTypeName (qui renvoient GetType().Name).
        // On trace aussi quels kinds étaient présents dans la pool brute : si l'anti-doublon vide
        // entièrement un kind (ex: seul gear de la pool == gear équipé), on le réinjecte pour
        // préserver la rotation Weapon/Movement — un doublon ponctuel est moins frustrant que
        // "plus jamais de gear qui drop".
        bool hasWeaponInRaw = false, hasMovementInRaw = false;
        var pool = new List<PickupItem>(rewardPrefabs.Length);
        for (int i = 0; i < rewardPrefabs.Length; i++)
        {
            var p = rewardPrefabs[i];
            if (p == null) continue;
            if (p.Kind == PickupItem.EquipmentKind.Weapon) hasWeaponInRaw = true;
            else hasMovementInRaw = true;
            var t = p.ComponentTypeName;
            if (!string.IsNullOrEmpty(t) &&
                (t == equippedWeapon || t == equippedGear))
                continue; // doublon de l'équipement actif → exclu
            pool.Add(p);
        }

        // Détection des kinds orphelins (présents dans la pool brute mais vidés par l'anti-doublon).
        bool hasWeaponInPool = false, hasMovementInPool = false;
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i].Kind == PickupItem.EquipmentKind.Weapon) hasWeaponInPool = true;
            else hasMovementInPool = true;
        }
        if (hasWeaponInRaw && !hasWeaponInPool)
        {
            Debug.LogWarning("[LEVEL] Pool Weapon vidée par l'anti-doublon → réinjection depuis la pool brute pour préserver la rotation.");
            for (int i = 0; i < rewardPrefabs.Length; i++)
                if (rewardPrefabs[i] != null && rewardPrefabs[i].Kind == PickupItem.EquipmentKind.Weapon)
                    pool.Add(rewardPrefabs[i]);
        }
        if (hasMovementInRaw && !hasMovementInPool)
        {
            Debug.LogWarning("[LEVEL] Pool Movement vidée par l'anti-doublon → réinjection depuis la pool brute pour préserver la rotation.");
            for (int i = 0; i < rewardPrefabs.Length; i++)
                if (rewardPrefabs[i] != null && rewardPrefabs[i].Kind == PickupItem.EquipmentKind.Movement)
                    pool.Add(rewardPrefabs[i]);
        }

        // Fallback ultime : pool brute vide ou constituée uniquement de null.
        if (pool.Count == 0)
        {
            Debug.LogWarning("[LEVEL] Aucune récompense tirable (pool brute vide ou invalide) → fallback tirage brut.");
            return rewardPrefabs[UnityEngine.Random.Range(0, rewardPrefabs.Length)];
        }

        return pool[UnityEngine.Random.Range(0, pool.Count)];
    }

    public int TotalEnemiesRemaining => totalEnemiesRemaining;
    public int UntriggeredArenas => untriggeredRooms.Count;
    public IReadOnlyList<ArenaRoom> UntriggeredRooms => untriggeredRooms;
    public bool InitialGenerationComplete => initialGenerationComplete;

    /// <summary>Secondes écoulées depuis la dernière découverte de salle / action de combat.</summary>
    public float TimeSinceLastAction => timeSinceLastAction;
    /// <summary>Vrai quand le joueur est resté inactif assez longtemps pour mériter la boussole.</summary>
    public bool IsCompassActive => timeSinceLastAction >= compassInactivityDelay;

    private void Update()
    {
        // Le chrono ne tourne que pendant la partie : pas avant la fin de génération, pas après la victoire.
        if (!initialGenerationComplete || victoryFired) return;
        timeSinceLastAction += Time.deltaTime;
    }

    /// <summary>
    /// Remet le chrono d'inactivité à 0 (et masque donc la boussole). Appelé quand une arène
    /// est déclenchée, quand un mob encaisse des dégâts, ou tant qu'un mob est aggro sur le joueur.
    /// </summary>
    public void NotifyPlayerAction() => timeSinceLastAction = 0f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void RegisterEnemy(MobBase mob)
    {
        if (mob == null) return;
        totalEnemiesRemaining++;
        hasEverRegistered = true;
        aliveMobs.Add(mob);
    }

    public void UnregisterEnemy(MobBase mob)
    {
        if (mob == null) return;
        aliveMobs.Remove(mob);
        totalEnemiesRemaining = Mathf.Max(0, totalEnemiesRemaining - 1);
        TryTriggerVictory();
    }

    /// <summary>Appelé par TerrainGenerator en fin de génération / spawn des arènes.</summary>
    public void MarkInitialGenerationComplete()
    {
        initialGenerationComplete = true;
        TryTriggerVictory();
    }

    /// <summary>Appelé par chaque <see cref="ArenaRoom"/> à son initialisation (Configure).</summary>
    public void RegisterArena(ArenaRoom room)
    {
        if (room == null) return;
        if (!untriggeredRooms.Contains(room)) untriggeredRooms.Add(room);
    }

    /// <summary>Appelé par une <see cref="ArenaRoom"/> dès qu'elle a été déclenchée (mobs spawnés).</summary>
    public void MarkArenaTriggered(ArenaRoom room)
    {
        if (room != null) untriggeredRooms.Remove(room);
        NotifyPlayerAction(); // nouvelle salle découverte → reset de la boussole
        // Les mobs tout juste instanciés s'enregistrent via MobBase.Start — donc à la frame
        // suivante. Évaluer la victoire tout de suite ouvrirait une race condition : le
        // compteur d'ennemis vaut encore 0 alors que des mobs vont s'ajouter. On diffère.
        StartCoroutine(DeferredVictoryCheck());
    }

    /// <summary>
    /// Retourne l'<see cref="ArenaRoom"/> non déclenchée la plus proche d'une position monde,
    /// ou <c>null</c> si toutes les arènes ont été déclenchées. Nettoie à la volée les
    /// entrées devenues null (arène détruite avant d'avoir été déclenchée).
    /// </summary>
    public ArenaRoom GetNearestUntriggeredArena(Vector3 playerPosition)
    {
        ArenaRoom nearest = null;
        float bestSq = float.PositiveInfinity;

        for (int i = untriggeredRooms.Count - 1; i >= 0; i--)
        {
            var room = untriggeredRooms[i];
            if (room == null) { untriggeredRooms.RemoveAt(i); continue; }

            float dSq = (room.transform.position - playerPosition).sqrMagnitude;
            if (dSq < bestSq)
            {
                bestSq = dSq;
                nearest = room;
            }
        }
        return nearest;
    }

    /// <summary>
    /// Cible de la boussole : l'élément le plus proche du joueur parmi les arènes non déclenchées
    /// ET les mobs vivants (un mob oublié dans un couloir compte autant qu'une arène).
    /// Distances mesurées dans le plan 2D du joueur — les mobs y sont projetés via
    /// <see cref="MobBase.Position2D"/> (leur racine vit sur le plan NavMesh).
    /// Retourne false s'il n'existe plus aucune cible.
    /// </summary>
    public bool TryGetNearestCompassTarget(Vector3 playerPosition, out Vector3 targetPosition)
    {
        targetPosition = default;
        float bestSq = float.PositiveInfinity;
        Vector2 player2D = playerPosition;

        var arena = GetNearestUntriggeredArena(playerPosition);
        if (arena != null)
        {
            targetPosition = arena.transform.position;
            bestSq = ((Vector2)arena.transform.position - player2D).sqrMagnitude;
        }

        for (int i = aliveMobs.Count - 1; i >= 0; i--)
        {
            var mob = aliveMobs[i];
            if (mob == null) { aliveMobs.RemoveAt(i); continue; }
            if (!mob.isActiveAndEnabled) continue;

            Vector3 pos = mob.Position2D;
            float dSq = ((Vector2)pos - player2D).sqrMagnitude;
            if (dSq < bestSq)
            {
                bestSq = dSq;
                targetPosition = pos;
            }
        }

        return bestSq < float.PositiveInfinity;
    }

    private IEnumerator DeferredVictoryCheck()
    {
        // Attendre la fin de frame laisse tourner les Start() des mobs fraîchement instanciés,
        // qui appellent RegisterEnemy() et remontent totalEnemiesRemaining avant l'évaluation.
        yield return new WaitForEndOfFrame();
        TryTriggerVictory();
    }

    private void TryTriggerVictory()
    {
        if (victoryFired) return;
        if (!initialGenerationComplete) return;
        if (!hasEverRegistered) return;
        if (totalEnemiesRemaining > 0) return;
        if (untriggeredRooms.Count > 0) return;

        victoryFired = true;
        Debug.Log("[LEVEL] Victoire ! Tous les ennemis ont été purgés dans toutes les arènes.");
        OnLevelCleared?.Invoke();
    }

    // ---- Accesseurs statiques safe (no-op si pas d'Instance) ----
    public static void SafeRegister(MobBase mob) { if (Instance != null) Instance.RegisterEnemy(mob); }
    public static void SafeUnregister(MobBase mob) { if (Instance != null) Instance.UnregisterEnemy(mob); }
    public static void SafeNotifyPlayerAction() { if (Instance != null) Instance.NotifyPlayerAction(); }
}
