using UnityEngine;

/// <summary>
/// Bloc "caméléon" : affiche son sprite révélé UNIQUEMENT quand le joueur a une
/// ligne de vue directe (aucun obstacle terrain interposé). Sinon, le SpriteRenderer
/// est désactivé et seule la tile Tilemap (sprite camouflage type Dirt) reste visible.
///
/// Attaché à un prefab instancié par <see cref="DestructibleTerrain.SpawnRelic"/> au
/// centre de chaque cellule UpgradeRelic. Le linecast part du joueur vers la relique :
/// si le premier hit du mask d'obstacles est à ≥ distance-epsilon de la cible, la vue
/// est directe. Tick à <see cref="scanInterval"/> pour éviter la charge par frame.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class RelicVisibility : MonoBehaviour
{
    [Header("Vision")]
    [Tooltip("Layers considérés comme obstacles (typiquement 'Default' avec la Tilemap). Si laissé à 0, hit tout.")]
    [SerializeField] private LayerMask obstacleMask = ~0;

    [Tooltip("Intervalle (s) entre deux scans de LOS. 0.2 par défaut : imperceptible et peu coûteux.")]
    [SerializeField] private float scanInterval = 0.2f;

    [Tooltip("Marge pour accepter que le hit tombe sur la cellule de la relique elle-même (la Tilemap y a une tile). Si hit.distance ≥ distToRelic - epsilon, on considère la vue dégagée.")]
    [SerializeField] private float hitEpsilon = 0.6f;

    [Header("Debug")]
    [Tooltip("Dessine le linecast dans la Scene View.")]
    [SerializeField] private bool debugDraw = false;

    private SpriteRenderer sr;
    private Transform player;
    private float nextScanTime;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    private void OnEnable()
    {
        // Masque au Start : tant qu'aucun scan n'a eu lieu, la relique reste cachée (sprite désactivé).
        if (sr != null) sr.enabled = false;
        TryBindPlayer();
    }

    private void TryBindPlayer()
    {
        var go = GameObject.FindGameObjectWithTag("Player");
        if (go != null) player = go.transform;
    }

    private void Update()
    {
        if (Time.time < nextScanTime) return;
        nextScanTime = Time.time + Mathf.Max(0.05f, scanInterval);

        if (player == null) { TryBindPlayer(); if (player == null) return; }

        Vector2 origin = player.position;
        Vector2 target = transform.position;
        var hit = Physics2D.Linecast(origin, target, obstacleMask);

        bool clear;
        if (hit.collider == null)
            clear = true;
        else
        {
            float distToHit = Vector2.Distance(origin, hit.point);
            float distToRelic = Vector2.Distance(origin, target);
            // Si le premier hit tombe sur (ou très proche de) la cellule de la relique, la vue est dégagée.
            clear = distToHit >= distToRelic - hitEpsilon;
        }

        if (sr != null && sr.enabled != clear) sr.enabled = clear;

        if (debugDraw)
            Debug.DrawLine(origin, target, clear ? Color.yellow : Color.gray, scanInterval);
    }
}
