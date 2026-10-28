using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Gera o executável Windows do protótipo em Client/Builds/Windows.
    /// No Editor: menu Protótipo &gt; Gerar build Windows.
    /// Em linha de comando: -executeMethod Game.Client.Editor.PrototypeBuild.BuildWindows
    /// </summary>
    public static class PrototypeBuild
    {
        private const string OutputPath = "Builds/Windows/TerrasDeAstralis.exe";

        /// <summary>
        /// Servidor padrão do build: a variável GAME_SERVER (o GitHub Actions passa, ex.: "jogo.com.br:7777") vira o
        /// servidor.txt ao lado do exe. Sem ela, copia o Client/servidor.txt se existir; sem nenhum, o jogo usa 127.0.0.1.
        /// </summary>
        private static void WriteServerFile()
        {
            var target = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(OutputPath) ?? ".", Net.GameMode.ServerFileName);
            var server = System.Environment.GetEnvironmentVariable("GAME_SERVER");
            if (!string.IsNullOrWhiteSpace(server))
            {
                System.IO.File.WriteAllText(target, "# Servidor padrão do jogo (endereço:porta).\njogo = " + server.Trim() + "\n");
                Debug.Log($"[Protótipo] servidor.txt: {server.Trim()}");
            }
            else if (System.IO.File.Exists(Net.GameMode.ServerFileName))
            {
                System.IO.File.Copy(Net.GameMode.ServerFileName, target, true);
            }
        }

        [MenuItem("Protótipo/Gerar build Windows")]
        public static void BuildWindows()
        {
            // Gera os controladores e prefabs KayKit antes do build para incluir animações e visuais recém-adicionados.
            KayKitBuilder.Build();

            // Jogo online: continua rodando sem foco (várias janelas abertas para teste, alt-tab durante a luta).
            PlayerSettings.runInBackground = true;
            GameIcon.Apply();

            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Treino.unity" },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded) WriteServerFile();
            Debug.Log($"[Protótipo] Build {summary.result}: {summary.outputPath} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalErrors} erros)");

            if (Application.isBatchMode && summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
