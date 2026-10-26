using System;
using Game.Shared.Text;

namespace Game.Shared.Combat
{
    /// <summary>
    /// Dados de uma skill. Campos públicos para o Unity e serializadores JSON lerem e gravarem direto;
    /// mais adiante estes dados vêm da planilha de design.
    /// </summary>
    [Serializable]
    public sealed class SkillData
    {
        /// <summary>Identificador único e estável (usado pela rede e pelo banco).</summary>
        public string Id = "";

        /// <summary>Nome no idioma do jogador (chave <c>skill.&lt;id&gt;</c>).</summary>
        public string Name => Loc.T(Loc.Key("skill.", Id));

        /// <summary>Físico ou mágico: define as rotas de combo e se gasta MP.</summary>
        public SkillKind Kind = SkillKind.Physical;

        /// <summary>Dano base antes de ataque, combo, defesa e crítico.</summary>
        public int BaseDamage = 100;

        /// <summary>
        /// Quanto do poder do personagem (ataque ou magia) entra no golpe: 1 no ataque básico, mais nas skills que custam
        /// MP e têm recarga longa. Sem isso o poder (centenas de pontos) apagava a diferença de dano base entre as skills, e
        /// nenhuma rendia mais que apertar o básico.
        /// </summary>
        public float PowerScale = 1f;

        /// <summary>Alcance em metros, medido até a borda do alvo.</summary>
        public float Range = 3f;

        /// <summary>Recarga em segundos.</summary>
        public float CooldownSeconds = 1f;

        /// <summary>0 = só o alvo; maior que 0 = atinge todos neste raio em volta do alvo.</summary>
        public float AreaRadius;

        /// <summary>Mana gasta por uso.</summary>
        public int MpCost;

        /// <summary>Tempo em que o personagem fica parado ao usar a skill.</summary>
        public float LockSeconds = 0.15f;

        /// <summary>
        /// Tempo de conjuração: enquanto o golpe acontece, outra skill espera na fila (fora do combo, onde quem dita o
        /// ritmo é a barra). Igual à duração da animação do golpe.
        /// </summary>
        public float CastSeconds = 0.5f;

        /// <summary>Construtor vazio para serializadores.</summary>
        public SkillData() { }

        /// <summary>Cria uma skill.</summary>
        public SkillData(string id, SkillKind kind, int baseDamage, float range, float cooldownSeconds,
            float areaRadius = 0f, int mpCost = 0, float lockSeconds = 0.15f, float castSeconds = 0.5f, float powerScale = 1f)
        {
            Id = id;
            Kind = kind;
            BaseDamage = baseDamage;
            Range = range;
            CooldownSeconds = cooldownSeconds;
            AreaRadius = areaRadius;
            MpCost = mpCost;
            LockSeconds = lockSeconds;
            CastSeconds = castSeconds;
            PowerScale = powerScale;
        }

        /// <summary>Lança <see cref="ArgumentException"/> se algum valor for inválido.</summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("A skill precisa de Id.", nameof(Id));
            if (BaseDamage < 0) throw new ArgumentException($"{Id}: dano base não pode ser negativo.", nameof(BaseDamage));
            if (Range <= 0) throw new ArgumentException($"{Id}: alcance deve ser maior que zero.", nameof(Range));
            if (CooldownSeconds < 0) throw new ArgumentException($"{Id}: recarga não pode ser negativa.", nameof(CooldownSeconds));
            if (AreaRadius < 0) throw new ArgumentException($"{Id}: raio não pode ser negativo.", nameof(AreaRadius));
            if (MpCost < 0) throw new ArgumentException($"{Id}: custo de MP não pode ser negativo.", nameof(MpCost));
            if (LockSeconds < 0) throw new ArgumentException($"{Id}: tempo parado não pode ser negativo.", nameof(LockSeconds));
            if (PowerScale <= 0) throw new ArgumentException($"{Id}: escala de poder deve ser maior que zero.", nameof(PowerScale));
            if (CastSeconds < 0) throw new ArgumentException($"{Id}: conjuração não pode ser negativa.", nameof(CastSeconds));
        }
    }
}
