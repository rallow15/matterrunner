using UnityEngine;

namespace Runner
{
    /// <summary>
    /// PlayerController — Boule de GÉLATINE (nouveau design "collecteur de gelatine").
    ///
    /// Rôle :
    ///  - Avance automatiquement sur l'axe Z (déplacement kinématique : transform).
    ///  - La TAILLE est la barre de vie : Size (1 → 3). Radius = baseRadius × Size.
    ///  - Ramasse les morceaux de gelatine sur la route → GROSSIT.
    ///  - Touche un obstacle → PERD de la gelatine (rétrécit). Trop petit = PERDU.
    ///  - Contrôle LATÉRAL (glisser ou A-D / flèches) + SAUT (tap court / Espace).
    ///    Les obstacles bas (mur, eau) se SAUTENT ; les pics doivent être ESQUIVÉS.
    ///  - Collisions par DISTANCE (comme le web, zéro collider) :
    ///    test x + z + hauteur (le dessous de la boule passe au-dessus → obstacle franchi).
    ///
    /// À attacher sur : la boule du joueur.
    /// Compatible Unity 2022+ / Unity 6.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        [Header("=== Vitesse (pilotée par le GameManager) ===")]
        [Tooltip("Vitesse d'avancement (reçue du niveau courant)")]
        public float speed = 10f;

        [Header("=== Taille (le cœur du jeu) ===")]
        [Tooltip("Taille de départ (échelle de la boule)")]
        public float startSize = 1f;

        [Tooltip("Taille maximum (on ne grossit pas à l'infini)")]
        public float maxSize = 3f;

        [Tooltip("En dessous de cette taille : PERDU")]
        public float minSize = 0.45f;

        [Tooltip("Gelatine gagnée par morceau ramassé")]
        public float growthPerBlob = 0.07f;

        [Tooltip("Rayon de la boule à taille 1")]
        public float baseRadius = 0.5f;

        [Header("=== Mouvement latéral ===")]
        [Tooltip("Amplitude de déplacement (x de -laneRange à +laneRange)")]
        public float laneRange = 4f;

        [Tooltip("Vitesse latérale (unités/s)")]
        public float lateralSpeed = 16f;

        [Header("=== Saut ===")]
        [Tooltip("Vitesse de saut (apex ≈ 1.7 : passe le mur et l'eau, PAS le pic)")]
        public float jumpVelocity = 9.5f;

        [Tooltip("Gravité (forte = saut court et nerveux, feel Voodoo)")]
        public float gravity = -24f;

        [Header("=== Matériau ===")]
        [Tooltip("Matériau visuel de la gelatine (transparent, molle)")]
        public Material gelatineMaterial;

        [Header("=== Effets (le Juice) ===")]
        [Tooltip("Le composant CameraShakeAndEffects posé sur la Main Camera")]
        public CameraShakeAndEffects fx;

        [Tooltip("Le pool de particules de la scène")]
        public ParticleBurst particles;

        [Header("=== Références ===")]
        [Tooltip("Le LevelSpawner de la scène (listes des obstacles / pickups actifs)")]
        public LevelSpawner spawner;

        // ---- Couleurs des particules de gelatine (comme le matériau cyan) ----
        private static readonly Color GelColor = new Color(0.33f, 0.85f, 1f);
        private static readonly Color PickupColor = new Color(0.5f, 1f, 0.85f);

        // ---- État interne ----
        /// <summary>LA barre de vie : 1 au départ, grossit en ramassant, rétrécit aux collisions.</summary>
        public float Size { get; private set; }
        public float Radius => baseRadius * Size;
        public float Speed { get => speed; set => speed = value; }

        private float velocityY;          // physique verticale custom
        private bool onGround;
        private float squash;             // écrasement à l'impact (1 → 0)
        private float stretch;            // petit pop à la collecte / au saut
        private float deadTimer;
        private GameManager gm;
        private float targetX;            // position latérale cible (lissée)

        // État du glisser (glisser = déplacement, tap court = saut)
        private bool pointerDown;
        private Vector2 lastPointerPos;
        private float dragTotalX;         // distance horizontale totale du drag (pixels)
        private float pointerDownTime;

        private void Awake()
        {
            Size = startSize;
            // NOTE : GameManager.Instance peut encore être null ICI (ordre des
            // Awake — la boule est créée avant le GameManager dans la hiérarchie).
            // Le champ sera (re)récupéré paresseusement dans EnsureGm().
            gm = GameManager.Instance;

            var mr = GetComponent<MeshRenderer>();
            if (mr != null && gelatineMaterial != null)
            {
                mr.sharedMaterial = gelatineMaterial;
            }
        }

        private void Start()
        {
            // Dans Start, tous les Awake du projet ont déjà tourné :
            // cette 2e tentative suffit dans presque tous les cas.
            if (gm == null) gm = GameManager.Instance;
            EnsureGm(true);

            if (fx == null) fx = FindAnyObjectByType<CameraShakeAndEffects>();
            if (particles == null) particles = FindAnyObjectByType<ParticleBurst>();
        }

        // ---- Anti-NullReferenceException : le joueur est créé AVANT le
        // GameManager, donc gm peut être stale/null selon l'ordre des Awake.
        private GameManager EnsureGm(bool force = false)
        {
            if (gm == null || force)
            {
                var inst = GameManager.Instance;
                if (inst != null) gm = inst;
            }
            return gm;
        }

        private void Update()
        {
            // Garde anti-recharge d'assemblies : un recompil en pleine partie remet
            // GameManager.Instance à null → NRE chaque frame sinon.
            var gmNow = EnsureGm();
            if (gmNow == null) return;   // le manager reviendra : on saute juste ce frame.
            switch (gmNow.State)
            {
                case GameManager.GameState.Playing:
                    ReadInput();
                    Advance();
                    UpdateVertical();
                    UpdateRoll();
                    UpdateSizeVisual();
                    ResolveObstacles();
                    CollectPickups();
                    break;

                case GameManager.GameState.Dead:
                    UpdateDeathAnimation();
                    break;

                case GameManager.GameState.Menu:
                    // Vitrine : la gelatine rebondit sur place (x = 0).
                    velocityY += gravity * Time.deltaTime;
                    Vector3 menuPos = transform.position;
                    menuPos.y += velocityY * Time.deltaTime;
                    if (menuPos.y <= Radius)
                    {
                        menuPos.y = Radius;
                        velocityY = 5f;
                        squash = 1f;
                    }
                    menuPos.x = 0f;
                    transform.position = menuPos;
                    UpdateSizeVisual();
                    break;
            }

            // Transmission de la vitesse à la caméra pour le FOV dynamique.
            if (fx != null) fx.SetSpeed(speed);
        }

        // ==================== ENTRÉES ====================

        /// <summary>
        /// Glisser = se déplacer à gauche/droite. Tap court = sauter.
        /// Clavier : A/D ou flèches pour bouger, Espace/↑/W pour sauter.
        /// </summary>
        private void ReadInput()
        {
            // 1) Clavier.
            float axis = BlobInput.KeyboardAxis();
            targetX += axis * lateralSpeed * Time.deltaTime;

            // 2) Pointeur : glisser pour bouger, tap court pour sauter.
            BlobInput.ReadPointer(out bool down, out Vector2 pos, out bool began, out bool released);

            if (began)
            {
                pointerDown = true;
                lastPointerPos = pos;
                dragTotalX = 0f;
                pointerDownTime = Time.time;
            }
            else if (pointerDown && down)
            {
                float dxPixels = pos.x - lastPointerPos.x;
                // Sensibilité : glisser sur la largeur d'écran ≈ 26 unités monde.
                targetX += dxPixels / Mathf.Max(Screen.width, 1) * 26f;
                dragTotalX += Mathf.Abs(dxPixels);
                lastPointerPos = pos;
            }

            if (pointerDown && (released || !down))
            {
                // Tap court (peu de déplacement, durée brève) = SAUT.
                if (dragTotalX < 14f && Time.time - pointerDownTime < 0.35f)
                {
                    Jump();
                }
                pointerDown = false;
            }

            // 3) Touche de saut (clavier).
            if (BlobInput.JumpKey()) Jump();

            // 4) Clamp + lissage : la boule glisse (jamais téléportée).
            targetX = Mathf.Clamp(targetX, -laneRange, laneRange);
            Vector3 p = transform.position;
            p.x = Mathf.MoveTowards(p.x, targetX, lateralSpeed * Time.deltaTime);
            transform.position = p;
        }

        private void Jump()
        {
            if (EnsureGm() == null || gm.State != GameManager.GameState.Playing) return;
            if (!onGround) return;

            velocityY = jumpVelocity;
            onGround = false;
            stretch = 1f;
            SoundFX.Play("switchGel");
            if (particles != null)
                particles.Burst(transform.position - Vector3.up * Radius * 0.7f, GelColor, 5, 1.4f);
        }

        // ==================== PHYSIQUE ====================

        /// <summary>Avancement automatique sur Z (kinématique).</summary>
        private void Advance()
        {
            transform.position += Vector3.forward * speed * Time.deltaTime;
        }

        /// <summary>Saut + gravité : apex ≈ 1.7 de haut (mur et eau passables, pic non).</summary>
        private void UpdateVertical()
        {
            velocityY += gravity * Time.deltaTime;
            transform.position += Vector3.up * velocityY * Time.deltaTime;

            if (transform.position.y <= Radius)
            {
                transform.position = new Vector3(transform.position.x, Radius, transform.position.z);
                if (!onGround && velocityY < -4f)
                {
                    squash = 1f;
                    SoundFX.Play("land");
                    if (particles != null)
                        particles.Burst(new Vector3(transform.position.x, 0.25f, transform.position.z), GelColor, 5, 1.8f);
                    if (fx != null) fx.ShakeCamera(0.06f, 0.12f);
                }
                velocityY = 0f;
                onGround = true;
            }
        }

        /// <summary>Roulement vers l'avant : la gelatine glisse-roule comme une boule molle.</summary>
        private void UpdateRoll()
        {
            transform.Rotate(Vector3.right, (speed * Time.deltaTime) / Mathf.Max(Radius, 0.2f) * 0.5f, Space.Self);
        }

        /// <summary>
        /// Squash & stretch COMPOSÉ avec la taille : la gelatine molle s'écrase
        /// à l'impact et s'étire en sautant — tout en gardant son volume Size.
        /// </summary>
        private void UpdateSizeVisual()
        {
            squash = Mathf.Max(squash - Time.deltaTime * 5f, 0f);
            stretch = Mathf.Max(stretch - Time.deltaTime * 5f, 0f);

            // Étirement en l'air selon la vitesse verticale.
            float airStretch = onGround ? 0f : Mathf.Clamp01(Mathf.Abs(velocityY) * 0.02f);

            float sy = Size * (1f - squash * 0.32f + airStretch * 0.18f + stretch * 0.15f);
            float sx = Size * (1f + squash * 0.26f - airStretch * 0.08f - stretch * 0.1f);
            transform.localScale = new Vector3(sx, sy, sx);
        }

        // ==================== COLLISIONS AVEC LES OBSTACLES ====================

        /// <summary>
        /// Résolution par DISTANCE (comme le web, zéro collider) :
        ///   - test x et z (boîte de collision de l'obstacle) ;
        ///   - si le DESSOUS de la boule passe au-dessus du sommet → obstacle FRANCHI en sautant ;
        ///   - sinon → on perd de la gelatine (pénalité selon l'obstacle).
        /// </summary>
        private void ResolveObstacles()
        {
            if (spawner == null) return;

            Vector3 p = transform.position;

            for (int i = spawner.ActiveObstacles.Count - 1; i >= 0; i--)
            {
                var o = spawner.ActiveObstacles[i];
                if (o == null || o.passed) continue;

                Vector3 oPos = o.transform.position;

                // Test Z puis X (boîte demi-largeur / demi-profondeur de l'obstacle).
                if (Mathf.Abs(oPos.z - p.z) > o.halfDepth + Radius * 0.8f) continue;
                if (Mathf.Abs(oPos.x - p.x) > o.halfWidth + Radius * 0.8f) continue;

                // Le dessous de la boule est au-dessus du sommet → FRANCHI en sautant.
                float ballBottom = p.y - Radius * 0.85f;
                if (ballBottom >= o.topHeight)
                {
                    o.passed = true;
                    continue;
                }

                Hit(o);
                var gmAfterHit = EnsureGm();
                if (gmAfterHit == null || gmAfterHit.State != GameManager.GameState.Playing) return;
            }
        }

        /// <summary>Contact avec un obstacle : on perd de la gelatine (rétrécit).</summary>
        private void Hit(Obstacle o)
        {
            o.passed = true;

            // On perd de la gelatine → on devient plus petit.
            Size = Mathf.Max(Size - o.penalty, 0.05f);
            squash = 1f;

            switch (o.kind)
            {
                case ObstacleKind.Hole:
                    SoundFX.Play("smash");
                    if (particles != null)
                        particles.Burst(new Vector3(transform.position.x, 0.4f, transform.position.z),
                                        new Color(0.1f, 0.1f, 0.14f), 10, 3f);
                    break;

                case ObstacleKind.Spike:
                    SoundFX.Play("smash");
                    if (particles != null)
                        particles.Burst(new Vector3(transform.position.x, 1f, transform.position.z),
                                        new Color(1f, 0.33f, 0.2f), 14, 3.5f);
                    break;

                case ObstacleKind.Wall:
                    SoundFX.Play("break");
                    if (particles != null)
                        particles.Burst(new Vector3(transform.position.x, 0.8f, transform.position.z),
                                        new Color(1f, 0.72f, 0.01f), 12, 3f);
                    break;
            }

            if (fx != null) fx.ShakeCamera(0.25f, 0.4f);
            if (EnsureGm() != null) gm.NotifyHit();

            // Trop petit : c'est la fin.
            if (Size <= minSize)
            {
                Die();
            }
        }

        // ==================== COLLECTE DE GELATINE ====================

        /// <summary>Ramasse les morceaux de gelatine sur la route → GROSSIT.</summary>
        private void CollectPickups()
        {
            if (spawner == null) return;
            if (EnsureGm() == null) return;   // le GameManager n'est (pas encore) là : on ignore ce frame.

            Vector3 p = transform.position;

            for (int i = spawner.ActivePickups.Count - 1; i >= 0; i--)
            {
                var pu = spawner.ActivePickups[i];
                if (pu == null) { spawner.ActivePickups.RemoveAt(i); continue; }

                Vector3 puPos = pu.transform.position;
                if (Mathf.Abs(puPos.z - p.z) < 1.1f + Radius * 0.5f
                    && Mathf.Abs(puPos.x - p.x) < 1.1f + Radius * 0.5f
                    && Mathf.Abs(puPos.y - p.y) < 1.2f + Radius * 0.5f)
                {
                    spawner.ReturnPickup(pu);
                    spawner.ActivePickups.RemoveAt(i);

                    // On GROSSIT : c'est la récompense principale du jeu.
                    Size = Mathf.Min(Size + growthPerBlob, maxSize);
                    squash = 0.6f;
                    SoundFX.Play("switchMet");
                    if (particles != null)
                        particles.Burst(puPos, PickupColor, 6, 2f);
                    gm.CollectBlob();
                }
            }
        }

        // ==================== MORT / RESET ====================

        /// <summary>Plus assez de gelatine : la boule se répand (splat) et c'est fini.</summary>
        private void Die()
        {
            deadTimer = 0f;
            velocityY = 0f;
            if (fx != null) fx.ShakeCamera(0.4f, 0.6f);
            SoundFX.Play("death");
            var gmDie = EnsureGm();
            if (gmDie != null) gmDie.OnPlayerDeath(GameManager.DeathReason.Impact);
        }

        /// <summary>Animation de mort : la gelatine s'écrase au sol en une flaque plate.</summary>
        private void UpdateDeathAnimation()
        {
            deadTimer += Time.deltaTime;

            velocityY += gravity * Time.deltaTime;
            Vector3 p = transform.position;
            p.y += velocityY * Time.deltaTime;
            if (p.y <= Radius * 0.4f)
            {
                p.y = Radius * 0.4f;
                velocityY = 0f;
            }
            transform.position = p;

            // Splat : s'aplatit et rétrécit vers 0.
            float k = Mathf.Clamp01(deadTimer / 0.8f);
            float s = Size * Mathf.Lerp(1f, 0.4f, k);
            float sy = s * 0.5f;
            float sx = s * 1.3f;
            transform.localScale = new Vector3(sx, sy, sx);
        }

        /// <summary>Remet la boule à zéro (appelé par GameManager.StartGame).</summary>
        public void ResetBall()
        {
            Size = startSize;
            transform.position = new Vector3(0f, baseRadius * startSize, 0f);
            transform.localScale = Vector3.one * startSize;
            transform.rotation = Quaternion.identity;
            targetX = 0f;
            velocityY = 0f;
            onGround = true;
            squash = 0f;
            stretch = 0f;
            deadTimer = 0f;
            pointerDown = false;
        }
    }
}