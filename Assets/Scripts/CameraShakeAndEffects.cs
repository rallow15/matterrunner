using UnityEngine;

/// <summary>
/// CameraShakeAndEffects — Le "Juice" signature Voodoo.
///
/// Rôle :
///  - ShakeCamera(duration, magnitude) : impact visuel fort quand la boule
///    passe en Métal ou détruit un obstacle (le "satisfying feedback").
///  - FOV dynamique : la Field of View augmente avec la vitesse
///    pour donner une sensation de vitesse grisante.
///  - Mathf.SmoothDamp partout : snappy mais JAMAIS saccadé.
///
/// À attacher sur : la Main Camera.
/// Compatible Unity 2022+.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraShakeAndEffects : MonoBehaviour
{
    [Header("=== Shake (impact) ===")]
    [Tooltip("Vitesse à laquelle la caméra revient à sa position d'origine")]
    public float shakeRecoverySpeed = 8f;

    [Tooltip("Décalage vertical léger du shake (plus naturel qu'un shake plat)")]
    public float shakeYBias = 0.6f;

    [Header("=== FOV dynamique (sensation de vitesse) ===")]
    [Tooltip("Référence au script du joueur (pour lire sa vitesse). Optionnel si la boule suit cameraController")]
    public MonoBehaviour playerSpeedSource;

    [Tooltip("Vitesse du joueur (en unités/s) transmise par le PlayerController via SetSpeed")]
    [SerializeField] private float playerSpeed;

    [Tooltip("FOV au repos")]
    public float baseFov = 60f;

    [Tooltip("FOV à pleine vitesse (sensation grisante)")]
    public float maxFov = 82f;

    [Tooltip("Vitesse du joueur à laquelle on atteint maxFov")]
    public float speedAtMaxFov = 25f;

    [Tooltip("Temps de lissage du FOV (SmoothDamp) — plus court = plus snappy")]
    public float fovSmoothTime = 0.35f;

    [Header("=== Suivi de la boule (comme le web) ===")]
    [Tooltip("La boule à suivre. Si null : la caméra reste à sa position de scène.")]
    public Transform followTarget;

    [Tooltip("Position de la caméra DERRIÈRE la boule (la boule avance en +Z : caméra à ball.z - 11.5)")]
    public Vector3 followOffset = new Vector3(0f, 5.6f, -11.5f);

    [Tooltip("Point visé DEVANT la boule (ball.z + 16) : on voit les obstacles arriver")]
    public Vector3 lookOffset = new Vector3(0f, 1f, 16f);

    // ---- État interne ----
    private Camera cam;
    private Vector3 originalLocalPosition;
    private Vector3 followBase;        // position de base chaque frame (suivi + shake)
    private float shakeTimer;          // temps de shake restant
    private float shakeDurationTotal;  // durée totale du shake en cours
    private float shakeMagnitude;
    private float fovVelocity;         // vitesse de SmoothDamp pour le FOV
    private Vector3 shakeVelocity;     // vitesse de SmoothDamp pour le shake
    private Vector3 shakeOffset;       // décalage actuel lissé

    /// <summary>Accès statique (utilisé par GameManager au restart).</summary>
    public static CameraShakeAndEffects Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        cam = GetComponent<Camera>();
        originalLocalPosition = transform.localPosition;
        cam.fieldOfView = baseFov;
    }

    /// <summary>
    /// À appeler depuis le PlayerController quand un événement mérite un impact :
    ///   - passage en mode Métal      => ShakeCamera(0.15f, 0.25f);
    ///   - destruction d'un obstacle  => ShakeCamera(0.25f, 0.45f);
    ///   - mort du joueur             => ShakeCamera(0.4f, 0.6f);
    /// </summary>
    public void ShakeCamera(float duration, float magnitude)
    {
        // On ne remplace un shake que si le nouveau est plus fort : pas de "double petit shake".
        if (duration >= shakeTimer && magnitude >= shakeMagnitude)
        {
            shakeDurationTotal = duration;
            shakeTimer = duration;
            shakeMagnitude = magnitude;
        }
    }

    /// <summary>
    /// Le PlayerController appelle ceci chaque frame avec sa vitesse actuelle
    /// (ou une fois par changement de vitesse). Exemple :
    ///     fx.SetSpeed(25f);
    /// </summary>
    public void SetSpeed(float speed)
    {
        playerSpeed = speed;
    }

    private void LateUpdate()
    {
        UpdateFollow();
        UpdateShake();
        UpdateFov();

        // Regard : légèrement devant la boule, au ras de la piste
        // (la boule avance en +Z : point visé = ball.z + 16).
        if (followTarget != null)
        {
            // La boule se déplace aussi en X : on vise un point entre elle et le centre
            // (×0.5 = la caméra pivote doucement, sans vertige, quand on glisse sur le côté).
            transform.LookAt(new Vector3(followTarget.position.x * 0.5f + lookOffset.x, lookOffset.y, followTarget.position.z + lookOffset.z));
        }
    }

    /// <summary>
    /// La caméra suit la boule en Z, DERRIÈRE elle (la boule avance en +Z :
    /// caméra à ball.z - 11.5, elle regarde devant vers ball.z + 16).
    /// X et Y restent fixes : travelling stable, la piste défile sous l'objectif.
    /// </summary>
    private void UpdateFollow()
    {
        if (followTarget == null)
        {
            followBase = originalLocalPosition;
            return;
        }
        // La caméra suit la boule EN X AUSSI (nouveau design : on glisse de gauche à droite),
        // mais amortie (×0.75 et clampée) : on voit la boule bouger, la piste reste lisible.
        followBase = new Vector3(followOffset.x + Mathf.Clamp(followTarget.position.x, -4.5f, 4.5f) * 0.75f, followOffset.y, followTarget.position.z + followOffset.z);
    }

    /// <summary>
    /// Shake en 3 temps : offset cible fort → décroissance → retour lissé à l'origine.
    /// On lisse l'OFFSET avec SmoothDamp, pas la position brute, pour éviter le jitter.
    /// </summary>
    private void UpdateShake()
    {
        if (shakeTimer > 0f)
        {
            shakeTimer -= Time.deltaTime;

            // Intensité en cloche : monte vite, redescend doucement (feel Voodoo).
            float progress = 1f - (shakeTimer / shakeDurationTotal);
            float envelope = Mathf.Sin(progress * Mathf.PI);
            Vector3 targetOffset = new Vector3(
                (Mathf.PerlinNoise(Time.time * 30f, 0f) - 0.5f) * 2f,
                (Mathf.PerlinNoise(0f, Time.time * 30f) - 0.5f) * 2f * shakeYBias,
                0f
            ) * (shakeMagnitude * envelope);

            // SmoothDamp : le décalage suit la cible sans à-coups.
            shakeOffset = Vector3.SmoothDamp(shakeOffset, targetOffset, ref shakeVelocity, 0.03f);
        }
        else
        {
            // Fin du shake : retour doux à la position d'origine.
            shakeOffset = Vector3.SmoothDamp(shakeOffset, Vector3.zero, ref shakeVelocity, 1f / shakeRecoverySpeed);
        }

        // Position = base (suivi de boule) + décalage de shake.
        transform.localPosition = followBase + shakeOffset;
    }

    /// <summary>
    /// FOV dynamique : plus le joueur va vite, plus l'angle s'ouvre.
    /// C'est LE trick Voodoo pour la sensation de vitesse grisante.
    /// </summary>
    private void UpdateFov()
    {
        float speedRatio = Mathf.Clamp01(playerSpeed / Mathf.Max(speedAtMaxFov, 0.01f));
        float targetFov = Mathf.Lerp(baseFov, maxFov, speedRatio);

        cam.fieldOfView = Mathf.SmoothDamp(
            cam.fieldOfView,
            targetFov,
            ref fovVelocity,
            fovSmoothTime
        );
    }

    /// <summary>Réinitialise tout (au restart du niveau / changement de niveau).</summary>
    public void ResetEffects()
    {
        shakeTimer = 0f;
        shakeMagnitude = 0f;
        shakeOffset = Vector3.zero;
        playerSpeed = 0f;
        transform.localPosition = originalLocalPosition;
        cam.fieldOfView = baseFov;
    }
}