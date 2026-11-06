using System;
using UnityEngine;

/// <summary>
/// Pilote un fond en bandes horizontales empilées verticalement.
/// Chaque Band définit un intervalle Y (yTop → yBottom), une couleur et devient un enfant SpriteRenderer.
/// Les bandes utilisent un unique Sprite + Material (URP 2D Sprite-Lit-Default) et sont teintées via SpriteRenderer.color.
/// Appelle Rebuild() depuis le ContextMenu (ou modifie les champs → OnValidate reconfigure en Editor).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class DepthBackground : MonoBehaviour
{
    [Serializable]
    public class Band
    {
        public string name = "Band";
        [Tooltip("Y monde du haut de la bande (inclus).")]
        public float yTop = 0f;
        [Tooltip("Y monde du bas de la bande (inclus). Doit être < yTop.")]
        public float yBottom = -10f;
        [Tooltip("Teinte de la bande (multipliée avec le sprite gradient).")]
        public Color color = Color.white;
    }

    [Header("Bands (de haut en bas)")]
    [SerializeField] private Band[] bands = Array.Empty<Band>();

    [Header("Rendu partagé")]
    [Tooltip("Sprite unique utilisé par toutes les bandes (gradient vertical conseillé).")]
    [SerializeField] private Sprite sharedSprite;

    [Tooltip("Material URP 2D Sprite-Lit-Default.")]
    [SerializeField] private Material sharedMaterial;

    [Tooltip("Largeur monde des bandes (en unités).")]
    [SerializeField] private float width = 50f;

    [Header("Sorting")]
    [SerializeField] private string sortingLayerName = "Default";

    [Tooltip("Sorting order de la 1re bande. Les bandes suivantes descendent de 1.")]
    [SerializeField] private int baseSortingOrder = -100;

    [Header("Auto-Fit au Terrain (runtime)")]
    [Tooltip("Si assigné : au Start, lit les bornes du TerrainGenerator pour ajuster la largeur et étirer les bandes Y afin de couvrir toute la grille. Laisse vide pour conserver les valeurs manuelles.")]
    [SerializeField] private TerrainGenerator fitToTerrain;

    [Tooltip("Marge horizontale ajoutée de chaque côté (unités monde). Compense les sprites non débordants.")]
    [SerializeField] private float horizontalMargin = 0f;

    [Tooltip("Marge verticale ajoutée en haut et en bas (unités monde).")]
    [SerializeField] private float verticalMargin = 0f;

    [Tooltip("Y 'de surface' dans les valeurs authored. Les bandes dont yBottom >= cette valeur (ex: le Ciel) gardent leur taille et sont posées au-dessus du bord supérieur du terrain. Les bandes en dessous sont étirées pour couvrir la profondeur générée.")]
    [SerializeField] private float authoredSurfaceY = 0f;

    private void Awake()
    {
        Apply();
    }

    private void Start()
    {
        // Après Awake de TerrainGenerator → le tilemap/grid est prêt. En Play uniquement
        // pour ne pas écraser les bandes authored en Edit mode.
        if (Application.isPlaying && fitToTerrain != null) FitToTerrain();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            if (!isActiveAndEnabled) return;
            Apply();
        };
    }
#endif

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
        Apply();
    }

    /// <summary>
    /// Ajuste la largeur, centre horizontalement et repositionne les bandes pour couvrir
    /// la grille du <see cref="fitToTerrain"/>. Les bandes au-dessus de <see cref="authoredSurfaceY"/>
    /// (ex: le Ciel) sont translatées au-dessus du bord supérieur du terrain sans étirement.
    /// Les bandes sous la surface sont remappées proportionnellement entre leur bande la plus
    /// profonde authored et <paramref name="authoredSurfaceY"/>, vers [terrain yMin, terrain yMax].
    /// </summary>
    [ContextMenu("Fit To Terrain")]
    public void FitToTerrain()
    {
        if (fitToTerrain == null || bands == null || bands.Length == 0) return;

        Bounds b = fitToTerrain.GetWorldBounds();

        // 1. Largeur = largeur monde de la grille (+ marge).
        width = b.size.x + horizontalMargin * 2f;

        // 2. Centrage horizontal (un offset arbitraire de l'origine ne doit pas désaxer le fond).
        Vector3 pos = transform.position;
        pos.x = b.center.x;
        transform.position = pos;

        // 3. Ligne de surface monde = STRICTEMENT le haut du terrain (pas de marge ici :
        //    sinon le haut de la bande Dirt remonte au-dessus des tuiles réelles et on voit
        //    du brun au-dessus du terrain). La verticalMargin ne sert qu'à prolonger la
        //    bande la plus profonde sous le sol du terrain.
        float worldSurfaceY = b.max.y;
        float worldBottomY = b.min.y - verticalMargin;

        // Bornes authored sous-surface : de la bande la plus profonde jusqu'à la surface.
        float authoredBottom = float.PositiveInfinity;
        for (int i = 0; i < bands.Length; i++)
            if (bands[i].yBottom < authoredBottom) authoredBottom = bands[i].yBottom;
        float authoredSpan = authoredSurfaceY - authoredBottom;

        for (int i = 0; i < bands.Length; i++)
        {
            bool aboveSurface = bands[i].yBottom >= authoredSurfaceY - 0.0001f;

            if (aboveSurface)
            {
                // Translation sans étirement : yBottom = worldSurfaceY, yTop conserve la hauteur authored.
                float height = bands[i].yTop - bands[i].yBottom;
                bands[i].yBottom = worldSurfaceY + (bands[i].yBottom - authoredSurfaceY);
                bands[i].yTop = bands[i].yBottom + height;
            }
            else if (authoredSpan > 0.0001f)
            {
                // Remap proportionnel [authoredBottom, authoredSurfaceY] → [worldBottomY, worldSurfaceY].
                float topT = (bands[i].yTop - authoredBottom) / authoredSpan;
                float botT = (bands[i].yBottom - authoredBottom) / authoredSpan;
                bands[i].yTop = Mathf.Lerp(worldBottomY, worldSurfaceY, topT);
                bands[i].yBottom = Mathf.Lerp(worldBottomY, worldSurfaceY, botT);
            }
        }

        Apply();
    }

    /// <summary>Crée/ajuste les enfants pour matcher la liste <see cref="bands"/>.</summary>
    public void Apply()
    {
        if (bands == null) return;

        while (transform.childCount < bands.Length)
        {
            var go = new GameObject("Band");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.AddComponent<SpriteRenderer>();
        }
        while (transform.childCount > bands.Length)
        {
            var extra = transform.GetChild(transform.childCount - 1).gameObject;
            if (Application.isPlaying) Destroy(extra);
            else DestroyImmediate(extra);
        }

        for (int i = 0; i < bands.Length; i++)
        {
            ConfigureBand(transform.GetChild(i), bands[i], i);
        }
    }

    private void ConfigureBand(Transform t, Band b, int index)
    {
        t.name = string.IsNullOrEmpty(b.name) ? $"Band_{index}" : b.name;

        float h = Mathf.Max(0.01f, b.yTop - b.yBottom);
        float cy = (b.yTop + b.yBottom) * 0.5f;
        t.localPosition = new Vector3(0f, cy, 0f);
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;

        var sr = t.GetComponent<SpriteRenderer>();
        if (sr == null) sr = t.gameObject.AddComponent<SpriteRenderer>();

        sr.sprite = sharedSprite;
        sr.sharedMaterial = sharedMaterial;
        sr.color = b.color;
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = new Vector2(width, h);
        if (!string.IsNullOrEmpty(sortingLayerName)) sr.sortingLayerName = sortingLayerName;
        sr.sortingOrder = baseSortingOrder - index;
    }
}
