using UnityEngine;

/// <summary>
/// Orbe de lumière : au contact du joueur, soigne, ajoute au score, puis se détruit.
/// Attendu : un prefab avec Collider2D en isTrigger, un SpriteRenderer, et une Light2D Point.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class LightOrb : MonoBehaviour
{
    [Header("Reward")]
    [Tooltip("Points de vie restaurés au joueur à la collecte.")]
    [SerializeField] private int healAmount = 1;

    [Tooltip("Points de score ajoutés au ScoreManager à la collecte.")]
    [SerializeField] private int scoreValue = 10;

    [Header("Juice")]
    [Tooltip("Vitesse de rotation de l'orbe (deg/s) juste pour que ça respire.")]
    [SerializeField] private float spinSpeed = 60f;

    private void Awake()
    {
        var col = GetComponent<Collider2D>();
        if (!col.isTrigger) Debug.LogWarning("[LightOrb] Collider2D devrait être en isTrigger.", this);
    }

    private void Update()
    {
        if (spinSpeed != 0f)
            transform.Rotate(0f, 0f, spinSpeed * Time.deltaTime, Space.Self);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var hp = other.GetComponent<PlayerStats>();
        if (hp == null) hp = other.GetComponentInParent<PlayerStats>();
        if (hp == null) return;

        hp.Heal(healAmount);;
        if (ScoreManager.Instance != null) ScoreManager.Instance.AddLight(scoreValue);
        Destroy(gameObject);
    }
}
