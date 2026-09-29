using System;
using System.Collections.Generic;
using UnityEngine;

namespace Runner
{
    /// <summary>Définition d'un niveau (ligne de la courbe de difficulté).</summary>
    [Serializable]
    public class LevelDef
    {
        [Tooltip("Vitesse d'avancement de la boule sur ce niveau (unités/s)")]
        public float speed;

        [Tooltip("Obstacle dominant de ce niveau")]
        public ObstacleKind type;

        [Tooltip("Fréquence de spawn (0..1) — réduit l'écart entre obstacles")]
        [Range(0f, 1f)] public float freq;

        [Tooltip("Niveau piège : affiche ⚠️ PIÈGE sur le banner")]
        public bool trap;
    }
}