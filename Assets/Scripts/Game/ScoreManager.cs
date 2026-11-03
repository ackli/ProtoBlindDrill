using System;
using UnityEngine;

/// <summary>
/// Singleton léger qui garde le "total de lumière accumulée" depuis le début de la partie.
/// Pas de DontDestroyOnLoad : réinitialisé à chaque chargement de scène (adapté à un prototype mono-scène).
/// </summary>
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    public int TotalLight { get; private set; }

    public event Action<int> OnLightChanged;

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

    public void AddLight(int amount)
    {
        if (amount <= 0) return;
        TotalLight += amount;
        OnLightChanged?.Invoke(TotalLight);
    }

    public void ResetScore()
    {
        TotalLight = 0;
        OnLightChanged?.Invoke(TotalLight);
    }
}
