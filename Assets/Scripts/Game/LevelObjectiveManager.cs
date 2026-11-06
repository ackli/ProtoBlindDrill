using System;
using System.Collections;
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

    private int totalEnemiesRemaining;
    private int untriggeredArenas = 0;
    private bool hasEverRegistered;
    private bool initialGenerationComplete;
    private bool victoryFired;

    public int TotalEnemiesRemaining => totalEnemiesRemaining;
    public int UntriggeredArenas => untriggeredArenas;
    public bool InitialGenerationComplete => initialGenerationComplete;

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
    }

    public void UnregisterEnemy(MobBase mob)
    {
        if (mob == null) return;
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
    public void RegisterArena()
    {
        untriggeredArenas++;
    }

    /// <summary>Appelé par une <see cref="ArenaRoom"/> dès qu'elle a été déclenchée (mobs spawnés).</summary>
    public void MarkArenaTriggered()
    {
        untriggeredArenas = Mathf.Max(0, untriggeredArenas - 1);
        // Les mobs tout juste instanciés s'enregistrent via MobBase.Start — donc à la frame
        // suivante. Évaluer la victoire tout de suite ouvrirait une race condition : le
        // compteur d'ennemis vaut encore 0 alors que des mobs vont s'ajouter. On diffère.
        StartCoroutine(DeferredVictoryCheck());
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
        if (untriggeredArenas > 0) return;

        victoryFired = true;
        Debug.Log("[LEVEL] Victoire ! Tous les ennemis ont été purgés dans toutes les arènes.");
        OnLevelCleared?.Invoke();
    }

    // ---- Accesseurs statiques safe (no-op si pas d'Instance) ----
    public static void SafeRegister(MobBase mob) { if (Instance != null) Instance.RegisterEnemy(mob); }
    public static void SafeUnregister(MobBase mob) { if (Instance != null) Instance.UnregisterEnemy(mob); }
}
