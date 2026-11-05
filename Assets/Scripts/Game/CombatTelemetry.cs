using UnityEngine;

/// <summary>
/// Télémétrie de combat centralisée. Un flag global <see cref="Enabled"/> coupe TOUS les logs
/// de cette classe en un seul endroit (utile pour filtrer la console ou désactiver en build).
///
/// Préfixes pour filtrer dans la console Unity :
///   [AI-AGGRO]      — un mob vient d'engager le joueur (transition only)
///   [AI-LOST]       — un mob vient de perdre le joueur (transition only)
///   [MOB-HIT]       — un mob a encaissé des dégâts
///   [MOB-DEATH]     — un mob a été éliminé (avec temps de survie)
///   [PLAYER-HIT]    — le joueur a encaissé des dégâts (détail absorption Overheal)
///   [PLAYER-STATUS] — rapport périodique de l'état du joueur (timer, pas par frame)
///
/// Activation / désactivation :
///   - À chaud (Edit ou Play) : CombatTelemetry.Enabled = false; dans l'inspecteur ou via code.
///   - En durci (prod) : passer à false par défaut et compiler. Les appels restent présents
///     mais sortent immédiatement (coût = un test booléen par événement, pas par frame).
/// </summary>
public static class CombatTelemetry
{
    /// <summary>Flag global. Mettre à false pour couper tous les logs d'un seul coup.</summary>
    public static bool Enabled = true;

    public static void AggroGained(string mobName, int id, float dist)
    {
        if (!Enabled) return;
        Debug.Log($"[AI-AGGRO] {mobName}_{id} a ciblé le joueur à une distance de {dist:F1}m.");
    }

    public static void AggroLost(string mobName, int id, float dist, string reason)
    {
        if (!Enabled) return;
        Debug.Log($"[AI-LOST] {mobName}_{id} a perdu le joueur (distance: {dist:F1}m / {reason}).");
    }

    public static void MobHit(string mobName, int id, int dmg, string source, int currentHP, int maxHP)
    {
        if (!Enabled) return;
        Debug.Log($"[MOB-HIT] {mobName}_{id} a reçu {dmg} dégât(s) (Source: {source}). HP restants : {currentHP}/{maxHP}.");
    }

    public static void MobDeath(string mobName, int id, float survivalTime)
    {
        if (!Enabled) return;
        Debug.Log($"[MOB-DEATH] {mobName}_{id} éliminé après {survivalTime:F1}s d'existence.");
    }

    public static void PlayerHit(int dmg, int overhealAbsorbed, int healthLoss, int currentHealth, int maxHealth, int currentOverheal)
    {
        if (!Enabled) return;
        Debug.Log($"[PLAYER-HIT] Dégâts reçus : {dmg} (Absorbé par Overheal : {overhealAbsorbed}, Santé impactée : {healthLoss}). PV restants : {currentHealth}/{maxHealth}, Overheal : {currentOverheal}.");
    }

    public static void PlayerStatus(float time, int currentHealth, int maxHealth, int currentOverheal, int recentDamageTaken)
    {
        if (!Enabled) return;
        Debug.Log($"[PLAYER-STATUS] T+{time:F0}s | PV : {currentHealth}/{maxHealth} | Overheal : {currentOverheal} | Dégâts totaux subis sur la période : {recentDamageTaken}.");
    }
}
