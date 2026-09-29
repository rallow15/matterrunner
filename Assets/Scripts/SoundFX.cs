using UnityEngine;

namespace Runner
{
    /// <summary>
    /// SoundFX — Un seul point d'entrée pour tous les sons du jeu.
    /// Portage des sons du prototype web (runner.html).
    ///
    /// Rôle :
    ///  - Clips assignés dans l'inspecteur (aucun code ne connaît les fichiers).
    ///  - SoundFX.Play("nom") partout dans le code : un seul AudioSource + PlayOneShot
    ///    (les sons peuvent se superposer sans se couper).
    ///  - Sons attendus : switchGel, switchMet, land, smash, splash, levelup, death, break.
    ///
    /// À attacher sur : un GameObject vide "SoundFX" de la scène (avec un AudioSource).
    /// Compatible Unity 2022+.
    /// </summary>
    public class SoundFX : MonoBehaviour
    {
        [Header("=== Clips (assignés dans l'inspecteur) ===")]
        public AudioClip switchGel;   // passage en Gélatine (pop mou)
        public AudioClip switchMet;   // passage en Métal (clank lourd)
        public AudioClip land;        // atterrissage de la boule
        public AudioClip smash;       // pic brisé
        public AudioClip splash;      // eau traversée
        public AudioClip levelup;     // nouveau niveau
        public AudioClip death;       // mort du joueur
        public AudioClip breakWall;   // mur brisé (le gros boom)

        public static SoundFX Instance { get; private set; }

        private AudioSource audioSource;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // AudioSource intégré si l'utilisateur en a posé un ou pas.
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
        }

        private void Start()
        {
            // Le son "levelup" est piloté par l'événement du GameManager (pas de couplage).
            GameManager.Instance.OnLevelUp += _ => Play("levelup");
        }

        /// <summary>
        /// Joue un son par son nom de système. Exemples du code :
        ///     SoundFX.Play("switchMet");  SoundFX.Play("breakWall");
        /// Silencieux si le clip n'est pas assigné (aucun crash, setup progressif).
        /// </summary>
        public static void Play(string kind)
        {
            if (Instance == null || Instance.audioSource == null) return;
            var clip = Instance.GetClip(kind);
            if (clip != null) Instance.audioSource.PlayOneShot(clip);
        }

        private AudioClip GetClip(string kind)
        {
            switch (kind)
            {
                case "switchGel": return switchGel;
                case "switchMet": return switchMet;
                case "land": return land;
                case "smash": return smash;
                case "splash": return splash;
                case "levelup": return levelup;
                case "death": return death;
                case "break":      // alias historique du web
                case "breakWall": return breakWall;
                default: return null;
            }
        }
    }
}