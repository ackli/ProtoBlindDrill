using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

public class MiningController : MonoBehaviour
{
    [Header("Target Tilemap")]
    [Tooltip("Tilemap destructible visée par le minage.")]
    [SerializeField] private Tilemap tilemap;

    [Tooltip("Caméra utilisée pour convertir la position souris en monde. Si null, Camera.main au Awake.")]
    [SerializeField] private Camera targetCamera;

    [Header("Mining")]
    [Tooltip("Portée maximale de minage (distance monde depuis le joueur).")]
    [SerializeField] private float miningRange = 3f;

    [Tooltip("À la manette, distance à laquelle le réticule se place depuis le joueur dans la direction du stick droit.")]
    [SerializeField] private float gamepadAimDistance = 2.5f;

    [Tooltip("Deadzone du stick droit avant de considérer qu'on vise à la manette.")]
    [SerializeField] private float gamepadDeadzone = 0.3f;

    [Header("Input")]
    [Tooltip("Référencer PlayerInputActions.inputactions.")]
    [SerializeField] private InputActionAsset inputActions;

    private InputAction mineAction;
    private InputAction aimAction;

    private Vector3Int currentTargetCell;
    private bool hasTarget;
    private bool targetInRange;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;

        if (inputActions == null)
        {
            Debug.LogError("[MiningController] InputActionAsset non assigné.", this);
            enabled = false;
            return;
        }

        var map = inputActions.FindActionMap("Player", throwIfNotFound: true);
        mineAction = map.FindAction("Mine", throwIfNotFound: true);
        aimAction = map.FindAction("Aim", throwIfNotFound: true);
    }

    private void OnEnable()
    {
        if (mineAction == null || aimAction == null) return;
        mineAction.Enable();
        aimAction.Enable();
        mineAction.performed += OnMinePerformed;
    }

    private void OnDisable()
    {
        if (mineAction == null || aimAction == null) return;
        mineAction.performed -= OnMinePerformed;
        mineAction.Disable();
        aimAction.Disable();
    }

    private void Update()
    {
        UpdateTargetCell();
    }

    /// <summary>
    /// Calcule la cellule Tilemap actuellement visée :
    /// - Souris : position écran → monde via Camera.ScreenToWorldPoint
    /// - Manette : direction du stick droit × gamepadAimDistance depuis le joueur
    /// Puis Tilemap.WorldToCell(worldPos) pour obtenir la coordonnée de cellule.
    /// </summary>
    private void UpdateTargetCell()
    {
        if (tilemap == null || targetCamera == null) { hasTarget = false; return; }

        Vector2 aim = aimAction.ReadValue<Vector2>();
        Vector3 targetWorld;

        if (IsAimingWithGamepad(aim))
        {
            Vector2 dir = aim.normalized;
            targetWorld = transform.position + (Vector3)(dir * gamepadAimDistance);
        }
        else
        {
            // aim.xy = position écran en pixels. On projette dans le plan Z=0 (2D).
            float planeDistance = Mathf.Abs(targetCamera.transform.position.z - transform.position.z);
            Vector3 screen = new Vector3(aim.x, aim.y, planeDistance);
            targetWorld = targetCamera.ScreenToWorldPoint(screen);
        }
        targetWorld.z = 0f;

        float dist = Vector2.Distance(transform.position, targetWorld);
        targetInRange = dist <= miningRange;

        currentTargetCell = tilemap.WorldToCell(targetWorld);
        hasTarget = true;
    }

    private bool IsAimingWithGamepad(Vector2 aim)
    {
        // activeControl pointe sur le binding ayant produit la dernière valeur.
        // On considère "manette" dès que la valeur du stick droit dépasse la deadzone.
        var active = aimAction.activeControl;
        if (active != null && active.device is Gamepad)
            return aim.magnitude > gamepadDeadzone;
        return false;
    }

    private void OnMinePerformed(InputAction.CallbackContext _)
    {
        if (!hasTarget || !targetInRange || tilemap == null) return;
        if (!tilemap.HasTile(currentTargetCell)) return;

        tilemap.SetTile(currentTargetCell, null);
    }

    private void OnDrawGizmos()
    {
        if (tilemap == null || !hasTarget) return;

        Vector3 center = tilemap.GetCellCenterWorld(currentTargetCell);
        Gizmos.color = targetInRange
            ? new Color(1f, 0.3f, 0.3f, 0.85f)   // rouge vif = dans la portée
            : new Color(0.5f, 0.5f, 0.5f, 0.4f); // gris = hors portée
        Gizmos.DrawWireCube(center, tilemap.cellSize);

        // Cercle de portée
        Gizmos.color = new Color(1f, 1f, 1f, 0.15f);
        DrawCircleXY(transform.position, miningRange, 32);
    }

    private static void DrawCircleXY(Vector3 center, float radius, int segments)
    {
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
