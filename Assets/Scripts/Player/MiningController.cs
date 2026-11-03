using System.Collections.Generic;
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

    [Tooltip("Layer(s) considéré(s) comme mur minable (= Ground, celui du TilemapCollider2D).")]
    [SerializeField] private LayerMask wallLayerMask;

    [Tooltip("À la manette, distance à laquelle le réticule se place depuis le joueur dans la direction du stick droit.")]
    [SerializeField] private float gamepadAimDistance = 2.5f;

    [Tooltip("Deadzone du stick droit avant de considérer qu'on vise à la manette.")]
    [SerializeField] private float gamepadDeadzone = 0.3f;

    [Header("Hover Cursor")]
    [Tooltip("Cadre affiché sur la tuile ciblée. Si null, créé automatiquement au Awake.")]
    [SerializeField] private LineRenderer hoverCursor;

    [Tooltip("Couleur du cadre (noir par défaut).")]
    [SerializeField] private Color hoverColor = Color.black;

    [Tooltip("Épaisseur du trait du cadre.")]
    [SerializeField] private float hoverWidth = 0.08f;

    [Header("Input")]
    [Tooltip("Référencer PlayerInputActions.inputactions.")]
    [SerializeField] private InputActionAsset inputActions;

    private InputAction mineAction;
    private InputAction aimAction;

    private Vector3Int currentTargetCell;
    private bool hasTarget;
    private DestructibleTerrain destructibleTerrain;

    // Buffers du BFS 4-connectivité pour éviter d'allouer chaque frame.
    private readonly HashSet<Vector3Int> bfsVisited = new HashSet<Vector3Int>();
    private readonly Queue<Vector3Int> bfsQueue = new Queue<Vector3Int>();

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;

        if (tilemap != null) destructibleTerrain = tilemap.GetComponent<DestructibleTerrain>();

        InitHoverCursorIfNeeded();

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

    private void InitHoverCursorIfNeeded()
    {
        if (hoverCursor != null) return;

        var go = new GameObject("MiningCursor");
        go.transform.SetParent(transform, worldPositionStays: false);
        hoverCursor = go.AddComponent<LineRenderer>();
        hoverCursor.positionCount = 5;
        hoverCursor.loop = false;
        hoverCursor.useWorldSpace = true;
        hoverCursor.startWidth = hoverCursor.endWidth = hoverWidth;
        hoverCursor.material = new Material(Shader.Find("Sprites/Default"));
        hoverCursor.startColor = hoverCursor.endColor = hoverColor;
        hoverCursor.sortingOrder = 100; // au-dessus des tiles
        hoverCursor.enabled = false;
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
        UpdateHoverCursor();
    }

    /// <summary>
    /// Deux étapes :
    ///  1) Raycast depuis le joueur vers le curseur pour trouver la *tentative* de cible
    ///     (première tuile Ground touchée dans miningRange).
    ///  2) Vérification BFS 4-connectivité : la cible n'est acceptée que si on peut
    ///     l'atteindre depuis la cellule du joueur par un chemin de cellules vides
    ///     en N/S/E/O, dans la portée. Ça filtre les tirs qui "glissent" dans un angle
    ///     diagonal entre deux tuiles pleines collées.
    /// </summary>
    private void UpdateTargetCell()
    {
        if (tilemap == null || targetCamera == null) { hasTarget = false; return; }

        Vector2 aim = aimAction.ReadValue<Vector2>();
        Vector3 cursorWorld;

        if (IsAimingWithGamepad(aim))
        {
            Vector2 dir = aim.normalized;
            cursorWorld = transform.position + (Vector3)(dir * gamepadAimDistance);
        }
        else
        {
            float planeDistance = Mathf.Abs(targetCamera.transform.position.z - transform.position.z);
            Vector3 screen = new Vector3(aim.x, aim.y, planeDistance);
            cursorWorld = targetCamera.ScreenToWorldPoint(screen);
        }
        cursorWorld.z = 0f;

        Vector2 origin = transform.position;
        Vector2 toCursor = (Vector2)cursorWorld - origin;
        if (toCursor.sqrMagnitude < 0.0001f) { hasTarget = false; return; }

        Vector2 direction = toCursor.normalized;
        RaycastHit2D hit = Physics2D.Raycast(origin, direction, miningRange, wallLayerMask);
        if (hit.collider == null) { hasTarget = false; return; }

        Vector3 pointInside = (Vector3)(hit.point + direction * 0.01f);
        Vector3Int candidate = tilemap.WorldToCell(pointInside);
        if (!tilemap.HasTile(candidate)) { hasTarget = false; return; }

        if (!IsCellOrthogonallyReachable(candidate))
        {
            hasTarget = false;
            return;
        }

        currentTargetCell = candidate;
        hasTarget = true;
    }

    /// <summary>
    /// BFS 4-connectivité à travers les cellules vides depuis la cellule du joueur.
    /// Retourne true si <paramref name="target"/> est une voisine orthogonale (N/S/E/O)
    /// de l'une de ces cellules vides, dans la limite de miningRange.
    /// Empêche les shots en diagonale à travers un angle de deux tuiles collées.
    /// </summary>
    private bool IsCellOrthogonallyReachable(Vector3Int target)
    {
        bfsVisited.Clear();
        bfsQueue.Clear();

        Vector3Int start = tilemap.WorldToCell(transform.position);
        if (start == target) return false;

        bfsVisited.Add(start);
        bfsQueue.Enqueue(start);

        Vector3 playerPos = transform.position;
        float rangeSq = miningRange * miningRange;
        // Garde-fou : la portée en cellules + 1 au carré, ×4 pour couvrir le fan-out du BFS.
        int maxVisits = Mathf.CeilToInt(miningRange + 1) * Mathf.CeilToInt(miningRange + 1) * 4;
        int visits = 0;

        Vector3Int[] dirs =
        {
            new Vector3Int( 1, 0, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int( 0, 1, 0),
            new Vector3Int( 0,-1, 0),
        };

        while (bfsQueue.Count > 0 && visits++ < maxVisits)
        {
            Vector3Int cell = bfsQueue.Dequeue();

            for (int i = 0; i < dirs.Length; i++)
            {
                Vector3Int next = cell + dirs[i];
                if (bfsVisited.Contains(next)) continue;

                // Clamp dans la portée (distance centre joueur → centre cellule).
                Vector3 centerNext = tilemap.GetCellCenterWorld(next);
                float dx = centerNext.x - playerPos.x;
                float dy = centerNext.y - playerPos.y;
                if (dx * dx + dy * dy > rangeSq) continue;

                bfsVisited.Add(next);

                if (next == target) return true;

                // On ne traverse QUE les cellules vides. Une cellule pleine est un
                // candidat de cible, mais n'est pas poursuivie plus loin par le BFS.
                if (!tilemap.HasTile(next)) bfsQueue.Enqueue(next);
            }
        }

        return false;
    }

    private void UpdateHoverCursor()
    {
        if (hoverCursor == null) return;

        if (!hasTarget || tilemap == null)
        {
            hoverCursor.enabled = false;
            return;
        }

        hoverCursor.enabled = true;
        Vector3 center = tilemap.GetCellCenterWorld(currentTargetCell);
        Vector2 cs = tilemap.cellSize;
        float hx = cs.x * 0.5f, hy = cs.y * 0.5f;
        float z = center.z - 0.1f; // juste devant la tuile

        hoverCursor.SetPosition(0, new Vector3(center.x - hx, center.y - hy, z));
        hoverCursor.SetPosition(1, new Vector3(center.x + hx, center.y - hy, z));
        hoverCursor.SetPosition(2, new Vector3(center.x + hx, center.y + hy, z));
        hoverCursor.SetPosition(3, new Vector3(center.x - hx, center.y + hy, z));
        hoverCursor.SetPosition(4, new Vector3(center.x - hx, center.y - hy, z));
    }

    private bool IsAimingWithGamepad(Vector2 aim)
    {
        var active = aimAction.activeControl;
        if (active != null && active.device is Gamepad)
            return aim.magnitude > gamepadDeadzone;
        return false;
    }

    private void OnMinePerformed(InputAction.CallbackContext _)
    {
        if (!hasTarget || tilemap == null) return;
        if (!tilemap.HasTile(currentTargetCell)) return;

        if (destructibleTerrain != null)
            destructibleTerrain.TryMine(currentTargetCell);
        else
            tilemap.SetTile(currentTargetCell, null);
    }

    private void OnDrawGizmos()
    {
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
