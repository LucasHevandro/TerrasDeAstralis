using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Game.Launcher
{
    /// <summary>
    /// <c>TerrasDeAstralisLauncher.exe</c>: abre a janela que atualiza e abre o jogo.
    /// <list type="bullet">
    /// <item><c>--pasta DIR</c> e <c>--url URL</c>: outra pasta do jogo ou outro endereço de atualização (testes).</item>
    /// <item><c>--verificar</c>: atualiza sem janela, escreve o <c>launcher.log</c> e fecha (0 = ok).</item>
    /// <item><c>--foto ARQUIVO</c>: abre a janela, espera ficar pronta, salva a imagem e fecha (testes).</item>
    /// <item><c>--publicar PASTA_DO_BUILD SAIDA VERSAO [NOVIDADES]</c>: monta a versão (Tools/launcher/publicar.ps1).
    /// Com <c>--anterior ARQUIVO</c> (o versao.json no ar) e <c>--historico-inicial ARQUIVO</c>, monta o histórico.</item>
    /// </list>
    /// </summary>
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            var publish = Array.IndexOf(args, "--publicar");
            if (publish >= 0)
            {
                var rest = args.Skip(publish + 1).TakeWhile(a => !a.StartsWith("--")).ToArray();
                if (rest.Length < 3) return Fail("Uso: --publicar PASTA_DO_BUILD SAIDA VERSAO [NOVIDADES]");
                var release = Publisher.Build(rest[0], rest[1], rest[2], rest.Length > 3 ? rest[3] : "",
                    Option(args, "--anterior"), Option(args, "--historico-inicial"));
                File.WriteAllText(Path.Combine(rest[1], "resumo.txt"),
                    $"{release.Version}: {release.Files.Count} arquivos, {Updater.Megabytes(release.Files.Sum(f => f.Packed))} compactados\n");
                return 0;
            }

            var folder = Option(args, "--pasta") ?? AppDomain.CurrentDomain.BaseDirectory;
            var url = Option(args, "--url") ?? Updater.UrlFrom(folder);
            var updater = new Updater(Path.GetFullPath(folder), url);
            updater.CleanUp();

            if (args.Contains("--verificar")) return Verify(updater);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LauncherForm(updater, args));
            return 0;
        }

        private static int Verify(Updater updater)
        {
            var log = Path.Combine(updater.Folder, "launcher.log");
            try
            {
                var result = Task.Run(async () =>
                {
                    var release = await updater.FetchAsync();
                    var missing = updater.Missing(release);
                    await updater.ApplyAsync(release, missing);
                    return $"{release.Version}: {missing.Count} de {release.Files.Count} arquivos baixados";
                }).GetAwaiter().GetResult();
                File.WriteAllText(log, "OK " + result + Environment.NewLine);
                return 0;
            }
            catch (Exception exception)
            {
                File.WriteAllText(log, "FALHA " + exception + Environment.NewLine);
                return 1;
            }
        }

        private static string? Option(string[] args, string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private static int Fail(string message)
        {
            Console.Error.WriteLine(message);
            return 2;
        }
    }
}
