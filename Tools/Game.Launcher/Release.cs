using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Game.Launcher
{
    /// <summary>
    /// Uma versão publicada (o <c>versao.json</c> na VPS): cada arquivo do jogo com o tamanho e o SHA-256. Os arquivos
    /// ficam em <c>arquivos/&lt;sha256&gt;.gz</c> (compactados e nomeados pelo conteúdo), então publicar uma versão nova
    /// nunca estraga a que está no ar: o <c>versao.json</c> sobe por último.
    /// </summary>
    [DataContract]
    public sealed class Release
    {
        public const string FileName = "versao.json";

        /// <summary>A cópia da última versão instalada, na pasta do jogo.</summary>
        public const string InstalledFileName = "versao-instalada.json";

        [DataMember(Name = "versao", Order = 0)] public string Version { get; set; } = "";
        [DataMember(Name = "novidades", Order = 1)] public string Notes { get; set; } = "";
        [DataMember(Name = "jogo", Order = 2)] public string Game { get; set; } = "TerrasDeAstralis.exe";
        [DataMember(Name = "arquivos", Order = 3)] public List<ReleaseFile> Files { get; set; } = new List<ReleaseFile>();

        /// <summary>
        /// As versões anteriores, da mais nova para a mais antiga (o launcher mostra numa linha do tempo). Os launchers
        /// antigos ignoram o campo.
        /// </summary>
        [DataMember(Name = "historico", Order = 4, EmitDefaultValue = false)] public List<ReleaseNote>? History { get; set; }

        /// <summary>Quantas versões anteriores o histórico guarda.</summary>
        public const int HistorySize = 30;

        /// <summary>Esta versão e as anteriores, da mais nova para a mais antiga.</summary>
        public IEnumerable<ReleaseNote> Timeline()
        {
            yield return new ReleaseNote { Version = Version, Notes = Notes };
            if (History == null) yield break;
            foreach (var note in History) yield return note;
        }

        public static List<ReleaseNote> ParseNotes(Stream json) =>
            (List<ReleaseNote>)new DataContractJsonSerializer(typeof(List<ReleaseNote>)).ReadObject(json);

        public static Release Parse(Stream json)
        {
            var serializer = new DataContractJsonSerializer(typeof(Release));
            return (Release)serializer.ReadObject(json);
        }

        public static Release? Load(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                using var stream = File.OpenRead(path);
                return Parse(stream);
            }
            catch (SerializationException)
            {
                return null;
            }
        }

        public void Save(string path)
        {
            using var stream = File.Create(path);
            using var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, true, true);
            new DataContractJsonSerializer(typeof(Release)).WriteObject(writer, this);
        }
    }

    /// <summary>Uma versão na linha do tempo: o número e as novidades.</summary>
    [DataContract]
    public sealed class ReleaseNote
    {
        [DataMember(Name = "versao", Order = 0)] public string Version { get; set; } = "";
        [DataMember(Name = "novidades", Order = 1)] public string Notes { get; set; } = "";

        /// <summary>A data vem do próprio número (0.1.1-20261103.2052 = 03/11/2026 20:52); nula se não tiver.</summary>
        public DateTime? Date
        {
            get
            {
                var dash = Version.LastIndexOf('-');
                if (dash < 0) return null;
                return DateTime.TryParseExact(Version.Substring(dash + 1), "yyyyMMdd.HHmm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date) ? date : (DateTime?)null;
            }
        }
    }

    [DataContract]
    public sealed class ReleaseFile
    {
        /// <summary>Caminho dentro da pasta do jogo, com <c>/</c>.</summary>
        [DataMember(Name = "caminho", Order = 0)] public string Path { get; set; } = "";
        [DataMember(Name = "tamanho", Order = 1)] public long Size { get; set; }
        [DataMember(Name = "sha256", Order = 2)] public string Sha256 { get; set; } = "";

        /// <summary>Tamanho do <c>.gz</c> (o que vai ser baixado).</summary>
        [DataMember(Name = "compactado", Order = 3)] public long Packed { get; set; }

        public string LocalPath(string folder) => System.IO.Path.Combine(folder, Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
    }
}
