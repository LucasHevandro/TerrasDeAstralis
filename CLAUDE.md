# Terras de Astralis: contexto para o Claude

Action MMORPG com combos de timing. Cliente Unity, servidor autoritativo .NET e regras compartilhadas em C#. O README
tem a estrutura e como rodar, e `Docs/` tem as decisões, o balanceamento, os visuais, o áudio e o deploy.

## Regras de trabalho (pedidas pelo usuário)

- **Português do Brasil** em tudo: respostas, resumos, legendas, comentários e mensagens de commit.
- **Commit por camada**, com prefixo: `Shared:`, `Servidor:`, `Cliente:`, `Tools:` e `Docs:`. Um commit por camada
  tocada, com a linha `Co-Authored-By` no fim.
- **Push e merge só com autorização.** O usuário costuma responder "Pode enviar a branch ao GitHub e mesclar na main."
  O fluxo é `git push origin develop` e `git push origin develop:main` (fast-forward), depois
  `git branch -f main develop`.
- **Depois de rodar o exe com `-screen-*`**, apague `Screenmanager*` e `UnitySelectMonitor*` em
  `HKCU:\Software\Astralis\Terras de Astralis`. Senão o jogo dele abre em janela no segundo monitor.
- **Senhas e tokens nunca são digitados pelo Claude.** Não peça e não repita credenciais; o usuário digita.
- **Remote Control:** o usuário costuma acompanhar pelo celular. Quando o box do wrap_up não aparece, a mensagem
  final começa pelo resumo e traz a pergunta e as opções.

## Repositório

- **GitHub:** `LucasHevandro/TerrasDeAstralis` (era `Cabal`). Só existem duas branches (desde 2026-10-27): a
  `develop`, onde se desenvolve, e a `main`, que recebe a `develop` por fast-forward quando o usuário autoriza. A VM
  roda a `main`.
- **Pastas:** `Shared/Game.Shared` (netstandard2.1, também usada pelo Unity como pacote local), `Server/Game.Server`,
  `Client/` (Unity), `Tools/` (Bots, Balance, Launcher, instalador, publicar e `Blender/personagem`, o gerador da
  Lanceira por peças, com o próprio LEIAME) e `deploy/` (VPS). O Blender 5.4 do PC fica em
  `D:\Download\blender-5.4.0-alpha+main\blender-5.4.0\blender.exe`.
- **Testes:** `dotnet test Game.slnx -c Release` (hoje são 252 no Shared e 194 no Servidor). Os testes de textos
  exigem chaves literais no código: nada de `"login.head_" + i`, use um array de chaves literais.
- **Protocolo:** `Shared/Game.Shared/Net/Messages.cs`, `Protocol.Version = 20`. Se mudar alguma mensagem, suba a
  versão e atualize o cliente e o servidor juntos. O `FuzzTests` conta os tipos de mensagem.
- **Migrações:** `cd Server/Game.Server && dotnet ef migrations add Nome -o Persistence/Migrations`.
- **Textos:** `Shared/Game.Shared/Resources/Locales/pt-BR.txt`, lidos por `Loc.T("chave", args)`.

## Unity (Windows)

- **Editor:** `"/c/Program Files/Unity/Hub/Editor/6000.6.6f1/Editor/Unity.exe" -batchmode -quit -projectPath Client
  -executeMethod <Metodo> -logFile <log>`. Leia o log com `grep -a`.
- **Uma instância por vez:** se o comando voltar antes da hora, espere o `Unity.exe` terminar. Uma segunda
  instância aborta.
- **Métodos de build:**
  - `Game.Client.Editor.PrototypeBuild.BuildWindows`: gera `Client/Builds/Windows/TerrasDeAstralis.exe` e aplica o
    ícone.
  - `Game.Client.Editor.KayKitBuilder.Build`: refaz todos os prefabs. Depois, volte com `git checkout` o que não
    era para mudar (npc, monstro, aldeão e controllers) e commite só o que importa.
  - `Game.Client.Editor.GameIcon.Generate`: refaz o brasão em `Assets/Art/Icone`.
  - `Game.Client.Editor.MusicExport.ExportAll -musicOut pasta`: exporta as músicas e o som ambiente em WAV.
- **Testes do exe** (DevTools): `-onlinetest pasta` com `-onlinefight`, `-onlineui`, `-onlinesanctuary`,
  `-onlinearena`, `-onlinesocial` e `-onlineswitch` (trocar de personagem); e também `-worldtest`, `-geartest`,
  `-logintest`, `-skilltest`, `-posetest` e `-bridgetest`.
  - Cada um grava `relatorio.txt` (SUCESSO ou FALHA) e screenshots.
  - Para os online: servidor local com `dotnet artifacts/bin/Game.Server/release/Game.Server.dll --db memory --guests --port 7790`
    e o exe com `-connect 127.0.0.1:7790 -name Teste`.
  - Rode um jogo por vez: duas instâncias ao mesmo tempo atrapalham o teste.
- **Modelos:** KayKit Adventurers (Knight, Mage, Ranger, Rogue e Barbarian) usam o mesmo `Rig_Medium`. As cabeças
  trocam entre eles (penteados), e na textura a pele fica na célula (0,0) e o cabelo na (1,0).
  - O zip `KayKit_Adventurers_2.0_FREE.zip` (em Downloads) tem mais armas na pasta `Assets/fbx(unity)`. De lá vieram
    o arco e a flecha da Ranger.

## Armadilhas já vistas

- **Edições com Python:** heredoc no bash com aspas complicadas quebra. Grave o script com a ferramenta Write.
  Em strings Python com caminhos do Windows, use raw strings (`r"..."`): `\a`, `\b`, `\r` e `\v` viram caracteres
  de controle. Isso já estragou o `.iss` uma vez.
- **Matar processos:** `Stop-Process` filtrando por texto da linha de comando pode matar o próprio shell, se o
  texto estiver no comando. Filtre pelo nome do processo.
- **Exe de janela no PowerShell:** com `&`, o PowerShell não espera o exe terminar. Use `| Out-Null` para esperar.

## Produção (VPS Oracle, ARM, Ubuntu 24.04)

- **Acesso:** `ssh astralis` (configurado no `~/.ssh/config` do PC, chave `~/.ssh/astralis.key`). O IP é
  137.131.249.110 e o domínio é `teradestralis.duckdns.org`.
- **Pasta na VM:** `~/TerrasDeAstralis` (branch `main`). A stack fica em `deploy/docker-compose.prod.yml`, com o
  projeto `game`: `postgres`, `game` (imagem `astralis-server:local`, construída na própria VM), `caddy` (HTTPS,
  `/api/*` e `/download/*`) e `backup` (pg_dump diário em `deploy/backups`).
- **Portas:** 80, 443 e 7777 abertas na Security List da Oracle e no iptables, antes do `REJECT`.
- **Atualizar o servidor:**
  ```bash
  cd ~/TerrasDeAstralis && git pull && cd deploy && docker compose -f docker-compose.prod.yml build game && docker compose -f docker-compose.prod.yml up -d game
  ```
- **Conferir:** `curl -s https://teradestralis.duckdns.org/api/health` (o `protocol` tem que bater com o cliente).

## Cliente para os jogadores (launcher)

- **Launcher:** `Tools/Game.Launcher` (.NET Framework 4.8 e WinForms, `TerrasDeAstralisLauncher.exe`). Ele baixa o
  `https://teradestralis.duckdns.org/download/versao.json` e só os arquivos que mudaram (`arquivos/<sha256>.gz`),
  troca a si mesmo e abre o jogo.
  - Testes: `--pasta`, `--url`, `--verificar` e `--foto`.
  - Ele não está no `Game.slnx`, porque o CI roda no Linux.
- **Publicar uma versão** (depois do build do Windows):
  ```powershell
  powershell -ExecutionPolicy Bypass -File Tools\launcher\publicar.ps1 -Novidades "texto"
  ```
  O script envia só os arquivos novos por `scp` para `~/TerrasDeAstralis/deploy/download` e refaz o instalador.
  A versão é o `bundleVersion` (hoje 0.1.1) mais a data.
- **Instalador:** `Tools/instalador` (Inno Setup 6, já instalado no PC) gera `Client/Builds/InstalarTerrasDeAstralis.exe`.
  Os atalhos abrem o launcher.
- **Passo a passo completo:** `Docs/deploy.md` (seções "Atualizar" e "Launcher").

## Onde paramos (2026-10-21)

O usuário pediu, nesta ordem: **4, 6, 7, 8, 3 e 5**.
- **Feito:**
  - 4: missões diárias, chefe de mundo com horário e a segunda dungeon (Santuário das Ruínas).
  - 6: música por região e som ambiente.
  - 7: penteados, tom de pele e esconder o elmo.
  - Extras: servidor na Oracle, ícone, ponte, instalador, launcher e troca do nome do repositório.
- **8: monstros elite (feito no código em 2026-10-21, protocolo 18).** `EliteRules` no `WorldContent.cs` (5%, só mundo
  aberto; vida ×4, dano ×1,5, XP ×4, Alz ×3 e uma rolagem de drop a mais), `ServerMonster.IsElite`, campo `Elite` no
  `EntitySpawned` e, no cliente, modelo 20% maior, dourado, com luz e o nome "{0} (Elite)". O servidor aceita `--elites`
  (todo monstro comum nasce elite) para testar o visual. Publicado na VM e no launcher.
- **3: classe Ranger (feito no código em 2026-10-21).** Arqueira com `Ranger.fbx` e arco na mão esquerda, 8 skills de
  flecha, três arcos, `ClassInfo` no Shared e `--guest-class ranger` no servidor para testar com o exe. Detalhes em
  `Docs/decisoes.md`. Publicada junto com os elites (protocolo 18).
- **5: mercado (feito no código em 2026-10-21, protocolo 19).** Quadro de ofertas da Escrivã Marta (vende com o
  vendedor offline, taxa de 5%, 72 h), tabela `market_listings` (migração `Mercado`) e a tela de progressão (tecla L).
  Publicado. Depois vieram a troca de personagem (Esc), a ponte com o rio por baixo e a janela do personagem (P,
  abre junto com o inventário).
- **Depois (combinado em 2026-10-21):** teste fechado com 20 jogadores. Outras ideias do GDD ficam para mais à frente:
  rotas de combo com efeito, localizador de grupo, guilda e guerra, crafting.
