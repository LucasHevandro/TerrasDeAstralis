# Análise de jogabilidade (2026-10-22)

Análise feita lendo o código, os números do Shared e rodando o simulador de balanceamento (`Tools/Game.Balance`). Ninguém
jogou: tudo o que fala de "sensação" é dedução do código e precisa ser conferido jogando. Os achados marcados como
**conferido** foram relidos no código depois do levantamento.

Prioridade em três ondas: **1** = antes do teste fechado (impacto alto, esforço pequeno), **2** = junto ou logo depois,
**3** = depois do teste. Esforço: P (horas), M (um dia ou dois), G (semana).

## Resumo

- **Combate:** a base é boa (golpe sai na hora, impacto sincronizado por skill, hit stop, recuo, som que sobe no combo).
  O que mais estraga a fluidez são **dois desencontros entre o combo do cliente e o do servidor** e o personagem que
  **desliza enquanto a animação do golpe ainda toca**.
- **Skills:** o dano é `(base + poder)`, e o poder (113 no Nv1, 666 no Nv30) pesa muito mais que a base das skills (90 a
  700). Com o básico a 0,5 s de recarga, **nenhuma skill de alvo único rende mais que apertar o básico**. Também há
  diferenças de classe: os golpes mágicos da Lâmina escalam pelo poder errado, e a Ranger e o Arcanista fecham a rota da
  classe (+20%) só com o básico.
- **Progressão:** a curva é suave (4 a 26 min por nível, Nv30 em ~7 h no simulador e ~8,4 h numa conta mais realista),
  com 1 missão nova por nível. O problema é o **mundo pequeno para 20 pessoas**: 27 monstros comuns, e só quem dá mais
  dano leva o XP.
- **Mundo:** a estrada liga as regiões em ordem de nível e os NPCs ficam ao lado das caças. Faltam **motivo para
  explorar** (não há baú, coleta, lore nem evento fora o chefe), **orientação** (minimapa sem marcador de missão) e
  **ponto de retorno** (morrer manda sempre à vila).

## Onda 1: antes do teste fechado

**Feita em 2026-10-25**, com testes. Diferenças do que estava proposto:

- **Item 2:** o cliente usa a mesma folga do servidor (`ComboConfig.OnlineGraceSeconds`), mas ainda não reativa o combo
  quando o servidor diz que ele continua.
- **Item 9:** as rotas do Arcanista e da Ranger, que fecham só com o básico, caíram de +20% para +10%. A da Lâmina
  continua +20%, e agora vale a pena com o item 8.
- **Item 7:** quem ajudou com pelo menos 15% do dano ganha o XP inteiro e conta na missão; o Alz e o drop ficam com
  quem matou.
- **Item 12:** o destino de cada missão vem de `QuestGuide` (Shared). No minimapa, o NPC com missão ganha a cor e o
  símbolo do marcador, e o lugar da caça aparece como um anel, preso à borda quando está longe. No mapa grande, aparece o
  nome de quem dá missões.

| # | Melhoria | Onde | Por quê | Esforço |
|---|---|---|---|---|
| 1 | Combo do cliente no relógio real (**conferido**) | `PlayerCombat.cs:219` usa `Time.time`; `HitStop.cs` põe `timeScale` em 0,02 | O hit stop congela o relógio do cliente por 35 a 180 ms a cada acerto, e o servidor julga em tempo real. A janela que o jogador vê fica deslocada do tamanho da própria janela (48 ms no fim do combo): acerta no verde e recebe Miss | P |
| 2 | Mesma tolerância de 0,08 s no cliente (**conferido**) | `ServerEntities.cs:18` tem 0,08; `ComboConfig.GraceSeconds` é 0 no cliente | Na borda da janela o cliente mostra Miss e encerra o combo, e o servidor continua dando dano de combo. Passar a constante para o Shared e reativar o combo local quando o `ComboState` do servidor vier ativo | P |
| 3 | Golpe trava o movimento até o impacto | `SkillData.LockSeconds` = 0,15 para todas; animações de 0,45 a 1,5 s | O personagem corre a 7,5 m/s com Execução ou Cataclismo tocando (patina). Usar `LockSeconds` = primeiro impacto + 0,1 s, ou andar a 30% até o fim | P |
| 4 | Flash e animação de dano do monstro só no impacto | `OnlineCombatBridge.cs:126` | O monstro pisca e a vida cai até 0,9 s antes da lâmina ou da flecha chegar. Mover para junto do número de dano (que já espera o impacto) | P |
| 5 | Correr por padrão (ou tecla de alternar) | `PlayerMotor.cs:111`, `KeyBindings.cs:32` | Hoje anda a 4,5 m/s e corre só segurando Shift. A arena fica a 59 s andando e 35 s correndo | P |
| 6 | Dobrar os monstros do mundo aberto | `WorldContent.cs:81-166` | 27 monstros comuns no mundo, 3 a 5 por acampamento: 2 ou 3 jogadores já saturam um acampamento. Sugestão: coelho 10, lobo 10, touro 8, aranha 8, golem 6, espectro 8, cavaleiro 6, com raio 11 a 14 m | P |
| 7 | XP e missão para todos que bateram (**conferido**) | `GameWorld.Combat.cs:462` (`TopDamager`) | Fora de grupo, só quem deu mais dano ganha XP e conta na missão. Em acampamento cheio vira roubo de abate. Dar XP e contagem a quem fez pelo menos 15% do dano (o chefe de mundo já faz algo parecido) | M |
| 8 | Golpes mágicos da Lâmina pelo poder certo | `Inventory.cs:306`, `Classes.cs:45` | Golpe Rúnico e Lâmina Flamejante usam o poder mágico da Lâmina (296 no Nv30, contra 666 de ataque): custam Alz e MP e rendem menos que o Corte. Usar o maior dos dois poderes | P |
| 9 | Rota da classe sem ser de graça | `Classes.cs:74-79` | Ranger (F, F, F) e Arcanista (M, M, M) ganham +20% em todo golpe do combo só com o básico; a Lâmina precisa do Golpe Rúnico. Dar à Lâmina uma rota com o básico ou exigir duas skills diferentes nas outras | P |
| 10 | Monstros longe dos NPCs de missão | `WorldContent.cs` (javalis a 13,6 m de Nara, golens a 13 m de Veska) | Um monstro pode nascer a ~5 m do NPC e atacar quem está conversando | P |
| 11 | Combo mais cedo | `Quests.cs` (`arte_do_combo` no Nv3) | O tutorial promete o combo, e ele só chega depois de 9 a 14 min. Baixar para o Nv2 | P |
| 12 | Marcador de missão no minimapa e local no painel | `Minimap.cs:114-117`, `GameWindows.cs:1031` e `:877-899` | Os NPCs são pontos amarelos iguais e o "!" só aparece a 40 m. Pintar os pontos com a cor do marcador, pôr o nome no mapa grande e "→ Nara, Mata Sombria (80 m)" no painel | P/M |

## Decisão sua: como as skills ganham espaço

**Escolhida a B (2026-10-25)**, feita com a regeneração de MP em 10 por segundo. Escalas e tabela em
`Docs/balanceamento.md`, seção "Escala de poder das skills".

Esta é a maior mudança de equilíbrio e muda a sensação do combate, então vale escolher antes de mexer:

- **A) Básico mais lento (só números):** recarga e conjuração de Corte, Faísca e Tiro Certeiro (e Chute e Golpe de
  Cajado) de 0,5 s para 1,0 a 1,2 s. Sobra uma janela para encaixar a skill. Precisa testar o ritmo do combo (a barra
  tem 0,6 a 1,2 s).
- **B) Escala de poder por skill:** um multiplicador em `SkillData` (por exemplo 1,5 a 2× nas intermediárias e 3 a
  3,5× nas de Nv25). Mantém o ritmo do básico; mexe no Shared, no cliente e nos testes.
- **Meta nas duas:** uma skill deve render pelo menos 1,3 vez o básico por segundo de conjuração.

Números de referência (Nv30, defesa 0): Corte 872 por golpe, ou 1.743 por segundo. A melhor skill de alvo único da
Lâmina, a Execução, dá 1.404 por golpe, mas com 100 MP e 15 s de recarga rende 1.332 por segundo de conjuração.

## Onda 2: junto ou logo depois

**Combate e skills**
- **Escada de skills em área invertida:** a skill nova rende menos por alvo que a antiga. Flecha Flamejante (Nv15, raio
  3 m) perde para a Chuva de Flechas (Nv5, raio 4 m); Meteoro e Cataclismo perdem para a Explosão Arcana. Valores
  sugeridos: Flecha Flamejante 480 de base, raio 4,5 e recarga 6 s; Meteoro 520 e recarga 7 s; Saraivada 560 e recarga
  7 s. A opção B resolve isso de forma geral.
- **Monstros sem ameaça:** um monstro do próprio nível leva de 74 a 200 s para matar o jogador, e morre em 3 a 25
  golpes. As poções quase não têm uso, e o Guardião (60 mil de vida) cai em ~1 min com um jogador. Sugestão: dano dos
  monstros ×2,5 e vida do chefe ×3, ou uma mecânica de grupo.
- **Esquiva:** o corpo não gira para a direção (desliza de lado ou de costas com a animação de frente), e a
  invulnerabilidade de 0,25 s conta da chegada da mensagem, sem descontar a latência. Girar o corpo, recuar a janela em
  até 0,1 s e bloquear a esquiva quando morto.
- **Golpes de outros jogadores:** a animação começa quando o dano chega, e o número aparece antes do golpe. Atrasar o
  número pelo tempo do impacto do atacante.
- **Monstro que apanha no aviso de ataque:** a animação de dano interrompe a de ataque, mas o golpe do servidor cai no
  tempo certo, e o monstro parece acertar do nada. Só piscar, sem trocar de animação, durante o aviso.
- **Golpes duplos com um número só** (Estocada Dupla, Raio Duplo, Tiro Duplo, Tempestade de Aço): dividir o número
  entre os dois impactos.
- **Correção de posição:** hoje é sempre um teleporte seco. Suavizar em 0,1 s quando for menor que ~3 m.
- **Câmera:** o pivô não é suavizado e a colisão aproxima a câmera de uma vez. Usar `SmoothDamp` na vertical e voltar
  devagar depois da colisão.
- **Descrição das skills:** não existe. A dica só mostra tipo, MP e recarga. Gerar o texto a partir dos dados (dano,
  alcance, área). Os nomes prometem efeitos que não existem: Chute de Recuo não empurra, Corrente Gélida não deixa lento,
  as flamejantes não queimam.

**Progressão e mundo**
- **Ponto de retorno por região** (fogueira ao lado de Nara, de Veska e da entrada da arena): morrer nas Ruínas custa
  26 a 35 s correndo de volta, e no chefe de mundo isso come a janela.
- **Monstros novos nas lacunas:** um Nv9-10 no norte dos Campos, um Nv19-20 nos 46 m vazios de estrada entre a Mata e
  as Ruínas, e um grupo Nv28-30 entre as Ruínas e a Arena. Do Nv24 ao Nv29 só existem 3 cavaleiros.
- **Dungeon possível em dupla:** a Cripta (que cobre 80% do Nv18) e o Santuário só terminam em grupo de 3 a 4 dentro
  do limite de 15 min. Reduzir a vida em ~25% para a dupla terminar em ~13 min, ou criar um canal de "procurando grupo".
- **Ajustes de XP:** lobo de 70 para 100 XP (o Nv1-6 é o trecho mais lento, a 17-21 mil XP/h); missão de Veska no Nv22
  (o único nível sem missão nova); primeira missão de Nara no Nv10; `diaria_lobos` de 3.000 para 1.500 XP.
- **Equipamento por sorte:** o Aço Negro e a Astralita só caem de monstro, e a arma de Aço tem 57% de chance de ter
  caído até o Nv12. Dar arma do grau nas missões de rank, ou vender o conjunto de Aço Negro por 5 a 8 mil Alz.
- **Alz concentrado no fim:** `ecos_espectrais` (80 mil) e `o_guardiao` (200 mil) são 65% do Alz das missões. Cortar
  para 40 mil e 60 mil.

## Onda 3: depois do teste

- **Exploração:** um baú por região (renova a cada dia), pedras de lore nas Ruínas, XP de descoberta na primeira entrada
  de cada região e neblina no mapa grande.
- **Marcos de longe:** uma torre iluminada nas Ruínas e uma árvore enorme na Mata, porque é fácil se perder na Mata.
- **Áreas vazias:** cerca de 40% do mapa (os lados, o sul da vila e o norte da arena) não tem nada. Usar para monstros
  raros fixos ou encolher o limite.
- **Tipos de missão novos:** de combo (por exemplo 20 acertos Excellent), de explorar, de melhorar equipamento, de usar
  o mercado e de jogar a arena, além de missão semanal.
- **Depois do Nv30:** o conteúdo de XP acaba em 7 a 11 h, e o jogador dedicado "termina" no 2º ou 3º dia do teste.
  Planejar Nv31 a 40 ou XP de mestre. Para o teste, deixar claro o que há no fim (dungeons, arena, melhoria +10).
- **Viagem rápida** paga entre a vila e os NPCs de região, e entrega da história em Nara e Veska.
- **Efeitos de skill:** empurrão, lentidão, queimadura e golpes duplos de verdade.
- **Skills nas lacunas de nível:** Arcanista e Ranger no Nv8; Lâmina no Nv18 e no Nv22; uma skill de Nv28 para todas.
  Finalizador com animação de cada classe (hoje o Arcanista martela com as duas mãos).
- **Equilíbrio de PvP:** a Lâmina não tem compensação contra o alcance de 12 m sem custo. Opções: +15% de vida e +10%
  de defesa, uma investida curta, ou básico de 8 a 9 m nas outras classes. Medir com bots em duelos 1v1.
- **Acabamento do movimento:** aceleração de 0,06 a 0,1 s, a animação de corrida acompanhando a velocidade (hoje satura
  a 6 m/s e corre a 7,5), giro de ~0,1 s até o alvo, hit stop sem `timeScale` global, extrapolação curta das entidades
  e regeneração de MP menor (hoje 20/s, e o MP quase não importa).

## Tempo de subida

Horas acumuladas para chegar a cada nível. **Simulador:** `Tools/Game.Balance`. **Realista:** com 25% do tempo parado,
5 min por missão e sem as missões de dungeon. **Com disputa:** o realista com o tempo de cada abate ×1,5.

| Nível | 5 | 10 | 15 | 20 | 25 | 30 |
|---|---|---|---|---|---|---|
| Simulador | 0,4 h | 1,6 h | 2,9 h | 4,3 h | 5,6 h | 6,8 h |
| Realista | 0,6 h | 2,0 h | 3,5 h | 5,3 h | 6,9 h | 8,4 h |
| Com disputa | 0,7 h | 2,7 h | 4,6 h | 7,2 h | 9,3 h | 11,4 h |

Numa semana de teste, quem joga 1 h por dia chega por volta do Nv20 a 27.

## O que já está bom

- O golpe sai na hora, sem esperar o servidor, e só o número espera o impacto. Há compensação de latência no combo.
- O impacto é sincronizado com a animação por skill, e o retorno é rico: hit stop, recuo em mola, flash, som que sobe
  no combo e borrão no Excellent.
- O movimento é previsto no cliente, e o servidor tem folga grande, então correções são raras.
- A estrada liga as regiões em ordem de nível, e as missões de falar levam o jogador ao NPC da região seguinte.
- A curva de XP não tem parede, e as missões cobrem cerca de 50% de cada nível.
- Música, som ambiente, clima e dia e noite são próprios de cada região.
