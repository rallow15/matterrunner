using UnityEngine;

namespace Runner
{
    /// <summary>
    /// Obstacle — attaché à la RACINE de chaque préfabriqué d'obstacle (Trou / Pic / Mur).
    ///
    /// Rôle :
    ///  - Porte le TYPE d'obstacle (les règles de collision en dépendent).
    ///  - Flag "passed" : la boule a déjà interagi avec cet obstacle.
    ///  - État "squashed" : le Métal l'a écrasé (scale.y 0.15, comme le web).
    ///
    /// Compatible Unity 2022+.
    /// </summary>
    public class Obstacle : MonoBehaviour
    {
        public ObstacleKind kind;   // enum partagé avec la courbe de difficulté

        [Header("=== Règles de collision (nouveau design) ===")]
        [Tooltip("Hauteur du sommet : la boule qui SAUTE passe au-dessus de cette hauteur")]
        public float topHeight = 1f;

        [Tooltip("Gelatine PERDUE au contact (la barre de vie du joueur)")]
        public float penalty = 0.3f;

        [Tooltip("Demi-largeur de la boîte de collision (axe X)")]
        public float halfWidth = 5f;

        [Tooltip("Demi-profondeur de la boîte de collision (axe Z)")]
        public float halfDepth = 1.5f;

        [Tooltip("Hauteur de pose au-dessus du sol (le trou : posé sur la piste)")]
        public float baseYOffset = 0f;

        // ---- État interne ----
        [HideInInspector] public bool passed;      // déjà traité par le joueur
        private Vector3 defaultScale;
        private const float SQUASH_SCALE_Y = 0.15f;

        private void Awake()
        {
            passed = false;
            defaultScale = transform.localScale;
            // Garde-fou : une racine de préfabriqué à échelle NULLE est invisible à l'écran
            // (l'échelle monde de tout l'enfant est écrasée à zéro). Origine des obstacles
            // « invisibles » rapportés : l'asset a été sauvé avec root (0,0,0). On s'en remet :
            // la racine ne porte aucune dimension réelle (les enfants les portent), donc
            // échelle nulle = toujours (1,1,1).
            if (defaultScale == Vector3.zero) defaultScale = Vector3.one;
        }

        /// <summary>Réinitialise l'obstacle à sa sortie du pool.</summary>
        public void Reset()
        {
            passed = false;
            transform.localScale = defaultScale;
            // On repose l'obstacle à SA hauteur de pose sur la piste.
            transform.position = new Vector3(transform.position.x, baseYOffset, transform.position.z);
        }

        /// <summary>Le Métal a détruit cet obstacle : écrase-le au sol (feedback Voodoo).
        /// Le pivot des préfabriqués est au sol : scale.y 0.15 écrase TOUT le groupe vers la piste.</summary>
        public void Squash()
        {
            Vector3 s = transform.localScale;
            s.y = SQUASH_SCALE_Y;
            transform.localScale = s;
            transform.position += Vector3.down * 0.05f;   // petit affaissement : posé, pas enterré
        }
    }

    /// <summary>Kind utilisé aussi par la courbe de difficulté (même enum, un seul endroit).</summary>
    [System.Serializable]
    public enum ObstacleKind { Hole, Spike, Wall }
}