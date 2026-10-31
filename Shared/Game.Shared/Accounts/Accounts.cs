using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Shared.Text;

namespace Game.Shared.Accounts
{
    /// <summary>Classes jogáveis do MVP (GDD: uma corpo a corpo e uma à distância).</summary>
    public enum CharacterClass
    {
        /// <summary>Lâmina: corpo a corpo.</summary>
        Blade = 0,

        /// <summary>Arcanista: à distância.</summary>
        Arcanist = 1,

        /// <summary>Ranger: arqueira, à distância com o arco.</summary>
        Ranger = 2,

        /// <summary>Guardião: tank corpo a corpo, com machado e escudo.</summary>
        Guardian = 3,
    }

    /// <summary>
    /// O que muda de uma classe para outra fora das skills: o id do modelo (o TypeId que o servidor manda), a chave do
    /// nome e a arma inicial.
    /// </summary>
    public static class ClassInfo
    {
        public static readonly CharacterClass[] All = { CharacterClass.Blade, CharacterClass.Arcanist, CharacterClass.Ranger, CharacterClass.Guardian };

        /// <summary>Id do modelo da classe (Visuals/class/&lt;id&gt;) e o TypeId do EntitySpawned do jogador.</summary>
        public static string TypeId(CharacterClass characterClass) => characterClass switch
        {
            CharacterClass.Arcanist => "arcanist",
            CharacterClass.Ranger => "ranger",
            CharacterClass.Guardian => "guardian",
            _ => "blade",
        };

        /// <summary>A classe pelo TypeId (desconhecido = Lâmina).</summary>
        public static CharacterClass FromTypeId(string typeId) => typeId switch
        {
            "arcanist" => CharacterClass.Arcanist,
            "ranger" => CharacterClass.Ranger,
            "guardian" => CharacterClass.Guardian,
            _ => CharacterClass.Blade,
        };

        /// <summary>Chave do nome da classe no arquivo de idioma.</summary>
        public static string NameKey(CharacterClass characterClass) => characterClass switch
        {
            CharacterClass.Arcanist => "class.arcanist",
            CharacterClass.Ranger => "class.ranger",
            CharacterClass.Guardian => "class.guardian",
            _ => "class.blade",
        };

        /// <summary>Arma com que o personagem nasce.</summary>
        public static string StarterWeapon(CharacterClass characterClass) => characterClass switch
        {
            CharacterClass.Arcanist => "cajado_ferro",
            CharacterClass.Ranger => "arco_ferro",
            CharacterClass.Guardian => "machado_ferro",
            _ => "espada_ferro",
        };

        /// <summary>Começo do id das armas da classe (espada_, cajado_, arco_ ou machado_ + grau).</summary>
        public static string WeaponPrefix(CharacterClass characterClass) => characterClass switch
        {
            CharacterClass.Arcanist => "cajado_",
            CharacterClass.Ranger => "arco_",
            CharacterClass.Guardian => "machado_",
            _ => "espada_",
        };

        /// <summary>
        /// Quanto do dano dos monstros a classe recebe (1 = tudo). O Guardião segura o monstro para o grupo, então
        /// apanha menos.
        /// </summary>
        public static double DamageTakenFactor(CharacterClass characterClass) =>
            characterClass == CharacterClass.Guardian ? 0.85 : 1.0;
    }

    /// <summary>Regras de conta, senha e nome de personagem, iguais no cliente (aviso na hora) e no servidor (decisão).</summary>
    public static class AccountRules
    {
        public const int MaxCharactersPerAccount = 3;
        public const int MinAccountLength = 4;
        public const int MaxAccountLength = 20;
        public const int MinPasswordLength = 8;
        public const int MaxPasswordLength = 64;
        public const int MinCharacterNameLength = 3;
        public const int MaxCharacterNameLength = 14;

        public static string ClassDisplayName(CharacterClass characterClass) => characterClass switch
        {
            CharacterClass.Blade => Loc.T("class.blade"),
            CharacterClass.Arcanist => Loc.T("class.arcanist"),
            CharacterClass.Ranger => Loc.T("class.ranger"),
            CharacterClass.Guardian => Loc.T("class.guardian"),
            _ => Loc.T("class.unknown"),
        };

        /// <summary>Forma canônica do nome da conta (minúsculas, sem espaços nas pontas).</summary>
        public static string NormalizeAccount(string account) => (account ?? "").Trim().ToLowerInvariant();

        /// <summary>Nulo se válida; senão, o motivo (chave de tradução).</summary>
        public static LocalText? ValidateAccount(string account)
        {
            var value = NormalizeAccount(account);
            if (value.Length < MinAccountLength || value.Length > MaxAccountLength)
                return new LocalText("account.length", MinAccountLength, MaxAccountLength);
            foreach (var c in value)
            {
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_')
                    return new LocalText("account.chars");
            }
            return null;
        }

        public static LocalText? ValidatePassword(string password)
        {
            password ??= "";
            if (password.Length < MinPasswordLength) return new LocalText("password.short", MinPasswordLength);
            if (password.Length > MaxPasswordLength) return new LocalText("password.long", MaxPasswordLength);
            var hasLetter = false;
            var hasDigit = false;
            foreach (var c in password)
            {
                if (char.IsLetter(c)) hasLetter = true;
                if (char.IsDigit(c)) hasDigit = true;
            }
            return hasLetter && hasDigit ? null : new LocalText("password.mix");
        }

        /// <summary>Nome do personagem: letras (acentos permitidos), números depois da primeira letra, sem espaços.</summary>
        public static LocalText? ValidateCharacterName(string name)
        {
            var value = (name ?? "").Trim();
            if (value.Length < MinCharacterNameLength || value.Length > MaxCharacterNameLength)
                return new LocalText("charname.length", MinCharacterNameLength, MaxCharacterNameLength);
            if (!char.IsLetter(value[0])) return new LocalText("charname.first");
            foreach (var c in value)
            {
                if (!char.IsLetterOrDigit(c)) return new LocalText("charname.chars");
            }
            return null;
        }

        /// <summary>
        /// Chave para conferir nomes repetidos: sem acentos e em minúsculas ("Ação" e "acao" são o mesmo nome).
        /// </summary>
        public static string NameKey(string name)
        {
            var decomposed = (name ?? "").Trim().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(char.ToLowerInvariant(c));
            }
            return builder.ToString();
        }
    }

    // ===== Mensagens da API de login (JSON). Campos públicos: leem igual no Unity (JsonUtility) e no .NET. =====

    [Serializable]
    public sealed class Credentials
    {
        public string Account = "";
        public string Password = "";

        /// <summary>
        /// Versão dos termos e da política que o jogador aceitou agora (<see cref="Legal.Version"/>). Obrigatória para
        /// criar conta; no login, só quando a versão mudou desde o último aceite.
        /// </summary>
        public string AcceptTerms = "";
    }

    [Serializable]
    public sealed class LoginResponse
    {
        public string Token = "";
        public int ExpiresInSeconds;
    }

    [Serializable]
    public sealed class ApiError
    {
        /// <summary>Motivo em português (para logs e ferramentas).</summary>
        public string Error = "";

        /// <summary>Motivo como chave de tradução e argumentos (o cliente mostra no idioma dele).</summary>
        public string Key = "";

        public string[] Args = Array.Empty<string>();

        public static ApiError From(LocalText text) => new ApiError { Error = text.ToString(), Key = text.Key, Args = text.Args };

        /// <summary>O motivo no idioma carregado (cai no texto em português se não vier chave).</summary>
        public string Display => !string.IsNullOrEmpty(Key) ? Loc.Format(LocalText.FromParts(Key, Args)) : Error;
    }

    [Serializable]
    public sealed class CharacterSummary
    {
        public int Id;
        public string Name = "";
        public int ClassId;
        public int Level;

        /// <summary>Cores de cabelo e roupa, penteado e tom de pele (índices de <see cref="Game.Shared.Items.Appearance"/>).</summary>
        public int Hair;
        public int Cloth;
        public int Head;
        public int Skin;
        public bool HideHelmet;

        /// <summary>Grau da couraça, das luvas e das botas vestidas (0 = sem), para a prévia da seleção.</summary>
        public int ArmorGrade;
        public int GlovesGrade;
        public int BootsGrade;
    }

    [Serializable]
    public sealed class CharacterList
    {
        public List<CharacterSummary> Characters = new List<CharacterSummary>();
    }

    [Serializable]
    public sealed class CreateCharacterRequest
    {
        public string Name = "";
        public int ClassId;
        public int Hair;
        public int Cloth;
        public int Head;
        public int Skin;
    }
}
