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

    private void Awake()
    {
        Apply();
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
