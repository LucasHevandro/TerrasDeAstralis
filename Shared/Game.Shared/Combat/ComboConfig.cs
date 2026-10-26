using System;
using System.Collections.Generic;

namespace Game.Shared.Combat
{
    /// <summary>
    /// Parâmetros do sistema de combo. Posições na barra são normalizadas: 0 = início, 1 = fim.
    /// Os valores padrão são um ponto de partida para os testes de diversão da Fase 1.
    /// </summary>
    public sealed class ComboConfig
    {
        /// <summary>SP gasto para ativar o combo.</summary>
        public int ActivationSpCost { get; set; } = 500;

        /// <summary>Tempo, em segundos, que o marcador leva para percorrer a barra inteira no primeiro ciclo.</summary>
        public double InitialBarDurationSeconds { get; set; } = 1.2;

        /// <summary>A cada acerto a duração da barra é multiplicada por este fator (menor que 1 = mais rápido).</summary>
        public double BarSpeedupFactor { get; set; } = 0.95;

        /// <summary>Duração mínima da barra, em segundos.</summary>
        public double MinBarDurationSeconds { get; set; } = 0.6;

        /// <summary>Centro da janela-alvo na barra.</summary>
        public double WindowCenter { get; set; } = 0.75;

        /// <summary>Largura inicial da janela-alvo.</summary>
        public double InitialWindowWidth { get; set; } = 0.30;

        /// <summary>A cada acerto a largura da janela é multiplicada por este fator.</summary>
        public double WindowShrinkFactor { get; set; } = 0.88;

        /// <summary>Largura mínima da janela.</summary>
        public double MinWindowWidth { get; set; } = 0.08;

        /// <summary>A partir deste número de acertos a janela e a velocidade param de mudar.</summary>
        public int StabilizeAfterHits { get; set; } = 11;

        /// <summary>
        /// Fração da meia-largura da janela, a partir do centro, que conta como Excellent.
        /// Ex.: 0,35 = os 35% mais próximos do centro, de cada lado.
        /// </summary>
        public double ExcellentZoneRatio { get; set; } = 0.35;

        /// <summary>Multiplicador de dano de um aperto Good.</summary>
        public double GoodDamageMultiplier { get; set; } = 1.10;

        /// <summary>Multiplicador de dano de um aperto Excellent.</summary>
        public double ExcellentDamageMultiplier { get; set; } = 1.20;

        /// <summary>Bônus acumulado para o finalizador a cada Good.</summary>
        public double GoodChainBonus { get; set; } = 0.03;

        /// <summary>Bônus acumulado para o finalizador a cada Excellent.</summary>
        public double ExcellentChainBonus { get; set; } = 0.06;

        /// <summary>Teto do bônus acumulado.</summary>
        public double MaxChainBonus { get; set; } = 1.5;

        /// <summary>Acertos mínimos para poder usar o finalizador.</summary>
        public int MinHitsForFinisher { get; set; } = 3;

        /// <summary>
        /// Tolerância extra, em segundos, aplicada dos dois lados da janela.
        /// Zero no jogo offline; online, o servidor e o cliente usam <see cref="OnlineGraceSeconds"/>.
        /// </summary>
        public double GraceSeconds { get; set; } = 0.0;

        /// <summary>
        /// Folga do combo online. O servidor julga com ela, e o cliente prevê com a mesma: se o cliente julgasse sem
        /// folga, mostraria "Miss" e encerraria o combo enquanto o servidor continua dando dano de combo.
        /// </summary>
        public const double OnlineGraceSeconds = 0.08;

        /// <summary>Rotas de combo reconhecidas. Lista vazia desliga as rotas.</summary>
        public List<ComboRoute> Routes { get; set; } = ComboRoute.CreateDefaults();

        /// <summary>Configuração padrão.</summary>
        public static ComboConfig Default => new ComboConfig();

        /// <summary>Lança <see cref="ArgumentException"/> se algum valor for inválido.</summary>
        public void Validate()
        {
            Require(ActivationSpCost >= 0, nameof(ActivationSpCost), "não pode ser negativo");
            Require(InitialBarDurationSeconds > 0, nameof(InitialBarDurationSeconds), "deve ser maior que zero");
            Require(MinBarDurationSeconds > 0 && MinBarDurationSeconds <= InitialBarDurationSeconds,
                nameof(MinBarDurationSeconds), "deve estar entre zero e a duração inicial");
            Require(BarSpeedupFactor > 0 && BarSpeedupFactor <= 1, nameof(BarSpeedupFactor), "deve estar em (0, 1]");
            Require(InitialWindowWidth > 0 && InitialWindowWidth <= 1, nameof(InitialWindowWidth), "deve estar em (0, 1]");
            Require(MinWindowWidth > 0 && MinWindowWidth <= InitialWindowWidth,
                nameof(MinWindowWidth), "deve estar entre zero e a largura inicial");
            Require(WindowShrinkFactor > 0 && WindowShrinkFactor <= 1, nameof(WindowShrinkFactor), "deve estar em (0, 1]");
            Require(WindowCenter - InitialWindowWidth / 2 >= 0 && WindowCenter + InitialWindowWidth / 2 <= 1,
                nameof(WindowCenter), "a janela inicial precisa caber inteira na barra");
            Require(StabilizeAfterHits >= 0, nameof(StabilizeAfterHits), "não pode ser negativo");
            Require(ExcellentZoneRatio >= 0 && ExcellentZoneRatio <= 1, nameof(ExcellentZoneRatio), "deve estar em [0, 1]");
            Require(GoodDamageMultiplier >= 1, nameof(GoodDamageMultiplier), "deve ser pelo menos 1");
            Require(ExcellentDamageMultiplier >= GoodDamageMultiplier,
                nameof(ExcellentDamageMultiplier), "deve ser pelo menos o multiplicador do Good");
            Require(GoodChainBonus >= 0 && ExcellentChainBonus >= 0, nameof(GoodChainBonus), "bônus não podem ser negativos");
            Require(MaxChainBonus >= 0, nameof(MaxChainBonus), "não pode ser negativo");
            Require(MinHitsForFinisher >= 1, nameof(MinHitsForFinisher), "deve ser pelo menos 1");
            Require(GraceSeconds >= 0, nameof(GraceSeconds), "não pode ser negativo");
            Require(Routes != null && !Routes.Contains(null!), nameof(Routes), "não pode ser nula nem ter itens nulos");
        }

        private static void Require(bool condition, string name, string message)
        {
            if (!condition) throw new ArgumentException($"{name} {message}.", name);
        }
    }
}
