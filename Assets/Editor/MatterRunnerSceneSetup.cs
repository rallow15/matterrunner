using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Runner;

/// <summary>
/// MatterRunnerSceneSetup — construit TOUTE la scène en un seul clic (ou TOUT SEUL).
///
/// Menu : MatterRunner → Monter la scène complète
///
/// Ce script fait automatiquement ce que le guide demandait de faire à la main :
///   - crée les 7 préfabriqués (Segment, Trou, Pic, Mur, Poteau, Cube, Cône),
///     les obstacles en GROUPES multi-parties fidèles au prototype web
///     (eau + 2 liserés lumineux, pic = base + 5 cônes + 5 pointes, mur + cap sombre),
///   - crée les matériaux (boule émissive, asphalte, hazard, rails lumineux, cap sombre),
///   - monte la hiérarchie (Boule, Caméra DERRIÈRE la boule, Lumière, GameManager,
///     LevelSpawner, Particles, SoundFX, GameVisuals, GameUI),
///   - câble TOUS les inspecteurs (aucune glisser-déposer à faire),
///   - sauvegarde la scène dans Assets/Scenes/MatterRunner.unity,
///   - règle Active Input Handling sur "Both" (requis par Input.GetMouseButtonDown).
///
/// VERSIONNEMENT : SCENE_VERSION. Si on l'incrémente, la scène se RECONSTRUIT
/// toute seule à la prochaine recompilation (l'utilisateur n'a rien à faire).
///
/// Compatible Unity 6 / URP.
/// </summary>
public static class MatterRunnerSceneSetup
{
    private const string MatFolder = "Assets/Materials";
    private const string PrefabFolder = "Assets/Prefabs";
    private const string MeshFolder = "Assets/Models";
    private const string ScenePath = "Assets/Scenes/MatterRunner.unity";

    /// <summary>Incrémente cette valeur à chaque changement visuel majeur :
    /// la scène se remonte AUTOMATIQUEMENT à la recompilation suivante.</summary>
    // v5 : texture route/hazard générées en ASSETS persistants au BuildScene
    // (la route ne dépend plus de ApplyTexture runtime) + fixes UI (jauge en bas).
    private const int SCENE_VERSION = 6;  // rebuild auto (v6 : eau remplacée par des TROUS partiels)

    private const string VersionKey = "MatterRunner_SceneVersion";

    /// <summary>
    /// Lancement AUTOMATIQUE après chaque recompilation : on (re)monte la scène
    /// si elle n'existe pas, ou si sa version est périmée.
    /// </summary>
    [InitializeOnLoadMethod]
    private static void AutoBuildOnce()
    {
        EditorApplication.delayCall += AttemptBuild;
    }

    /// <summary>
    /// Auto-corrigé : si le jeu TOURNE au moment du check, on ne saute plus
    /// silencieusement le rebuild — on COUPE le play nous-mêmes, puis on
    /// rebâtit la scène dès que le play est terminé (le problème v6 : une
    /// recompilation en pleine partie faisait fuir le rebuild à jamais).
    /// </summary>
    private static void AttemptBuild()
    {
        if (System.IO.File.Exists(ScenePath) && EditorPrefs.GetInt(VersionKey, 0) >= SCENE_VERSION) return;

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.isPlaying = false;          // on arrête la partie
            EditorApplication.update += BuildOnceStopped; // et on rebâtit juste après
            return;
        }
        DoRebuild();
    }

    private static void BuildOnceStopped()
    {
        // On attend que le play soit VRAIMENT terminé avant de toucher à la scène.
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
        EditorApplication.update -= BuildOnceStopped;
        if (System.IO.File.Exists(ScenePath) && EditorPrefs.GetInt(VersionKey, 0) >= SCENE_VERSION) return;
        DoRebuild();
    }

    private static void DoRebuild()
    {
        bool rebuilt = System.IO.File.Exists(ScenePath);   // rebuild d'une scène existante
        BuildScene();
        // On ouvre la scène tout de suite : l'utilisateur voit le jeu directement.
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Debug.Log(rebuilt
            ? $"[MatterRunner] Scène RECONSTRUITE en v{SCENE_VERSION} (eau → trous partiels) — appuie sur PLAY."
            : "[MatterRunner] La scène a été montée AUTOMATIQUEMENT — appuie simplement sur PLAY.");
    }

    [MenuItem("MatterRunner/Monter la scène complète")]
    public static void BuildScene()
    {
        // ---------- Dossiers ----------
        EnsureFolder("Assets", "Materials");
        EnsureFolder("Assets", "Prefabs");
        EnsureFolder("Assets", "Models");
        EnsureFolder("Assets", "Scenes");

        // ---------- 1. Matériaux ----------
        // Boule GÉLATINE : cyan translucide + émissif doux (le web : 0x54d8ff, emissive 0x1d86c9 ×0.3).
        var gelatine = MakeLit($"{MatFolder}/GelatineMat.mat",
            new Color(0.33f, 0.85f, 1f, 0.9f), transparent: true, smoothness: 0.85f,
            emission: new Color(0.11f, 0.53f, 0.79f) * 0.35f);
        // Boule MÉTAL : chrome froid + émissif faible (le web : 0xc3cad4, emissive 0x3a3f46 ×0.25).
        var metal = MakeLit($"{MatFolder}/MetalMat.mat",
            new Color(0.765f, 0.792f, 0.831f), metallic: 0.95f, smoothness: 0.78f,
            emission: new Color(0.23f, 0.25f, 0.27f) * 0.25f);
        // TROU : presque NOIR — une dalle sombre posée sur la piste simule un trou
        // (l'intérieur du sol n'existe pas : le noir fait office d'abyssal Voodoo).
        var hole = MakeLit($"{MatFolder}/HoleMat.mat",
            new Color(0.045f, 0.045f, 0.06f), smoothness: 0.08f);
        // ROUTE : BLANCHE — la texture asphalte sombre (générée au lancement) s'affiche telle quelle.
        var road = MakeLit($"{MatFolder}/RoadMat.mat",
            Color.white, smoothness: 0.25f);
        // BUG FIX « route invisible » (v5) : les textures procédurales sont
        // générées ICI au BuildScene et sauvées en ASSETS PERSISTANTS, puis
        // posées directement sur les matériaux. Plus aucune dépendance au
        // runtime ApplyTexture (qui se perdait selon l'ordre d'initialisation).
        road.SetTexture("_BaseMap", SaveTexAsset(Runner.RuntimeTextureGen.Road(), $"{MatFolder}/RoadTex.asset"));
        road.mainTextureScale = new Vector2(1f, 1.25f);
        // HAZARD : jaune — les bandes noires diagonales viennent de la texture générée.
        var hazard = MakeLit($"{MatFolder}/HazardMat.mat",
            new Color(1f, 0.76f, 0.11f), smoothness: 0.35f);
        hazard.SetTexture("_BaseMap", SaveTexAsset(Runner.RuntimeTextureGen.Hazard(), $"{MatFolder}/HazardTex.asset"));
        hazard.mainTextureScale = new Vector2(4f, 0.5f);
        // Liserés lumineux de l'eau (le web : 0x9fe8ff, emissive 0x66d4ff ×0.9).
        var railGlow = MakeLit($"{MatFolder}/RailGlowMat.mat",
            new Color(0.624f, 0.910f, 1f), emission: new Color(0.40f, 0.83f, 1f) * 0.9f);
        // Cap sombre du mur (le web : 0x22252b).
        var darkCap = MakeLit($"{MatFolder}/DarkCapMat.mat",
            new Color(0.133f, 0.145f, 0.169f), smoothness: 0.4f);
        // Cônes du pic (le web : 0xd42a2a, emissive 0x7a0d0d ×0.4).
        var spikeRed = MakeLit($"{MatFolder}/SpikeRedMat.mat",
            new Color(0.83f, 0.16f, 0.16f), smoothness: 0.6f,
            emission: new Color(0.48f, 0.05f, 0.05f) * 0.4f);
        // Pointes blanches du pic (le web : 0xffe9e9, emissive 0xff8888).
        var spikeTip = MakeLit($"{MatFolder}/SpikeTipMat.mat",
            new Color(1f, 0.914f, 0.914f), smoothness: 0.7f,
            emission: new Color(1f, 0.53f, 0.53f) * 0.35f);
        // Décor : blanc, coloré par la palette du niveau à l'exécution.
        var decor = MakeLit($"{MatFolder}/DecorMat.mat", Color.white, smoothness: 0.45f);
        // Morceau de gelatine à RAMASSER : cyan très émissif, on le voit de loin.
        var pickup = MakeLit($"{MatFolder}/PickupMat.mat",
            new Color(0.35f, 0.95f, 1f), smoothness: 0.8f,
            emission: new Color(0.4f, 0.9f, 1f) * 1.2f);

        // ---------- 2. Préfabriqués ----------
        // Sol : cube 10×0.5×10 (le LevelSpawner pose le sommet à y = 0).
        var segment = MakeObstaclelessPrefab($"{PrefabFolder}/Segment.prefab",
            PrimitiveType.Cube, new Vector3(10f, 0.5f, 10f), road);

        // Obstacles en GROUPES multi-parties (pivot au sol, comme le web).
        var holePf = MakeHolePrefab($"{PrefabFolder}/Hole.prefab", hole, hazard);
        // Nettoyage de l'ancien obstacle EAU (remplacé par le trou).
        AssetDatabase.DeleteAsset($"{PrefabFolder}/Water.prefab");
        AssetDatabase.DeleteAsset($"{MatFolder}/WaterMat.mat");
        var spikePf = MakeSpikePrefab($"{PrefabFolder}/Spike.prefab", hazard, spikeRed, spikeTip);
        var wallPf = MakeWallPrefab($"{PrefabFolder}/Wall.prefab", hazard, darkCap);

        // Décor latéral en GROUPES : pivot au sol, bas de la forme enterré de 0.3
        // (exactement comme le web : centre y = hauteur/2·sc − 0.3).
        var pillarPf = MakeSceneryPrefab($"{PrefabFolder}/Pillar.prefab",
            PrimitiveType.Cylinder, new Vector3(1f, 2.5f, 1f), 2.2f, decor);   // cylindre : hauteur réelle 5
        var cubeDecorPf = MakeSceneryPrefab($"{PrefabFolder}/CubeDecor.prefab",
            PrimitiveType.Cube, new Vector3(1.8f, 1.8f, 1.8f), 0.6f, decor);
        var conePf = MakeSceneryPrefab($"{PrefabFolder}/Cone.prefab",
            PrimitiveType.Cylinder, new Vector3(1.3f, 1.7f, 1.3f), 1.4f, decor); // hauteur réelle 3.4

        // Morceaux de gelatine à ramasser (le cœur du nouveau design).
        var gelBlobPf = MakePickupPrefab($"{PrefabFolder}/GelBlob.prefab", pickup);

        // ---------- 3. Scène ----------
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Lumière directionnelle chaude, plus douce (le web : 0xfff4e0, intensity 1.0
        // + hémisphérique 0.75 — l'ambiance Trilight de GameVisuals fait le reste).
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.color = new Color(1f, 0.957f, 0.878f);   // le web : 0xfff4e0
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // Caméra DERRIÈRE la boule (la boule avance en +Z : caméra à ball.z − 11.5,
        // elle regarde devant vers ball.z + 16 — les obstacles arrivent EN FACE).
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.36f, 0.72f, 1f);
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 300f;
        camGo.AddComponent<AudioListener>();
        camGo.AddComponent<UniversalAdditionalCameraData>();
        var fx = camGo.AddComponent<CameraShakeAndEffects>();
        camGo.transform.position = new Vector3(0f, 5.6f, -11.5f);
        camGo.transform.LookAt(new Vector3(0f, 1f, 16f));

        // Systèmes — IMPORTANT : le GameManager est créé AVANT la boule, pour que
        // PlayerController.Awake trouve GameManager.Instance (sinon NullReferenceException).
        var gmGo = new GameObject("GameManager");
        var gm = gmGo.AddComponent<GameManager>();
        var spGo = new GameObject("LevelSpawner");
        var spawner = spGo.AddComponent<LevelSpawner>();
        var partGo = new GameObject("Particles");
        var particles = partGo.AddComponent<ParticleBurst>();
        var sfxGo = new GameObject("SoundFX");
        sfxGo.AddComponent<SoundFX>();
        var visGo = new GameObject("GameVisuals");
        var vis = visGo.AddComponent<GameVisuals>();
        var uiGo = new GameObject("GameUI");
        uiGo.AddComponent<GameUI>();

        // Boule du joueur (sans SphereCollider : physique 100 % custom).
        var ballGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ballGo.name = "Ball";
        ballGo.transform.position = new Vector3(0f, 0.5f, 0f);
        Object.DestroyImmediate(ballGo.GetComponent<Collider>());
        ballGo.GetComponent<MeshRenderer>().sharedMaterial = gelatine;
        var player = ballGo.AddComponent<PlayerController>();

        // ---------- 4. Câblage des inspecteurs ----------
        player.gelatineMaterial = gelatine;
        player.fx = fx;
        player.particles = particles;
        player.spawner = spawner;

        fx.followTarget = ballGo.transform;

        gm.player = player;
        gm.spawner = spawner;

        spawner.ball = ballGo.transform;
        spawner.segmentPrefabs = new GameObject[] { segment };
        // Ordre contractuel : [0] Trou, [1] Pic, [2] Mur.
        spawner.obstaclePrefabs = new GameObject[] { holePf, spikePf, wallPf };
        // Ordre contractuel : [0] poteau, [1] cube, [2] cône (pyramide du web).
        spawner.sceneryPrefabs = new GameObject[] { pillarPf, cubeDecorPf, conePf };
        // La gelatine à ramasser entre les obstacles.
        spawner.pickupPrefab = gelBlobPf;

        vis.sun = light;
        vis.ball = ballGo.transform;

        // ---------- 5. Sauvegarde ----------
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        EditorPrefs.SetInt(VersionKey, SCENE_VERSION);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "[MatterRunner] Scène montée (version " + SCENE_VERSION + ") : " +
            "préfabriqués multi-parties + matériaux émissifs + câblage OK.\n" +
            $"Scène : {ScenePath}\n" +
            "Appuie sur PLAY pour jouer (tap = clic / espace / doigt).");

        // ---------- 6. Input (dernier : l'éditeur redémarre après ce changement) ----------
        // "Both" = ancien Input (Input.GetMouseButtonDown) + nouveau Input System.
        // API PlayerSettings.activeInputHandler retirée en Unity 6 : on écrit
        // directement la propriété dans ProjectSettings/ProjectSettings.asset.
        var settings = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
        var inputHandler = settings.FindProperty("activeInputHandler");
        if (inputHandler != null && inputHandler.intValue != 2)
        {
            inputHandler.intValue = 2;
            settings.ApplyModifiedProperties();
            Debug.LogWarning(
                "[MatterRunner] Active Input Handling réglé sur 'Both' (requis pour les taps). " +
                "Unity va redémarrer — relance simplement le Play ensuite.");
        }
    }

    // ==================== HELPERS ====================

    /// <summary>Sauve une Texture2D générée en ASSET persistent (réutilisée par les matériaux,
    /// survive aux redémarrages de l'éditeur — contrairement au cache statique runtime).</summary>
    private static Texture2D SaveTexAsset(Texture2D tex, string path)
    {
        AssetDatabase.DeleteAsset(path);
        tex.name = System.IO.Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(tex, path);
        return tex;
    }

    /// <summary>Matériau URP/Lit, sauvegardé en asset. Recette transparent URP complète si demandé.
    /// ÉMISSION : le keyword "_EMISSION" est REQUIS (la couleur seule ne fait rien).</summary>
    private static Material MakeLit(string path, Color color, bool transparent = false,
        float metallic = 0f, float smoothness = 0.5f, Color? emission = null)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.name = System.IO.Path.GetFileNameWithoutExtension(path);
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", smoothness);

        if (transparent)
        {
            // Recette URP transparente (la même que l'éditeur applique à la main).
            mat.SetFloat("_Surface", 1f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }

        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission.Value);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    /// <summary>Préfabriqué simple (sol) sans composant de jeu.</summary>
    private static GameObject MakeObstaclelessPrefab(string path, PrimitiveType type,
        Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = System.IO.Path.GetFileNameWithoutExtension(path);
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    /// <summary>
    /// Préfabriqué TROU en groupe (pivot au sol) — PARTIEL, PAS sur toute la largeur :
    ///   - dalle NOIRE 4.5 × 3.5 posée sur la piste (simule le vide),
    ///   - liseré HAZARD (bandes jaune/noir) tout autour : lisibilité danger,
    ///   la piste fait 10 de large → il reste ~5.5 de route DE CHAQUE côté du trou
    ///   (centré) : on l'ESQUIVE en glissant, ou on le SAUTE (topHeight 0.35).
    /// Le trou est décentré aléatoirement au spawn, comme les pics.
    /// </summary>
    private static GameObject MakeHolePrefab(string path, Material holeMat, Material hazardMat)
    {
        var root = new GameObject("Hole");

        const float PIT_W = 4.5f;    // largeur du trou : PARTIELLE (piste = 10)
        const float PIT_D = 3.5f;    // profondeur : se saute (l'eau était à 5.5)

        // Dalle noire posée SUR la piste (à peine au-dessus : cache la texture route).
        var pit = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pit.name = "HolePit";
        pit.transform.SetParent(root.transform, false);
        pit.transform.localPosition = new Vector3(0f, 0.03f, 0f);
        pit.transform.localScale = new Vector3(PIT_W, 0.06f, PIT_D);
        Object.DestroyImmediate(pit.GetComponent<Collider>());
        pit.GetComponent<MeshRenderer>().sharedMaterial = holeMat;

        // Liseré de danger : 4 barres fines hazard autour du trou.
        float zEdge = PIT_D * 0.5f;   // 1.75
        float xEdge = PIT_W * 0.5f;   // 2.25
        for (int i = 0; i < 4; i++)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "HoleEdge" + i;
            bar.transform.SetParent(root.transform, false);
            // 0/1 = barres avant et arrière (le long de X) ; 2/3 = côtés (le long de Z).
            bool alongX = i < 2;
            bar.transform.localPosition = alongX
                ? new Vector3(0f, 0.05f, (i == 0 ? -1f : 1f) * (zEdge + 0.07f))
                : new Vector3((i == 2 ? -1f : 1f) * (xEdge + 0.07f), 0.05f, 0f);
            bar.transform.localScale = alongX
                ? new Vector3(PIT_W + 0.42f, 0.08f, 0.14f)
                : new Vector3(0.14f, 0.08f, PIT_D + 0.28f);
            Object.DestroyImmediate(bar.GetComponent<Collider>());
            bar.GetComponent<MeshRenderer>().sharedMaterial = hazardMat;
        }

        var ob = root.AddComponent<Obstacle>();
        ob.kind = ObstacleKind.Hole;
        ob.baseYOffset = 0f;
        // Nouveau design : le trou est BAS (0.35) → il se SAUTE ; on perd peu de gelatine.
        ob.topHeight = 0.35f;
        ob.penalty = 0.25f;
        ob.halfWidth = 2.25f;
        ob.halfDepth = 2.1f;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>
    /// Préfabriqué PIC en groupe (pivot au sol), adapté au nouveau design :
    ///   - base hazard 7 × 0.22 × 2.2,
    ///   - 3 cônes rouges (r 0.65, h 1.8) à x = −2.4 / 0 / +2.4,
    ///   - 3 pointes blanches au sommet.
    /// TROP HAUT (2.2) pour être sauté : le pic ne se SAUTE pas, on l'ESQUIVE
    /// en glissant à gauche/droite (il est décentré aléatoirement au spawn).
    /// </summary>
    private static GameObject MakeSpikePrefab(string path, Material hazardMat,
        Material redMat, Material tipMat)
    {
        var root = new GameObject("Spike");

        var baseGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        baseGo.name = "SpikeBase";
        baseGo.transform.SetParent(root.transform, false);
        baseGo.transform.localPosition = new Vector3(0f, 0.11f, 0f);
        baseGo.transform.localScale = new Vector3(7f, 0.22f, 2.2f);
        Object.DestroyImmediate(baseGo.GetComponent<Collider>());
        baseGo.GetComponent<MeshRenderer>().sharedMaterial = hazardMat;

        // Mesh de cône généré (Unity n'a pas de primitive cône) — pivot AU SOL.
        var coneMesh = MakeConeMesh("SpikeCone", 0.65f, 1.8f, 10);
        var tipMesh = MakeConeMesh("SpikeTip", 0.16f, 0.4f, 8);
        AssetDatabase.DeleteAsset($"{MeshFolder}/SpikeCone.asset");
        AssetDatabase.CreateAsset(coneMesh, $"{MeshFolder}/SpikeCone.asset");
        AssetDatabase.DeleteAsset($"{MeshFolder}/SpikeTip.asset");
        AssetDatabase.CreateAsset(tipMesh, $"{MeshFolder}/SpikeTip.asset");

        for (int i = 0; i < 3; i++)
        {
            float x = -2.4f + i * 2.4f;   // 3 pics compacts : une ESQUIVE latérale suffit

            var coneGo = new GameObject("SpikeCone" + i);
            coneGo.transform.SetParent(root.transform, false);
            coneGo.transform.localPosition = new Vector3(x, 0.22f, 0f);   // pivot au sol : posé sur la base
            coneGo.AddComponent<MeshFilter>().sharedMesh = coneMesh;
            coneGo.AddComponent<MeshRenderer>().sharedMaterial = redMat;   // zéro collider dans ce jeu

            var tipGo = new GameObject("SpikeTip" + i);
            tipGo.transform.SetParent(root.transform, false);
            tipGo.transform.localPosition = new Vector3(x, 1.85f, 0f);   // pointe au sommet du cône
            tipGo.AddComponent<MeshFilter>().sharedMesh = tipMesh;
            tipGo.AddComponent<MeshRenderer>().sharedMaterial = tipMat;
        }

        var ob = root.AddComponent<Obstacle>();
        ob.kind = ObstacleKind.Spike;
        ob.baseYOffset = 0f;
        // Nouveau design : le pic est trop HAUT (2.2) → PAS sautable, il faut glisser à côté.
        ob.topHeight = 2.2f;
        ob.penalty = 0.45f;
        ob.halfWidth = 2.4f;
        ob.halfDepth = 1.4f;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>
    /// Préfabriqué MUR en groupe (pivot au sol), ABAISSÉ pour le nouveau design :
    ///   - corps hazard 10 × 0.9 × 0.8 (hauteur 0.95 → il se SAUTE),
    ///   - cap sombre 10.3 × 0.22 × 1.0 au sommet (le web : 0x22252b).
    /// </summary>
    private static GameObject MakeWallPrefab(string path, Material hazardMat, Material capMat)
    {
        var root = new GameObject("Wall");

        var bodyGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bodyGo.name = "WallBody";
        bodyGo.transform.SetParent(root.transform, false);
        bodyGo.transform.localPosition = new Vector3(0f, 0.45f, 0f);
        bodyGo.transform.localScale = new Vector3(10f, 0.9f, 0.8f);
        Object.DestroyImmediate(bodyGo.GetComponent<Collider>());
        bodyGo.GetComponent<MeshRenderer>().sharedMaterial = hazardMat;

        var capGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        capGo.name = "WallCap";
        capGo.transform.SetParent(root.transform, false);
        capGo.transform.localPosition = new Vector3(0f, 1.0f, 0f);
        capGo.transform.localScale = new Vector3(10.3f, 0.22f, 1.0f);
        Object.DestroyImmediate(capGo.GetComponent<Collider>());
        capGo.GetComponent<MeshRenderer>().sharedMaterial = capMat;

        var ob = root.AddComponent<Obstacle>();
        ob.kind = ObstacleKind.Wall;
        ob.baseYOffset = 0f;
        // Nouveau design : le mur est BAS (0.95) → il se SAUTE ; contact = grosse perte.
        ob.topHeight = 0.95f;
        ob.penalty = 0.4f;
        ob.halfWidth = 5.2f;
        ob.halfDepth = 0.7f;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>
    /// Préfabriqué GelBlob : le morceau de gelatine à RAMASSER (le cœur du nouveau design).
    /// Petite sphère émissive cyan posée sur la route, sans collider (collecte par distance).
    /// </summary>
    private static GameObject MakePickupPrefab(string path, Material pickupMat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "GelBlob";
        go.transform.localScale = Vector3.one * 0.7f;
        Object.DestroyImmediate(go.GetComponent<Collider>());   // collecte par DISTANCE, zéro collider
        go.GetComponent<MeshRenderer>().sharedMaterial = pickupMat;

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    /// <summary>
    /// Préfabriqué de DÉCOR en groupe : pivot au sol, forme enfant décalée
    /// pour que sa base soit enterrée de 0.3 (comme le web). Le scale du parent
    /// appliqué par le LevelSpawner préserve la pose au sol.
    /// </summary>
    private static GameObject MakeSceneryPrefab(string path, PrimitiveType type,
        Vector3 scale, float childY, Material mat)
    {
        var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));

        var shape = GameObject.CreatePrimitive(type);
        shape.name = "Shape";
        shape.transform.SetParent(root.transform, false);
        shape.transform.localPosition = new Vector3(0f, childY, 0f);
        shape.transform.localScale = scale;
        Object.DestroyImmediate(shape.GetComponent<Collider>());
        shape.GetComponent<MeshRenderer>().sharedMaterial = mat;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>Mesh de cône (pivot au sol : base à y = 0, pointe à y = height).</summary>
    private static Mesh MakeConeMesh(string name, float radius, float height, int segments)
    {
        var mesh = new Mesh { name = name };

        // Base (segments sommets) + pointe.
        var verts = new Vector3[segments + 1];
        var norms = new Vector3[segments + 1];
        var uvs = new Vector2[segments + 1];

        // Normale de surface d'un cône : inclinée de r/slant vers l'extérieur, h/slant vers le haut.
        float slant = Mathf.Sqrt(radius * radius + height * height);
        for (int i = 0; i < segments; i++)
        {
            float a = (float)i / segments * Mathf.PI * 2f;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            verts[i] = new Vector3(c * radius, 0f, s * radius);
            norms[i] = new Vector3(c * radius / slant, height / slant, s * radius / slant);
            uvs[i] = new Vector2((float)i / segments, 0f);
        }
        verts[segments] = new Vector3(0f, height, 0f);   // pointe
        norms[segments] = Vector3.up;
        uvs[segments] = new Vector2(0.5f, 1f);

        // Triangles face externe : base → pointe → voisin (winding horaire vu de dehors).
        var tris = new int[segments * 3];
        for (int i = 0; i < segments; i++)
        {
            int b = i * 3;
            tris[b] = i;
            tris[b + 1] = segments;               // pointe
            tris[b + 2] = (i + 1) % segments;
        }

        mesh.vertices = verts;
        mesh.normals = norms;
        mesh.uv = uvs;
        mesh.triangles = tris;
        return mesh;
    }

    private static void EnsureFolder(string parent, string folder)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{folder}"))
        {
            AssetDatabase.CreateFolder(parent, folder);
        }
    }
}