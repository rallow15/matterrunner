using UnityEngine;

namespace Runner
{
    /// <summary>
    /// GameVisuals — Directeur artistique : palettes Voodoo par niveau, fog, rails émissifs.
    /// Portage exact de la boucle visuelle du prototype web (runner.html).
    ///
    /// Rôle :
    ///  - 6 palettes (ciel haut, brume basse, accent, décor) cyclées par niveau.
    ///  - ApplyLevel(i) : couleur de fond caméra, FOG (départ 40 / fin 100), lumière
    ///    ambiante, émissif des rails, teinte du décor latéral.
    ///  - Directional light qui SUIT la boule : ombres nettes autour du joueur
    ///    (le web repositionnait la lumière sur ball.z chaque frame).
    ///  - Rails blancs émissifs le long de la piste (2 boxes, recyclées en continu).
    ///
    /// À attacher sur : un GameObject vide "GameVisuals" de la scène.
    /// Compatible Unity 2022+ / URP.
    /// </summary>
    public class GameVisuals : MonoBehaviour
    {
        [System.Serializable]
        public class Palette
        {
            public Color top;       // couleur de ciel (fond caméra + lumière ambiante haute)
            public Color bot;       // couleur de brume basse (fog + lumière ambiante basse)
            public Color accent;    // accent (rails, cubes)
            public Color side;      // décor latéral (poteaux, pyramides)
        }

        [Header("=== Les 6 palettes (copie exacte de PALS du web) ===")]
        public Palette[] pals = new Palette[]
        {
            new Palette { top = Hex(0x5db9ff), bot = Hex(0xdff3ff), accent = Hex(0x38b6ff), side = Hex(0x2f7fd6) },
            new Palette { top = Hex(0x8f6bff), bot = Hex(0xefe4ff), accent = Hex(0x9b7bff), side = Hex(0x6a4fd6) },
            new Palette { top = Hex(0xff7b7b), bot = Hex(0xffe6e0), accent = Hex(0xff8a5c), side = Hex(0xd6573f) },
            new Palette { top = Hex(0x3ed69a), bot = Hex(0xe2fff1), accent = Hex(0x35d98f), side = Hex(0x1fa06b) },
            new Palette { top = Hex(0xffb84d), bot = Hex(0xfff2d9), accent = Hex(0xffc35c), side = Hex(0xe08a2b) },
            new Palette { top = Hex(0x5c5fff), bot = Hex(0xe4e6ff), accent = Hex(0x7d7bff), side = Hex(0x4a48d6) },
        };

        [Header("=== Fog (valeurs du web) ===")]
        public float fogStart = 40f;
        public float fogEnd = 100f;

        [Header("=== Rails ===")]
        [Tooltip("Largeur de la piste (rails à ±(largeur/2 + 1.2))")]
        public float trackWidth = 10f;

        public float railWidth = 0.4f;
        public float railHeight = 0.5f;

        [Header("=== Références ===")]
        [Tooltip("La Directional Light (elle suivra la boule)")]
        public Light sun;

        [Tooltip("La boule du joueur")]
        public Transform ball;

        // ---- État interne ----
        public static GameVisuals Instance { get; private set; }

        private Camera cam;
        private GameObject railLeft;
        private GameObject railRight;
        private Material railMaterial;
        private int currentPalette;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            cam = Camera.main;

            // FOG activé : profondeur de scène gratuite, comme le web (Fog(P.bot, 40, 100)).
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;

            // Lumière ambiante Trilight : ciel = haut de palette, sol = bas de palette.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;

            CreateRails();

            // Application immédiate de la palette du niveau 1.
            var gm = GameManager.Instance;
            ApplyLevel(gm.LevelIndex);

            // Abonnement : chaque niveau change la palette (comme le web).
            gm.OnLevelUp += ApplyLevel;
            gm.OnRestart += OnRestart;
        }

        private void OnRestart()
        {
            ApplyLevel(GameManager.Instance.LevelIndex);
        }

        private void OnDestroy()
        {
            if (GameManager.Instance == null) return;
            GameManager.Instance.OnLevelUp -= ApplyLevel;
            GameManager.Instance.OnRestart -= OnRestart;
        }

        /// <summary>Crée les 2 rails émissifs une seule fois (pas d'asset à importer).</summary>
        private void CreateRails()
        {
            float x = trackWidth * 0.5f + 1.2f;

            railLeft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            railLeft.name = "rail_left";
            Object.Destroy(railLeft.GetComponent<Collider>());
            railLeft.transform.localScale = new Vector3(railWidth, railHeight, 300f);
            railLeft.transform.SetParent(transform, false);

            railRight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            railRight.name = "rail_right";
            Object.Destroy(railRight.GetComponent<Collider>());
            railRight.transform.localScale = new Vector3(railWidth, railHeight, 300f);
            railRight.transform.SetParent(transform, false);

            // Matériau émissif : les rails GLOSENT le long de la piste (signature du web).
            railMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            railMaterial.EnableKeyword("_EMISSION");
            railMaterial.SetColor("_EmissionColor", Color.white);
            railMaterial.globalIlluminationFlags = UnityEngine.MaterialGlobalIlluminationFlags.None;

            railLeft.GetComponent<MeshRenderer>().material = railMaterial;
            railRight.GetComponent<MeshRenderer>().material = railMaterial;
        }

        private void Update()
        {
            // Rails : suivent la boule (infinis visuellement, position recalculée).
            float z = ball != null ? ball.position.z : 0f;
            if (railLeft != null) railLeft.transform.position = new Vector3(-trackWidth * 0.5f - 1.2f, railHeight * 0.5f, z);
            if (railRight != null) railRight.transform.position = new Vector3(trackWidth * 0.5f + 1.2f, railHeight * 0.5f, z);

            // Directional light : suit la boule pour des ombres nettes autour du joueur.
            if (sun != null && ball != null)
            {
                sun.transform.position = new Vector3(6f, 15f, ball.position.z + 4f);
                sun.transform.LookAt(new Vector3(0f, 0f, ball.position.z - 6f));
            }
        }

        // ==================== PALETTES ====================

        /// <summary>
        /// Applique la palette du niveau i (cyclée par 6, comme le web : PALS[level % 6]).
        /// Appelée par l'événement OnLevelUp du GameManager.
        /// </summary>
        public void ApplyLevel(int levelIndex)
        {
            currentPalette = levelIndex % pals.Length;
            var P = pals[currentPalette];

            // Fond caméra + fog : le haut du ciel se fond dans la brume (profondeur du web).
            if (cam != null) cam.backgroundColor = P.top;
            RenderSettings.fogColor = P.bot;

            // Ambiance : ciel haut = top, sol = bot (comme la HemisphereLight du web).
            RenderSettings.ambientSkyColor = P.top * 0.6f;
            RenderSettings.ambientGroundColor = P.bot;
            RenderSettings.ambientEquatorColor = Color.Lerp(P.top, P.bot, 0.5f);

            // Rails : émissif accent (ils changent de couleur à chaque niveau).
            if (railMaterial != null)
            {
                railMaterial.SetColor("_BaseColor", Color.white);
                railMaterial.SetColor("_EmissionColor", P.accent * 0.8f);
            }

            // Le décor déjà posé change aussi de couleur (comme le web, qui recolorait tout).
            var spawner = GameManager.Instance != null ? GameManager.Instance.spawner : null;
            if (spawner != null)
            {
                foreach (var (go, typeIndex) in spawner.ActiveScenery)
                {
                    if (go == null) continue;
                    Colorize(go, typeIndex == 1 ? P.accent : P.side);
                }
            }
        }

        /// <summary>Colore un objet de décor (pool du LevelSpawner). Un matériau par instance.</summary>
        public void ColorScenery(GameObject go, int typeIndex)
        {
            var P = pals[currentPalette];
            // type 0 = poteau (side), 1 = cube (accent), 2 = pyramide (side).
            Colorize(go, typeIndex == 1 ? P.accent : P.side);
        }

        /// <summary>Applique une couleur opaque + léger émissif au matériau de l'objet
        /// (instance : sans effet ailleurs). L'émissif rend le décor vivant, comme le web
        /// (matériau décor : color + emissive accent).</summary>
        private static void Colorize(GameObject go, Color color)
        {
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                var mat = r.material;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                else mat.color = color;

                // Émissif doux : le décor "flotte" visuellement dans la palette du niveau.
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor"))
                    mat.SetColor("_EmissionColor", color * 0.22f);
            }
        }

        /// <summary>Helper hex → Color (les palettes du web sont en hex).</summary>
        private static Color Hex(int rgb)
        {
            return new Color(
                ((rgb >> 16) & 0xFF) / 255f,
                ((rgb >> 8) & 0xFF) / 255f,
                (rgb & 0xFF) / 255f,
                1f);
        }
    }
}