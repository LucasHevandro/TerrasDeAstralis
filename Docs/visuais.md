# Como colocar modelos no jogo

O jogo procura um modelo para cada coisa pelo **id**. Se achar, usa o modelo; se não achar, continua com a forma
simples de hoje (cápsula, cubo, esfera). Assim dá para trocar um modelo por vez, sem mexer em código e sem o jogo
ficar quebrado no meio do caminho.

## Onde colocar

Crie um **prefab** com o nome exato do id dentro de `Client/Assets/Resources/Visuals/<categoria>/`:

| Categoria | Pasta | Ids (exemplos) |
|---|---|---|
| Monstros e chefes | `Visuals/monster/` | `coelho_saltador`, `lobo_cinzento`, `guardiao_das_ruinas`, `senhor_da_cripta` |
| NPCs | `Visuals/npc/` | `anciao_doran`, `ferreira_ilsa`, `guarda_volumes_bento`, `mercadora_lia`, `guardia_selene` |
| Personagem do jogador (por classe) | `Visuals/class/` | `blade` (Lâmina), `arcanist` (Arcanista), `ranger` (Ranger), `guardian` (Guardião) |
| Itens no chão | `Visuals/item/` | `espada_ferro`, `pocao_hp_p`, `nucleo_baixo` |

Os ids de todos os monstros, NPCs e itens estão no arquivo de textos `Shared/Game.Shared/Resources/Locales/pt-BR.txt`
(linhas `monster.<id>`, `npc.<id>`, `item.<id>`).

Hoje todas as classes, NPCs e monstros já têm modelo (veja as seções Personagens e Monstros abaixo).

## Regras do prefab

- **Pivô no chão**, no centro dos pés. O jogo põe o prefab na posição da entidade.
- **Frente para +Z** (o eixo azul do Unity). O jogo gira o objeto para onde ele anda ou olha.
- **Escala em metros:** um humano tem cerca de 1,8 m. A altura do modelo define onde aparecem o nome, a barra de vida e
  os números de dano; a largura define o anel de seleção.
- **Sem colisores** (se tiver, o jogo remove): quem colide e recebe o clique é o objeto do jogo, não o modelo.
- **Materiais URP** (Universal Render Pipeline/Lit ou Simple Lit). Para desempenho com muitos jogadores na tela, prefira
  1 material por modelo e uma textura de paleta compartilhada.
- Orçamento sugerido: personagem 5 a 10 mil triângulos, monstro comum menos que isso.

## Animação (opcional)

Se o prefab tiver um **Animator**, o jogo preenche estes parâmetros, se existirem no Animator Controller:

| Parâmetro | Tipo | Quando |
|---|---|---|
| `Speed` | float | Velocidade no chão, em m/s (0 parado; ~4 andando; ~7 correndo) |
| `Attack` | trigger | O monstro começa o golpe (aviso no chão) ou o jogador usa uma skill |
| `Hit` | trigger | Levou dano |
| `Dodge` | trigger | O jogador esquivou |
| `Dead` | bool | Reservado para pose de queda (hoje o corpo some ao morrer) |

Parâmetro que não existe é ignorado: um modelo só com `Speed` (parado → andar → correr) já funciona. O "Apply Root
Motion" fica desligado: quem move o personagem é o jogo (o servidor, no online), não a animação.

Com animações do **Mixamo**: importe o personagem como Humanoid, crie um Animator Controller com um Blend Tree de
`Speed` (parado, andar, correr) e estados de ataque, dano e esquiva ligados aos triggers. Mantendo o mesmo esqueleto
Humanoid, as mesmas animações servem para as três classes.

## Conferir

Rode o jogo (ou o teste automático com `-onlinetest pasta -onlinefight`) e veja nas capturas se o modelo apareceu no
tamanho certo, com o nome em cima da cabeça e o anel de seleção nos pés.

# Mundo: terreno, céu, névoa, luz e pós-processamento

A cena `Treino.unity` é gerada por código. Depois de mudar o mapa, gere de novo:
**Protótipo > Criar cena de treino**, ou em linha de comando com `-executeMethod Game.Client.Editor.TrainingSceneBuilder.Build`.

| Parte | Onde | O que faz |
|---|---|---|
| Altura do chão | `Scripts/World/Ground.cs` | Fórmula única do relevo. O construtor do terreno usa a fórmula, e o jogo a usa para pôr no chão o que o servidor manda só em X e Z (jogadores, monstros, NPCs, itens e avisos de golpe). |
| Terreno | `Scripts/Editor/TerrainBuilder.cs` | Relevo, chão pintado por zona (grama, folhas da mata, pedra das ruínas, cascalho da arena, terra nas estradas, rocha nas encostas) e tufos de grama e flores low-poly (malhas paradas, só até 45 m da câmera). As texturas são geradas e ficam em `Assets/Art/Mundo`. O chão é fosco: a suavidade das camadas é a constante 0 (`ConstantOnly`). Com o padrão (alfa da textura), ele espelhava o céu e ficava com um véu branco, forte à noite. |
| Cenário | `Scripts/Editor/PropBuilder.cs` + `MeshKit.cs` | Árvores, pinheiros, arbustos, pedras, colunas, muros, casas, cercas, poço e braseiros em low-poly, gerados por código. As malhas ficam em `Assets/Art/Mundo/Malhas`. |
| Clima | `Scripts/World/Atmosphere.cs` | Névoa, luz ambiente, cor do sol e filtro de cor por zona, misturados aos poucos. A dungeon e a arena PvP têm o próprio clima. |
| Pós-processamento | `Assets/Materials/Prototipo/PosProcessamento.asset` | Bloom suave, tonemapping neutro, contraste, vinheta e sombras levemente azuladas. Na qualidade **Baixa** ele é desligado. |
| Céu | `Assets/Materials/Prototipo/Ceu.mat` | Céu procedural do Unity, com o sol da cena. À noite, `World/NightSky` põe por cima uma cúpula de 1.800 estrelas que cintilam e a lua com halo (`Shaders/CeuNoturno.shader`, sem neblina), na direção da luz da noite. |

**Regras do relevo**
- **Planos:** a praça da vila, as estradas, os grupos de monstros (o raio deles + 3 m), os NPCs e a arena do chefe. Assim os avisos de golpe no chão ficam retos.
- **Morros suaves:** ficam entre esses lugares. São mais altos na mata e nas ruínas.
- **Montanhas:** sobem a partir da borda da área jogável do servidor (x de −130 a 130, z de −70 a 230) e fecham o horizonte. O que fica nelas é só enfeite, sem colisão.
- **Servidor:** não muda nada. Ele continua só com X e Z.

**Trocar pelo KayKit (ou outro pacote).** O `PropBuilder` escolhe uma lista de malhas por tipo (`arvore`, `pinheiro`, `pedra`, `coluna`, `muro`, `casa`...). Para usar os modelos do pacote, troque a variante gerada pelo modelo importado no tipo correspondente. Os mesmos lugares, colisões e escalas continuam valendo. Os modelos dos pacotes também têm a frente para +Z e o pivô no chão.

# Personagens (KayKit)

As classes e os NPCs usam os pacotes grátis **KayKit** de Kay Lousberg (www.kaylousberg.com), em licença CC0: uso livre, inclusive comercial, sem crédito obrigatório. As licenças estão em `Assets/Art/KayKit/LICENSE_*.txt`.

| Pacote (versão grátis) | O que usamos |
|---|---|
| Character Pack: Adventurers 2.0 | Knight, Mage, Barbarian, Rogue, Rogue_Hooded e Ranger, além de armas e acessórios |
| Character Pack: Adventurers 2.0 EXTRA (pago) | Barbarian_Large (Guardião), `axe_1handed_Large` e `shield_round_barbarian_Large`. Os arquivos ficam em Downloads (`KayKit_Adventurers_2.0_EXTRA` e `_SOURCE`, com os `.blend`) |
| Character Animations 1.1 | Rig_Medium: General, MovementBasic, MovementAdvanced, CombatMelee, CombatRanged e Special. Rig_Large (Guardião): General, MovementBasic, MovementAdvanced, CombatMelee, Simulation e Special |
| Character Pack: Skeletons 1.1 | Esqueletos (Warrior, Mage, Minion e Rogue) e as armas deles, para a etapa dos monstros |

Para gerar tudo: **Jogo > Visuais > Montar personagens KayKit**, ou `-executeMethod Game.Client.Editor.KayKitBuilder.Build`. O construtor:
- configura a importação (rig Generic, laço nas animações de parado, andar e correr);
- cria os materiais URP com as texturas de cada personagem;
- monta o Animator `Assets/Art/KayKit/Gerado/Personagem.controller`;
- grava os prefabs que o jogo procura.

| Prefab | Modelo | Na mão |
|---|---|---|
| `Visuals/class/blade` (Lâmina) | Knight | espada e escudo |
| `Visuals/class/arcanist` (Arcanista) | Mage | cajado |
| `Visuals/class/ranger` (Ranger) | Ranger | arco na mão esquerda |
| `Visuals/class/guardian` (Guardião) | Barbarian_Large | machado e escudo grandes |
| `Visuals/npc/anciao_doran` | Rogue_Hooded | livro aberto |
| `Visuals/npc/escriva_marta` (mercado) | Rogue | livro aberto |
| `Visuals/npc/ferreira_ilsa` | Barbarian | machado |
| `Visuals/npc/guarda_volumes_bento` | Ranger | caneca |
| `Visuals/npc/mercadora_lia` | Rogue | adaga |
| `Visuals/npc/guardia_selene` | Knight sem viseira | espada de duas mãos |

**Animator**
- **Locomoção:** blend por `Speed` (parado em 0, andar em 2,2 m/s, correr em 6 m/s).
- **Golpes:** um estado para cada `AttackKind`, definido em `Fx/AttackStyle.cs`: 0 corte, 1 golpe de cima, 2 estocada, 3 giro, 4 golpe com salto, 5 corte diagonal, 6 disparo mágico, 7 magia em área, 8 magia erguida e 9 finalizador. Cada skill escolhe o seu estilo nesse arquivo.
- **Outros estados:** dano (`Hit`), esquiva (`Dodge`) e morte (`Dead`).
- **Morte no jogo:** o personagem cai. Monstros animados somem 2 s depois; jogadores ficam caídos até renascer.
- **Golpes dos outros:** o golpe de outro jogador também anima o modelo dele, pela skill que veio no evento de combate.

**Conferir as animações no jogo:** `TerrasDeAstralis.exe -posetest pasta`. Ele põe as classes e os NPCs lado a lado e captura cada estado. Faço a conferência no jogo porque, no Editor em modo batch, a malha não é deformada pelos ossos.

# Monstros

| Monstro | Modelo | Como anima |
|---|---|---|
| Coelho Saltador, Lobo Cinzento, Javali Furioso, Aranha da Mata, Golem de Pedra | low-poly em partes, gerado por `Scripts/Editor/CreatureBuilder.cs` (menu **Jogo > Visuais > Montar bichos**) | por código (`Fx/ProceduralRig.cs`): patas pela velocidade, pulo do coelho, bote no ataque, recuo ao apanhar e tombo na morte |
| Esqueleto Guerreiro, Esqueleto Arqueiro, Cavaleiro Ossudo, Servo da Cripta, Senhor da Cripta (chefe) | KayKit Skeletons, com as armas do pacote | Animator `Esqueleto.controller` (parado, andar e morte próprios de esqueleto e os golpes do personagem) |
| Cavaleiro Caído, Espectro (translúcido), Guardião das Ruínas (chefe) | KayKit Skeletons; o Espectro com material roxo transparente e o Guardião com olhos vermelhos | o mesmo Animator |

- **Golpe de cada monstro:** sai de `AttackStyle.ForMonster` (arqueiro dispara, espectro e Senhor da Cripta lançam magia, os chefes guerreiros dão o golpe de duas mãos).
- **Tamanhos:** os modelos são escalados para a altura definida em `KayKitBuilder.MonsterLooks`. A colisão e o alcance continuam sendo os do servidor.
- **Conferir no jogo:** `TerrasDeAstralis.exe -posetest pasta -posemonsters`.

# Skills: golpe, rastro e impacto

Cada uma das 16 skills tem o próprio golpe em `Fx/SkillMoves.cs`. Ele define:
- a sequência de animações (por exemplo, a Estocada Dupla são duas estocadas e a Tempestade de Aço é um giro seguido de um giro largo);
- quando a arma acerta ou quando a magia sai da ponta do cajado;
- quanto o corpo avança e a janela do rastro na arma;
- se a magia junta energia antes de sair.

O Animator ganha um estado para cada passo (no `KayKitBuilder`), no ritmo do parâmetro `AttackSpeed`. O Excellent sai 15% mais rápido.

**No jogo**
- **Na hora do aperto:** aparecem a nota e a rota do combo e começa a animação. A resposta é imediata, como o ritmo do combo pede.
- **No momento do acerto (`Impacts`):**
  - **Golpes físicos:** caem as faíscas, o número de dano, o tremor e a pausa de impacto.
  - **Magias:** o projétil sai da ponta do cajado. Antes, a energia junta na ponta (`Charge`), e o dano aparece quando ele chega.
- **Online:** o servidor continua decidindo no aperto, e o resultado dele espera o golpe chegar para aparecer.
- **Arma (`Fx/WeaponFx.cs`, nos prefabs das classes):**
  - rastro da cor da skill (dourado e mais largo no Excellent);
  - brasas na Lâmina Flamejante;
  - avanço do modelo nas investidas. A posição de verdade continua sendo a do jogo, e o andar não conta esse avanço.
- **Assinatura no impacto (`SkillFx.Signature`):**
  - runas no Golpe Rúnico, rachaduras na Fenda Sísmica, labareda na Lâmina Flamejante e X vermelho na Execução;
  - cristais e geada na Corrente Gélida, implosão e duas ondas na Explosão Arcana;
  - explosão com fumaça no Meteoro, e chuva de pedras com três ondas de choque no Cataclismo.

**Conferir:** `TerrasDeAstralis.exe -skilltest pasta` toca as 16 skills na frente de um esqueleto. Ele captura o preparo e o acerto de cada uma, alternando Good e Excellent.

**Clarões:** usam um brilho grande de partícula, que o bloom espalha. Luzes pontuais deixavam um ponto branco solto na tela.

# Equipamento visível e brilho da melhoria

- **Arma na mão:** cada arma tem o próprio modelo em `Visuals/weapon/<id>`, gerado pelo `ItemModelBuilder` (menu **Jogo > Visuais > Montar itens**).
  - **Ferro:** o modelo do KayKit.
  - **Aço negro:** maior e escurecido.
  - **Astralita:** a espada colorida, ou o cajado em azul-cristal, com um brilho leve.
- **Brilho da melhoria** (`WeaponFx.GlowFor`): um "casco" da mesma malha, aditivo, pulsando sobre a arma.

  | Melhoria | Brilho |
  |---|---|
  | +4 a +6 | azul fraco |
  | +7 e +8 | ciano, com partículas subindo da lâmina |
  | +9 | roxo |
  | +10 | dourado, pulsando mais forte |

- **Outros jogadores:** o servidor manda a arma e a melhoria de cada jogador no `EntitySpawned` e, quando mudam, no `EntityLook` (protocolo v13; desde o v16 também os graus da couraça, das luvas e das botas). Assim todo mundo vê o brilho de todo mundo.
- **Itens no chão:** os 37 itens têm modelo em `Visuals/item/<id>`: as armas, armadura, luvas, botas e anel por nível, cristais dos núcleos, gemas, poções, materiais e a chave da cripta. Giram e flutuam com o feixe da raridade.
- **Conferir:** `TerrasDeAstralis.exe -geartest pasta`. Mostra as duas classes com cada arma em +0, +4, +7, +9 e +10, a
  vitrine de aparência (`aparencia.png`) e depois todos os itens.

# Aparência: couraça, luvas, botas, cabelo, roupa, pele, penteado e elmo

A textura dos personagens do KayKit é uma paleta: uma grade de 8×4 cores em degradê. Mudar a cor de uma parte é
repintar a célula dela. O `Fx/CharacterPaint` faz isso no cliente:

1. Lê a paleta pela GPU em 256×256.
2. Repinta as células mantendo o degradê de cada pixel.
3. Guarda um material por combinação de cores.

Só as malhas com esqueleto (o corpo) são repintadas; a arma na mão tem o brilho próprio. Cada braço e cada perna é
uma malha separada do modelo, então o metal segue peças diferentes: a couraça no corpo, as luvas nos braços e as botas
nas pernas.

| Parte | Knight (Lâmina) | Mage (Arcanista) | Ranger |
|---|---|---|---|
| Metal (em todas as malhas) | células (3,0), (7,0) e (7,1) | (3,0) e a capa (2,1) | placas (3,0) e couro (7,0) |
| Só nos braços (luvas) | manopla (4,0) | punhos (4,0) | luvas (7,2) |
| Só nas pernas (botas) | — | sapatos (3,2) | pernas e botas (3,2) |
| Cabelo | (1,0) | (1,0) | (1,0) |
| Roupa | tabardo e capa (0,1) | túnica e chapéu (0,1) e (1,1) | túnica e capa (0,1) |

- **Metal por grau da peça vestida:**
  - sem a peça: cor original;
  - Ferro: prata;
  - Aço Negro: quase preto;
  - Astralita: azul-claro.
- **Cabelo** (6 opções): original, loiro, ruivo, preto, grisalho e castanho.
- **Roupa** (6 opções): original, azul, verde, roxo, preto e branco.
- **Pele** (5 opções): original, clara, morena, parda e negra. É a célula (0,0), a mesma nos cinco aventureiros do pacote.
- **Penteado** (6 opções): a cabeça da classe ou a de um dos aventureiros do pacote (Knight, Mage, Ranger, Rogue e
  Barbarian), que dividem o mesmo esqueleto.
  - O `KayKitBuilder` põe as cinco cabeças no prefab de cada classe, escondidas e presas aos mesmos ossos pelo nome.
    Cada uma fica com a textura do próprio modelo; o cabelo é a célula (1,0) em todas, então a cor de cabelo vale para
    qualquer penteado.
  - Na tela os nomes descrevem o penteado: da classe, curto, longo, cavanhaque, volumoso e barba.
- **Elmo:** o jogador mostra ou esconde o elmo (o chapéu, no Arcanista) quando quiser, na caixa do inventário. O
  servidor guarda (`HideHelmet`) e avisa quem o vê pelo `EntityLook`. Na criação, a prévia começa sem elmo para mostrar
  o penteado.
- **Na criação:** a cor de cabelo e a de roupa são escolhidas ao lado de uma prévia 3D girando (`Net/CharacterPreview`).
  O servidor guarda as escolhas (colunas `Hair` e `Cloth`, migração `AparenciaPersonagem`; `Head`, `Skin` e `HideHelmet`,
  migração `PenteadoEElmo`) e corrige índices fora da lista.
- **No protocolo (v17):** o `EntitySpawned` leva o grau da couraça, das luvas e das botas, o cabelo, a roupa, o penteado,
  a pele e o elmo. O `EntityLook` leva os três graus e o elmo quando o jogador troca de peça ou mostra/esconde o elmo
  (`AppearanceCommand`).
- **Na seleção de personagem:** a prévia mostra quem está sob o mouse, com as cores e os graus das peças vestidas (a
  lista de personagens traz os três graus).
- **Convidados** (testes e bots) variam as cores pelo id.
- **Conferir:**
  - `-geartest` (`aparencia.png`): Lâmina e Arcanista com o conjunto de cada grau, peças misturadas e algumas cores;
    `penteados.png`: os seis penteados sem elmo, com tons de pele diferentes;
  - `-onlineui` (`5b-elmo.png`): esconde o elmo pelo servidor e volta a mostrar;
  - `-logintest`: `criacao_arcanista.png`, `criacao_lamina.png`, `criacao_ranger.png` e `selecao.png`.

# Cripta e arena PvP

- **Peças** em `Visuals/dungeon/<id>`, geradas pelo `DungeonKitBuilder`: parede de tijolo, pilar, portão de ferro, tocha, caixão, ossos, velas, estandarte, trono, baú; na arena, muro, rochas, bandeira, arquibancada e braseiro. Pisos com textura própria (lajes da cripta, areia da arena, tapete).
- **Montagem:** o `DungeonView` e o `ArenaView` põem as peças sobre os blocos de sempre, que ficam invisíveis e continuam sendo os colisores. As bandeiras da arena pegam a cor do time que domina o ponto.
- **Clima da cripta:** poeira clara flutuando e névoa roxa rente ao chão no corredor inteiro (`Kit.Haze`). Um círculo
  rúnico roxo brilha no meio da sala do chefe.
- **Runas da arena** (`Kit.Rune`, o shader das marcas das skills parado):
  - cada ponto tem um círculo rúnico na cor do dono;
  - cada base tem três círculos na cor do time, com piso escuro puxado para ela e as bandeiras do time nos cantos.
- **Santuário das Ruínas** (`DungeonDefinition.Theme = "santuario"`): sem teto, com o céu do mundo.
  - Usa as peças da arena: piso de arenito, muros e rochas. Pelo caminho, pedras caídas (baixas e tortas),
    braseiros nos muros e estandartes.
  - No altar, um anel de seis pedras baixas junto aos muros, em volta de um círculo rúnico ciano com um dourado por dentro.
  - No ar, poeira dourada; no chão, névoa azulada.
- **Variantes de modelo** (`VisualCatalog.Variants`): um id sem prefab próprio usa o modelo de outro, maior e tingido.
  - O tom multiplica a cor e a emissão dos materiais, em cópias reaproveitadas para cada par de material e tom.
  - Os monstros do santuário usam: golem → Sentinela de Pedra (arenito), espectro → Espectro Ancestral (ciano),
    Cavaleiro Caído → Cavaleiro Juramentado (dourado), servo → Eco do Oráculo, Senhor da Cripta → Oráculo Corrompido.
- **Conferir:** teste online com `-onlinedungeon` (cripta inteira), `-onlinesanctuary` (santuário inteiro) e `-onlinearena`.

# Polimento do combate

- **Clarão no acerto** (`HitFlash`): o modelo atingido clareia por 0,12 s.
- **Recuo de quem apanha** (`Fx/HitRecoil`): o modelo é empurrado para longe de quem bateu e cambaleia para trás, e volta
  com uma mola em uns 0,3 s. Só o modelo se mexe; a posição do servidor não muda.
  - A força cresce com o crítico, o Excellent e o peso da skill; o finalizador empurra mais que tudo.
  - Monstros grandes recuam menos (a força é dividida pela altura). O golpe de outros jogadores empurra pela metade.
- **Pausa no impacto e tremor de câmera** (`HitStop` e `OrbitCamera.Shake`): o crítico segura e treme mais que um golpe
  comum, além do Excellent e do finalizador.
- **Tom do combo:** o som de cada acerto do combo sobe um semitom (até uma oitava), para sentir o ritmo sem olhar a
  barra (`SoundPlayer.PlayPitched`).
- **Poeira nos pés** (`Fx/FootDust`): uma nuvenzinha a cada passada de quem corre e um punhado na arrancada (esquiva,
  investida). Vale para o jogador, os outros jogadores e os monstros, maior nos monstros grandes.
- **Lascas no impacto:** junto das faíscas, riscos curtos e quentes esticados na direção do voo, como metal batendo.
- **Números de dano** com contorno escuro nos oito lados. O crítico entra bem maior, treme um instante, sobe mais e tem
  um brilho claro por cima.
- **Volta à vida** com coluna de luz; monstros **surgem** com um anel de poeira e brilho.
- **Projéteis rápidos** ficam alongados e apontados para o destino.

# Vida no mundo

- **Rio** (`Ground.RiverZ`) cavado no terreno entre a vila e os Campos do Vento, com água correndo e **ponte de madeira** na estrada. Árvores, pedras, cercas e grama ficam fora da água.
- **Dia e noite:** um dia dura 40 minutos (`Atmosphere.DayMinutes`). Online, a hora vem do relógio do servidor, então todos veem o mesmo céu. Há pôr do sol e amanhecer alaranjados e uma noite azulada. As lanternas da vila (`NightLight`) acendem à noite. Na cripta e na arena o clima é fixo.
- **Vila:** praça calçada, e cada NPC ganhou o seu canto: forja e bigorna, banca do mercador, estante e tapete, e o arco do portal. Há postes de lanterna na praça, e o treino usa bonecos de palha.
- **Conferir:** `TerrasDeAstralis.exe -worldtest pasta` fotografa a praça, a ponte e os campos de dia, no pôr do sol e à noite.

# Interface

- **Tema** (`UiTheme`): painéis com degradê escuro, cantos arredondados e moldura bronze. Barras de vida, mana, SP e XP com brilho. Botões, campos, casas de item e caixas de marcar próprios. Fonte Segoe UI do sistema, com Arial de reserva. Tudo é gerado em código.
- **Arte do tema** (`UiArt`), também em código:
  - **Títulos** numa fonte serifada que já vem no Windows (Palatino Linotype; senão Constantia, Book Antiqua ou
    Georgia), em dourado claro. Valem para as janelas, o nome da zona, as opções, o tutorial e a tela de entrada. A
    dica entre parênteses do título fica clara e menor.
  - **Ornamento dourado** nos quatro cantos de todo painel com pelo menos 120×70: filetes que somem ao longo da borda,
    um losango e um ponto. A tela de entrada usa o mesmo painel.
  - **Silhuetas** nas casas de equipamento vazias: espada, couraça, luva, bota e anel.
- **Ícones de skill** (`SkillIcons`): um medalhão na cor do tipo com o símbolo de cada uma das 16 skills (corte, giro, runa, chama, floco de gelo, raio...).
- **Ícones de item** (`ItemIcons`): uma câmera escondida fotografa o modelo do item na primeira vez que ele aparece numa janela, com luz própria e fundo transparente. Sem modelo, fica o quadradinho com o nome.

# Pacotes de cenário e de monstros (CC0)

Todos são CC0 (domínio público): uso livre, inclusive comercial, sem precisar de crédito. As licenças ficam junto, em
`Assets/Art/KayKit` e `Assets/Art/Quaternius`.

- **KayKit Forest Nature Pack (grátis)**, em `Assets/Art/KayKit/Natureza`. Fornece as árvores, arbustos, pedras e
  rochedos do mundo. O `PropBuilder` usa o pacote quando ele está no projeto e volta às formas feitas em código quando
  não está. Na Mata Sombria o mesmo atlas ganha um tom mais escuro; nas Ruínas, um tom seco.
- **KayKit Medieval Hexagon Pack (grátis)**, em `Assets/Art/KayKit/Vila`:
  - casas, taverna, igreja e serraria em volta da praça;
  - moinho de vento nos campos e moinho de água no rio;
  - cerca e poço;
  - nos cantos dos NPCs: caixotes, barris, frutas, alvo, armeiro e as barracas dos acampamentos.
  O pacote é feito na escala de um tabuleiro, por isso tudo é aumentado de 4 a 6,5 vezes.
- **Quaternius Ultimate Monsters e Ultimate Animated Animals**, em `Assets/Art/Quaternius`, montados pelo
  `QuaterniusBuilder` (menu **Jogo > Visuais > Montar monstros (Quaternius)**):
  - o Coelho Saltador é o Bunny, com a cenoura;
  - o Lobo Cinzento é o Wolf;
  - cada um tem Animator próprio: parado, andando e correndo pela velocidade, golpe, dano e morte.
  - **Bichos de enfeite:** cervos, veados e raposas pastam nos campos e na mata. Não lutam e não bloqueiam; cada um
    começa a animação num ponto diferente.
  - O Javali Furioso virou o **Touro Furioso**, com o Bull do Quaternius. Os ids do jogo continuam os mesmos e só os
    textos mudaram.
  - O golem é montado com as pedras do KayKit Forest (tronco, cabeça, braços e pernas) e anima por código.
  - A aranha continua feita em código, porque nenhum pacote tem uma. O espectro
    continua sendo o esqueleto-fantasma do KayKit.
- **Conferir um pacote novo:** `-executeMethod Game.Client.Editor.AssetSheet.Run -sheetFolder Assets/Art/... -sheetOut pasta`
  monta uma folha de fotos e lista tamanho, materiais e animações de cada modelo.

# Contorno (acabamento único)

`Shaders/Contorno.shader` desenha um traço de desenho animado em tela cheia, antes do pós-processamento, no
renderizador do PC. O `RenderLook` liga o shader toda vez que a cena é montada.

- O traço acha as bordas pela profundidade e pelas normais da cena (as mesmas que a oclusão de ambiente já calcula).
- Ele escurece a cor de cada borda sem trocar o tom.
- Some a partir de 25 m e desaparece aos 45 m, para o fundo não virar rabisco.
- O traço é leve (força 0,5) porque a grama tem muitas folhinhas.
- O renderizador de celular fica sem o contorno.

# Efeitos das skills com shader

`Fx/MagicFx` cuida de três efeitos novos. Nada é criado nem destruído por golpe: cada um sai de uma reserva.

- **Marcas no chão** (`Shaders/MarcaChao.shader`):
  - os desenhos são feitos em código, sem textura: runa girando, rachaduras com brilho, queimado com brasas e geada
    cristalina;
  - cada marca aparece rápido, esmaece e acompanha a inclinação do morro.
  - Por skill: Golpe Rúnico, Giro e Tempestade fazem runa; Fenda Sísmica e Execução, rachadura; Lâmina Flamejante e
    Meteoro, queimado; Corrente Gélida, geada; Explosão Arcana, runa; Cataclismo, queimado e runa juntos.
- **Cúpulas de energia** (`Shaders/Energia.shader`):
  - o shader tem borda brilhante, energia ondulando por dentro e dissolve aos pedaços;
  - as cúpulas crescem do ponto do golpe nas magias de área e nas explosões;
  - o mesmo shader faz a aura dos projéteis.
  As marcas e cúpulas usam a cor própria da skill; o dourado do Excellent fica nas faíscas e no clarão.
- **Impacto de tela:** borrão radial a partir do alvo, com clarão, que some em cerca de 0,3 s.
  - É forte no finalizador, mais leve no Excellent e nos golpes pesados.
  - Fica no mesmo passo de tela cheia do contorno, sem custo extra.
- **Conferir:** `TerrasDeAstralis.exe -skilltest pasta`.

# NPCs e moradores

- **Marcadores** em cima de quem dá missões:
  - "!" amarelo quando há missão nova para o nível;
  - "?" amarelo quando há missão pronta para entregar;
  - "?" cinza quando a missão está em andamento.
- **Conversa:** o NPC vira para o jogador e faz um gesto (`Interact`). Ao fechar, volta para a pose de antes. Parado, ele
  troca de pose de vez em quando (`Idle_B`). Os gatilhos `Talk` e `Idle2` estão no Animator dos personagens (`World/NpcLife.cs`).
- **Moradores** (`World/VillageLife`), em `Resources/Visuals/villager`: dois adultos andam pela praça e duas crianças
  (menores, para não confundir com os NPCs) correm de um lado para o outro. São só enfeite: não são alvo e não falam.

# Clima das regiões

`World/RegionAmbience.cs` põe partículas em volta da câmera, na altura do chão. Cada camada pesa conforme a distância do
centro da sua região (cheia até 80% do raio, some até 130%), então o clima muda aos poucos ao andar.

| Região | Camadas |
|---|---|
| Campos do Vento | pólen claro e folhinhas levadas pelo vento |
| Mata Sombria | vaga-lumes (mais fortes à noite) e névoa rasteira |
| Ruínas Esquecidas | partículas mágicas roxas subindo e brilhos ciano (mais fortes à noite) |

- Usam o material `Resources/Visuals/fx/particula_suave` e acendem e apagam devagar.
- **Tempo que muda** (segue o relógio de verdade, UTC, então quem está online vê mais ou menos o mesmo):
  - **Chuva** na Mata Sombria: pancadas de alguns minutos com intervalos maiores (`RegionAmbience.Rain`). Gotas
    esticadas caindo rápido; enquanto chove, o tempo fecha (`Atmosphere.Overcast`): sol fraco, neblina cinza mais densa,
    cores apagadas e céu mais escuro e cinza.
  - **Rajadas** nos Campos do Vento: o pólen e as folhas aumentam e correm mais rápido quando o vento aperta.
- **Conferir:** `TerrasDeAstralis.exe -worldtest pasta` fotografa também a mata (com chuva) e as ruínas, de dia, no pôr do
  sol e à noite, e o céu olhando para a lua (`ceu-noite`).

# Água

`Shaders/Agua.shader` (material `Materials/Prototipo/Agua.mat`, montado pelo `VillageBuilder`):

- **Cor:** rasa perto da margem e funda no meio, pela textura de profundidade da cena.
- **Ondinhas** que correm com a corrente (`_Flow`), com reflexo do céu quando se olha de lado (fresnel) e brilho do sol.
- **Espuma** onde a água fica rasa: margens, pedras e pilares da ponte.
- **De noite** a água escurece junto com a luz ambiente.
- Sem textura de profundidade (perfil de qualidade baixa) fica só a cor funda, com reflexo e brilho.
- **Conferir:** as vistas `ponte-*` do `-worldtest`.

# Minimapa

Ao montar a cena, o `Editor/MinimapBaker` fotografa o mundo de cima (câmera ortográfica, norte para cima, sem neblina) e
guarda em `Resources/Visuals/mapa_mundo.png` (1024×1024, 256 m de lado, centro em `Minimap.WorldMapCenter`). A foto é
tirada com a cena já salva e reaberta: na cena recém-montada, com os materiais ainda só na memória, pedras e colunas
saíam azul forte e as casas cinza-escuras.

- O minimapa e o mapa grande (tecla M) desenham essa foto por baixo das zonas, dos pontos e da seta do jogador.
- Os discos das zonas ficam bem fracos por cima da foto e os nomes ganham sombra.
- Sem a foto (ou na dungeon e na arena), o mapa volta aos discos coloridos.
- **Conferir:** abrir `mapa_mundo.png`; a vista `9c-mapa` do teste online (`-onlineui`).
