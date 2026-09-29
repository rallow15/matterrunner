using UnityEngine;
using UnityEngine.UI;

namespace Runner
{
    /// <summary>
    /// GameUI — Toute l'interface UGUI, construite 100% EN CODE (aucun prefab à monter).
    /// Design "collecteur de gelatine".
    ///
    /// Rôle :
    ///  - Score (haut), chip "NIVEAU X", compteur de GÉLATINE (bas) + BARRE DE TAILLE.
    ///  - Barre de taille : la barre de vie visuelle — elle se vide quand on touche
    ///    des obstacles, se remplit quand on ramasse (InversLerp minSize → maxSize).
    ///  - Banner "NIVEAU X" / "⚠️ TROP PETIT !" / popup "+X".
    ///  - Écrans BLOB RUNNER / PERDU (avec record + cause), flash rouge à la mort.
    ///  - Le tap lance / relance la partie.
    ///
    /// À attacher sur : un GameObject vide "GameUI" de la scène. C'est tout.
    /// Compatible Unity 2022+.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        [Header("=== Réglages ===")]
        [Tooltip("Décalage (en s) avant l'écran PERDU, le temps de voir le splat")]
        public float gameOverDelay = 0.9f;

        [Tooltip("Durée de vie d'un popup de bonus")]
        public float popupLife = 0.8f;

        // ---- Hiérarchie créée en code ----
        private Canvas canvas;
        private Text scoreText;
        private Text chipText;
        private Text matterText;
        private Image sizeBarBg;         // fond de la barre de taille
        private Image sizeBarFill;       // remplissage (largeur = ratio de taille)
        private Text sizeBarText;
        private Text bannerText;
        private CanvasGroup bannerGroup;
        private Text popupText;
        private CanvasGroup popupGroup;
        private GameObject startScreen;
        private Text startBestText;
        private GameObject overScreen;
        private Text overReasonText;
        private Text overScoreText;
        private Text overBestText;
        private Image flash;

        // ---- État interne ----
        private GameManager gm;
        private float bannerTimer;
        private float popupTimer;
        private float deathTime = -999f;
        private float matterPop;         // pop de scale du compteur de gelatine
        private bool overShown;

        private const float DEATH_FLASH_ALPHA = 0.35f;   // le web : #ff3b3b op 0.35
        private const float BAR_W = 460f;
        private const float BAR_H = 34f;

        private void Start()
        {
            gm = GameManager.Instance;
            BuildCanvas();

            // ---- Événements du GameManager → réactions UI ----
            gm.OnScoreChanged += OnScoreChanged;
            gm.OnLevelUp += OnLevelUp;
            gm.OnPopup += OnPopup;
            gm.OnCollect += OnCollect;
            gm.OnHit += OnHit;
            gm.OnDeath += OnDeath;
            gm.OnRestart += OnRestart;

            // État initial : menu affiché, meilleur score connu.
            startBestText.text = "RECORD : " + gm.Best;
            ShowStartScreen(true);
        }

        private void OnDestroy()
        {
            gm.OnScoreChanged -= OnScoreChanged;
            gm.OnLevelUp -= OnLevelUp;
            gm.OnPopup -= OnPopup;
            gm.OnCollect -= OnCollect;
            gm.OnHit -= OnHit;
            gm.OnDeath -= OnDeath;
            gm.OnRestart -= OnRestart;
        }

        // ==================== INPUT ====================

        private void Update()
        {
            // Le même tap lance / relance la partie (compatible les deux systèmes d'input).
            bool tapped = TapInput.WasPressed();

            if (tapped)
            {
                if (gm.State == GameManager.GameState.Menu)
                {
                    gm.StartGame();
                }
                else if (gm.State == GameManager.GameState.Dead && overShown)
                {
                    gm.StartGame();
                }
            }

            AnimatePopups();
            UpdateSizeBar();
        }

        // ==================== RÉACTIONS AUX ÉVÉNEMENTS ====================

        private void OnScoreChanged(int score)
        {
            scoreText.text = score.ToString();
            // Le compteur suit aussi le total de gelatine (score = distance + morceaux).
            matterText.text = "GÉLATINE : " + gm.Bonus;
        }

        private void OnCollect(int total)
        {
            matterText.text = "GÉLATINE : " + total;
            matterText.color = new Color(0.35f, 0.95f, 1f);
            matterPop = 1f;   // pop de scale (feel tactile Voodoo)
        }

        private void OnHit()
        {
            // Rouge bref : on vient de PERDRE de la gelatine.
            matterText.color = new Color(1f, 0.45f, 0.4f);
            matterPop = 1f;
        }

        private void OnLevelUp(int levelIndex)
        {
            var L = gm.Current;
            chipText.text = "NIVEAU " + (levelIndex + 1);

            // Banner : niveau normal, ou avertissement piège (le web : ⚠️ PIÈGE).
            bool trap = L.trap;
            bannerText.text = trap ? "⚠️ PIÈGE" : "NIVEAU " + (levelIndex + 1);
            bannerText.color = trap ? new Color(1f, 0.55f, 0.2f) : Color.white;
            bannerGroup.alpha = 1f;
            bannerTimer = 1.2f;
            bannerText.transform.localScale = Vector3.one * 1.4f;
        }

        private void OnPopup(string label)
        {
            popupText.text = label;
            popupGroup.alpha = 1f;
            popupTimer = popupLife;
            popupText.transform.localPosition = new Vector3(0f, 120f, 0f);   // repart du bas
        }

        private void OnDeath(GameManager.DeathReason reason)
        {
            deathTime = Time.time;
            flash.color = new Color(1f, 0.23f, 0.23f, DEATH_FLASH_ALPHA);   // flash rouge (le web : #ff3b3b)

            // EXPLICATION IMMÉDIATE (pendant les 0.9 s avant l'écran PERDU).
            bannerText.text = "⚠️ TROP PETIT ! TU AS PERDU TA GÉLATINE";
            bannerText.color = new Color(1f, 0.55f, 0.2f);
            bannerGroup.alpha = 1f;
            bannerTimer = 1.4f;
            bannerText.transform.localScale = Vector3.one * 1.4f;

            overShown = false;
        }

        private void OnRestart()
        {
            ShowStartScreen(false);
            ShowOverScreen(false);
            bannerGroup.alpha = 0f;
            popupGroup.alpha = 0f;
            chipText.text = "NIVEAU 1";
            matterText.text = "GÉLATINE : 0";
            matterText.color = new Color(0.35f, 0.95f, 1f);
        }

        private void ShowStartScreen(bool show)
        {
            startScreen.SetActive(show);
        }

        private void ShowOverScreen(bool show)
        {
            overScreen.SetActive(show);
            if (show)
            {
                // Rappel de la règle : le joueur sait quoi faire la prochaine fois.
                overReasonText.text = "CAUSE : PLUS ASSEZ DE GÉLATINE\n→ ramasse les morceaux, évite les obstacles";
                overReasonText.color = new Color(1f, 0.55f, 0.2f);
                overScoreText.text = "SCORE : " + gm.Score;
                overBestText.text = gm.Score >= gm.Best ? "NOUVEAU RECORD !" : "RECORD : " + gm.Best;
                overBestText.color = gm.Score >= gm.Best ? new Color(1f, 0.84f, 0.2f) : Color.white;
            }
        }

        // ==================== ANIMATIONS ====================

        private void AnimatePopups()
        {
            // Banner : scale 1.4 → 1 puis fondu.
            if (bannerTimer > 0f)
            {
                bannerTimer -= Time.deltaTime;
                bannerText.transform.localScale = Vector3.Lerp(bannerText.transform.localScale, Vector3.one, Time.deltaTime * 8f);
                if (bannerTimer <= 0f)
                {
                    bannerGroup.alpha = 0f;
                }
                else if (bannerTimer < 0.4f)
                {
                    bannerGroup.alpha = bannerTimer / 0.4f;
                }
            }

            // Popup "+X" : monte et disparaît.
            if (popupTimer > 0f)
            {
                popupTimer -= Time.deltaTime;
                var tr = popupText.transform;
                tr.localPosition += Vector3.up * 90f * Time.deltaTime;
                if (popupTimer < popupLife * 0.5f)
                    popupGroup.alpha = popupTimer / (popupLife * 0.5f);
                if (popupTimer <= 0f) popupGroup.alpha = 0f;
            }

            // Compteur de gelatine : pop de scale 1.35 → 1.
            if (matterPop > 0f)
            {
                matterPop = Mathf.Max(matterPop - Time.deltaTime * 5f, 0f);
                float s = 1f + matterPop * 0.35f;
                matterText.transform.localScale = new Vector3(s, s, 1f);
            }

            // Flash rouge : fondu vers transparent.
            if (flash.color.a > 0f)
            {
                var c = flash.color;
                c.a = Mathf.Max(c.a - Time.deltaTime * 0.8f, 0f);
                flash.color = c;
            }

            // Écran PERDU : apparaît après le délai (le web : 0.9 s).
            if (gm.State == GameManager.GameState.Dead && !overShown && Time.time - deathTime >= gameOverDelay)
            {
                overShown = true;
                ShowOverScreen(true);
            }
        }

        /// <summary>
        /// La BARRE DE TAILLE (chaque frame) : ratio = taille entre minSize et maxSize.
        /// Le remplissage glisse de gauche à droite comme une jauge de vie.
        /// </summary>
        private void UpdateSizeBar()
        {
            if (gm.player == null) return;

            float ratio = Mathf.InverseLerp(gm.player.minSize, gm.player.maxSize, gm.player.Size);
            ratio = Mathf.Clamp01(ratio);
            sizeBarFill.rectTransform.sizeDelta = new Vector2((BAR_W - 8f) * ratio, BAR_H - 8f);
            // Cyan quand on grossit, orange quand on est presque à sec.
            sizeBarFill.color = ratio < 0.3f ? new Color(1f, 0.5f, 0.2f) : new Color(0.35f, 0.95f, 1f);
            sizeBarText.text = "TAILLE " + Mathf.RoundToInt(ratio * 100f) + "%";
        }

        // ==================== CONSTRUCTION UGUI EN CODE ====================

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("GameUICanvas");
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // Référence portrait 720×1280 (mobile), mais la fenêtre editor est
            // LANDSCAPE : matcher 0.5 garde un canvas logique équilibré dans
            // les deux cas (l'ancien match=0 écrasait la hauteur → jauge au MILIEU).
            scaler.referenceResolution = new Vector2(720, 1280);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            canvasGo.transform.SetParent(transform, false);

            // ---- Score (haut centre, énorme, comme le web) ----
            scoreText = MakeText("SCORE", 64, FontStyle.Bold, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(400, 80));
            scoreText.alignment = TextAnchor.UpperCenter;

            // ---- Chip "NIVEAU X" (haut gauche) ----
            chipText = MakeText("NIVEAU 1", 26, FontStyle.Bold, new Vector2(0f, 1f), new Vector2(24f, -40f), new Vector2(220, 40));
            chipText.alignment = TextAnchor.MiddleLeft;

            // ---- Compteur de gelatine (bas centre, au-dessus de la barre) ----
            // BUG FIX anchors FRACTIONNELS : sous fenêtre landscape, l'ancien
            // pos y=180 en ancre pixel tombait au milieu de l'écran.
            matterText = MakeText("GÉLATINE : 0", 34, FontStyle.Bold, new Vector2(0.5f, 0.16f), new Vector2(0f, 0f), new Vector2(400, 60));
            matterText.alignment = TextAnchor.MiddleCenter;
            matterText.color = new Color(0.35f, 0.95f, 1f);

            // ---- Barre de taille (la jauge de gelatine, bas centre) ----
            var barGo = new GameObject("SizeBar");
            barGo.transform.SetParent(canvasGo.transform, false);
            var barRect = barGo.AddComponent<RectTransform>();
            barRect.anchorMin = barRect.anchorMax = new Vector2(0.5f, 0.098f);
            barRect.anchoredPosition = Vector2.zero;
            barRect.sizeDelta = new Vector2(BAR_W, BAR_H);
            sizeBarBg = barGo.AddComponent<Image>();
            sizeBarBg.color = new Color(1f, 1f, 1f, 0.35f);
            sizeBarBg.raycastTarget = false;

            // Remplissage : ancré à GAUCHE du fond, largeur pilotée chaque frame.
            var fillGo = new GameObject("SizeBarFill");
            fillGo.transform.SetParent(barGo.transform, false);
            var fillRect = fillGo.AddComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = new Vector2(4f, 0f);
            fillRect.sizeDelta = new Vector2(BAR_W - 8f, BAR_H - 8f);
            sizeBarFill = fillGo.AddComponent<Image>();
            sizeBarFill.color = new Color(0.35f, 0.95f, 1f);
            sizeBarFill.raycastTarget = false;

            sizeBarText = MakeText("TAILLE 100%", 20, FontStyle.Bold, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(BAR_W, BAR_H), barGo.transform);
            sizeBarText.alignment = TextAnchor.MiddleCenter;

            // ---- Banner (centre, NIVEAU X / ⚠️ TROP PETIT) ----
            var bannerGo = new GameObject("Banner");
            bannerGo.transform.SetParent(canvasGo.transform, false);
            bannerGroup = bannerGo.AddComponent<CanvasGroup>();
            bannerGroup.alpha = 0f;
            var bannerRect = bannerGo.AddComponent<RectTransform>();
            bannerRect.anchorMin = bannerRect.anchorMax = new Vector2(0.5f, 0.5f);
            bannerRect.anchoredPosition = new Vector2(0f, 60f);
            bannerRect.sizeDelta = new Vector2(600, 90);
            bannerText = MakeText("NIVEAU 1", 52, FontStyle.Bold, Vector2.zero, Vector2.zero, Vector2.zero, bannerGo.transform);
            bannerText.alignment = TextAnchor.MiddleCenter;
            bannerText.rectTransform.sizeDelta = new Vector2(600, 90);

            // ---- Popup de bonus ("+X") ----
            var popupGo = new GameObject("Popup");
            popupGo.transform.SetParent(canvasGo.transform, false);
            popupGroup = popupGo.AddComponent<CanvasGroup>();
            popupGroup.alpha = 0f;
            var popupRect = popupGo.AddComponent<RectTransform>();
            popupRect.anchorMin = popupRect.anchorMax = new Vector2(0.5f, 0.5f);
            popupRect.anchoredPosition = new Vector2(0f, 120f);
            popupRect.sizeDelta = new Vector2(500, 70);
            popupText = MakeText("+1", 44, FontStyle.Bold, Vector2.zero, Vector2.zero, Vector2.zero, popupGo.transform);
            popupText.alignment = TextAnchor.MiddleCenter;
            popupText.rectTransform.sizeDelta = new Vector2(500, 70);
            popupText.color = new Color(0.4f, 1f, 0.85f);

            // ---- Écran de démarrage ----
            startScreen = MakeScreen("StartScreen");
            MakeText("BLOB RUNNER", 58, FontStyle.Bold, new Vector2(0.5f, 0.68f), Vector2.zero, new Vector2(650, 100), startScreen.transform)
                .alignment = TextAnchor.MiddleCenter;
            MakeText("GLISSE : GAUCHE ↔ DROITE  •  TAP : SAUTER", 30, FontStyle.Normal, new Vector2(0.5f, 0.57f), Vector2.zero, new Vector2(650, 50), startScreen.transform)
                .alignment = TextAnchor.MiddleCenter;
            MakeText("RAMASSE LA GÉLATINE POUR GROSSIR", 30, FontStyle.Bold, new Vector2(0.5f, 0.47f), Vector2.zero, new Vector2(650, 50), startScreen.transform)
                .alignment = TextAnchor.MiddleCenter;
            var ruleText = MakeText("• GROS : tu écrases mieux les obstacles\n• TOUCHER un obstacle = tu perds de la gelatine", 24, FontStyle.Normal, new Vector2(0.5f, 0.38f), Vector2.zero, new Vector2(650, 70), startScreen.transform);
            ruleText.alignment = TextAnchor.MiddleCenter;
            ruleText.color = new Color(0.85f, 0.9f, 1f);
            startBestText = MakeText("RECORD : 0", 32, FontStyle.Bold, new Vector2(0.5f, 0.24f), Vector2.zero, new Vector2(650, 50), startScreen.transform);
            startBestText.alignment = TextAnchor.MiddleCenter;
            startBestText.color = new Color(1f, 0.84f, 0.2f);

            // ---- Écran de fin ----
            overScreen = MakeScreen("OverScreen");
            MakeText("PERDU", 72, FontStyle.Bold, new Vector2(0.5f, 0.62f), Vector2.zero, new Vector2(650, 120), overScreen.transform)
                .alignment = TextAnchor.MiddleCenter;
            // Cause de la mort + rappel de la règle (le joueur comprend son erreur).
            overReasonText = MakeText("CAUSE", 28, FontStyle.Bold, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(650, 90), overScreen.transform);
            overReasonText.alignment = TextAnchor.MiddleCenter;
            overReasonText.color = new Color(1f, 0.55f, 0.2f);
            overScoreText = MakeText("SCORE : 0", 44, FontStyle.Bold, new Vector2(0.5f, 0.38f), Vector2.zero, new Vector2(650, 60), overScreen.transform);
            overScoreText.alignment = TextAnchor.MiddleCenter;
            overBestText = MakeText("RECORD : 0", 32, FontStyle.Bold, new Vector2(0.5f, 0.27f), Vector2.zero, new Vector2(650, 50), overScreen.transform);
            overBestText.alignment = TextAnchor.MiddleCenter;
            overScreen.SetActive(false);

            // ---- Flash rouge (plein écran, invisible au départ) ----
            var flashGo = new GameObject("Flash");
            flashGo.transform.SetParent(canvasGo.transform, false);
            var fr = flashGo.AddComponent<RectTransform>();
            fr.anchorMin = Vector2.zero;
            fr.anchorMax = Vector2.one;
            fr.offsetMin = fr.offsetMax = Vector2.zero;
            flash = flashGo.AddComponent<Image>();
            flash.color = new Color(1f, 0.23f, 0.23f, 0f);
            flash.raycastTarget = false;
        }

        /// <summary>Helper : crée un Text UGUI standard (police intégrée de Unity).</summary>
        private Text MakeText(string label, int size, FontStyle style, Vector2 anchor, Vector2 pos, Vector2 sizeDelta, Transform parent = null)
        {
            var go = new GameObject("Text_" + label);
            go.transform.SetParent(parent != null ? parent : canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = pos;
            rect.sizeDelta = sizeDelta;

            var text = go.AddComponent<Text>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");   // police intégrée (2022+)
            text.fontSize = size;
            text.fontStyle = style;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Panneau plein écran semi-transparent (écrans start / over).</summary>
        private GameObject MakeScreen(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(canvas.transform, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.45f);
            img.raycastTarget = true;   // bloque les clics vers le jeu derrière
            return go;
        }
    }
}