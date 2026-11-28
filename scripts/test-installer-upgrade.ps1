[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$LegacyInstallerPath,
    [Parameter(Mandatory)][string]$CandidateInstallerPath,
    [Parameter(Mandatory)][string]$LegacyExecutablePath,
    [Parameter(Mandatory)][string]$CandidateExecutablePath
)

$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'O teste de upgrade/rollback é exclusivo de um runner GitHub Actions descartável.' }
if ($env:OS -ne 'Windows_NT') { throw 'O teste de upgrade/rollback só pode rodar no Windows.' }

$target = 'C:\ynoez'
if (Test-Path -LiteralPath $target) { throw "O caminho $target já existe no runner. O teste foi bloqueado para preservar qualquer dado preexistente." }

$paths = @{
    LegacyInstaller = (Resolve-Path -LiteralPath $LegacyInstallerPath -ErrorAction Stop).Path
    CandidateInstaller = (Resolve-Path -LiteralPath $CandidateInstallerPath -ErrorAction Stop).Path
    LegacyExecutable = (Resolve-Path -LiteralPath $LegacyExecutablePath -ErrorAction Stop).Path
    CandidateExecutable = (Resolve-Path -LiteralPath $CandidateExecutablePath -ErrorAction Stop).Path
}
$legacyHash = (Get-FileHash -LiteralPath $paths.LegacyExecutable -Algorithm SHA256).Hash
$candidateHash = (Get-FileHash -LiteralPath $paths.CandidateExecutable -Algorithm SHA256).Hash
if ($legacyHash -eq $candidateHash) { throw 'Os executáveis da branch main e do candidato são idênticos; o teste não comprovaria substituição nem rollback.' }

$folders = @('Data', 'Backups', 'Logs', 'Downloads')
$markers = @{}
foreach ($folder in $folders) { $markers[$folder] = Join-Path (Join-Path $target $folder) '.ynoez-upgrade-rollback-preserve' }

function Invoke-SilentInstaller {
    param([Parameter(Mandatory)][string]$Path)
    $process = Start-Process -FilePath $Path -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-' -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "O instalador '$Path' terminou com código $($process.ExitCode)." }
}

function Assert-InstalledPayload {
    param([Parameter(Mandatory)][string]$ExpectedHash, [Parameter(Mandatory)][string]$Stage)
    $application = Join-Path $target 'ynoez.exe'
    if (-not (Test-Path -LiteralPath $application -PathType Leaf)) { throw "$($Stage): ynoez.exe não existe após a operação." }
    $actualHash = (Get-FileHash -LiteralPath $application -Algorithm SHA256).Hash
    if ($actualHash -ne $ExpectedHash) { throw "$($Stage): SHA-256 do executável instalado não corresponde ao pacote esperado." }
    foreach ($folder in $folders) {
        if (-not (Test-Path -LiteralPath $markers[$folder] -PathType Leaf)) { throw "$($Stage): a operação removeu ou alterou dados em $folder." }
    }
}

try {
    Write-Host 'Fase 1/5: instalar o build legado da branch main.'
    Invoke-SilentInstaller -Path $paths.LegacyInstaller
    $legacyUninstaller = Join-Path $target 'unins000.exe'
    if (-not (Test-Path -LiteralPath $legacyUninstaller -PathType Leaf)) { throw 'O build legado não registrou o desinstalador Inno Setup.' }
    foreach ($folder in $folders) {
        $directory = Join-Path $target $folder
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw "O build legado não criou a pasta persistente $folder." }
        Set-Content -LiteralPath $markers[$folder] -Value "Dados do usuário: $folder" -Encoding UTF8
    }
    Assert-InstalledPayload -ExpectedHash $legacyHash -Stage 'Instalação legada'

    Write-Host 'Fase 2/5: atualizar o build legado para o candidato atual.'
    Invoke-SilentInstaller -Path $paths.CandidateInstaller
    Assert-InstalledPayload -ExpectedHash $candidateHash -Stage 'Upgrade para o candidato'

    Write-Host 'Fase 3/5: desinstalar o candidato mantendo os dados e reinstalar o baseline legado.'
    $candidateUninstaller = Join-Path $target 'unins000.exe'
    if (-not (Test-Path -LiteralPath $candidateUninstaller -PathType Leaf)) { throw 'O candidato não registrou o desinstalador para rollback.' }
    $rollbackRemoval = Start-Process -FilePath $candidateUninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-' -Wait -PassThru
    if ($rollbackRemoval.ExitCode -ne 0) { throw "A remoção do candidato para rollback terminou com código $($rollbackRemoval.ExitCode)." }
    if (Test-Path -LiteralPath (Join-Path $target 'ynoez.exe')) { throw 'A desinstalação do candidato deixou o executável instalado antes do rollback.' }
    foreach ($folder in $folders) {
        if (-not (Test-Path -LiteralPath $markers[$folder] -PathType Leaf)) { throw "A desinstalação do candidato removeu dados de $folder antes do rollback." }
    }
    Invoke-SilentInstaller -Path $paths.LegacyInstaller
    Assert-InstalledPayload -ExpectedHash $legacyHash -Stage 'Rollback para o build legado'

    Write-Host 'Fase 4/5: reaplicar o candidato após o rollback.'
    Invoke-SilentInstaller -Path $paths.CandidateInstaller
    Assert-InstalledPayload -ExpectedHash $candidateHash -Stage 'Upgrade após rollback'

    Write-Host 'Fase 5/5: desinstalar o candidato e confirmar retenção dos dados.'
    $uninstaller = Join-Path $target 'unins000.exe'
    if (-not (Test-Path -LiteralPath $uninstaller -PathType Leaf)) { throw 'O pacote candidato não deixou um desinstalador.' }
    $uninstallProcess = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-' -Wait -PassThru
    if ($uninstallProcess.ExitCode -ne 0) { throw "A desinstalação após rollback terminou com código $($uninstallProcess.ExitCode)." }
    if (Test-Path -LiteralPath (Join-Path $target 'ynoez.exe')) { throw 'A desinstalação após rollback deixou ynoez.exe instalado.' }
    foreach ($folder in $folders) {
        if (-not (Test-Path -LiteralPath $markers[$folder] -PathType Leaf)) { throw "A desinstalação após rollback removeu dados em $folder." }
    }

    Write-Host 'Upgrade, rollback, re-upgrade e desinstalação verificados; Data/Backups/Logs/Downloads preservados.' -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
}
