using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Game.Shared.Accounts;
using Game.Shared.Net;
using Game.Shared.Text;
using UnityEngine;

namespace Game.Client.Net
{
    /// <summary>Resposta da API de login: o valor ou a mensagem de erro para mostrar ao jogador.</summary>
    public readonly struct ApiResult<T>
    {
        public bool Ok { get; }
        public T Value { get; }
        public string Error { get; }

        /// <summary>Chave do motivo (ex.: "auth.terms_updated"), para o menu reagir a casos específicos.</summary>
        public string ErrorKey { get; }

        public ApiResult(bool ok, T value, string error, string errorKey = "")
        {
            Ok = ok;
            Value = value;
            Error = error;
            ErrorKey = errorKey ?? "";
        }
    }

    /// <summary>
    /// Cliente da API HTTP de contas do servidor. Usa HttpClient (funciona no Unity sem a opção de "HTTP inseguro");
    /// o JSON é o mesmo do servidor (DTOs do Shared com JsonUtility).
    /// Endereço: IP usa HTTP na porta do jogo + 1 (desenvolvimento); domínio usa HTTPS (produção, atrás do Caddy);
    /// o servidor.txt ou o -api trocam (<see cref="GameMode.ApiOverride"/>).
    /// </summary>
    public sealed class AccountClient
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        private readonly string _baseUrl;

        public string Token { get; private set; } = "";

        public AccountClient(string host, int gamePort)
        {
            _baseUrl = (GameMode.ApiOverride.Length > 0 ? GameMode.ApiOverride : ServerAddress.ApiUrl(host, gamePort)).TrimEnd('/');
        }

        /// <summary>Continua com o login que já existe (trocar de personagem sem entrar de novo).</summary>
        public void UseToken(string token) => Token = token ?? "";

        /// <param name="acceptTerms">Versão dos termos que o jogador aceitou (<see cref="Legal.Version"/>); o servidor exige.</param>
        public Task<ApiResult<ApiError>> RegisterAsync(string account, string password, string acceptTerms) =>
            SendAsync<ApiError>(HttpMethod.Post, "/register", new Credentials { Account = account, Password = password, AcceptTerms = acceptTerms });

        /// <param name="acceptTerms">Só quando o servidor pediu o aceite de termos novos ("auth.terms_updated").</param>
        /// <param name="remember">"Manter conectado": a resposta traz também o token do computador.</param>
        public async Task<ApiResult<LoginResponse>> LoginAsync(string account, string password, string acceptTerms = "", bool remember = false)
        {
            var result = await SendAsync<LoginResponse>(HttpMethod.Post, "/login",
                new Credentials { Account = account, Password = password, AcceptTerms = acceptTerms, Remember = remember });
            if (result.Ok) Token = result.Value.Token;
            return result;
        }

        /// <summary>Entra com o token do computador ("Manter conectado"); a resposta traz o token novo para guardar.</summary>
        public async Task<ApiResult<LoginResponse>> DeviceLoginAsync(string deviceToken, string acceptTerms = "")
        {
            var result = await SendAsync<LoginResponse>(HttpMethod.Post, "/login/device",
                new DeviceLogin { DeviceToken = deviceToken, AcceptTerms = acceptTerms });
            if (result.Ok) Token = result.Value.Token;
            return result;
        }

        /// <summary>"Sair da conta": o servidor esquece o token do computador e encerra a sessão.</summary>
        public async Task<ApiResult<ApiError>> LogoutAsync(string deviceToken)
        {
            var result = await SendAsync<ApiError>(HttpMethod.Post, "/logout", new DeviceLogin { DeviceToken = deviceToken ?? "" });
            Token = "";
            return result;
        }

        public Task<ApiResult<CharacterList>> ListCharactersAsync() => SendAsync<CharacterList>(HttpMethod.Get, "/characters", null);

        public Task<ApiResult<CharacterSummary>> CreateCharacterAsync(string name, CharacterClass characterClass, int hair = 0, int cloth = 0,
            int head = 0, int skin = 0) =>
            SendAsync<CharacterSummary>(HttpMethod.Post, "/characters",
                new CreateCharacterRequest { Name = name, ClassId = (int)characterClass, Hair = hair, Cloth = cloth, Head = head, Skin = skin });

        private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, object body)
        {
            try
            {
                using var request = new HttpRequestMessage(method, _baseUrl + path);
                if (body != null) request.Content = new StringContent(JsonUtility.ToJson(body), Encoding.UTF8, "application/json");
                if (!string.IsNullOrEmpty(Token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

                using var response = await Http.SendAsync(request);
                var text = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode) return new ApiResult<T>(true, JsonUtility.FromJson<T>(text), "");

                var error = string.IsNullOrEmpty(text) ? null : JsonUtility.FromJson<ApiError>(text);
                return new ApiResult<T>(false, default, error == null || (string.IsNullOrEmpty(error.Error) && string.IsNullOrEmpty(error.Key)) ? Loc.T("net.http_error", (int)response.StatusCode) : error.Display,
                    error?.Key);
            }
            catch (TaskCanceledException)
            {
                return new ApiResult<T>(false, default, Loc.T("net.api_timeout"));
            }
            catch (HttpRequestException)
            {
                return new ApiResult<T>(false, default, Loc.T("net.api_unreachable"));
            }
            catch (ArgumentException)
            {
                return new ApiResult<T>(false, default, Loc.T("net.api_bad_response"));
            }
        }
    }
}
