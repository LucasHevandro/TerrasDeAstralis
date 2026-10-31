using System;
using System.Collections;
using System.IO;
using Game.Client.Net;
using UnityEngine;

namespace Game.Client.DevTools
{
    /// <summary>
    /// Tela de entrada: <c>-logintest pasta</c> espera o fundo ficar pronto, fotografa a tela e fecha. Confere que a
    /// câmera do jogo ficou desligada atrás do menu (o fundo é uma foto parada).
    /// </summary>
    public sealed class LoginScreenTest : MonoBehaviour
    {
        private string _folder = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-logintest");
            if (index < 0) return;
            var test = new GameObject("Teste da tela de entrada").AddComponent<LoginScreenTest>();
            test._folder = index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "logintest");
            Directory.CreateDirectory(test._folder);
        }

        private IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(2.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(_folder, "entrada.png"));
            yield return new WaitForSecondsRealtime(0.5f);
            // Câmera desligada não conta como Camera.main.
            var stopped = ConnectMenu.IsOpen && Camera.main == null;
            // A criação de personagem, com a prévia nas duas classes e cores diferentes.
            var menu = FindAnyObjectByType<ConnectMenu>();
            if (menu != null)
            {
                menu.OpenCreateForTest(Game.Shared.Accounts.CharacterClass.Arcanist, 1, 3, head: 2, skin: 1);
                yield return new WaitForSecondsRealtime(1.5f);
                ScreenCapture.CaptureScreenshot(Path.Combine(_folder, "criacao_arcanista.png"));
                yield return new WaitForSecondsRealtime(0.4f);
                menu.OpenCreateForTest(Game.Shared.Accounts.CharacterClass.Blade, 3, 1, head: 5, skin: 3);
                yield return new WaitForSecondsRealtime(1.5f);
                ScreenCapture.CaptureScreenshot(Path.Combine(_folder, "criacao_lamina.png"));
                yield return new WaitForSecondsRealtime(0.4f);
                menu.OpenCreateForTest(Game.Shared.Accounts.CharacterClass.Ranger, 2, 4, head: 3, skin: 2);
                yield return new WaitForSecondsRealtime(1.5f);
                ScreenCapture.CaptureScreenshot(Path.Combine(_folder, "criacao_ranger.png"));
                yield return new WaitForSecondsRealtime(0.4f);
                menu.OpenCreateForTest(Game.Shared.Accounts.CharacterClass.Guardian, 1, 2, skin: 3);
                yield return new WaitForSecondsRealtime(1.5f);
                ScreenCapture.CaptureScreenshot(Path.Combine(_folder, "criacao_guardiao.png"));
                yield return new WaitForSecondsRealtime(0.4f);
                menu.OpenCharactersForTest(new System.Collections.Generic.List<Game.Shared.Accounts.CharacterSummary>
                {
                    new Game.Shared.Accounts.CharacterSummary { Id = 1, Name = "Aria", ClassId = 1, Level = 24, Hair = 2, Cloth = 1, ArmorGrade = 3, GlovesGrade = 3, BootsGrade = 3 },
                    new Game.Shared.Accounts.CharacterSummary { Id = 2, Name = "Bruno", ClassId = 0, Level = 12, Hair = 5, Cloth = 4, ArmorGrade = 2, GlovesGrade = 3, BootsGrade = 1 },
                });
                yield return new WaitForSecondsRealtime(1.5f);
                ScreenCapture.CaptureScreenshot(Path.Combine(_folder, "selecao.png"));
                yield return new WaitForSecondsRealtime(0.4f);
            }
            var servers = string.Join("; ", ServerList.Entries.ConvertAll(e => $"{e.Name} {e.Address} = {e.Status} ({e.Players} jogadores, {e.PingMs} ms)"));
            // Escolheu jogar (offline): o jogo volta a ser desenhado.
            GameMode.SetOffline();
            yield return new WaitForSecondsRealtime(0.5f);
            var back = Camera.main != null;
            var ok = stopped && back;
            File.WriteAllText(Path.Combine(_folder, "relatorio.txt"),
                (ok ? "SUCESSO: tela de entrada com a foto do mundo ao fundo e a câmera parada; ao jogar, a câmera volta\n"
                    : $"FALHA: câmera parada no menu = {stopped}, câmera de volta ao jogar = {back}\n") + "Servidores: " + servers + "\n");
            Application.Quit(ok ? 0 : 1);
        }
    }
}
