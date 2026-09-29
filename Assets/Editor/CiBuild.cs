using System;
using System.Threading;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MatterRunner
{
    /// <summary>
    /// CiBuild — construction AUTOMATIQUE pour le CI (Codemagic).
    ///
    /// Le Mac distant de Codemagic lance Unity en ligne de commande avec
    /// -executeMethod MatterRunner.CiBuild.BuildIos : ce script :
    ///   1) récupère le bundle ID fourni par une variable d'environnement ;
    ///   2) construit le PROJET XCODE iOS dans build_xcode/ (pas l'IPA : c'est
    ///      le rôle du second script Codemagic, xcodebuild, qui a les certificats) ;
    ///   3) fait SORTIR Unity avec le code 0 (succès) ou 1 (échec) — Codemagic
    ///      compte sur ce code d'arrêt pour marquer le build bon ou cassé.
    ///
    /// Menu testable aussi en local : "MatterRunner/Build iOS (local)".
    /// </summary>
    public static class CiBuild
    {
        /// <summary>Dossier de sortie du projet Xcode (racine du repo, git-ignoré).</summary>
        private const string XcodeDir = "build_xcode";

        /// <summary>La scène d'entrée du jeu (montée par MatterRunnerSceneSetup).</summary>
        private const string MainScene = "Assets/Scenes/MatterRunner.unity";

        [MenuItem("MatterRunner/Build iOS (local, pour test)")]
        public static void BuildIos()
        {
            try
            {
                DoBuild();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[CiBuild] ÉCHEC du build : {e.Message}");
                EditorApplication.Exit(1);
            }
        }

        private static void DoBuild()
        {
            // 1) Bundle ID : variable d'environnement de Codemagic, sinon valeur
            //    de repli locale (modifiable plus tard dans Player Settings).
            string bundleId = Environment.GetEnvironmentVariable("MR_BUNDLE_ID");
            if (string.IsNullOrEmpty(bundleId)) bundleId = "com.matterrunner.app";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, bundleId);
            Debug.Log($"[CiBuild] Bundle ID = {bundleId}");

            // 2) Orientation : portrait, comme le jeu (runner vertical).
            //    (Dans Unity 6, ces réglages sont passés sur PlayerSettings.)
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.statusBarHidden = true;

            // 3) La liste des scènes : la scène principale suffit.
            var options = new BuildPlayerOptions
            {
                scenes = new[] { MainScene },
                locationPathName = XcodeDir,
                target = BuildTarget.iOS
                // NB : BuildOptions.AcceptExternalModifications n'existe plus
                // dans Unity 6 — le projet Xcode est régénéré proprement.
            };

            Debug.Log("[CiBuild] Build iOS démarré (projet Xcode)…");
            BuildReport report = BuildPipeline.BuildPlayer(options);

            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception($"BuildPlayer a échoué : {report.summary.result} — {report.summary.totalErrors} erreur(s). Total errors : {report.summary.totalErrors}");

            Debug.Log($"[CiBuild] Projet Xcode OK dans {XcodeDir}/ ({report.summary.totalSize / (1024 * 1024)} Mo).");
        }
    }
}