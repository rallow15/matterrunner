using System.Collections.Generic;
using UnityEngine;

namespace Runner
{
    /// <summary>
    /// LevelSpawner — Générateur de niveau infini style Voodoo (Hyper-Casual).
    ///
    /// Rôle :
    ///  - Le joueur (une boule) avance automatiquement sur l'axe Z.
    ///  - Sol : segments générés devant la boule, recyclés derrière (object pooling STRICT).
    ///  - Obstacles : spawn piloté par la COURBE DE DIFFICULTÉ du GameManager
    ///    (écart = temps de réaction × fréquence du niveau, 30% d'aléatoire, niveaux-pièges).
    ///  - Décor latéral (poteaux, cubes, pyramides) coloré par la palette du niveau.
    ///  - TROUS PARTIELS dans la piste : PAS sur toute la largeur, esquivables
    ///    latéralement et/ou sautables (remplacent l'ancienne eau animée).
    ///
    /// À attacher sur : un GameObject vide "LevelSpawner" de la scène.
    /// Compatible Unity 2022+.
    /// </summary>
    public class LevelSpawner : MonoBehaviour
    {
        [Header("=== Distances (unités monde) ===")]
        [Tooltip("Distance devant la boule à laquelle on génère (le web : 110)")]
        public float spawnAhead = 110f;

        [Tooltip("Distance derrière la boule à laquelle on recycle")]
        public float destroyBehind = 18f;

        [Tooltip("Longueur d'un segment de sol")]
        public float segmentLength = 10f;

        [Tooltip("Largeur de la piste (pour le décor latéral)")]
        public float trackWidth = 10f;

        [Header("=== Préfabriqués ===")]
        [Tooltip("Préfabriqués de segments de sol (au moins 1 requis)")]
        public GameObject[] segmentPrefabs;

        [Tooltip("Préfabriqués d'obstacles — ordre attendu : [0]=Trou, [1]=Pic, [2]=Mur")]
        public GameObject[] obstaclePrefabs;

        [Tooltip("Préfabriqués de décor latéral — ordre attendu : [0]=poteau, [1]=cube, [2]=pyramide")]
        public GameObject[] sceneryPrefabs;

        [Header("=== Gelatine à ramasser (le cœur du nouveau design) ===")]
        [Tooltip("Préfabriqué du morceau de gelatine (petite sphère émissive cyan)")]
        public GameObject pickupPrefab;

        [Tooltip("Nombre de morceaux par groupe posé entre deux obstacles")]
        public int blobsPerGroupMin = 4;
        public int blobsPerGroupMax = 8;

        [Header("=== Décor latéral ===")]
        [Tooltip("Écart vertical de spawn du décor")]
        public float sceneryGapMin = 8f;
        public float sceneryGapMax = 16f;

        [Header("=== Textures générées à l'exécution ===")]
        [Tooltip("Applique automatiquement les textures procédurales (route / hazard) aux préfabriqués")]
        public bool applyGeneratedTextures = true;

        [Header("=== Références ===")]
        [Tooltip("La boule du joueur (pour connaître la position Z)")]
        public Transform ball;

        // ---- État interne ----
        private float nextSpawnZ;                // Z du prochain segment de sol
        private float nextObstacleZ;             // Z du prochain obstacle (courbe de difficulté)
        private float nextSceneryZ;

        /// <summary>Obstacles actuellement posés en scène (lu par PlayerController).</summary>
        public List<Obstacle> ActiveObstacles { get; } = new List<Obstacle>();

        /// <summary>Morceaux de gelatine actifs en scène (lu par PlayerController).</summary>
        public List<GameObject> ActivePickups { get; } = new List<GameObject>();

        /// <summary>Décor latéral actif avec son type (0=poteau, 1=cube, 2=pyramide), lu par GameVisuals.</summary>
        public List<(GameObject go, int typeIndex)> ActiveScenery { get; } = new List<(GameObject, int)>();

        // ==================== OBJECT POOLING ====================

        /// <summary>Un pool d'objets pour UN préfabriqué donné.</summary>
        private class ObjectPool
        {
            public readonly GameObject prefab;
            public readonly Transform parent;
            public readonly Queue<GameObject> inactive = new Queue<GameObject>();

            public ObjectPool(GameObject prefab, Transform spawner)
            {
                this.prefab = prefab;
                var parentGo = new GameObject("Pool_" + prefab.name);
                parent = parentGo.transform;
                parent.SetParent(spawner, false);
            }

            public GameObject Get()
            {
                GameObject go = inactive.Count > 0 ? inactive.Dequeue() : Object.Instantiate(prefab, parent);
                go.SetActive(true);
                return go;
            }

            public void Return(GameObject go)
            {
                go.SetActive(false);
                inactive.Enqueue(go);
            }
        }

        // Un pool par préfabriqué (clé = le préfabriqué lui-même).
        private readonly Dictionary<GameObject, ObjectPool> pools = new Dictionary<GameObject, ObjectPool>();

        // Segments actuellement posés en scène, du plus ancien au plus récent.
        private readonly Queue<SpawnedSegment> activeSegments = new Queue<SpawnedSegment>();

        private class SpawnedSegment
        {
            public GameObject floor;
            public float zEnd;
        }

        private GameManager gm;

        // Probe de diagnostic : log des premiers segments posés après un reset.
        private int probeLogged;
        private int obstacleProbeLogged;

        private void Start()
        {
            gm = GameManager.Instance;

            if (ball == null)
            {
                Debug.LogError("[LevelSpawner] Aucune boule assignée ! Glisse le Transform du joueur dans l'inspecteur.");
                enabled = false;
                return;
            }
            if (segmentPrefabs == null || segmentPrefabs.Length == 0)
            {
                Debug.LogError("[LevelSpawner] Aucun préfabriqué de segment assigné !");
                enabled = false;
                return;
            }
            if (obstaclePrefabs == null || obstaclePrefabs.Length < 3)
            {
                Debug.LogWarning("[LevelSpawner] Préfabriqués d'obstacles incomplets (attendu : Trou, Pic, Mur).");
            }
            if (pickupPrefab == null)
            {
                Debug.LogWarning("[LevelSpawner] pickupPrefab non assigné : pas de gelatine à ramasser.");
            }

            if (applyGeneratedTextures) ApplyGeneratedTextures();

            ResetLevel();
        }

        /// <summary>Colle les textures procédurales sur les matériaux des préfabriqués (zéro asset).
        /// Protégé : une erreur de texture ne doit JAMAIS empêcher le jeu de tourner.</summary>
        private void ApplyGeneratedTextures()
        {
            try
            {
                Texture2D road = RuntimeTextureGen.Road();
                Texture2D hazard = RuntimeTextureGen.Hazard();

                foreach (var prefab in segmentPrefabs)
                {
                    if (prefab == null) continue;
                    // Route : repeat (1, 10/8 = 1.25) comme le web (repeat.set(1, SEG_LEN/8)).
                    foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                        ApplyTexture(r.gameObject, road, new Vector2(1f, 1.25f), "RoadMat");
                }
                if (obstaclePrefabs != null && obstaclePrefabs.Length >= 3)
                {
                    // Pic et Mur = hazard (repeat (4, 0.5) comme le web) ; on ne cible QUE les
                    // enfants au matériau HazardMat : le cap du mur (sombre) et les liserés d'eau
                    // ne doivent pas recevoir la texture jaune.
                    ApplyTexture(obstaclePrefabs[1], hazard, new Vector2(4f, 0.5f), "HazardMat");
                    ApplyTexture(obstaclePrefabs[2], hazard, new Vector2(4f, 0.5f), "HazardMat");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[LevelSpawner] Textures procédurales non appliquées (non bloquant) : " + e.Message);
            }
        }

        private static void ApplyTexture(GameObject go, Texture2D tex, Vector2 tiling, string materialFilter)
        {
            if (go == null || tex == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r.sharedMaterial == null) continue;
                // Filtre par NOM de matériau : on ne texture que les surfaces prévues pour
                // (le cap sombre du mur, les cônes rouges du pic, etc. restent unis).
                if (!string.IsNullOrEmpty(materialFilter) && r.sharedMaterial.name != materialFilter) continue;
                // sharedMaterial (PAS .material) : le getter .material lève une
                // NullReferenceException sur un préfabriqué asset (hors scène).
                // Tous les objets du même type partagent ce matériau de toute façon.
                r.sharedMaterial.mainTexture = tex;
                r.sharedMaterial.mainTextureScale = tiling;
            }
        }

        private void Update()
        {
            // Lazy re-attach du GameManager : s'il avait été nul au Start (spawner créé
            // avant le manager, ou recompil pendant le play), les OBSTACLES et la
            // GELATINE ne spawnent JAMAIS (SpawnObstaclesAhead return tôt). On retente chaque frame.
            if (gm == null) gm = GameManager.Instance;
            float ballZ = ball.position.z;

            // 1) Générer le sol devant, jusqu'à la distance de spawn (le web : 110 m).
            while (nextSpawnZ < ballZ + spawnAhead)
            {
                SpawnFloorSegment();
            }

            // 2) Recycler derrière : le plus vieux segment d'abord
            while (activeSegments.Count > 0 &&
                   activeSegments.Peek().zEnd < ballZ - destroyBehind)
            {
                RecycleSegment(activeSegments.Dequeue());
            }

            // 3) Obstacles selon la courbe de difficulté
            SpawnObstaclesAhead(ballZ);

            // 4) Décor latéral
            SpawnScenery(ballZ);

            // 5) Sonde visibilité : toutes les 1 s, on projette le plus proche obstacle
            //    DEVANT la boule en coordonnées écran. Ça prouve (ou infirme) le rendu
            //    réel à l'écran : position monde, distance caméra, frustum, fog.
            visProbeTimer -= Time.deltaTime;
            if (visProbeTimer <= 0f)
            {
                visProbeTimer = 1f;
                VisProbe(ballZ);
            }

            // Capture de preuve DANS le moteur (ScreenCapture = vérité du rendu réel,
            // insensible au gel GDI/DWM des captures de fenêtre) : une seule fois
            // par partie, ~30 m après le départ pour avoir obstacles + route + UI.
            if (!screenProofDone && gm != null && gm.State == GameManager.GameState.Playing && ballZ > 30f)
            {
                screenProofDone = true;
                ScreenCapture.CaptureScreenshot("Assets/ScreenProof.png", 2);
                DoScreenProof(ballZ);
            }
        }

        private bool screenProofDone;

        /// <summary>Probe de vérité terrain du rendu : route proche, caméra, UI.</summary>
        private void DoScreenProof(float ballZ)
        {
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 cp = cam.transform.position;
            Vector3 cr = cam.transform.eulerAngles;
            Debug.Log($"[ScreenProof] CAM pos=({cp.x:F2},{cp.y:F2},{cp.z:F2}) rot=({cr.x:F1},{cr.y:F1},{cr.z:F1}) fov={cam.fieldOfView:F0} clear={cam.clearFlags}");

            // Les 4 segments de route les plus proches (devant ET derrière la boule).
            int n = 0;
            foreach (var s in activeSegments)
            {
                if (s.floor == null) continue;
                float sz = s.floor.transform.position.z;
                if (Mathf.Abs(sz - ballZ) > 25f) continue;
                var mr = s.floor.GetComponentInChildren<MeshRenderer>();
                var mf = s.floor.GetComponentInChildren<MeshFilter>();
                Vector3 vp = cam.WorldToViewportPoint(s.floor.transform.position);
                Debug.Log($"[ScreenProof] SEG z={sz:F1} pos=({s.floor.transform.position.x:F1},{s.floor.transform.position.y:F2},{s.floor.transform.position.z:F1}) " +
                          $"lossy=({s.floor.transform.lossyScale.x:F1},{s.floor.transform.lossyScale.y:F2},{s.floor.transform.lossyScale.z:F1}) " +
                          $"vp=({vp.x:F2},{vp.y:F2},{vp.z:F1}) vis={(mr != null && mr.enabled)} mesh={(mf == null || mf.sharedMesh == null ? "none" : mf.sharedMesh.name)}");
                if (++n >= 6) break;
            }

            // Les 3 obstacles les plus proches devant.
            n = 0;
            foreach (var o in ActiveObstacles)
            {
                if (o == null || o.transform.position.z < ballZ) continue;
                Vector3 vp = cam.WorldToViewportPoint(o.transform.position);
                Debug.Log($"[ScreenProof] OBS {o.kind} z={o.transform.position.z:F1} vp=({vp.x:F2},{vp.y:F2},{vp.z:F1}) dist={vp.z:F1}");
                if (++n >= 3) break;
            }

            // UI : le canvas existe-t-il et est-il rendu ?
            var canv = FindAnyObjectByType<UnityEngine.Canvas>();
            string camName = "none";
            if (canv != null && canv.worldCamera != null) camName = canv.worldCamera.name;
            string canvasInfo = (canv == null)
                ? "ABSENT"
                : $"present enabled={canv.enabled} goActive={canv.gameObject.activeInHierarchy} cams={camName}";
            Debug.Log($"[ScreenProof] CANVAS {canvasInfo}");
            Debug.Log("[ScreenProof] capture demandée -> Assets/ScreenProof.png");
        }

        private float visProbeTimer = 2f;

        private void VisProbe(float ballZ)
        {
            if (ActiveObstacles.Count == 0) return;

            var cam = Camera.main;
            if (cam == null) return;

            // Une sonde par KIND présent devant (max 3) : comparaison directe entre types.
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var o in ActiveObstacles)
            {
                if (o == null) continue;
                float oz = o.transform.position.z;
                if (oz < ballZ - 2f || oz > ballZ + 130f) continue;
                string k = o.kind.ToString();
                if (seen.Contains(k)) continue;
                seen.Add(k);

                var t = o.transform;
                Vector3 ls = t.localScale;
                Transform par = t.parent;
                Vector3 vp = cam.WorldToViewportPoint(t.position);

                // Chiffres séparés (x, y, z) — pas d'ambiguïté de format sur le Vector3.
                var mr = o.GetComponentInChildren<MeshRenderer>();
                string part = "noRenderer";
                if (mr != null)
                {
                    var mt = mr.transform;
                    Vector3 mls = mt.localScale, mlsy = mt.lossyScale;
                    Vector3 mvp = cam.WorldToViewportPoint(mt.position);
                    part = $"child={mt.name} lS=({mls.x:F2},{mls.y:F2},{mls.z:F2}) " +
                           $"lSy=({mlsy.x:F2},{mlsy.y:F2},{mlsy.y:F2}) " +
                           $"vp=({mvp.x:F2},{mvp.y:F2},{mvp.z:F1}) vis={mr.isVisible}";
                }
                Debug.Log($"[VisProbe2] {k} rootPos=({t.position.x:F1},{t.position.y:F1},{t.position.z:F1}) " +
                          $"rootLS=({ls.x:F2},{ls.y:F2},{ls.z:F2}) " +
                          $"parent={(par == null ? "world" : par.name + " pS=({}) ")} " +
                          $"rootVP=({vp.x:F2},{vp.y:F2},{vp.z:F1}) :: {part}");
            }

            // Diagnostic caméra + fog (une ligne seulement).
            Debug.Log($"[VisProbe2] ballZ={ballZ:F1} camZ={cam.transform.position.z:F1} " +
                      $"camF={cam.farClipPlane:F0} fog={RenderSettings.fog} mode={RenderSettings.fogMode} " +
                      $"range={RenderSettings.fogStartDistance}-{RenderSettings.fogEndDistance}");
        }

        // ==================== SOL ====================

        private void SpawnFloorSegment()
        {
            var segment = new SpawnedSegment { zEnd = nextSpawnZ + segmentLength };

            var floorPrefab = segmentPrefabs[Random.Range(0, segmentPrefabs.Length)];
            var floor = GetPool(floorPrefab).Get();
            // Cube de 0.5 de haut, pivot au centre : on descend d'une demi-hauteur
            // pour que le SOMMET du sol soit à y = 0 (où roule la boule).
            float halfHeight = floorPrefab.transform.localScale.y * 0.5f;
            floor.transform.position = new Vector3(0f, -halfHeight, nextSpawnZ + segmentLength * 0.5f);
            segment.floor = floor;
            activeSegments.Enqueue(segment);
            nextSpawnZ += segmentLength;

            // Probe : les 3 premiers segments d'un reset détaillent leur état de rendu
            // (position, renderer, matériau, mesh) → vérité terrain dans Editor.log.
            if (probeLogged < 3)
            {
                probeLogged++;
                var mr = floor.GetComponent<MeshRenderer>();
                Debug.Log($"[LevelSpawner] Segment #{probeLogged} : pos={floor.transform.position:0.0} scale={floor.transform.localScale} active={floor.activeSelf} " +
                          $"renderer={(mr == null ? "ABSENT" : (mr.enabled ? "on" : "OFF"))} " +
                          $"mat={(mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.name + " tex=" + (mr.sharedMaterial.mainTexture != null) : "NULL")} " +
                          $"mesh={(mr != null && mr.GetComponent<MeshFilter>() != null && mr.GetComponent<MeshFilter>().sharedMesh != null ? mr.GetComponent<MeshFilter>().sharedMesh.name : "NULL")}");
            }
        }

        private void RecycleSegment(SpawnedSegment segment)
        {
            if (segment.floor != null)
            {
                GetPool(FindPoolPrefab(segment.floor)).Return(segment.floor);
            }
        }

        // ==================== OBSTACLES (courbe de difficulté) ====================

        /// <summary>
        /// Spawn des obstacles piloté par la courbe — portage exact du web :
        ///   écart = temps de réaction (speed × 1.25) × squeeze (réduit par la fréquence),
        ///   minimum 7 m, plus un aléa de ±25% ; 30% des obstacles sont tirés au hasard.
        /// </summary>
        private void SpawnObstaclesAhead(float ballZ)
        {
            if (gm == null || obstaclePrefabs == null || obstaclePrefabs.Length < 3) return;

            LevelDef L = gm.Current;

            // La boule avance en +Z : on sème des obstacles DEVANT, jusqu'à spawnAhead.
            while (nextObstacleZ < ballZ + spawnAhead)
            {
                // 30% de chance (ou au-delà de la courbe) : type aléatoire.
                ObstacleKind kind = L.type;
                if (Random.value < 0.3f || gm.LevelIndex >= gm.levels.list.Count - 1)
                {
                    kind = (ObstacleKind)Random.Range(0, 3);
                }

                SpawnObstacle(kind, nextObstacleZ);

                // Écart minimal = temps de réaction, réduit par la fréquence du niveau.
                float react = L.speed * 1.25f;
                float squeeze = 1f - L.freq * 0.55f;
                float gap = Mathf.Max(react * squeeze, 7f) * Random.Range(0.95f, 1.45f);

                // 85% du temps : un groupe de gelatine DANS l'écart (récompense entre les pièges).
                if (pickupPrefab != null && Random.value < 0.85f)
                {
                    SpawnPickupGroup(nextObstacleZ + gap * 0.5f);
                }

                nextObstacleZ += gap;
            }

            // Recyclage des morceaux de gelatine passés DERRIÈRE la boule.
            for (int i = ActivePickups.Count - 1; i >= 0; i--)
            {
                var pu = ActivePickups[i];
                if (pu != null && pu.transform.position.z < ballZ - destroyBehind)
                {
                    ReturnPickup(pu);
                    ActivePickups.RemoveAt(i);
                }
            }

            // Recyclage des obstacles passés DERRIÈRE la boule.
            for (int i = ActiveObstacles.Count - 1; i >= 0; i--)
            {
                var o = ActiveObstacles[i];
                if (o.transform.position.z < ballZ - destroyBehind - 10f)
                {
                    ReturnObstacle(o);
                    ActiveObstacles.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Pose un GROUPE de morceaux de gelatine entre deux obstacles.
        /// 3 motifs (le "guidage visuel" Voodoo) :
        ///   0 = ligne droite sur une voie, 1 = diagonale (gauche → droite),
        ///   2 = courbe qui fait sauter (arcs au-dessus du sol).
        /// </summary>
        private void SpawnPickupGroup(float zMid)
        {
            int n = Random.Range(blobsPerGroupMin, blobsPerGroupMax + 1);
            int pattern = Random.Range(0, 3);
            float laneX = Random.Range(-3.5f, 3.5f);

            for (int i = 0; i < n; i++)
            {
                float t = n > 1 ? (float)i / (n - 1) : 0.5f;
                float x, y;

                switch (pattern)
                {
                    case 0:   // Ligne droite sur une voie
                        x = laneX;
                        y = 0.35f;
                        break;
                    case 1:   // Diagonale : pousse le joueur à glisser d'un côté
                        x = Mathf.Lerp(-3.5f, 3.5f, t);
                        y = 0.35f;
                        break;
                    default:  // Courbe montante : incite au saut
                        x = laneX + Mathf.Sin(t * Mathf.PI) * 2.5f;
                        y = 0.35f + Mathf.Sin(t * Mathf.PI) * 1.5f;
                        break;
                }

                var go = GetPool(pickupPrefab).Get();
                go.transform.position = new Vector3(x, y, zMid + (i - n * 0.5f) * 2.1f);
                ActivePickups.Add(go);
            }
        }

        /// <summary>Remet un morceau de gelatine dans son pool (ramassé ou recyclé).</summary>
        public void ReturnPickup(GameObject go)
        {
            if (go == null) return;
            var pool = GetPool(pickupPrefab);
            if (pool != null) pool.Return(go);
            else Destroy(go);
        }

        private void SpawnObstacle(ObstacleKind kind, float z)
        {
            int index = (int)kind;
            var prefab = obstaclePrefabs[index];
            var go = GetPool(prefab).Get();

            var ob = go.GetComponent<Obstacle>();
            if (ob == null) ob = go.AddComponent<Obstacle>();
            ob.kind = kind;
            SpawnObstacleProbe(kind, go);

            // PIC décentré au hasard : il ne se saute pas (trop haut, on l'ESQUIVE).
            // TROU PARTIEL : PAS sur toute la largeur — il reste de la route à côté,
            // donc il glisse aussi à gauche/droite dans l'axe du trou.
            // MUR : il occupe toute la piste, centré.
            float x = kind == ObstacleKind.Spike
                ? Random.Range(-2.5f, 2.5f)
                : kind == ObstacleKind.Hole ? Random.Range(-2.2f, 2.2f) : 0f;

            go.transform.position = new Vector3(x, 0f, z);
            ob.Reset();   // passed = false, scale d'origine

            ActiveObstacles.Add(ob);
        }

        // Probe : le premier obstacle posé après un reset détaille son état de rendu.
        private void SpawnObstacleProbe(ObstacleKind kind, GameObject go)
        {
            if (obstacleProbeLogged >= 2) return;
            obstacleProbeLogged++;
            var mr = go.GetComponentInChildren<MeshRenderer>();
            Debug.Log($"[LevelSpawner] Obstacle #{obstacleProbeLogged} ({kind}) : pos={go.transform.position:0.0} active={go.activeSelf} " +
                      $"renderer={(mr == null ? "ABSENT" : (mr.enabled ? "on" : "OFF"))} " +
                      $"mat={(mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.name + " tex=" + (mr.sharedMaterial.mainTexture != null) : "NULL")}");
        }

        private void ReturnObstacle(Obstacle ob)
        {
            // On retrouve le pool via le préfabriqué (nom du parent "Pool_xxx" → préfabriqué).
            foreach (var pool in pools.Values)
            {
                if (ob.gameObject.name == pool.prefab.name || ob.transform.parent == pool.parent)
                {
                    pool.Return(ob.gameObject);
                    return;
                }
            }
            Debug.LogWarning($"[LevelSpawner] Aucun pool trouvé pour {ob.name} — objet détruit.");
            Destroy(ob.gameObject);
        }

        // ==================== DÉCOR LATÉRAL ====================

        /// <summary>Poteaux / cubes / pyramides hors piste, colorés par la palette du niveau.</summary>
        private void SpawnScenery(float ballZ)
        {
            if (sceneryPrefabs == null || sceneryPrefabs.Length < 3) return;

            // Spawn devant (la boule avance en +Z, on sème jusqu'à spawnAhead).
            while (nextSceneryZ < ballZ + spawnAhead)
            {
                int n = Random.value < 0.4f ? 2 : 1;
                for (int k = 0; k < n; k++)
                {
                    int typeIndex = Random.Range(0, 3);
                    var go = GetPool(sceneryPrefabs[typeIndex]).Get();

                    float side = Random.value < 0.5f ? -1f : 1f;
                    float scale = Random.Range(0.7f, 1.8f);
                    // Échelle relative qui PRÉSERVE les proportions du préfabriqué
                    // (un poteau 1×5×1 reste un poteau, pas un cube).
                    go.transform.localScale = Vector3.Scale(sceneryPrefabs[typeIndex].transform.localScale, Vector3.one * scale);
                    go.transform.position = new Vector3(
                        side * Random.Range(trackWidth * 0.5f + 4f, trackWidth * 0.5f + 13f),
                        0f,
                        nextSceneryZ + Random.Range(-4f, 4f));

                    // Coloration par la palette du niveau courant (comme le web).
                    if (GameVisuals.Instance != null)
                    {
                        GameVisuals.Instance.ColorScenery(go, typeIndex);
                    }

                    ActiveScenery.Add((go, typeIndex));
                }
                nextSceneryZ += Random.Range(sceneryGapMin, sceneryGapMax);
            }

            // Recyclage derrière.
            for (int i = ActiveScenery.Count - 1; i >= 0; i--)
            {
                var s = ActiveScenery[i];
                if (s.go == null || s.go.transform.position.z < ballZ - destroyBehind - 10f)
                {
                    if (s.go != null)
                        GetPool(sceneryPrefabs[s.typeIndex]).Return(s.go);
                    ActiveScenery.RemoveAt(i);
                }
            }
        }

        // ==================== POOLS ====================

        private ObjectPool GetPool(GameObject prefab)
        {
            if (!pools.TryGetValue(prefab, out var pool))
            {
                pool = new ObjectPool(prefab, transform);
                pools[prefab] = pool;
            }
            return pool;
        }

        /// <summary>Retrouve le préfabriqué d'une instance en scène (via son parent "Pool_xxx").</summary>
        private GameObject FindPoolPrefab(GameObject instance)
        {
            foreach (var pool in pools.Values)
            {
                if (pool.parent == instance.transform.parent)
                {
                    return pool.prefab;
                }
            }
            Debug.LogWarning($"[LevelSpawner] Aucun pool trouvé pour {instance.name} — objet détruit.");
            Destroy(instance);
            return null;
        }

        /// <summary>
        /// Vide tout et regénère devant la boule (à appeler au restart du niveau).
        /// Exemple d'utilisation depuis un autre script :
        ///     GameManager.Instance.StartGame();   // appelle ResetLevel() lui-même
        /// </summary>
        public void ResetLevel()
        {
            screenProofDone = false;   // chaque nouvelle partie peut regénérer sa preuve
            // Sol
            while (activeSegments.Count > 0) RecycleSegment(activeSegments.Dequeue());

            // Obstacles
            for (int i = ActiveObstacles.Count - 1; i >= 0; i--) ReturnObstacle(ActiveObstacles[i]);
            ActiveObstacles.Clear();

            // Gelatine à ramasser
            for (int i = ActivePickups.Count - 1; i >= 0; i--) ReturnPickup(ActivePickups[i]);
            ActivePickups.Clear();

            // Décor
            for (int i = ActiveScenery.Count - 1; i >= 0; i--)
            {
                var s = ActiveScenery[i];
                if (s.go != null) GetPool(sceneryPrefabs[s.typeIndex]).Return(s.go);
            }
            ActiveScenery.Clear();

            // Curseurs de spawn : sol sous la boule, premier obstacle / décor devant (+Z).
            nextSpawnZ = ball.position.z - segmentLength;
            nextObstacleZ = ball.position.z + 35f;
            nextSceneryZ = ball.position.z + 20f;
            probeLogged = 0;
            obstacleProbeLogged = 0;

            // Pré-remplissage immédiat du sol devant la boule.
            while (nextSpawnZ < ball.position.z + spawnAhead)
            {
                SpawnFloorSegment();
            }

            Debug.Log($"[LevelSpawner] ResetLevel : {activeSegments.Count} segments posés, prochain z = {nextSpawnZ:0.0}");
        }
    }
}