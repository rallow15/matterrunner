using System;
using System.Collections.Generic;
using UnityEngine;

namespace Runner
{
    /// <summary>
    /// GameManager — Cerveau du jeu : états, niveaux, score, mort / restart.
    ///
    /// Rôle :
    ///  - Avance la progression (mètres) et change de niveau tous les LEVEL_LEN mètres.
    ///  - Score = distance + bonus de destructions (comme le web : +3 eau, +5 pic, +10 mur).
    ///  - Record sauvegardé via PlayerPrefs.
    ///  - Diffuse tout en ÉVÉNEMENTS : l'UI, les sons, les visuels s'abonnent —
    ///    zéro couplage entre les systèmes (pattern Voodoo : petits scripts indépendants).
    ///
    /// À attacher sur : un GameObject vide "GameManager" de la scène.
    /// Compatible Unity 2022+.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public enum GameState { Menu, Playing, Dead }
        public enum DeathReason { Drown, Impact }

        [Header("=== Courbe de difficulté (éditable dans l'inspecteur) ===")]
        public ListLevels levels;

        [Header("=== Références ===")]
        [Tooltip("La boule du joueur")]
        public PlayerController player;

        [Tooltip("Le LevelSpawner de la scène")]
        public LevelSpawner spawner;

        [Header("=== Progression ===")]
        [Tooltip("Longueur d'un niveau en mètres")]
        public float levelLength = 150f;

        // ---- État ----
        public static GameManager Instance { get; private set; }

        public GameState State { get; private set; } = GameState.Menu;
        public int LevelIndex { get; private set; }
        public float Meters { get; private set; }
        public int Bonus { get; private set; }
        public int Score => Mathf.FloorToInt(Meters) + Bonus;
        public int Best { get; private set; }

        /// <summary>Définition du niveau courant (clampée sur la liste).</summary>
        public LevelDef Current => levels.list[Mathf.Clamp(LevelIndex, 0, levels.list.Count - 1)];

        // ---- Événements ----
        public event Action<int> OnLevelUp;            // nouveau niveau (index 0-based)
        public event Action<int> OnScoreChanged;       // score entier
        public event Action<DeathReason> OnDeath;      // le joueur est mort
        public event Action OnRestart;                 // retour au jeu
        public event Action<int> OnCollect;            // un morceau de gelatine ramassé (total)
        public event Action OnHit;                     // un obstacle a touché la boule
        public event Action<string> OnPopup;           // label de bonus affiché en popup ("+10 BRIS !")

        private const string BEST_KEY = "runner_best";
        private float levelStartMeters;

        private void Awake()
        {
            // Singleton : on garde la première instance.
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (levels == null || levels.list == null || levels.list.Count == 0)
            {
                levels = new ListLevels();  // les 20 niveaux par défaut du prototype web
            }

            Best = PlayerPrefs.GetInt(BEST_KEY, 0);
        }

        private void Update()
        {
            switch (State)
            {
                case GameState.Playing:
                    Meters += player.Speed * Time.deltaTime;
                    OnScoreChanged?.Invoke(Score);

                    // Niveau suivant ?
                    if (Meters - levelStartMeters >= levelLength && LevelIndex < levels.list.Count - 1)
                    {
                        LevelIndex++;
                        levelStartMeters = Meters;
                        player.Speed = Current.speed;
                        OnLevelUp?.Invoke(LevelIndex);
                    }
                    break;

                case GameState.Dead:
                    // Rien à avancer ici : le délai avant l'écran "PERDU" est géré par la GameUI.
                    break;
            }
        }

        /// <summary>Démarre (ou redémarre) une partie depuis le niveau 1.</summary>
        public void StartGame()
        {
            // Reset de la scène via les systèmes existants.
            if (spawner != null) spawner.ResetLevel();
            if (CameraShakeAndEffects.Instance != null) CameraShakeAndEffects.Instance.ResetEffects();
            if (player != null) player.ResetBall();

            LevelIndex = 0;
            Meters = 0f;
            Bonus = 0;
            levelStartMeters = 0f;
            if (player != null) player.Speed = Current.speed;

            State = GameState.Playing;
            OnScoreChanged?.Invoke(Score);
            OnRestart?.Invoke();
        }

        /// <summary>Appelé par PlayerController quand un obstacle tue la boule.</summary>
        public void OnPlayerDeath(DeathReason reason)
        {
            if (State != GameState.Playing) return;
            State = GameState.Dead;

            bool isNewBest = Score > Best;
            if (isNewBest)
            {
                Best = Score;
                PlayerPrefs.SetInt(BEST_KEY, Best);
                PlayerPrefs.Save();
            }

            OnDeath?.Invoke(reason);
        }

        // ---- Points de passage pour les autres systèmes ----

        public void AddBonus(int points, string popupLabel = null)
        {
            Bonus += points;
            OnScoreChanged?.Invoke(Score);
            if (!string.IsNullOrEmpty(popupLabel)) OnPopup?.Invoke(popupLabel);
        }

        /// <summary>Appelé par PlayerController quand un morceau de gelatine est ramassé : +1 pt.</summary>
        public void CollectBlob()
        {
            Bonus += 1;
            OnScoreChanged?.Invoke(Score);
            OnCollect?.Invoke(Bonus);
        }

        /// <summary>Appelé par PlayerController quand un obstacle touche la boule (perte de gelatine).</summary>
        public void NotifyHit() => OnHit?.Invoke();
        public void NotifyMaterialSwitch(bool gelatine) { }
        public void SubscribeLevel(Action<int> cb) => OnLevelUp += cb;
        public void UnsubscribeLevel(Action<int> cb) => OnLevelUp -= cb;
    }

    /// <summary>
    /// Wrapper sérialisable pour la liste de niveaux (Unity ne sérialise pas
    /// les List&lt;T&gt; de champs de classes [Serializable] sans wrapper explicite en 2022).
    /// </summary>
    [Serializable]
    public class ListLevels
    {
        public List<LevelDef> list = new List<LevelDef>
        {
            // Copie exacte de LEVELS dans runner.html
            new LevelDef { speed = 10, type = ObstacleKind.Hole, freq = 0.20f },
            new LevelDef { speed = 10, type = ObstacleKind.Hole, freq = 0.25f },
            new LevelDef { speed = 11, type = ObstacleKind.Hole, freq = 0.30f },
            new LevelDef { speed = 12, type = ObstacleKind.Spike, freq = 0.40f },
            new LevelDef { speed = 13, type = ObstacleKind.Wall,  freq = 0.60f, trap = true },
            new LevelDef { speed = 13, type = ObstacleKind.Spike, freq = 0.45f },
            new LevelDef { speed = 14, type = ObstacleKind.Hole, freq = 0.50f },
            new LevelDef { speed = 15, type = ObstacleKind.Spike, freq = 0.55f },
            new LevelDef { speed = 16, type = ObstacleKind.Wall,  freq = 0.60f },
            new LevelDef { speed = 17, type = ObstacleKind.Spike, freq = 0.75f, trap = true },
            new LevelDef { speed = 17, type = ObstacleKind.Hole, freq = 0.60f },
            new LevelDef { speed = 18, type = ObstacleKind.Wall,  freq = 0.65f },
            new LevelDef { speed = 19, type = ObstacleKind.Spike, freq = 0.70f },
            new LevelDef { speed = 20, type = ObstacleKind.Wall,  freq = 0.70f },
            new LevelDef { speed = 20, type = ObstacleKind.Spike, freq = 0.85f, trap = true },
            new LevelDef { speed = 21, type = ObstacleKind.Hole, freq = 0.70f },
            new LevelDef { speed = 22, type = ObstacleKind.Spike, freq = 0.75f },
            new LevelDef { speed = 23, type = ObstacleKind.Wall,  freq = 0.80f },
            new LevelDef { speed = 24, type = ObstacleKind.Spike, freq = 0.85f },
            new LevelDef { speed = 25, type = ObstacleKind.Wall,  freq = 0.90f },
        };
    }
}