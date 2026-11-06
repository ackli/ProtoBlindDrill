using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

/// <summary>
/// Foreuse continue : après un temps de chauffe, tant que le bouton de minage est
/// maintenu et qu'une cible valide est sous la visée, applique des dégâts à cadence
/// fixe via <see cref="DestructibleTerrain.TryMine"/>. La visée et le hover cursor
/// restent gérés par <see cref="MiningController"/>.
/// </summary>
[RequireComponent(typeof(MiningController))]
public class DrillController : MonoBehaviour
{
    [Header("Drill Timing")]
    [Tooltip("Durée de maintien avant que la foreuse ne devienne active.")]
    [SerializeField] private float chargeUpTime = 0.07f;

    [Tooltip("Intervalle entre deux tentatives de destruction une fois la foreuse active (secondes).")]
    [SerializeField] private float drillTickRate = 0.1f;

    [Header("Dépendances")]
    [Tooltip("Source de la cellule ciblée et du hover. Auto-résolu depuis le même GameObject si vide.")]
    [SerializeField] private MiningController miningController;

    [Tooltip("Référencer PlayerInputActions.inputactions (même asset que MiningController).")]
    [SerializeField] private InputActionAsset inputActions;

    private InputAction drillAction;

    // État interne (lisibles en Inspector en debug via les propriétés publiques).
    private bool isHoldingInput;
    private float chargeTimer;
    private float timeSinceLastBreak;

    /// <summary>True une fois la chauffe terminée et le bouton toujours maintenu.</summary>
    public bool IsDrillActive => isHoldingInput && chargeTimer >= chargeUpTime;

    /// <summary>Progression normalisée [0,1] de la chauffe (utile pour un HUD).</summary>
    public float ChargeProgress => chargeUpTime <= 0f ? 1f : Mathf.Clamp01(chargeTimer / chargeUpTime);

    private void Awake()
    {
        if (miningController == null) miningController = GetComponent<MiningController>();

        if (inputActions == null)
        {
            Debug.LogError("[DrillController] InputActionAsset non assigné.", this);
            enabled = false;
            return;
        }

        var map = inputActions.FindActionMap("Player", throwIfNotFound: true);
        drillAction = map.FindAction("Mine", throwIfNotFound: true);
    }

    private void OnEnable()
    {
        if (drillAction == null) return;

        // started : pression détectée (avant seuil éventuel du Press).
        // performed : seuil franchi (press point atteint pour la gâchette).
        // canceled : relâchement — on reset tout immédiatement.
        drillAction.started += OnDrillStarted;
        drillAction.performed += OnDrillPerformed;
        drillAction.canceled += OnDrillCanceled;
        drillAction.Enable();
    }

    private void OnDisable()
    {
        if (drillAction == null) return;

        drillAction.started -= OnDrillStarted;
        drillAction.performed -= OnDrillPerformed;
        drillAction.canceled -= OnDrillCanceled;
        drillAction.Disable();
        ResetDrillState();
    }

    private void Update()
    {
        if (!isHoldingInput) return;

        chargeTimer += Time.deltaTime;
        if (chargeTimer < chargeUpTime) return;

        timeSinceLastBreak += Time.deltaTime;
        if (timeSinceLastBreak < drillTickRate) return;

        TryDrillTick();
    }

    private void TryDrillTick()
    {
        // Pas de cible atteignable : on ne spamme pas TryMine et on n'avance pas le prochain tick.
        if (miningController == null || !miningController.HasTarget) return;

        var tm = miningController.Tilemap;
        if (tm == null) return;

        var cell = miningController.CurrentTargetCell;
        // Cellule déjà vidée depuis l'évaluation précédente : rien à casser, on ne reset pas le timer.
        if (!tm.HasTile(cell)) return;

        var dt = miningController.DestructibleTerrain;
        if (dt != null)
            dt.TryMine(cell);
        else
            tm.SetTile(cell, null);

        // Un tick a été consommé (même sur Indestructible : la foreuse travaille dans le vide, c'est le feedback voulu).
        timeSinceLastBreak = 0f;
    }

    private void OnDrillStarted(InputAction.CallbackContext _)
    {
        isHoldingInput = true;
    }

    private void OnDrillPerformed(InputAction.CallbackContext _)
    {
        // Garantit que isHoldingInput est vrai même si started a été manqué (ex: binding à press point).
        isHoldingInput = true;
    }

    private void OnDrillCanceled(InputAction.CallbackContext _)
    {
        ResetDrillState();
    }

    private void ResetDrillState()
    {
        isHoldingInput = false;
        chargeTimer = 0f;
        timeSinceLastBreak = 0f;
    }
}
