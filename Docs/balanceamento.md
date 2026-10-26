# Balanceamento: nível 1 → 30

Meta: **6 a 10 horas** de jogo do nível 1 ao 30 (o GDD pedia 8 a 12; com as missões das regiões ficou em ~7 h, a pedido, para a subida não cansar). Conferido pelo simulador
`Shared/Game.Shared/Progression/LevelingSimulation.cs` (o teste `LevelingBalanceTests` falha se sair da faixa ou
se um nível sozinho passar de 8% do tempo total). Para ver a tabela atualizada:

```bash
dotnet run --project Tools/Game.Balance -- --markdown
```

## O que o simulador faz

- Usa as regras reais do `Shared`: atributos (60% no atributo principal, 40% em destreza), equipamento completo do
  grau do nível (Ferro até o 9, Aço Negro do 10 ao 19, Astralita do 20 em diante) com melhoria média +2/+4/+5,
  skills disponíveis por nível e missão (supõe que o jogador aprende no mestre assim que pode), fórmula de dano contra a defesa do monstro, crítico e MP.
- A cada nível escolhe o monstro que dá mais XP por segundo entre os de até 6 níveis abaixo e 5 acima.
- Soma a XP das missões do catálogo quando o nível e a missão anterior permitem.
- Premissas de um jogador típico: 0,9 s por ação, 7 s entre um monstro e outro, 20% do tempo parado (vila,
  inventário, descanso), +15% de dano médio pelo combo depois de liberado, 3 min extras por missão.
- **Não conta** skills em área acertando vários monstros: na prática quem junta monstros sobe mais rápido. Por isso
  a meta ficou no meio da faixa, com folga.

## Ajustes feitos (2026-10-17)

| O quê | Antes | Depois | Por quê |
|---|---|---|---|
| Curva de XP | `80·N^1,6 + 20·N` | `450·N^1,6 + 110·N` | 1→30 levava ~2 h |
| Vida dos monstros comuns | — | ×2 | morriam em 2–5 s: pouco tempo para o combo |
| XP das missões | — | ×2 | manter as missões valendo ~1 nível cada |
| Monstro novo | — | Cavaleiro Caído (Nv 29, Ruínas) | do 27 ao 29 não havia monstro do nível: o nível 29 levava 67 min |
| Missões (2026-10-19) | 8, só a história | 29: história + 20 paralelas em 3 NPCs de região + a da Cripta | poucas missões: a subida dependia só de caçar (10,4 h → 7,0 h) |

## Resultado (2026-10-19)

### Lâmina: 7,0 horas

| Nível | Caça | s/abate | XP/abate | Abates | XP de missão | Minutos | Acumulado (h) |
|---:|---|---:|---:|---:|---:|---:|---:|
| 1 | lobo_cinzento (Nv 6) | 14,2 | 105 | 3 | 300 | 4 | 0,1 |
| 2 | lobo_cinzento (Nv 6) | 13,8 | 98 | 7 | 900 | 5 | 0,1 |
| 3 | lobo_cinzento (Nv 6) | 12,6 | 91 | 20 | 1200 | 8 | 0,3 |
| 4 | lobo_cinzento (Nv 6) | 12,3 | 84 | 28 | 2300 | 10 | 0,5 |
| 5 | lobo_cinzento (Nv 6) | 12,0 | 77 | 43 | 3200 | 14 | 0,7 |
| 6 | lobo_cinzento (Nv 6) | 11,8 | 70 | 66 | 4000 | 19 | 1,0 |
| 7 | javali_furioso (Nv 12) | 19,1 | 300 | 20 | 4900 | 11 | 1,2 |
| 8 | javali_furioso (Nv 12) | 17,9 | 280 | 20 | 8000 | 10 | 1,4 |
| 9 | javali_furioso (Nv 12) | 17,5 | 260 | 41 | 5600 | 18 | 1,7 |
| 10 | javali_furioso (Nv 12) | 15,7 | 240 | 38 | 10000 | 15 | 1,9 |
| 11 | aranha_da_mata (Nv 16) | 16,8 | 450 | 25 | 11000 | 12 | 2,1 |
| 12 | aranha_da_mata (Nv 16) | 16,1 | 420 | 31 | 12700 | 13 | 2,3 |
| 13 | aranha_da_mata (Nv 16) | 15,8 | 390 | 37 | 14300 | 15 | 2,6 |
| 14 | aranha_da_mata (Nv 16) | 15,5 | 360 | 45 | 16100 | 18 | 2,9 |
| 15 | aranha_da_mata (Nv 16) | 15,3 | 330 | 55 | 18000 | 21 | 3,2 |
| 16 | aranha_da_mata (Nv 16) | 15,1 | 300 | 60 | 21900 | 22 | 3,6 |
| 17 | golem_de_pedra (Nv 22) | 30,7 | 1050 | 17 | 26200 | 14 | 3,8 |
| 18 | golem_de_pedra (Nv 22) | 30,1 | 980 | 0 | 67000 | 6 | 3,9 |
| 19 | golem_de_pedra (Nv 22) | 29,5 | 910 | 38 | 18200 | 26 | 4,4 |
| 20 | golem_de_pedra (Nv 22) | 25,4 | 840 | 34 | 28000 | 21 | 4,7 |
| 21 | espectro (Nv 26) | 18,0 | 1350 | 23 | 30500 | 12 | 4,9 |
| 22 | espectro (Nv 26) | 17,8 | 1260 | 53 | 0 | 20 | 5,2 |
| 23 | espectro (Nv 26) | 17,6 | 1170 | 31 | 35200 | 14 | 5,5 |
| 24 | cavaleiro_caido (Nv 29) | 26,0 | 2100 | 18 | 37700 | 13 | 5,7 |
| 25 | cavaleiro_caido (Nv 29) | 25,0 | 1960 | 21 | 40000 | 14 | 5,9 |
| 26 | cavaleiro_caido (Nv 29) | 24,7 | 1820 | 24 | 42700 | 15 | 6,2 |
| 27 | cavaleiro_caido (Nv 29) | 24,3 | 1680 | 25 | 49900 | 16 | 6,4 |
| 28 | cavaleiro_caido (Nv 29) | 24,0 | 1540 | 25 | 57700 | 16 | 6,7 |
| 29 | cavaleiro_caido (Nv 29) | 23,7 | 1400 | 30 | 61000 | 18 | 7,0 |

### Arcanista: 7,0 horas

| Nível | Caça | s/abate | XP/abate | Abates | XP de missão | Minutos | Acumulado (h) |
|---:|---|---:|---:|---:|---:|---:|---:|
| 1 | lobo_cinzento (Nv 6) | 13,8 | 105 | 3 | 300 | 4 | 0,1 |
| 2 | lobo_cinzento (Nv 6) | 13,4 | 98 | 7 | 900 | 5 | 0,1 |
| 3 | lobo_cinzento (Nv 6) | 12,3 | 91 | 20 | 1200 | 8 | 0,3 |
| 4 | lobo_cinzento (Nv 6) | 12,0 | 84 | 28 | 2300 | 10 | 0,4 |
| 5 | lobo_cinzento (Nv 6) | 11,7 | 77 | 43 | 3200 | 13 | 0,7 |
| 6 | lobo_cinzento (Nv 6) | 11,5 | 70 | 66 | 4000 | 19 | 1,0 |
| 7 | javali_furioso (Nv 12) | 18,4 | 300 | 20 | 4900 | 11 | 1,2 |
| 8 | javali_furioso (Nv 12) | 17,9 | 280 | 20 | 8000 | 10 | 1,3 |
| 9 | javali_furioso (Nv 12) | 17,4 | 260 | 41 | 5600 | 18 | 1,6 |
| 10 | javali_furioso (Nv 12) | 15,6 | 240 | 38 | 10000 | 15 | 1,9 |
| 11 | aranha_da_mata (Nv 16) | 16,7 | 450 | 25 | 11000 | 12 | 2,1 |
| 12 | aranha_da_mata (Nv 16) | 16,2 | 420 | 31 | 12700 | 13 | 2,3 |
| 13 | aranha_da_mata (Nv 16) | 15,9 | 390 | 37 | 14300 | 15 | 2,6 |
| 14 | aranha_da_mata (Nv 16) | 15,7 | 360 | 45 | 16100 | 18 | 2,9 |
| 15 | aranha_da_mata (Nv 16) | 15,4 | 330 | 55 | 18000 | 21 | 3,2 |
| 16 | aranha_da_mata (Nv 16) | 15,1 | 300 | 60 | 21900 | 22 | 3,6 |
| 17 | golem_de_pedra (Nv 22) | 31,0 | 1050 | 17 | 26200 | 14 | 3,8 |
| 18 | golem_de_pedra (Nv 22) | 30,3 | 980 | 0 | 67000 | 6 | 3,9 |
| 19 | golem_de_pedra (Nv 22) | 29,7 | 910 | 38 | 18200 | 26 | 4,3 |
| 20 | golem_de_pedra (Nv 22) | 25,8 | 840 | 34 | 28000 | 21 | 4,7 |
| 21 | espectro (Nv 26) | 18,3 | 1350 | 23 | 30500 | 12 | 4,9 |
| 22 | espectro (Nv 26) | 18,0 | 1260 | 53 | 0 | 20 | 5,2 |
| 23 | espectro (Nv 26) | 17,8 | 1170 | 31 | 35200 | 14 | 5,5 |
| 24 | cavaleiro_caido (Nv 29) | 26,3 | 2100 | 18 | 37700 | 13 | 5,7 |
| 25 | cavaleiro_caido (Nv 29) | 25,9 | 1960 | 21 | 40000 | 14 | 5,9 |
| 26 | cavaleiro_caido (Nv 29) | 25,5 | 1820 | 24 | 42700 | 16 | 6,2 |
| 27 | cavaleiro_caido (Nv 29) | 25,1 | 1680 | 25 | 49900 | 16 | 6,5 |
| 28 | cavaleiro_caido (Nv 29) | 24,8 | 1540 | 25 | 57700 | 16 | 6,7 |
| 29 | cavaleiro_caido (Nv 29) | 24,4 | 1400 | 30 | 61000 | 18 | 7,0 |

### Sensibilidade

Jogador rápido = 4 s entre monstros, 8% parado, +30% pelo combo; sem pressa = 10 s, 30% parado, +5%.

- Lâmina: jogador rápido 5,1 h, sem pressa 9,2 h
- Arcanista: jogador rápido 5,2 h, sem pressa 9,2 h

## Economia (2026-10-19)

`dotnet run --project Tools/Game.Balance` também estima o Alz de 1 a 30 com a mesma caçada da simulação acima.

- **Entra:** o Alz dos monstros, a recompensa das missões e a venda dos drops na mercadora. Núcleos e pedras não são
  vendidos: vão para a melhoria (os núcleos das missões também).
- **Sai:** melhoria das 5 peças de cada grau até o nível médio (+2, +4 e +5), núcleos que faltarem (comprados na
  loja), 25 poções por hora de caça, o conjunto de Ferro da loja e as skills do Mestre Aldric.

| Faixa | Horas | Monstros | Missões | Vendas | Sai | Saldo |
|---|---:|---:|---:|---:|---:|---:|
| Nv 1–10 (Ferro) | 1,7 | 6.971 | 9.100 | 7.538 | 10.480 | 13.129 |
| Nv 10–20 (Aço Negro) | 2,7 | 24.945 | 54.300 | 18.495 | 58.844 | 38.896 |
| Nv 20–30 (Astralita) | 2,6 | 52.305 | 162.700 | 18.766 | 151.453 | 82.318 |

Valores da Lâmina; o Arcanista fica praticamente igual.

- No total entram cerca de 355 mil Alz e saem 62%. Sobram cerca de 134 mil no nível 30, para trocas, para a entrada
  da dungeon e para tentar passar do +5.
- Antes, a conta deixava de fora o Alz das missões e as skills do mestre. As missões da história são as que mais
  pagam (as das regiões dão cerca de 12% da XP em Alz), e as skills custam 40 × nível² (60 mil no total).
- Os núcleos que caem e os das missões bastam: a compra de núcleos aparece zerada, e a loja fica como reserva.
- O teste `Economia_fecha_positiva_em_cada_faixa_e_gasta_boa_parte` exige saldo positivo em cada faixa e um gasto
  entre 60% e 90% do que entra.
- **Revisar** quando a dungeon (entrada e baú) e o PvP entrarem na conta.

## Cripta Esquecida (2026-10-17)

Estimativa do `Tools/Game.Balance`. Soma a vida dos monstros de cada sala e divide pelo dano por segundo do grupo
(o mesmo cálculo da subida de nível). Acrescenta 12 s por grupo de monstros, 15 s por sala, e no chefe os servos dos
dois escudos e 15% de dano perdido fugindo do golpe no chão.

| Grupo | Tempo |
|---|---:|
| 4 no nível 22 (2 Lâminas, 2 Arcanistas) | 10:06 |
| 4 no nível 18 (mínimo) | 12:22 |
| 4 no nível 28 | 9:08 |
| 2 no nível 22 | 17:37 (não termina) |

- A primeira versão levava 4 minutos. A vida dos monstros da dungeon foi multiplicada por 5 (e o XP por 3), para um
  grupo típico ficar perto de 10 minutos e a medalha S (até 8 minutos com combo de 12) pedir jogo bom.
- O teste `Grupo_de_quatro_termina_em_menos_de_15_minutos_e_um_grupo_bom_tira_S` cobra que:
  - o grupo típico fique entre 8 e 13 minutos;
  - o grupo no nível mínimo termine antes do limite;
  - um grupo bom (+7, 35% de bônus de combo) tire S.
- **Revisar** com grupos de verdade: os 12 s por grupo de monstros são um chute.

## Santuário das Ruínas (2026-10-20)

Segunda dungeon, para o fim da subida: nível 26+, Chave do Santuário (1.500 Alz na Guardiã Selene), a céu aberto. Cada
dungeon tem as regras do próprio chefe (`DungeonDefinition.Boss`); o portal mostra uma aba para cada uma e o ranking é
separado.

| Sala | Monstros |
|---|---|
| Pátio das Sentinelas | 3 Sentinelas de Pedra (golpe em área) e 2 Espectros Ancestrais (somem e reaparecem) |
| Corredor dos Lamentos | 4 Espectros Ancestrais |
| Salão dos Juramentos | 3 Cavaleiros Juramentados (fúria) e 1 Sentinela |
| Altar do Oráculo | Oráculo Corrompido (560 mil de vida) |

- **Oráculo:** três escudos (75%, 50% e 25%), cada um com 3 Ecos do Oráculo; golpe no chão a cada 7 s (aviso de 1,8 s,
  raio de 5,5 m, 900 de dano). Na cripta são dois escudos e o golpe vem a cada 9 s.
- **Baú:** 1.500 a 2.800 Alz, núcleos altos, pedras de refino, fragmento do Guardião e peças de astralita.
- **Missão** "O Santuário das Ruínas" (Erudita Veska, Nv 26): derrotar o Oráculo; 60.000 XP, 8.000 Alz e 2 núcleos altos.

Estimativa (mesmo cálculo da cripta):

| Grupo | Tempo |
|---|---:|
| 4 no nível 28 | 11:26 |
| 4 no nível 26 (mínimo) | 11:47 |
| 4 no nível 30 | 11:07 |
| 2 no nível 28 | 20:05 (não termina) |

- A primeira versão dava 13 minutos com o grupo típico, perto demais do limite; a vida caiu uns 15%.
- O teste `Santuario_pede_grupo_mais_forte_e_cabe_no_limite` cobra o grupo típico entre 9 e 13 minutos e o grupo no
  nível mínimo antes do limite.

## Comportamento dos monstros (2026-10-20)

Cada tipo luta de um jeito (`MonsterData.Behavior`). A decisão fica no `MonsterBrain` (o mesmo no servidor e no modo
offline); o servidor cuida da matilha e do golpe em área. Os números ficam em `MonsterBehaviors`. O estado de cada
comportamento (fuga, fúria, recargas) é de cada monstro, não dos dados, que são os mesmos para o grupo inteiro.

| Monstro | Comportamento | Como é |
|---|---|---|
| Coelho Saltador | Foge (`Skittish`) | Com 30% de vida entra em pânico, até no meio do preparo, e corre 7 m para longe por 3,5 s. Uma vez por luta. |
| Lobo Cinzento | Matilha (`Pack`) | Quando apanha, os lobos a até 12 m entram na briga contra quem bateu. |
| Touro Furioso | Investida (`Charger`) | Entre 4,5 e 11 m, prepara 0,9 s (com aviso) e dispara a 3,2× a velocidade, passando 2,5 m do ponto mirado. Só dá dano se pegar o jogador no caminho. Recarga de 7 s. |
| Aranha da Mata | Bate e recua (`HitAndRun`) | Depois de cada golpe, recua 3,5 m por 1,1 s. |
| Golem de Pedra | Golpe em área (`AreaSlam`) | O chão avisa num raio de 3,2 m em volta dele, pelo tempo do preparo (1 s). Acerta todos lá dentro. |
| Espectro | Some e reaparece (`Blink`) | Se o alvo se afasta 6 m, reaparece 1,8 m atrás dele, com um clarão. Recarga de 8 s. |
| Cavaleiro Caído | Fúria (`Berserk`) | Com 35% de vida: recarga do golpe × 0,55 e 25% mais rápido. Mostra a aura de furioso. |

- **Respiro:** no começo de cada luta, a investida e o sumiço só ficam prontos depois de 1,5 s.
- **Voltar para casa** (ou morrer) esquece a fuga, a fúria e as recargas.
- **Guardião das Ruínas:**
  - fase 2 (50% de vida): enfurece e passa a jogar golpes em área;
  - fase 3 (25%): avisa que está desesperado; cada golpe em área vem em dobro (o segundo círculo cai a 4,5 m do
    primeiro, num ângulo sorteado) e o intervalo cai para 60%. Quem está nos dois círculos leva um golpe só.
- **Simulador:** ele não conta a fuga, a investida nem o recuo. Coelhos fugindo e aranhas recuando deixam a luta um
  pouco mais longa; vale revisar o tempo de subida com jogadores de verdade.

## Missões diárias e chefe de mundo (2026-10-20)

**Diárias** (`QuestDefinition.Daily`): voltam a ficar disponíveis à meia-noite de Brasília (`DailyRules`). Não ocupam as
5 vagas de missão ativa e ficam fora do simulador de subida (são um bônus para quem volta todo dia). O banco guarda o
dia da última entrega (`character_quests.CompletedDay`, migração `MissoesDiarias`). Na virada, quem está online recebe
o aviso e o diário atualiza. No NPC, a história aparece antes; se só houver diária, o "!" fica azul.

| Diária | NPC | Nível | Pede | Recompensa |
|---|---|---|---|---|
| Horta em paz | Caçador Rurik | 1 | 12 coelhos | 700 XP, 400 Alz, 3 poções (P) |
| Ronda da matilha | Caçador Rurik | 6 | 10 lobos | 3.000 XP, 1.000 Alz, núcleo baixo |
| Trilha segura | Batedora Nara | 12 | 8 touros e 6 aranhas | 11.000 XP, 2.500 Alz, núcleo médio |
| Vigília nas Ruínas | Erudita Veska | 22 | 6 golens e 6 espectros | 28.000 XP, 5.000 Alz, núcleo alto |
| O Guardião das Ruínas | Ancião Doran | 28 | o Guardião | 20.000 XP, 10.000 Alz, 2 núcleos altos |

**Chefe de mundo** (`SpawnDefinition.Scheduled`, o Guardião das Ruínas): em vez de renascer a cada 2 minutos, desperta
de 2 em 2 horas, nas horas pares de Brasília (`WorldBossRules`).
- O servidor inteiro recebe o aviso 10 minutos antes, quando ele desperta e quando é derrotado.
- Todo mundo que bateu nele (e está online) ganha a XP inteira e a contagem da diária; o Alz é dividido e o drop fica
  com quem causou mais dano.
- Ao ligar o servidor, ele espera o próximo horário. `/gm chefe` desperta na hora (testes e eventos); `/chefe` no
  chat mostra o próximo horário (o cliente calcula sozinho).

## Mercado (2026-10-21)

O mercado da escrivã cobra **5%** de cada venda (`MarketRules.FeePercent`): é mais um sumidouro de Alz, junto com
melhoria, poções, chaves e skills. O simulador de economia ainda não conta o mercado (ele não move Alz para o jogo,
só entre jogadores, menos a taxa).

## Ranger (2026-10-21)

No simulador, a Ranger sobe do 1 ao 30 em **6,8 horas**, igual à Lâmina (6,8 h) e ao Arcanista (6,9 h). Os arcos têm
um pouco menos de ataque que as espadas (22, 54 e 100 contra 25, 60 e 110), e o crítico do arco compensa. As skills
de área (Chuva de Flechas, Flecha Flamejante e Saraivada) seguem a régua das do Arcanista, com custo de MP parecido.

## Monstros elite (2026-10-21)

Todo monstro comum do mundo aberto tem **5%** de chance de nascer (ou renascer) elite (`EliteRules`, no
`WorldContent.cs`). Chefes, o chefe de mundo e os monstros de dungeon nunca são elite. O sorteio se repete a cada
renascimento: um elite morto pode voltar comum, e vice-versa.

| | Normal | Elite |
|---|---|---|
| Vida | ×1 | ×4 |
| Dano | ×1 | ×1,5 (sobre o dano final, depois da defesa) |
| XP | ×1 | ×4 (antes do ajuste por nível e da divisão do grupo) |
| Alz | ×1 | ×3 |
| Drop | 1 rolagem | 2 rolagens (só os itens; o Alz é o da primeira) |

- Com 5% de chance e XP ×4, a XP média por monstro sobe uns 15%, mas o elite leva umas 4 vezes mais tempo para cair.
  Na prática o ritmo de subida fica quase igual; o simulador não sorteia elites.
- No cliente, o elite é 20% maior, tem o tom dourado, uma luz dourada em volta e o nome "{0} (Elite)" em dourado.
- O `EntitySpawned` leva o campo `Elite` (protocolo 18). Quando o elite muda no renascimento, o servidor faz quem via o
  monstro esquecê-lo e manda o `EntitySpawned` de novo.
- Teste do visual: `Game.Server --elites` faz todo monstro comum do mundo aberto nascer elite.

## Escala de poder das skills (2026-10-25)

O dano do golpe era `base + poder`, e o poder do personagem (113 no Nv1, 666 no Nv30) pesava muito mais que a base das
skills (90 a 700). Com o ataque básico a 0,5 s de recarga, nenhuma skill de alvo único rendia mais que apertar o básico.

- **Fórmula:** `base + poder × escala`, com a escala em `SkillData.PowerScale` (`ClassCatalog.SkillPower`). O básico tem
  escala 1.
- **Meta:** skill de alvo único rende pelo menos 1,3 vez o básico por segundo de conjuração; skill de área, cerca de 1,0
  por alvo (ganha a partir de dois alvos). As sem custo de MP (Giro, Chute, Golpe de Cajado) são golpes de apoio.
- **MP:** regeneração de 20 para 10 por segundo, para o MP voltar a ser o freio das skills fortes.
- **Subida:** Lâmina 6,0 h, Arcanista 6,3 h e Ranger 6,2 h no simulador (antes ~6,8 h). As faixas de teste do jogador
  lento (7,5 a 16 h) e do Alz gasto (55% a 90%) foram alargadas um pouco por causa da caça mais rápida.

"Rende × básico" compara o golpe por segundo de conjuração no Nv30 com o básico (que é limitado pela recarga de 0,5 s).
"Ganho líquido" é o golpe menos o que o básico faria no mesmo tempo, no nível em que a skill é liberada. Equipamento
completo do grau do nível, com melhoria +2, +4 e +5, defesa 0 e sem crítico.
### Lâmina

| Skill | Nv | Escala | Golpe no Nv | Golpe Nv30 | Rende × básico (Nv30) | Ganho líquido no Nv |
|---|---:|---:|---:|---:|---:|---:|
| corte | 1 | 1,0 | 233 | 786 | 1,11 | +23 |
| giro_de_lamina | 3 | 1,2 | 259 | 889 | 1,03 | -28 |
| golpe_runico | 5 | 1,7 | 427 | 1272 | 1,35 | +80 |
| estocada_dupla | 8 | 2,0 | 622 | 1532 | 1,30 | +126 |
| fenda_sismica | 12 | 1,6 | 782 | 1326 | 0,99 | +24 |
| lamina_flamejante | 15 | 1,6 | 889 | 1366 | 1,58 | +352 |
| tempestade_de_aco | 20 | 2,4 | 1642 | 1978 | 1,01 | +27 |
| execucao | 25 | 2,8 | 2269 | 2465 | 1,65 | +909 |

### Arcanista

| Skill | Nv | Escala | Golpe no Nv | Golpe Nv30 | Rende × básico (Nv30) | Ganho líquido no Nv |
|---|---:|---:|---:|---:|---:|---:|
| faisca | 1 | 1,0 | 223 | 776 | 1,11 | +22 |
| lanca_de_forca | 1 | 1,6 | 341 | 1226 | 1,32 | +73 |
| golpe_de_cajado | 3 | 2,5 | 305 | 845 | 0,99 | +29 |
| explosao_arcana | 5 | 1,4 | 457 | 1152 | 0,99 | +39 |
| corrente_gelida | 12 | 2,2 | 997 | 1745 | 1,32 | +256 |
| meteoro | 15 | 1,8 | 1042 | 1579 | 1,02 | +86 |
| raio_duplo | 20 | 2,2 | 1607 | 1915 | 1,76 | +717 |
| cataclismo | 25 | 2,5 | 2190 | 2365 | 1,02 | +72 |

### Ranger

| Skill | Nv | Escala | Golpe no Nv | Golpe Nv30 | Rende × básico (Nv30) | Ganho líquido no Nv |
|---|---:|---:|---:|---:|---:|---:|
| tiro_certeiro | 1 | 1,0 | 216 | 757 | 1,11 | +22 |
| flecha_perfurante | 1 | 1,6 | 330 | 1195 | 1,32 | +71 |
| chute_de_recuo | 3 | 1,3 | 274 | 941 | 1,24 | +30 |
| chuva_de_flechas | 5 | 1,5 | 453 | 1180 | 0,97 | +18 |
| tiro_duplo | 12 | 1,9 | 863 | 1499 | 1,32 | +230 |
| flecha_flamejante | 15 | 1,7 | 962 | 1460 | 1,07 | +127 |
| saraivada | 20 | 2,0 | 1444 | 1724 | 1,04 | +87 |
| tiro_do_falcao | 25 | 3,1 | 2469 | 2686 | 1,31 | +614 |
