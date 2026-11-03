using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Game.Client.Net;
using Game.Shared.Accounts;
using UnityEngine;

namespace Game.Client.DevTools
{
    /// <summary>
    /// "Manter conectado" de ponta a ponta, em dois processos (fechar e abrir o jogo de verdade):
    /// <code>
    /// exe -server 127.0.0.1:7790 -savedlogin pasta\dados -remembertest pasta -rememberphase 1
    /// exe -server 127.0.0.1:7790 -savedlogin pasta\dados -remembertest pasta -rememberphase 2
    /// </code>
    /// Fase 1: cria a conta com um personagem, entra pela tela com "Manter conectado" e confere o arquivo cifrado.
    /// Fase 2: sem digitar nada, tem que chegar à seleção de personagem; depois "Sair da conta" apaga o arquivo e o
    /// servidor recusa o token antigo. A fase 2 grava o <c>relatorio.txt</c>. O <c>-savedlogin</c> mantém o login de
    /// quem joga intocado.
    /// </summary>
    public sealed class RememberLoginTest : MonoBehaviour
    {
        private string _folder = "";
        private string _data = "";
        private int _phase = 1;
        private const string Password = "teste123";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-remembertest");
            if (index < 0) return;
            var test = new GameObject("Teste do login salvo").AddComponent<RememberLoginTest>();
            test._folder = index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "remembertest");
            var phase = Array.IndexOf(args, "-rememberphase");
            if (phase >= 0 && phase + 1 < args.Length) int.TryParse(args[phase + 1], out test._phase);
            var data = Array.IndexOf(args, "-savedlogin");
            test._data = data >= 0 && data + 1 < args.Length ? args[data + 1] : "";
            Directory.CreateDirectory(test._folder);
        }

        private IEnumerator Start()
        {
            if (_data.Length == 0)
            {
                Finish("relatorio.txt", false, "FALHA: rode com -savedlogin pasta (para não mexer no login de quem joga)");
                yield break;
            }
            yield return new WaitForSecondsRealtime(2.5f);
            var menu = FindAnyObjectByType<ConnectMenu>();
            if (menu == null)
            {
                Finish("relatorio.txt", false, "FALHA: a tela de entrada não abriu");
                yield break;
            }
            if (_phase == 1) yield return PhaseOne(menu);
            else yield return PhaseTwo(menu);
        }

        private static string Server => ServerList.Entries[0].ApiUrl;

        private IEnumerator PhaseOne(ConnectMenu menu)
        {
            var log = new StringBuilder();
            var account = "lembra" + UnityEngine.Random.Range(10000, 99999);
            var started = menu.ScreenName == "Login" && !SavedLogin.HasAnyToken;
            log.AppendLine($"Começou na tela de entrada sem token salvo: {started}");

            // Conta e personagem prontos pela API, como se o jogador já tivesse jogado antes.
            var api = new AccountClient(GameMode.Host, GameMode.Port);
            var setup = Setup(api, account);
            yield return new WaitUntil(() => setup.IsCompleted);
            log.AppendLine($"Conta {account} com personagem: {setup.Result}");

            menu.FillLoginForTest(account, Password, staySignedIn: true);
            yield return new WaitForSecondsRealtime(0.6f);
            ScreenCapture.CaptureScreenshot(Path.Combine(_folder, "entrada_manter_conectado.png"));
            yield return new WaitForSecondsRealtime(0.4f);
            var login = menu.SubmitLoginForTest();
            yield return new WaitUntil(() => login.IsCompleted);
            yield return WaitScreen(menu, "Characters", 10f);
            var entered = menu.ScreenName == "Characters";
            log.AppendLine($"Entrou na seleção de personagem: {entered} ({menu.ScreenName})");

            var token = SavedLogin.Token(Server);
            var file = Path.Combine(_data, "sessao.dat");
            var cipher = File.Exists(file) ? File.ReadAllBytes(file) : Array.Empty<byte>();
            var hidden = token.Length > 0 && cipher.Length > 0 && Encoding.UTF8.GetString(cipher).IndexOf(token, StringComparison.Ordinal) < 0
                && Encoding.Unicode.GetString(cipher).IndexOf(token, StringComparison.Ordinal) < 0;
            log.AppendLine($"Token salvo e cifrado (não aparece no sessao.dat): {hidden}");
            var accountFile = Path.Combine(_data, "conta.json");
            var remembered = File.Exists(accountFile) && File.ReadAllText(accountFile).Contains(account) && !File.ReadAllText(accountFile).Contains(Password);
            log.AppendLine($"Conta lembrada sem a senha: {remembered}");
            log.AppendLine("Conta: " + account);

            var ok = started && setup.Result && entered && hidden && remembered;
            Finish("relatorio_fase1.txt", ok, (ok ? "SUCESSO" : "FALHA") + ": fase 1 (entrar com \"Manter conectado\")\n" + log);
        }

        private IEnumerator PhaseTwo(ConnectMenu menu)
        {
            var log = new StringBuilder();
            var first = Path.Combine(_folder, "relatorio_fase1.txt");
            var phaseOne = File.Exists(first) ? File.ReadAllText(first) : "";
            var account = "";
            foreach (var line in phaseOne.Split('\n'))
                if (line.StartsWith("Conta: ", StringComparison.Ordinal)) account = line.Substring(7).Trim();
            log.AppendLine($"Fase 1: {(phaseOne.StartsWith("SUCESSO", StringComparison.Ordinal) ? "SUCESSO" : "FALHA")}");

            // Abriu o jogo: ninguém digita nada, tem que chegar sozinho à seleção.
            yield return WaitScreen(menu, "Characters", 10f);
            var direct = menu.ScreenName == "Characters";
            log.AppendLine($"Abriu direto na seleção de personagem: {direct} ({menu.ScreenName})");
            var filled = account.Length > 0 && menu.AccountForTest == account;
            log.AppendLine($"Conta lembrada no campo: {filled}");
            yield return new WaitForSecondsRealtime(1.2f);
            ScreenCapture.CaptureScreenshot(Path.Combine(_folder, "selecao_direta.png"));
            yield return new WaitForSecondsRealtime(0.4f);

            var token = SavedLogin.Token(Server);
            menu.LogoutForTest();
            yield return new WaitForSecondsRealtime(1.5f);
            var forgotten = !SavedLogin.HasAnyToken && !File.Exists(Path.Combine(_data, "sessao.dat")) && menu.ScreenName == "Login";
            log.AppendLine($"\"Sair da conta\" apagou o token e voltou à entrada: {forgotten}");
            var check = new AccountClient(GameMode.Host, GameMode.Port).DeviceLoginAsync(token);
            yield return new WaitUntil(() => check.IsCompleted);
            var refused = token.Length > 0 && !check.Result.Ok && check.Result.ErrorKey == "auth.device_expired";
            log.AppendLine($"Servidor recusa o token antigo: {refused} ({check.Result.ErrorKey})");
            ScreenCapture.CaptureScreenshot(Path.Combine(_folder, "depois_de_sair.png"));
            yield return new WaitForSecondsRealtime(0.4f);

            var ok = phaseOne.StartsWith("SUCESSO", StringComparison.Ordinal) && direct && filled && forgotten && refused;
            Finish("relatorio.txt", ok, (ok
                ? "SUCESSO: \"Manter conectado\" entrou direto na seleção depois de fechar e abrir o jogo; \"Sair da conta\" apagou o token\n"
                : "FALHA: login salvo\n") + log + "\n--- fase 1 ---\n" + phaseOne);
        }

        private static async Task<bool> Setup(AccountClient api, string account)
        {
            if (!(await api.RegisterAsync(account, Password, Legal.Version)).Ok) return false;
            if (!(await api.LoginAsync(account, Password)).Ok) return false;
            return (await api.CreateCharacterAsync("Lembra" + account.Substring(6), CharacterClass.Ranger, 2, 1)).Ok;
        }

        private static IEnumerator WaitScreen(ConnectMenu menu, string screen, float seconds)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (menu.ScreenName != screen && Time.realtimeSinceStartup < until) yield return null;
        }

        private void Finish(string file, bool ok, string text)
        {
            File.WriteAllText(Path.Combine(_folder, file), text);
            Debug.Log("[Teste] " + text);
            Application.Quit(ok ? 0 : 1);
        }
    }
}
