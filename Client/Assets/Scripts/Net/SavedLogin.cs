using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Game.Client.Net
{
    /// <summary>
    /// Login salvo neste computador: "Lembrar conta" guarda o nome da conta (<c>conta.json</c>, texto simples) e
    /// "Manter conectado" guarda o token do computador de cada servidor (<c>sessao.dat</c>), cifrado com a DPAPI do
    /// Windows para o usuário atual: copiado para outro PC ou outro usuário, o arquivo não abre. A senha nunca é
    /// guardada. Fora do Windows só o nome da conta fica salvo.
    /// Pasta: a de dados do jogo, ou a do <c>-savedlogin pasta</c> (testes, para não mexer no login de quem joga).
    /// </summary>
    public static class SavedLogin
    {
        private const string AccountFile = "conta.json";
        private const string TokenFile = "sessao.dat";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TerrasDeAstralis.sessao.v1");

        [Serializable]
        private sealed class AccountData
        {
            public string Account = "";
            public bool RememberAccount = true;
            public bool StaySignedIn;
        }

        [Serializable]
        private sealed class TokenEntry
        {
            public string Server = "";
            public string Token = "";
        }

        [Serializable]
        private sealed class TokenData
        {
            public List<TokenEntry> Entries = new List<TokenEntry>();
        }

        private static AccountData _account;
        private static string _folder;
        private static bool _testFolder;

        private static string Folder
        {
            get
            {
                if (_folder != null) return _folder;
                var args = Environment.GetCommandLineArgs();
                var index = Array.IndexOf(args, "-savedlogin");
                _testFolder = index >= 0 && index + 1 < args.Length;
                return _folder = _testFolder ? args[index + 1] : Application.persistentDataPath;
            }
        }

        /// <summary>Rodando com <c>-savedlogin</c>: o menu também não mexe no servidor escolhido (PlayerPrefs) de quem joga.</summary>
        public static bool IsTestFolder
        {
            get
            {
                _ = Folder;
                return _testFolder;
            }
        }

        /// <summary>Dá para guardar o token neste sistema (Windows).</summary>
        public static bool CanKeepToken
        {
            get
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                return true;
#else
                return false;
#endif
            }
        }

        public static string Account => Load().RememberAccount ? Load().Account : "";
        public static bool RememberAccount => Load().RememberAccount;
        public static bool StaySignedIn => Load().StaySignedIn && CanKeepToken;

        /// <summary>Grava as escolhas da tela de entrada depois de um login que deu certo.</summary>
        public static void SaveChoices(string account, bool rememberAccount, bool staySignedIn)
        {
            var data = Load();
            data.RememberAccount = rememberAccount || staySignedIn;
            data.StaySignedIn = staySignedIn;
            data.Account = data.RememberAccount ? account ?? "" : "";
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(Path.Combine(Folder, AccountFile), JsonUtility.ToJson(data));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Login] Não consegui salvar a conta: {ex.Message}");
            }
        }

        private static AccountData Load()
        {
            if (_account != null) return _account;
            try
            {
                var path = Path.Combine(Folder, AccountFile);
                if (File.Exists(path)) _account = JsonUtility.FromJson<AccountData>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Login] Não consegui ler a conta salva: {ex.Message}");
            }
            return _account ??= new AccountData();
        }

        // ===== Token do computador ("Manter conectado") =====

        /// <summary>Token salvo para o servidor (endereço da API), ou vazio.</summary>
        public static string Token(string server)
        {
            foreach (var entry in LoadTokens().Entries)
                if (entry.Server == server) return entry.Token;
            return "";
        }

        public static void SaveToken(string server, string token)
        {
            if (!CanKeepToken || string.IsNullOrEmpty(token)) return;
            var data = LoadTokens();
            data.Entries.RemoveAll(e => e.Server == server);
            data.Entries.Add(new TokenEntry { Server = server, Token = token });
            SaveTokens(data);
        }

        /// <summary>Esquece o token do servidor ("Sair da conta" ou o servidor recusou).</summary>
        public static void ForgetToken(string server)
        {
            var data = LoadTokens();
            if (data.Entries.RemoveAll(e => e.Server == server) > 0) SaveTokens(data);
        }

        /// <summary>Existe o arquivo do token com algum servidor dentro (testes).</summary>
        public static bool HasAnyToken => LoadTokens().Entries.Count > 0;

        private static TokenData LoadTokens()
        {
            try
            {
                var path = Path.Combine(Folder, TokenFile);
                if (!CanKeepToken || !File.Exists(path)) return new TokenData();
                var plain = Unprotect(File.ReadAllBytes(path));
                return plain == null ? new TokenData() : JsonUtility.FromJson<TokenData>(Encoding.UTF8.GetString(plain)) ?? new TokenData();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Login] Não consegui ler a sessão salva: {ex.Message}");
                return new TokenData();
            }
        }

        private static void SaveTokens(TokenData data)
        {
            try
            {
                var path = Path.Combine(Folder, TokenFile);
                if (data.Entries.Count == 0)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }
                var cipher = Protect(Encoding.UTF8.GetBytes(JsonUtility.ToJson(data)));
                if (cipher == null) return;
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes(path, cipher);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Login] Não consegui salvar a sessão: {ex.Message}");
            }
        }

        // ===== DPAPI (crypt32) =====

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        private const int UiForbidden = 0x1;

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DataBlob dataIn, string description, ref DataBlob entropy, IntPtr reserved,
            IntPtr prompt, int flags, ref DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy, IntPtr reserved,
            IntPtr prompt, int flags, ref DataBlob dataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        private static byte[] Protect(byte[] plain) => Run(plain, true);

        private static byte[] Unprotect(byte[] cipher) => Run(cipher, false);

        private static byte[] Run(byte[] input, bool protect)
        {
            var dataIn = Pin(input);
            var entropy = Pin(Entropy);
            var dataOut = new DataBlob();
            try
            {
                var ok = protect
                    ? CryptProtectData(ref dataIn, "Terras de Astralis", ref entropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref dataOut)
                    : CryptUnprotectData(ref dataIn, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref dataOut);
                if (!ok)
                {
                    Debug.LogWarning($"[Login] DPAPI recusou ({(protect ? "cifrar" : "abrir")}), erro {Marshal.GetLastWin32Error()}");
                    return null;
                }
                var result = new byte[dataOut.Size];
                Marshal.Copy(dataOut.Data, result, 0, dataOut.Size);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(dataIn.Data);
                Marshal.FreeHGlobal(entropy.Data);
                if (dataOut.Data != IntPtr.Zero) LocalFree(dataOut.Data);
            }
        }

        private static DataBlob Pin(byte[] bytes)
        {
            var blob = new DataBlob { Size = bytes.Length, Data = Marshal.AllocHGlobal(Math.Max(1, bytes.Length)) };
            Marshal.Copy(bytes, 0, blob.Data, bytes.Length);
            return blob;
        }
#else
        private static byte[] Protect(byte[] plain) => null;

        private static byte[] Unprotect(byte[] cipher) => null;
#endif
    }
}
