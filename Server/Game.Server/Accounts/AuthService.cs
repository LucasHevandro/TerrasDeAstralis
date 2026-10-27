using System.Collections.Concurrent;
using Game.Server.Persistence;
using Game.Shared.Accounts;
using Game.Shared.Items;
using Game.Shared.Net;
using Game.Shared.Progression;
using Game.Shared.Text;
using Game.Shared.World;

namespace Game.Server.Accounts;

/// <summary>Resultado de uma operação de conta: o valor ou a mensagem de erro com o código HTTP correspondente.</summary>
public readonly record struct AuthResult<T>(bool Ok, T? Value, LocalText Error, int StatusCode)
{
    public static AuthResult<T> Success(T value) => new(true, value, LocalText.Empty, 200);
    public static AuthResult<T> Fail(LocalText error, int status = 400) => new(false, default, error, status);
}

/// <summary>
/// Contas, login e personagens. Usada pela API HTTP (cadastro, login, lista e criação de personagens) e pelo
/// servidor do jogo (conferir o token e o personagem do Hello).
/// </summary>
public sealed class AuthService
{
    /// <summary>Tentativas erradas seguidas antes de travar a conta.</summary>
    public const int MaxFailedLogins = 5;

    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    /// <summary>Contas novas que um mesmo IP pode criar em <see cref="AccountWindow"/> (contra quem cria contas sem fim).</summary>
    public const int MaxAccountsPerIp = 3;

    public static readonly TimeSpan AccountWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// Pedidos de login e cadastro que um mesmo IP pode fazer por minuto. Cada um custa um hash de senha, que é
    /// pesado de propósito: sem esse limite, um script ocupa a CPU da VPS e o jogo fica com lag.
    /// </summary>
    public const int MaxAuthRequestsPerMinute = 10;

    /// <summary>Hashes de senha calculados ao mesmo tempo; os outros esperam a vez e sobra CPU para o loop do jogo.</summary>
    public static readonly int MaxConcurrentHashes = Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>Acima disso, as listas por IP e por conta são limpas das entradas que já venceram.</summary>
    private const int PruneAbove = 10_000;

    private static readonly LocalText WrongCredentials = new("auth.wrong_credentials");

    private readonly IGameStore _store;
    private readonly PasswordHasher _hasher;
    private readonly SessionTokens _tokens;
    private readonly Func<DateTimeOffset> _now;
    private readonly ConcurrentDictionary<string, (int Failures, DateTimeOffset LockedUntil)> _throttle = new();
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _accountsByIp = new();
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _requestsByIp = new();
    private readonly SemaphoreSlim _hashGate = new(MaxConcurrentHashes);
    private readonly string _dummyHash;

    /// <summary>Ids únicos de item do servidor (o mundo usa o mesmo contador).</summary>
    public ItemUidAllocator Uids { get; }

    private readonly HashSet<string> _gmAccounts;
    private readonly string? _termsVersion;

    /// <param name="gmAccounts">Contas tratadas como GM mesmo sem a marca no banco (opção --gm, para testes locais).</param>
    /// <param name="termsVersion">
    /// Versão dos termos que o cadastro exige e que o login pede de novo quando muda (o servidor passa
    /// <see cref="Legal.Version"/>); nula não confere (testes das outras regras).
    /// </param>
    public AuthService(IGameStore store, PasswordHasher hasher, SessionTokens tokens, Func<DateTimeOffset>? now = null,
        ItemUidAllocator? uids = null, IEnumerable<string>? gmAccounts = null, string? termsVersion = null)
    {
        _termsVersion = termsVersion;
        _gmAccounts = new HashSet<string>((gmAccounts ?? Array.Empty<string>()).Select(AccountRules.NormalizeAccount));
        Uids = uids ?? new ItemUidAllocator();
        _store = store;
        _hasher = hasher;
        _tokens = tokens;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        // Usado quando a conta não existe, para o tempo de resposta ser igual ao de senha errada.
        _dummyHash = hasher.Hash("conta-inexistente-123");
    }

    public SessionTokens Tokens => _tokens;

    /// <param name="clientIp">IP de quem pede (nulo = sem limite por IP, como nos testes e nas ferramentas locais).</param>
    public async Task<AuthResult<string>> RegisterAsync(Credentials credentials, string? clientIp = null)
    {
        var account = AccountRules.NormalizeAccount(credentials.Account);
        var error = AccountRules.ValidateAccount(account) ?? AccountRules.ValidatePassword(credentials.Password);
        if (error != null) return AuthResult<string>.Fail(error);
        if (_termsVersion != null && credentials.AcceptTerms != _termsVersion) return AuthResult<string>.Fail(new LocalText("auth.terms_required"));
        var now = _now();
        if (!AllowRequest(clientIp, now)) return AuthResult<string>.Fail(new LocalText("auth.try_again"), 429);
        if (clientIp != null && RecentAccounts(clientIp, now) >= MaxAccountsPerIp)
            return AuthResult<string>.Fail(new LocalText("auth.too_many_accounts", MaxAccountsPerIp), 429);
        // Nome já usado: recusa antes do hash, para o pedido repetido não custar CPU.
        if (await _store.FindAccountAsync(account) != null) return AuthResult<string>.Fail(new LocalText("auth.account_exists"), 409);

        try
        {
            var hash = await HashAsync(() => _hasher.Hash(credentials.Password));
            await _store.CreateAccountAsync(account, hash, _termsVersion ?? credentials.AcceptTerms ?? "");
            if (clientIp != null)
            {
                var created = _accountsByIp.GetOrAdd(clientIp, _ => new List<DateTimeOffset>());
                lock (created) created.Add(now);
            }
            return AuthResult<string>.Success(account);
        }
        catch (StoreConflictException ex)
        {
            return AuthResult<string>.Fail(ex.Text, 409);
        }
    }

    /// <summary>
    /// Troca a senha da conta sem pedir a antiga (comando --set-password, rodado por quem administra o servidor).
    /// Nulo = trocou; senão, o motivo (senha fora das regras ou conta que não existe).
    /// </summary>
    public async Task<LocalText?> ResetPasswordAsync(string account, string newPassword)
    {
        var error = AccountRules.ValidatePassword(newPassword);
        if (error != null) return error;
        var hash = await HashAsync(() => _hasher.Hash(newPassword));
        return await _store.SetPasswordHashAsync(AccountRules.NormalizeAccount(account), hash) ? null : new LocalText("auth.no_account");
    }

    /// <summary>Quantas contas o IP criou na janela (e esquece as mais velhas).</summary>
    private int RecentAccounts(string ip, DateTimeOffset now)
    {
        if (!_accountsByIp.TryGetValue(ip, out var created)) return 0;
        lock (created)
        {
            created.RemoveAll(at => now - at >= AccountWindow);
            return created.Count;
        }
    }

    /// <param name="clientIp">
    /// IP de quem pede (nulo = sem limite por IP). A trava de senhas erradas vale para a conta naquele IP, para
    /// ninguém conseguir travar a conta de outro jogador errando a senha de propósito.
    /// </param>
    public async Task<AuthResult<LoginResponse>> LoginAsync(Credentials credentials, string? clientIp = null)
    {
        var name = AccountRules.NormalizeAccount(credentials.Account);
        // Nome maior que o permitido não existe: recusa sem hash e sem guardar nada.
        if (name.Length > AccountRules.MaxAccountLength) return AuthResult<LoginResponse>.Fail(WrongCredentials, 401);
        var now = _now();
        if (!AllowRequest(clientIp, now)) return AuthResult<LoginResponse>.Fail(new LocalText("auth.try_again"), 429);
        var throttleKey = clientIp == null ? name : $"{name}|{clientIp}";
        if (_throttle.TryGetValue(throttleKey, out var state) && now < state.LockedUntil)
        {
            var minutes = Math.Ceiling((state.LockedUntil - now).TotalMinutes);
            return AuthResult<LoginResponse>.Fail(new LocalText("auth.locked", minutes), 429);
        }

        var account = await _store.FindAccountAsync(name);
        var valid = await HashAsync(() => _hasher.Verify(credentials.Password ?? "", account?.PasswordHash ?? _dummyHash)) && account != null;
        if (!valid)
        {
            RegisterFailure(throttleKey, now);
            return AuthResult<LoginResponse>.Fail(WrongCredentials, 401);
        }
        if (account!.IsBanned(now)) return AuthResult<LoginResponse>.Fail(Banned(account), 403);

        _throttle.TryRemove(throttleKey, out _);
        // Termos novos desde o último aceite: o cliente mostra e manda de novo com o aceite.
        if (_termsVersion != null && account.TermsVersion != _termsVersion)
        {
            if (credentials.AcceptTerms != _termsVersion) return AuthResult<LoginResponse>.Fail(new LocalText("auth.terms_updated", _termsVersion), 403);
            await _store.AcceptTermsAsync(account.Id, _termsVersion);
        }
        var rehash = _hasher.NeedsRehash(account!.PasswordHash) ? await HashAsync(() => _hasher.Hash(credentials.Password!)) : null;
        await _store.UpdateLoginAsync(account.Id, rehash);
        return AuthResult<LoginResponse>.Success(new LoginResponse
        {
            Token = _tokens.Create(account.Id), ExpiresInSeconds = (int)_tokens.Lifetime.TotalSeconds,
        });
    }

    public async Task<AuthResult<CharacterList>> ListCharactersAsync(string? token)
    {
        if (_tokens.Validate(token) is not { } accountId) return AuthResult<CharacterList>.Fail(new LocalText("auth.session_expired"), 401);
        var list = new CharacterList();
        foreach (var c in await _store.ListCharactersAsync(accountId))
            list.Characters.Add(new CharacterSummary
            {
                Id = c.Id, Name = c.Name, ClassId = (int)c.Class, Level = c.Level, Hair = c.Hair, Cloth = c.Cloth,
                Head = c.Head, Skin = c.Skin, HideHelmet = c.HideHelmet,
                ArmorGrade = Game.Shared.Items.Appearance.GradeOf(c.ArmorId, Game.Shared.Items.EquipSlot.Armor),
                GlovesGrade = Game.Shared.Items.Appearance.GradeOf(c.GlovesId, Game.Shared.Items.EquipSlot.Gloves),
                BootsGrade = Game.Shared.Items.Appearance.GradeOf(c.BootsId, Game.Shared.Items.EquipSlot.Boots),
            });
        return AuthResult<CharacterList>.Success(list);
    }

    public async Task<AuthResult<CharacterSummary>> CreateCharacterAsync(string? token, CreateCharacterRequest request)
    {
        if (_tokens.Validate(token) is not { } accountId) return AuthResult<CharacterSummary>.Fail(new LocalText("auth.session_expired"), 401);
        var error = AccountRules.ValidateCharacterName(request.Name);
        if (error != null) return AuthResult<CharacterSummary>.Fail(error);
        if (!Enum.IsDefined(typeof(CharacterClass), request.ClassId)) return AuthResult<CharacterSummary>.Fail(new LocalText("auth.bad_class"));

        try
        {
            var created = await _store.CreateCharacterAsync(accountId, request.Name, (CharacterClass)request.ClassId,
                InitialCharacter((CharacterClass)request.ClassId, Uids.Next) with
                {
                    Hair = Game.Shared.Items.Appearance.Hair(request.Hair), Cloth = Game.Shared.Items.Appearance.Cloth(request.Cloth),
                    Head = Game.Shared.Items.Appearance.Head(request.Head), Skin = Game.Shared.Items.Appearance.Skin(request.Skin),
                });
            return AuthResult<CharacterSummary>.Success(new CharacterSummary
            {
                Id = created.Id, Name = created.Name, ClassId = (int)created.Class, Level = created.Level,
                Hair = created.Hair, Cloth = created.Cloth, Head = created.Head, Skin = created.Skin,
            });
        }
        catch (StoreConflictException ex)
        {
            return AuthResult<CharacterSummary>.Fail(ex.Text, 409);
        }
    }

    /// <summary>Para o Hello do jogo: o personagem pedido, se o token for válido e o personagem for da conta.</summary>
    public async Task<AuthResult<CharacterRecord>> ResolveCharacterAsync(string? token, int characterId)
    {
        if (_tokens.Validate(token) is not { } accountId) return AuthResult<CharacterRecord>.Fail(new LocalText("auth.session_expired"), 401);
        var character = await _store.LoadCharacterAsync(characterId);
        if (character == null || character.AccountId != accountId) return AuthResult<CharacterRecord>.Fail(new LocalText("auth.character_not_found"), 404);
        // O token pode ser de antes do banimento: confere a conta de novo na hora de entrar no jogo.
        var account = await _store.FindAccountByIdAsync(accountId);
        if (account == null) return AuthResult<CharacterRecord>.Fail(new LocalText("auth.character_not_found"), 404);
        if (account.IsBanned(_now())) return AuthResult<CharacterRecord>.Fail(Banned(account), 403);
        return AuthResult<CharacterRecord>.Success(character with
        {
            IsGm = account.IsGm || _gmAccounts.Contains(account.Name), MutedUntil = account.MutedUntil,
        });
    }

    /// <summary>Motivo da recusa de uma conta banida (até quando e por quê; permanente se passar de 50 anos).</summary>
    private LocalText Banned(AccountRecord account)
    {
        var until = account.BannedUntil!.Value;
        return until - _now() > TimeSpan.FromDays(365 * 50)
            ? new LocalText("auth.banned_forever", account.BanReason)
            : new LocalText("auth.banned_until", until.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), account.BanReason);
    }

    /// <summary>Valores de um personagem novo: nível 1 com a vida e a mana cheias, na vila.</summary>
    /// <summary>
    /// Personagem novo: atributos iniciais da classe e, com <paramref name="newUid"/>, a arma inicial vestida e
    /// algumas poções na bolsa.
    /// </summary>
    public static CharacterRecord InitialCharacter(CharacterClass characterClass = CharacterClass.Blade, Func<long>? newUid = null)
    {
        var attributes = Attributes.StartingFor(characterClass);
        var stats = StatCalculator.Compute(1, attributes, new Equipment());
        var items = new List<ItemRecord>();
        if (newUid != null)
        {
            items.Add(new ItemRecord(newUid(), ItemContainerKind.Equipment, (int)EquipSlot.Weapon, ClassInfo.StarterWeapon(characterClass), 1));
            items.Add(new ItemRecord(newUid(), ItemContainerKind.Bag, 0, "pocao_hp_p", 5));
        }
        return new CharacterRecord(0, 0, "", characterClass, 1, 0, 0, WorldContent.PlayerSpawnX, WorldContent.PlayerSpawnZ,
            stats.MaxHp, stats.MaxMp, PlayerRules.StartingSp, items, attributes);
    }

    /// <summary>Conta um pedido de login ou cadastro do IP no último minuto; falso se passou do limite.</summary>
    private bool AllowRequest(string? ip, DateTimeOffset now)
    {
        if (ip == null) return true;
        if (_requestsByIp.Count > PruneAbove)
            foreach (var (key, old) in _requestsByIp)
                lock (old)
                    if (old.Count == 0 || now - old.Peek() >= TimeSpan.FromMinutes(1)) _requestsByIp.TryRemove(key, out _);

        var times = _requestsByIp.GetOrAdd(ip, _ => new Queue<DateTimeOffset>());
        lock (times)
        {
            while (times.Count > 0 && now - times.Peek() >= TimeSpan.FromMinutes(1)) times.Dequeue();
            if (times.Count >= MaxAuthRequestsPerMinute) return false;
            times.Enqueue(now);
            return true;
        }
    }

    /// <summary>Roda um hash de senha respeitando o limite de hashes ao mesmo tempo.</summary>
    private async Task<T> HashAsync<T>(Func<T> work)
    {
        await _hashGate.WaitAsync();
        try { return work(); }
        finally { _hashGate.Release(); }
    }

    private void RegisterFailure(string key, DateTimeOffset now)
    {
        if (_throttle.Count > PruneAbove)
            foreach (var (old, s) in _throttle)
                if (now >= s.LockedUntil) _throttle.TryRemove(old, out _);

        _throttle.AddOrUpdate(key,
            _ => (1, DateTimeOffset.MinValue),
            (_, s) =>
            {
                var failures = s.LockedUntil != DateTimeOffset.MinValue && now >= s.LockedUntil ? 1 : s.Failures + 1;
                return failures >= MaxFailedLogins ? (0, now + LockDuration) : (failures, DateTimeOffset.MinValue);
            });
    }
}
