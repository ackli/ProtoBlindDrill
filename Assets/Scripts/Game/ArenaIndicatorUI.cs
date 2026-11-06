using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Boussole / radar d'arènes non déclenchées. S'attache à un GameObject enfant d'un
/// Canvas en Screen Space - Overlay. Chaque frame :
///   1. récupère l'arène non déclenchée la plus proche du joueur via <see cref="LevelObjectiveManager"/>,
///   2. convertit sa position monde en coordonnées écran,
///   3. clampe l'icône sur le bord de l'écran (avec padding) si l'arène est hors champ,
///      et oriente l'icône vers l'extérieur,
///   4. anime l'échelle de l'icône via une sinusoïde dont la vitesse augmente à l'approche.
/// </summary>
public class ArenaIndicatorUI : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("RectTransform de l'icône à déplacer et faire pulser. Peut être ce même GameObject.")]
    [SerializeField] private RectTransform indicatorRect;

    [Tooltip("Transform du joueur. Si null, résolu via GameObject.FindWithTag(\"Player\").")]
    [SerializeField] private Transform player;

    [Tooltip("Caméra utilisée pour WorldToScreenPoint. Si null, Camera.main au Awake.")]
    [SerializeField] private Camera targetCamera;

    [Header("Screen Edge")]
    [Tooltip("Marge intérieure en pixels depuis le bord de l'écran lorsque l'icône est clampée.")]
    [SerializeField] private float edgePadding = 50f;

    [Header("Pulse Speed")]
    [Tooltip("Vitesse angulaire du sin lorsque le joueur est loin.")]
    [SerializeField] private float minPulseSpeed = 2f;

    [Tooltip("Vitesse angulaire du sin lorsque le joueur est à distanceForMaxPulse ou moins.")]
    [SerializeField] private float maxPulseSpeed = 10f;

    [Tooltip("Distance monde au-dessous de laquelle la pulsation atteint son maximum.")]
    [SerializeField] private float distanceForMaxPulse = 10f;

    [Tooltip("Distance monde au-delà de laquelle la pulsation reste à son minimum.")]
    [SerializeField] private float distanceForMinPulse = 100f;

    [Tooltip("Amplitude de la pulsation (variation d'échelle autour de 1).")]
    [SerializeField] private float pulseAmplitude = 0.2f;

    [Header("Rotation")]
    [Tooltip("Oriente l'icône vers la cible lorsqu'elle est clampée au bord.")]
    [SerializeField] private bool rotateTowardTarget = true;

    [Tooltip("Décalage angulaire appliqué quand rotateTowardTarget est activé. -90° convient pour une icône dessinée pointant vers le haut.")]
    [SerializeField] private float iconForwardOffsetDeg = -90f;

    private Graphic indicatorGraphic;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (indicatorRect == null) indicatorRect = GetComponent<RectTransform>();
        if (indicatorRect != null) indicatorGraphic = indicatorRect.GetComponent<Graphic>();
        // On masque via le Graphic (Image) et pas le GameObject : désactiver le GameObject
        // qui porte ce script arrêterait Update() et l'icône ne reviendrait jamais.
        SetVisible(false);
    }

    private void Update()
    {
        if (indicatorRect == null) return;

        if (LevelObjectiveManager.Instance == null) { SetVisible(false); return; }

        if (player == null)
        {
            var go = GameObject.FindWithTag("Player");
            if (go != null) player = go.transform;
        }
        if (player == null) { SetVisible(false); return; }

        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) { SetVisible(false); return; }

        var target = LevelObjectiveManager.Instance.GetNearestUntriggeredArena(player.position);
        if (target == null) { SetVisible(false); return; }

        SetVisible(true);

        UpdatePosition(target.transform.position);
        UpdatePulse(target.transform.position);
    }

    private void SetVisible(bool visible)
    {
        if (indicatorGraphic != null && indicatorGraphic.enabled != visible)
            indicatorGraphic.enabled = visible;
    }

    /// <summary>
    /// Calcule la position écran de l'indicateur. Si la cible est hors champ (ou derrière
    /// la caméra), rayon depuis le centre de l'écran clampé sur le rectangle intérieur
    /// (Screen - 2*edgePadding). Compatible Canvas Screen Space - Overlay : on écrit
    /// <c>indicatorRect.position</c> directement en pixels écran.
    /// </summary>
    private void UpdatePosition(Vector3 targetWorld)
    {
        Vector3 screenPos = targetCamera.WorldToScreenPoint(targetWorld);
        Vector3 screenCenter = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);

        // Derrière la caméra : WorldToScreenPoint retourne un point miroité. On inverse pour
        // garantir que la direction depuis le centre pointe effectivement vers la cible.
        bool isBehind = screenPos.z < 0f;
        if (isBehind)
        {
            screenPos.x = Screen.width - screenPos.x;
            screenPos.y = Screen.height - screenPos.y;
        }
        screenPos.z = 0f;

        bool offScreen = isBehind
            || screenPos.x < edgePadding
            || screenPos.x > Screen.width - edgePadding
            || screenPos.y < edgePadding
            || screenPos.y > Screen.height - edgePadding;

        if (offScreen)
        {
            Vector3 dir = screenPos - screenCenter;

            float halfW = Mathf.Max(1f, Screen.width * 0.5f - edgePadding);
            float halfH = Mathf.Max(1f, Screen.height * 0.5f - edgePadding);

            float absX = Mathf.Abs(dir.x);
            float absY = Mathf.Abs(dir.y);

            // Scale uniform pour que le plus grand des deux composants atteigne EXACTEMENT
            // le bord du rectangle intérieur. L'autre composant se retrouve donc à l'intérieur.
            float maxRatio = Mathf.Max(absX / halfW, absY / halfH);
            if (maxRatio > 0f) dir /= maxRatio;

            screenPos = screenCenter + dir;

            if (rotateTowardTarget)
            {
                float angleDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                indicatorRect.rotation = Quaternion.Euler(0f, 0f, angleDeg + iconForwardOffsetDeg);
            }
        }
        else if (rotateTowardTarget)
        {
            indicatorRect.rotation = Quaternion.identity;
        }

        indicatorRect.position = screenPos;
    }

    private void UpdatePulse(Vector3 targetWorld)
    {
        float distance = Vector3.Distance(player.position, targetWorld);

        // InverseLerp(min, max, value) : si min > max (ce qui est le cas ici,
        // distanceForMinPulse > distanceForMaxPulse), Unity renvoie 0 pour value >= min
        // et 1 pour value <= max. On veut exactement ça : loin → t=0 → minPulseSpeed ;
        // près → t=1 → maxPulseSpeed.
        float t = Mathf.InverseLerp(distanceForMinPulse, distanceForMaxPulse, distance);
        float currentSpeed = Mathf.Lerp(minPulseSpeed, maxPulseSpeed, t);

        float scale = 1f + pulseAmplitude * Mathf.Sin(Time.time * currentSpeed);
        indicatorRect.localScale = new Vector3(scale, scale, 1f);
    }
}
