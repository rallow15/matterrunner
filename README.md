# MatterRunner

Jeu runner mobile (Unity 6.3 LTS + URP) : tu es une boule de **GÉLATINE** qui
avance tout droite, ramasse les GelBlobs cyan pour GROSSIR, esquive / saute
les obstacles (trous partiels, pics, murs). Trop petite → tu perds.

## Structure

- `Assets/Scenes/MatterRunner.unity` — la scène montée automatiquement
- `Assets/Scripts/` — tout le gameplay (pilotée par `GameManager` + `LevelSpawner`)
- `Assets/Editor/MatterRunnerSceneSetup.cs` — remonte la scène TOUTE SEULE
  après chaque changement de version (menu `MatterRunner/Monter la scène complète`)
- `Assets/Editor/CiBuild.cs` — build iOS pour le CI (menu `MatterRunner/Build iOS`)

## Build iOS (Codemagic)

Le fichier `codemagic.yaml` à la racine décrit tout le pipeline :

1. Installation de Unity `6000.3.25f1` (module iOS) sur le Mac distant
2. Activation de la licence Unity (`UNITY_LICENSE_B64` **ou** `UNITY_EMAIL`/`UNITY_PASSWORD`)
3. Build Unity → projet Xcode dans `build_xcode/`
4. `xcodebuild archive` + export → **IPA** dans les artifacts Codemagic

Variables à créer dans Codemagic → App settings → Environment variables
(marquées "secure") : `UNITY_EMAIL`, `UNITY_PASSWORD`, `MR_BUNDLE_ID`.