using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Hub de triches pour les tests d'arène / équilibrage. Attaché au GameObject _DEBUG_CHEATS.
/// Toggles via F1–F4 (Input System), ou case à cocher dans l'Inspector, ou clic-droit → ContextMenu.
///
/// Les trois toggles (God Mode / One-Shot / Full Vision) sont stateful : état interne synchronisé
/// avec les booléens Inspector via un diff en Update (une seule application par changement).
/// Nuke est une action instantanée (pas de toggle).
/// </summary>
public class DebugCheatController : MonoBehaviour
{
    [Header("Cheats — cocher dans l'Inspector, ou F1..F5 en Play")]
    [SerializeField] private bool godMode;
    [SerializeField] private bool oneShotKill;
    [SerializeField] private bool fullVision;
    [SerializeField] private bool arenaRadar;

    [Header("References (auto-résolues au Awake si vides)")]
    [SerializeField] private PlayerStats playerStats;
    [Tooltip("Light2D en mode Global à forcer à pleine luminosité pour la Vision Totale.")]
    [SerializeField] private Light2D[] globalLights;
    [Tooltip("Indicateur UI d'arènes non déclenchées (resté caché tant que ce cheat n'est pas activé).")]
    [SerializeField] private ArenaIndicatorUI arenaIndicator;

    // Flag statique lu par MobBase.OnChildTriggerEnter pour appliquer les dégâts one-shot.
    public static bool OneShotKillEnabled { get; private set; }

    // Suivi d'état pour n'appliquer un effet qu'au changement.
    private bool godModeSynced;
    private bool oneShotSynced;
    private bool fullVisionSynced;
    private bool arenaRadarSynced;

    // Snapshot ambient pour la restauration de la Vision Totale.
    private float[] originalIntensities;
    private Color[] originalColors;

    private void Awake()
    {
        if (playerStats == null) playerStats = FindFirstObjectByType<PlayerStats>();
        if (arenaIndicator == null) arenaIndicator = FindFirstObjectByType<ArenaIndicatorUI>(FindObjectsInactive.Include);

        if (globalLights == null || globalLights.Length == 0)
        {
            var all = FindObjectsByType<Light2D>(FindObjectsSortMode.None);
            var list = new List<Light2D>();
            foreach (var l in all)
                if (l.lightType == Light2D.LightType.Global) list.Add(l);
            globalLights = list.ToArray();
        }

        originalIntensities = new float[globalLights.Length];
        originalColors = new Color[globalLights.Length];
        for (int i = 0; i < globalLights.Length; i++)
        {
            if (globalLights[i] == null) continue;
            originalIntensities[i] = globalLights[i].intensity;
            originalColors[i] = globalLights[i].color;
        }
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.f1Key.wasPressedThisFrame) godMode = !godMode;
            if (kb.f2Key.wasPressedThisFrame) oneShotKill = !oneShotKill;
            if (kb.f3Key.wasPressedThisFrame) Nuke();
            if (kb.f4Key.wasPressedThisFrame) fullVision = !fullVision;
            if (kb.f5Key.wasPressedThisFrame) arenaRadar = !arenaRadar;
        }

        if (godMode != godModeSynced) { ApplyGodMode(godMode); godModeSynced = godMode; }
        if (oneShotKill != oneShotSynced) { ApplyOneShot(oneShotKill); oneShotSynced = oneShotKill; }
        if (fullVision != fullVisionSynced) { ApplyFullVision(fullVision); fullVisionSynced = fullVision; }
        if (arenaRadar != arenaRadarSynced) { ApplyArenaRadar(arenaRadar); arenaRadarSynced = arenaRadar; }
    }

    [ContextMenu("Toggle God Mode (F1)")]
    public void ToggleGodMode() => godMode = !godMode;

    [ContextMenu("Toggle One-Shot Kill (F2)")]
    public void ToggleOneShotKill() => oneShotKill = !oneShotKill;

    [ContextMenu("Nuke All Mobs (F3)")]
    public void Nuke()
    {
        var mobs = FindObjectsByType<MobBase>(FindObjectsSortMode.None);
        int count = 0;
        foreach (var m in mobs)
        {
            if (m == null) continue;
            m.TakeDamage(9999, "CHEAT_NUKE");
            count++;
        }
        Debug.LogWarning($"[CHEAT] Nuke déclenchée : {count} mobs éliminés.");
    }

    [ContextMenu("Toggle Full Vision (F4)")]
    public void ToggleFullVision() => fullVision = !fullVision;

    [ContextMenu("Toggle Arena Radar (F5)")]
    public void ToggleArenaRadar() => arenaRadar = !arenaRadar;

    private void ApplyGodMode(bool on)
    {
        if (playerStats != null)
        {
            playerStats.GodMode = on;
            // Confort : quand on active God Mode, on remonte au cap complet pour repartir propre.
            if (on) playerStats.Heal(playerStats.MaxHealth + playerStats.MaxOverheal);
        }
        Debug.LogWarning($"[CHEAT] God Mode {(on ? "activé" : "désactivé")}");
    }

    private void ApplyOneShot(bool on)
    {
        OneShotKillEnabled = on;
        Debug.LogWarning($"[CHEAT] One-Shot Kill {(on ? "activé" : "désactivé")}");
    }

    private void ApplyArenaRadar(bool on)
    {
        if (arenaIndicator == null) arenaIndicator = FindFirstObjectByType<ArenaIndicatorUI>(FindObjectsInactive.Include);
        if (arenaIndicator != null) arenaIndicator.isRadarEnabled = on;

        Debug.Log($"Cheat: Radar d'arènes {(on ? "ACTIVÉ" : "DÉSACTIVÉ")}");
    }

    private void ApplyFullVision(bool on)
    {
        if (globalLights == null) return;
        for (int i = 0; i < globalLights.Length; i++)
        {
            if (globalLights[i] == null) continue;
            if (on)
            {
                globalLights[i].intensity = 1f;
                globalLights[i].color = Color.white;
            }
            else
            {
                globalLights[i].intensity = originalIntensities[i];
                globalLights[i].color = originalColors[i];
            }
        }
        Debug.LogWarning($"[CHEAT] Full Vision {(on ? "activée" : "désactivée")}");
    }

    private void OnDisable()
    {
        // Sécurité : à la désactivation du GO (reload de scène, exit Play), on remet les flags neutres.
        if (fullVisionSynced) ApplyFullVision(false);
        if (arenaRadarSynced && arenaIndicator != null) arenaIndicator.isRadarEnabled = false;
        OneShotKillEnabled = false;
        if (playerStats != null) playerStats.GodMode = false;
    }
}
