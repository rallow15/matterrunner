using UnityEngine;

namespace Runner
{
    /// <summary>
    /// RuntimeTextureGen — Génère à l'EXÉCUTION les textures du jeu (zéro asset à importer).
    ///
    /// Portage exact des textures canvas du prototype web :
    ///  - Road()   : asphalte sombre bruité + pointillé central + bordures jaunes (256²)
    ///  - Hazard() : bandes jaune / noir en diagonale, pour murs et socles de pics (128²)
    ///
    /// Usage : les préfabriqués (segments de sol, murs, socles de pics) appellent
    ///     mat.mainTexture = RuntimeTextureGen.Road();
    /// depuis un petit script d'init, OU le SetupGuide indique de poser ces textures
    /// sur les matériaux du projet. Appelé une seule fois (cache statique).
    ///
    /// Compatible Unity 2022+ (URP : à utiliser dans un matériau URP/Lit).
    /// </summary>
    public static class RuntimeTextureGen
    {
        private static Texture2D road;
        private static Texture2D hazard;

        /// <summary>Texture de route : asphalte + pointillé + bordures jaunes. Répétable verticalement.</summary>
        public static Texture2D Road()
        {
            if (road != null) return road;

            const int S = 256;
            road = NewTexture(S, S);
            Color32[] px = new Color32[S * S];

            // Asphalte sombre (#252a33) + grain
            Fill(px, S, new Color32(0x25, 0x2a, 0x33, 255));
            System.Random rng = new System.Random(42);   // seed fixe : texture déterministe
            for (int i = 0; i < 900; i++)
            {
                // Grain clair sur asphalte sombre : 243..254 (BUG FIX : l'ancien
                // `(byte)(rng.Next(0,13)+255)` tronquait modulo 256 → grain QUASI NOIR).
                byte v = (byte)(rng.Next(243, 256));
                Set(px, S, rng.Next(S), rng.Next(S), v, v, v, 255);
            }

            // Pointillé central (4 segments, comme le web)
            for (int y = 0; y < S; y += S / 4)
            {
                for (int dy = 0; dy < S / 8; dy++)
                for (int dx = 0; dx < 6; dx++)
                    Set(px, S, S / 2 - 3 + dx, y + dy, 191, 191, 191, 255);
            }

            // Bordures jaunes pleines (#ffd34d)
            for (int y = 0; y < S; y++)
            {
                for (int dx = 0; dx < 8; dx++)
                {
                    Set(px, S, dx, y, 0xff, 0xd3, 0x4d, 255);
                    Set(px, S, S - 1 - dx, y, 0xff, 0xd3, 0x4d, 255);
                }
            }

            road.SetPixels32(px);
            road.Apply();
            return road;
        }

        /// <summary>Texture hazard : bandes jaune/noir diagonales (murs, socles de pics).</summary>
        public static Texture2D Hazard()
        {
            if (hazard != null) return hazard;

            const int S = 128;
            hazard = NewTexture(S, S);
            Color32[] px = new Color32[S * S];

            // Fond jaune (#ffc21d)
            Fill(px, S, new Color32(0xff, 0xc2, 0x1d, 255));

            // Bandes noires (#1c1c1c) inclinées à 45°, largeur 24 pour un pas de 48 (comme le web)
            Color32 black = new Color32(0x1c, 0x1c, 0x1c, 255);
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                // Coordonnées pivotées de -45° autour du centre (équivalent du save/rotate du canvas).
                int cx = x - S / 2, cy = y - S / 2;
                float c = 0.7071f;
                int rx = Mathf.RoundToInt(cx * c + cy * c) + S;   // translate(-w,-h) simplifié
                int ry = Mathf.RoundToInt(-cx * c + cy * c) - S;
                // Bandes de largeur 24 tous les 48 px sur l'axe pivoté
                if (((rx + ry) % 48 + 48) % 48 < 24)
                    px[y * S + x] = black;
            }

            hazard.SetPixels32(px);
            hazard.Apply();
            return hazard;
        }

        // ==================== Helpers canvas-like ====================

        private static Texture2D NewTexture(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Repeat;
            t.filterMode = FilterMode.Bilinear;
            return t;
        }

        private static void Fill(Color32[] px, int size, Color32 c)
        {
            for (int i = 0; i < px.Length; i++) px[i] = c;
        }

        private static void Set(Color32[] px, int size, int x, int y, byte r, byte g, byte b, byte a)
        {
            if (x < 0 || x >= size || y < 0 || y >= size) return;
            px[y * size + x] = new Color32(r, g, b, a);
        }
    }
}