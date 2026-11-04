# Deploy: servidor na VPS, backup, monitoramento e atualização

## Como fica

```
Jogadores ──TCP 7777──────────────────────────▶ game (jogo + API de login, .NET)
          ──HTTPS 443 ─▶ caddy ──/api/*──▶ game:7778          │
                                                              ▼
                       backup (pg_dump todo dia) ◀──── postgres (só na rede interna)
                       offsite (rclone, opcional) ──▶ outro lugar (B2, Drive, S3...)
```

Tudo em `deploy/`: `docker-compose.prod.yml`, `Caddyfile`, `.env.example`, `server.Dockerfile` e `scripts/`.

- **Portas abertas para a internet:** 7777 (jogo), 80 e 443 (Caddy).
- **Portas internas:** a 7778 (API e `/metrics`) só responde no `127.0.0.1` da própria VPS, e o banco não tem porta nenhuma.
- **Endereço que o jogador usa:** só o domínio, por exemplo `jogo.exemplo.com.br:7777`. Com domínio, o cliente usa sozinho `https://jogo.exemplo.com.br/api` para o login; com um IP, usa `http://ip:7778/api` (só para teste).

## 0. Servidor no seu PC com Tailscale (grátis, para jogar com amigos)

Sem VPS e sem mexer no roteador (funciona até com CGNAT). O Tailscale cria uma rede privada entre o seu PC e o de cada
amigo. O jogo só fica no ar com o PC ligado, e cada amigo instala o Tailscale.

1. **No seu PC:** instale o Tailscale (tailscale.com/download), entre com a sua conta e anote o IP `100.x.x.x` que ele
   mostra.
2. **Firewall do Windows** (PowerShell como administrador, uma vez só): libera as portas só para quem vem pelo Tailscale.

   ```powershell
   New-NetFirewallRule -DisplayName "Terras de Astralis (Tailscale)" -Direction Inbound -Protocol TCP -LocalPort 7777,7778 -RemoteAddress 100.64.0.0/10 -Action Allow
   ```

3. **Ligar o servidor:** com o Docker Desktop aberto, na raiz do projeto:

   ```powershell
   powershell -ExecutionPolicy Bypass -File deploy\scripts\servidor-pc.ps1
   ```

   O script liga o banco, mostra o endereço para os amigos e sobe o servidor. Para desligar: Ctrl+C (salva todos antes).
4. **Cliente:** no `servidor.txt` (ao lado do `TerrasDeAstralis.exe` e em `Client/servidor.txt`, para os próximos builds):

   ```
   servidor = Servidor do Lucas | 100.x.x.x:7777
   ```

   Com um IP, o cliente faz o login direto na porta 7778, sem HTTPS: a senha viaja dentro da rede do Tailscale, que já é
   criptografada.
5. **Amigos:** no painel do Tailscale → Machines → os três pontinhos do seu PC → **Share**. Cada amigo instala o Tailscale,
   entra com a conta dele e abre o link: ele enxerga só o seu PC. Mande também o `.zip` do jogo (a pasta do build sem a
   `*_BackUpThisFolder_*`).
6. **Enquanto tiver gente jogando:** deixe o PC sem suspensão e o Tailscale aberto.

## 1. VPS

Para o teste fechado (até umas 50 pessoas), uma VPS Linux com **2 vCPU e 2 a 4 GB de RAM** sobra: 30 bots usam cerca de 1 ms por tick.

- **Região:** prefira São Paulo, para ter menos ping.
- **Sistema:** Ubuntu 24.04.

Na primeira vez, entre por SSH como root:

```bash
apt update && apt upgrade -y
adduser jogo && usermod -aG sudo jogo
curl -fsSL https://get.docker.com | sh
usermod -aG docker jogo
ufw allow OpenSSH && ufw allow 80/tcp && ufw allow 443/tcp && ufw allow 7777/tcp && ufw enable
```

- **SSH:** use chave em vez de senha e desligue o login por senha (`PasswordAuthentication no` em `/etc/ssh/sshd_config`).
- **Docker e firewall:** o Docker abre as portas publicadas por fora do ufw. Por isso o compose só publica a 7777, a 80 e a 443, e a 7778 fica presa ao 127.0.0.1.

### Google Cloud (Compute Engine), com o teste grátis

A conta nova do Google Cloud costuma ganhar um crédito de teste (por volta de US$ 300 por 90 dias), e há região em
**São Paulo**. Durante o teste, o Google não cobra sozinho quando o crédito ou o prazo acabam: as máquinas param, e só
há cobrança se você ativar a conta paga. Confira as condições na hora do cadastro.

1. **Conta:** cloud.google.com → *Get started for free*. O cartão é pedido só para conferir a identidade.
2. **Projeto:** no topo do console, crie um projeto `astralis`. Abra Compute Engine e clique em *Enable* para ativar a
   API (leva um minuto).
3. **Alerta de gastos:** Billing → Budgets & alerts → Create budget, com alertas por e-mail (por exemplo, a 50%, 90% e
   100% do crédito).
4. **Máquina:** Compute Engine → VM instances → Create instance.
   - **Nome:** `astralis`. **Região:** `southamerica-east1` (São Paulo), zona `-a`.
   - **Tipo:** série E2, `e2-small` (2 vCPU compartilhadas e 2 GB) ou `e2-medium` (4 GB), que sobra.
   - **Disco** (OS and storage → Change): Ubuntu, **Ubuntu 24.04 LTS** x86/64 (amd64), *Balanced persistent disk*,
     **20 GB**.
   - **Rede (Networking):**
     - marque *Allow HTTP traffic* e *Allow HTTPS traffic*;
     - em *Network tags*, escreva `jogo`;
     - em *Network interfaces* → default → *External IPv4 address*, escolha **Reserve static external IP address**
       (nome `astralis`). É o IP fixo que vai no domínio.
5. **Porta do jogo:** VPC network → Firewall → Create firewall rule.
   - Nome `jogo-7777`, direção *Ingress*, ação *Allow*.
   - *Targets:* *Specified target tags* → `jogo`.
   - *Source IPv4 ranges:* `0.0.0.0/0`.
   - *Protocols and ports:* TCP `7777`.
6. **Entrar:** na lista de VM instances, clique em **SSH** ao lado da máquina. Abre um terminal no navegador, sem
   precisar de chave. O usuário é o da sua conta Google, não `ubuntu`.
7. **Memória:** com a `e2-small`, crie 2 GB de swap como no passo 6 da AWS.
8. Daí em diante, siga os comandos abaixo, menos a linha do `ufw` (o firewall é o do passo 5). Para usar o Docker sem
   `sudo`: `sudo usermod -aG docker $USER`, e feche e abra o SSH de novo.

### AWS (EC2), com os créditos de conta nova

A conta nova da AWS ganha créditos que cobrem os primeiros meses. Depois, a `t4g.small` em São Paulo, o disco e o IP
público custam por volta de US$ 15 a 25 por mês (confira na calculadora da AWS). A imagem do servidor roda em ARM, então
a máquina Graviton (`t4g`), mais barata, serve.

1. **Alerta de gastos antes de tudo:** Billing and Cost Management → Budgets → Create budget → modelo *Zero spend budget*
   (ou um de US$ 1), com o seu e-mail. Assim você fica sabendo de qualquer cobrança.
2. **Região:** no canto de cima, à direita, escolha **South America (São Paulo) sa-east-1**.
3. **Máquina:** EC2 → Launch instance.
   - **Nome:** `astralis`.
   - **Imagem:** Ubuntu Server **24.04 LTS**, arquitetura **64-bit (Arm)**.
   - **Tipo:** `t4g.small` (2 vCPU e 2 GB).
   - **Par de chaves:** Create new key pair → nome `astralis`, tipo ED25519, formato `.pem`. Guarde o arquivo baixado:
     sem ele não dá para entrar na máquina, e a AWS não o envia de novo.
   - **Rede (Edit):**
     - SSH com origem **My IP**;
     - marque HTTPS e HTTP;
     - Add security group rule → Custom TCP, porta **7777**, origem `0.0.0.0/0`.
   - **Disco:** 20 GiB, gp3.
4. **IP fixo:** EC2 → Elastic IPs → Allocate → Actions → Associate → a instância `astralis`. É esse IP que vai no
   domínio. Sem ele, o IP muda toda vez que a máquina desliga e liga.
5. **Entrar no Windows (PowerShell):** o SSH recusa a chave se outros usuários puderem lê-la. Rode uma vez:

   ```powershell
   icacls astralis.pem /inheritance:r /grant:r "$($env:USERNAME):(R)"
   ssh -i astralis.pem ubuntu@IP_ELASTICO
   ```

6. **Memória:** com 2 GB, vale criar 2 GB de swap, uma vez só:

   ```bash
   sudo fallocate -l 2G /swapfile && sudo chmod 600 /swapfile && sudo mkswap /swapfile && sudo swapon /swapfile
   echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
   ```

7. Daí em diante, siga os comandos abaixo. O usuário padrão da AWS é `ubuntu`: dá para pular o `adduser jogo` e usar o
   `ubuntu`, com `sudo usermod -aG docker ubuntu` e saindo e entrando de novo no SSH.

### Opção grátis: Oracle Cloud (Always Free)

A camada grátis da Oracle tem máquinas **ARM (Ampere A1)** que sobram para o teste fechado. A imagem do servidor é
publicada para x64 e ARM, então o passo 3 funciona igual.

1. **Conta:** crie em cloud.oracle.com. O cartão é pedido só para conferir a identidade; a camada grátis não cobra.
   - **Região de casa:** escolha **Brazil East (São Paulo)** no cadastro. Ela não muda depois, e os recursos grátis
     só existem nela.
2. **Máquina:** Compute → Instances → Create instance.
   - **Imagem:** Canonical Ubuntu 24.04 (a versão aarch64 aparece sozinha com o shape ARM).
   - **Shape:** Ampere → `VM.Standard.A1.Flex`, com 2 OCPU e 12 GB (o limite grátis é 4 OCPU e 24 GB no total).
   - **Rede:** deixe criar a VCN com sub-rede pública e IP público.
   - **SSH:** cole a sua chave pública (`ssh-keygen -t ed25519` no seu PC, se ainda não tiver).
   - **"Out of capacity"?** Falta máquina livre naquele momento. Tente outro domínio de disponibilidade ou de novo mais
     tarde.
3. **Portas na nuvem:** Networking → Virtual cloud networks → a sua VCN → Security Lists → Default → Add Ingress Rules.
   Crie três regras com origem `0.0.0.0/0`, protocolo TCP e portas de destino **80**, **443** e **7777**.
4. **Portas na máquina:** o Ubuntu da Oracle já vem com regras de `iptables` que bloqueiam tudo além do SSH. **Não use
   o ufw** do passo seguinte nessa máquina; em vez dele, rode:

   ```bash
   sudo iptables -L INPUT -n --line-numbers   # anote o número da linha do REJECT (na A1 com Ubuntu 24.04 é a 5)
   sudo iptables -I INPUT 5 -m state --state NEW -p tcp --dport 80 -j ACCEPT
   sudo iptables -I INPUT 5 -m state --state NEW -p tcp --dport 443 -j ACCEPT
   sudo iptables -I INPUT 5 -m state --state NEW -p tcp --dport 7777 -j ACCEPT
   sudo netfilter-persistent save
   ```

   Use o número do REJECT no lugar do 5: as regras precisam ficar **antes** dele, senão não valem.

5. **Usuário:** entre com `ssh ubuntu@IP_DA_MAQUINA`. Daí em diante, siga os comandos abaixo, menos a linha do `ufw`.
6. **Máquina parada:** a Oracle pode recolher instâncias grátis que ficam ociosas por muito tempo. Um servidor com gente
   jogando não fica ocioso. Se acontecer, os backups fora da VPS (passo 4) permitem subir outra.

## 2. Domínio

No painel do domínio, crie um registro **A** apontando para o IP da VPS, por exemplo `jogo.exemplo.com.br`. O Caddy pega o certificado HTTPS sozinho na primeira vez que sobe, e para isso o DNS já precisa estar apontando.

**Sem domínio pago: DuckDNS (grátis).**
1. Entre em duckdns.org com uma conta do GitHub ou do Google.
2. Crie um subdomínio, por exemplo `meujogo`, e no campo *current ip* ponha o IP público da VPS.
3. No `.env`, use `GAME_DOMAIN=meujogo.duckdns.org`. O Caddy tira o certificado HTTPS para esse endereço do mesmo jeito.
4. No `servidor.txt` do cliente: `servidor = Servidor Brasil | meujogo.duckdns.org:7777`.

O IP público da Oracle não muda enquanto a instância existir, então não precisa do script de atualização do DuckDNS.

## 3. Subir o servidor

Entre como o usuário `jogo`:

```bash
git clone -b main https://github.com/LucasHevandro/TerrasDeAstralis.git
cd TerrasDeAstralis/deploy
cp .env.example .env
nano .env
```

No `.env`, preencha `GAME_DOMAIN` e `GAME_DB_PASSWORD`. Para gerar a senha, use `openssl rand -hex 24`. Depois suba:

```bash
docker compose -f docker-compose.prod.yml pull
docker compose -f docker-compose.prod.yml up -d
curl -s https://jogo.exemplo.com.br/api/health
```

- **Saúde:** a resposta deve trazer `"status":"ok"`. As migrações do banco rodam sozinhas quando o servidor liga.
- **Imagem privada:** o pacote no GHCR nasce privado. Antes do `pull`, rode `docker login ghcr.io` com um token do GitHub que tenha `read:packages`. Outra saída é deixar o pacote público em GitHub → Packages → astralis-server → Settings.
- **Sem imagem publicada:** dá para construir na própria VPS com `docker compose -f docker-compose.prod.yml build game`.

Comandos do dia a dia (dentro de `deploy/`, com `C="docker compose -f docker-compose.prod.yml"`):

| Para | Comando |
|---|---|
| Ver o log do jogo | `$C logs -f --tail 200 game` |
| Ver se está tudo de pé | `$C ps` |
| Reiniciar o jogo (salva todos antes) | `$C restart game` |
| Métricas | `curl -s http://127.0.0.1:7778/metrics` |
| Tornar uma conta GM (vale no próximo login) | `$C run --rm --no-deps game --set-gm nomedaconta` |
| Tirar o GM | `$C run --rm --no-deps game --unset-gm nomedaconta` |
| Relatório de métricas (últimos 7 dias) | `$C run --rm --no-deps game --report 7` |
| Apagar uma conta a pedido do jogador (LGPD) | `$C run --rm --no-deps game --delete-account nomedaconta` |

## 4. Backup

O serviço `backup` faz um `pg_dump` compactado:
- **Quando:** todo dia às `BACKUP_HOUR` (padrão 4h, horário de Brasília) e também logo depois de ligar.
- **Onde:** em `deploy/backups/`.
- **Quantos:** guarda os `BACKUP_KEEP` mais novos (padrão 14).
- **Conferência:** cada arquivo é verificado com `pg_restore --list` antes de ser aceito.

Para fazer um backup na hora, por exemplo antes de atualizar:

```bash
docker compose -f docker-compose.prod.yml exec backup sh /scripts/backup.sh
```

**Cópia fora da VPS (recomendado).** Se a VPS morrer, os backups que estão nela somem junto. Para copiar para outro lugar com o rclone (Backblaze B2, Google Drive, S3 etc.):

```bash
mkdir -p rclone
docker run --rm -it -v "$PWD/rclone:/config/rclone" rclone/rclone:1.68 config
```

Siga as perguntas e crie um remoto chamado, por exemplo, `b2`. Depois, no `.env`, defina `RCLONE_TARGET=b2:meu-bucket/jogo` e suba o serviço:

```bash
docker compose -f docker-compose.prod.yml --profile offsite up -d
```

Ele copia a pasta de backups a cada 6 horas.

**Restaurar.** O script desliga o jogo (salvando quem está online), faz um backup do estado atual por garantia, troca o banco e liga de novo. Ele pede para digitar RESTAURAR antes de começar:

```bash
sh scripts/restore.sh backups/game-2026-10-20_040000.dump
```

**Teste a restauração de vez em quando** num banco à parte, sem mexer no de verdade:

```bash
C="docker compose -f docker-compose.prod.yml"
$C exec -T postgres psql -U game -d game -c "DROP DATABASE IF EXISTS restore_test" -c "CREATE DATABASE restore_test"
$C exec -T postgres pg_restore -U game -d restore_test --no-owner < backups/ARQUIVO.dump
$C exec -T postgres psql -U game -d restore_test -c "select count(*) from accounts"
```

## 5. Monitoramento

- **UptimeRobot** (grátis), monitor do tipo *Keyword*:
  - URL `https://jogo.exemplo.com.br/api/health`, palavra-chave `"status":"ok"`, a cada 5 minutos, com alerta no e-mail ou no Discord.
  - A resposta é 503 se o servidor está ligando ou se o loop do jogo travou por mais de 5 s.
  - Crie também um monitor do tipo *Port* na 7777.
- **O que o `/api/health` mostra:**
  - jogadores, monstros e conexões;
  - ticks por segundo (o normal é 20) e tempo médio e máximo do tick;
  - erros pegos no loop desde que ligou (o normal é 0; cada um vira uma linha `[erro]` no log);
  - memória, protocolo e `build` (a versão da imagem).
- **Prometheus/Grafana (opcional):** o `/metrics` está no formato do Prometheus, em `http://127.0.0.1:7778/metrics`.
- **Logs:** o Docker guarda até 5 arquivos de 20 MB por serviço.
- **O que procurar no log:** `[erro]` (defeito no servidor), `[suspeita]` (detecção de bot), `[gm]` (ações de GM) e `Falha` (gravação no banco).

## 6. Atualizar em menos de 1 hora

1. **Corrija e teste no PC:** `dotnet test Game.slnx` e o teste online do cliente.
2. **Mande para a `main`** (merge ou push). O GitHub Actions roda os testes e publica a imagem `ghcr.io/lucashevandro/astralis-server:latest`, com o commit como tag. Isso leva uns 10 minutos.
3. **Avise no Discord** que o servidor vai reiniciar em 5 minutos.
4. **Na VPS:**
   ```bash
   cd ~/TerrasDeAstralis/deploy
   docker compose -f docker-compose.prod.yml exec backup sh /scripts/backup.sh   # se a atualização mexe no banco
   docker compose -f docker-compose.prod.yml pull game
   docker compose -f docker-compose.prod.yml up -d game
   curl -s https://jogo.exemplo.com.br/api/health
   ```
   Os jogadores ficam fora por uns 10 segundos. Todos são salvos ao desligar, porque o `docker stop` dá 30 s para isso.
5. **Confira** que o `build` no `/api/health` é o commit novo.

**Deu errado? Volte à versão anterior.** No `.env`, ponha `GAME_IMAGE=ghcr.io/lucashevandro/astralis-server:<commit-anterior>` e rode `up -d game`. Se a versão nova mudou o banco (migração), restaure o backup feito no passo 4.

**Mudou o protocolo** (`Protocol.Version`)? Aí o cliente também precisa ser atualizado, porque o servidor recusa cliente de versão diferente. Crie uma tag `v0.x.y`: o Actions gera o cliente e, se o itch.io estiver configurado, envia. O jogador só precisa baixar de novo.

## 7. GitHub Actions

O arquivo é `.github/workflows/ci.yml`.

- **Testes:** em todo push na `main` e em todo pull request, com um PostgreSQL de verdade.
- **Imagem do servidor:** na `main` e nas tags `v*`, publicada no GHCR. Não precisa configurar nada, porque ela usa o `GITHUB_TOKEN`.
- **Branch padrão:** hoje a branch padrão do repositório no GitHub é `feat/fase3-rede`. Troque para `main` em Settings → General → Default branch.
- **Cliente Windows (opcional):**
  - Usa o [game-ci](https://game.ci/docs/github/activation). O build pelo Actions ainda não foi testado; ele roda no Linux com o backend Mono, que é o do projeto.
  - Variável do repositório (Settings → Secrets and variables → Actions → Variables): `BUILD_CLIENT` = `true`.
  - Variável `GAME_SERVER` com o endereço padrão (por exemplo `jogo.exemplo.com.br:7777`). Ela vira o `servidor.txt` do build.
  - Segredos `UNITY_LICENSE`, `UNITY_EMAIL` e `UNITY_PASSWORD`. Para a licença Personal, siga o passo a passo de ativação do game-ci.
  - O build sai como artefato do Actions e fica disponível por 14 dias.
- **itch.io (opcional, só em tags `v*`):** segredo `BUTLER_API_KEY` (gerado no itch.io → Settings → API keys) e variável `ITCH_TARGET` (por exemplo `usuario/nome-do-jogo`).

## 8. Lista de servidores no cliente

O jogador escolhe o servidor numa lista na tela de entrada, sem digitar o endereço. Cada servidor mostra o status lido
do `/api/health` dele: no ar (com jogadores e ping), instável, outra versão do jogo ou fora do ar. O último escolhido
fica salvo. O "Outro endereço…" continua lá para testes. A lista vem do `servidor.txt`, que fica ao lado do
`TerrasDeAstralis.exe`:

```
# Servidores da lista: nome | host:porta | api (opcional, só se a API não estiver no mesmo domínio)
servidor = Servidor Brasil | jogo.exemplo.com.br:7777
servidor = Servidor de testes | teste.exemplo.com.br:7777
# Lista na internet, no mesmo formato, baixada ao abrir o jogo (abre servidor novo sem mandar outro cliente)
lista = https://exemplo.com.br/servidores.txt
# Formato antigo (ainda vale): vira o "Servidor principal", no topo da lista
jogo = jogo.exemplo.com.br:7777
```

- **Na linha de comando:** `-server host:porta` e `-api URL` passam na frente do arquivo.
- **No Editor do Unity:** o arquivo é `Client/servidor.txt`, que não vai para o Git.
- **No build:** o build copia esse arquivo, ou usa a variável `GAME_SERVER`.

## 9. Testar a pilha de produção no próprio PC

Com o Docker Desktop aberto:

```bash
docker build -f deploy/server.Dockerfile -t game-server:local .
cd deploy
printf 'GAME_DOMAIN=localhost\nGAME_DB_PASSWORD=teste_local\nGAME_IMAGE=game-server:local\n' > .env
docker compose -f docker-compose.prod.yml up -d
curl -sk https://localhost/api/health
```

- O banco de desenvolvimento (`game-postgres`) pode continuar ligado; só não deixe um servidor do jogo de desenvolvimento aberto na 7777.
- Os bots e o cliente entram com `127.0.0.1:7777`. A API em `127.0.0.1:7778` está publicada só no loopback.
- Para desligar e apagar o banco de teste: `docker compose -f docker-compose.prod.yml down -v`.

## Limites conhecidos

- **Contas por IP:** cada IP cria no máximo 3 contas em 24 horas (`AuthService.MaxAccountsPerIp`). O IP vem do `X-Forwarded-For` que o Caddy envia, aceito só quando o pedido chega pela rede interna. O limite fica na memória e zera quando o servidor reinicia. O login trava a conta após 5 senhas erradas.
- **Termos e privacidade:** preencha os campos `<<preencher>>` dos textos antes de abrir o teste (veja `Docs/metricas.md`).
- **Uma VPS só:** se ela cair, o jogo cai. O monitoramento avisa, e o backup fora da VPS garante que nada se perde.

## Launcher, atualização automática e instalador

O jogador instala uma vez (pelo instalador). O atalho abre o **launcher** (`TerrasDeAstralisLauncher.exe`, em
`Tools/Game.Launcher`, .NET Framework 4.8, que já vem no Windows 10 e 11).

**O que o launcher faz ao abrir:**
- baixa o `https://DOMINIO/download/versao.json`;
- confere cada arquivo do jogo pelo SHA-256 e baixa **só os que mudaram** (compactados), mostrando o progresso;
- apaga o que saiu da versão e troca a si mesmo quando ele também mudou;
- mostra as novidades numa linha do tempo (a versão atual e as anteriores) e se o servidor está no ar, e libera o
  botão **Jogar**;
- sem internet, deixa abrir a versão instalada.

**Publicar uma versão nova** (no PC, depois do build do Windows, com o `ssh astralis` configurado):

```powershell
powershell -ExecutionPolicy Bypass -File Tools\launcher\publicar.ps1 -Novidades "Monstros elite nos campos."
```

- **Envio:** só manda para a VPS os arquivos que ela ainda não tem, e o `versao.json` vai por último. Quem abrir o
  launcher no meio do envio ainda pega a versão anterior inteira.
- **Versão:** a do Player Settings (`bundleVersion`) mais a data, ou `-Versao 0.2.0`.
- **Instalador:** refaz o `Client/Builds/InstalarTerrasDeAstralis.exe` com a mesma versão (`-SemInstalador` pula).
- **Limpeza:** `-Limpar` apaga da VPS os arquivos que a versão nova não usa mais.
- **Histórico:** o script baixa o `versao.json` que está no ar, e a versão nova sai com o campo `historico` (a anterior
  e o histórico dela, até 30 versões). As versões de antes do histórico existir estão em
  `Tools/launcher/historico-inicial.json`. Para corrigir um texto antigo, edite esse arquivo e publique de novo.
- **Endereço:** o launcher usa `https://teradestralis.duckdns.org/download/`. Para outro, ponha
  `atualizacao = https://outro/download/` no `servidor.txt` (ele vai junto na versão).

**Na VPS (uma vez):** a pasta `deploy/download` é servida pelo Caddy em `/download/`. Depois do `git pull`, rode
`docker compose -f docker-compose.prod.yml up -d caddy` para ele enxergar a pasta.

**Mudou o protocolo?** Publique a versão do cliente junto com a atualização do servidor: o launcher baixa o cliente
novo antes de o jogador entrar.

**Só o instalador**, sem publicar: `powershell -ExecutionPolicy Bypass -File Tools\instalador\gerar-instalador.ps1`.
- **Sai em** `Client/Builds/InstalarTerrasDeAstralis.exe` (uns 35 MB). Precisa do Inno Setup 6 (`winget install --id JRSoftware.InnoSetup -e`).
- **O que faz:** instala na pasta do usuário, sem pedir administrador; cria os atalhos (para o launcher) na Área de
  Trabalho e no Menu Iniciar; aparece em "Adicionar ou remover programas".
- **Aviso do Windows:** sem assinatura digital, o SmartScreen mostra "O Windows protegeu o computador". O jogador
  clica em **Mais informações → Executar assim mesmo**.

**Testar o launcher sem a VPS:** `TerrasDeAstralisLauncher.exe --publicar PASTA_DO_BUILD SAIDA 0.1.0-teste`, sirva a
`SAIDA` (por exemplo `python -m http.server 8099 --directory SAIDA`) e rode o launcher com
`--pasta OUTRA_PASTA --url http://127.0.0.1:8099/`. Com `--verificar` ele atualiza sem janela e escreve o
`launcher.log`; com `--foto arquivo.png` ele salva a janela pronta.
