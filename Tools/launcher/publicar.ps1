# Publica uma versão nova do cliente na VPS: os launchers dos jogadores baixam só o que mudou na próxima vez que abrirem.
# Uso (gere o build do Windows antes, menu Protótipo > Gerar build Windows):
#   powershell -ExecutionPolicy Bypass -File Tools/launcher/publicar.ps1 -Novidades "Monstros elite nos campos."
# Opções: -Servidor astralis (o Host do ~/.ssh/config), -Versao 0.2.0, -Limpar (apaga da VPS os arquivos que nenhuma
# versão usa mais), -SemInstalador (não refaz o InstalarTerrasDeAstralis.exe).
# Na VPS, os arquivos ficam em ~/TerrasDeAstralis/deploy/download (o Caddy serve em https://DOMINIO/download/).
param(
    [string]$Novidades = '',
    [string]$Servidor = 'astralis',
    [string]$Versao = '',
    [switch]$Limpar,
    [switch]$SemInstalador
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$build = Join-Path $root 'Client\Builds\Windows'
if (-not (Test-Path (Join-Path $build 'TerrasDeAstralis.exe'))) { throw "Sem build em $build. Gere antes: menu Protótipo > Gerar build Windows." }

# 1. Launcher (entra na versão: os jogadores recebem o launcher novo também).
dotnet build (Join-Path $root 'Tools\Game.Launcher\Game.Launcher.csproj') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'O launcher não compilou.' }
$launcher = Join-Path $root 'artifacts\bin\Game.Launcher\release\TerrasDeAstralisLauncher.exe'

# 2. Versão: a do Player Settings mais a data (cada publicação é diferente).
if (-not $Versao) {
    $settings = Get-Content (Join-Path $root 'Client\ProjectSettings\ProjectSettings.asset') -Raw
    $Versao = if ($settings -cmatch '(?m)^\s*bundleVersion:\s*(\S+)') { $Matches[1] } else { '0.1.0' }
    $Versao += '-' + (Get-Date -Format 'yyyyMMdd.HHmm')
}
$out = Join-Path $root 'Client\Builds\Publicar'
Remove-Item (Join-Path $out 'versao.json') -ErrorAction SilentlyContinue
# O "| Out-Null" faz o PowerShell esperar o exe (ele é de janela, não de console).
& $launcher --publicar $build $out $Versao $Novidades | Out-Null
if (-not (Test-Path (Join-Path $out 'versao.json'))) { throw 'O launcher não montou a versão.' }
Write-Host (Get-Content (Join-Path $out 'resumo.txt'))

# 3. Envia só os arquivos que a VPS ainda não tem; o versao.json vai por último (troca de uma vez).
$remote = '~/TerrasDeAstralis/deploy/download'
$existing = @(ssh $Servidor "mkdir -p $remote/arquivos && ls $remote/arquivos")
if ($LASTEXITCODE -ne 0) { throw "Não consegui entrar na VPS ($Servidor)." }
$release = Get-Content (Join-Path $out 'versao.json') -Raw | ConvertFrom-Json
$needed = $release.arquivos | ForEach-Object { $_.sha256 + '.gz' } | Sort-Object -Unique
$missing = @($needed | Where-Object { $existing -notcontains $_ })
Write-Host "Enviando $($missing.Count) de $($needed.Count) arquivos..."
for ($i = 0; $i -lt $missing.Count; $i += 25) {
    $chunk = @($missing[$i..([Math]::Min($i + 24, $missing.Count - 1))] | ForEach-Object { Join-Path $out "arquivos\$_" })
    scp -q @chunk "${Servidor}:$remote/arquivos/"
    if ($LASTEXITCODE -ne 0) { throw 'Falhou ao enviar os arquivos.' }
}
scp -q (Join-Path $out 'versao.json') "${Servidor}:$remote/versao.json.novo"
if ($LASTEXITCODE -ne 0) { throw 'Falhou ao enviar o versao.json.' }
ssh $Servidor "mv $remote/versao.json.novo $remote/versao.json"

if ($Limpar) {
    $keep = ($needed -join "`n")
    $keep | ssh $Servidor "cd $remote/arquivos && cat > ../manter.txt && ls | grep -vxF -f ../manter.txt | xargs -r rm -f; rm -f ../manter.txt"
    Write-Host 'Arquivos antigos apagados da VPS.'
}
Write-Host "Publicado: $Versao"

# 4. Instalador com o launcher e esta mesma versão (para quem ainda não tem o jogo).
if (-not $SemInstalador) {
    & (Join-Path $root 'Tools\instalador\gerar-instalador.ps1') -Version $Versao
}
