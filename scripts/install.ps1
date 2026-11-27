[CmdletBinding()]
param([switch]$Elevated)

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$PublishDir = Join-Path $ProjectRoot 'dist'
$Target = 'C:\ynoez'

if (-not $Elevated) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        $arguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ('"' + $PSCommandPath + '"'), "-Elevated")
        $elevatedProcess = Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -Verb RunAs -Wait -PassThru
        exit $elevatedProcess.ExitCode
    }
}

if (-not (Test-Path (Join-Path $PublishDir 'ynoez.exe'))) {
    throw 'Executável não encontrado em dist. Execute scripts\publish.ps1 primeiro.'
}

New-Item -ItemType Directory -Path $Target -Force | Out-Null
Copy-Item -Path (Join-Path $PublishDir '*') -Destination $Target -Recurse -Force
$folders = @('Toolbox','Data','Backups','Logs','Downloads','Toolbox\Debloat','Toolbox\Internet','Toolbox\Windows','Toolbox\Servicos','Toolbox\Drivers','Toolbox\GPU','Toolbox\CPU','Toolbox\RAM','Toolbox\Energia','Toolbox\Jogos','Toolbox\Brave','Toolbox\Discord','Toolbox\Ferramentas','Toolbox\Diagnostico','Toolbox\Restauracao','Toolbox\Placas-mae')
foreach ($folder in $folders) { New-Item -ItemType Directory -Path (Join-Path $Target $folder) -Force | Out-Null }
# Concede escrita apenas nas pastas de dados; executável e arquivos do programa ficam protegidos.
foreach ($folder in @('Data','Backups','Logs','Downloads')) {
    $dataPath = Join-Path $Target $folder
    & icacls.exe $dataPath /grant '*S-1-5-32-545:(OI)(CI)M' /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Não foi possível configurar permissões para $dataPath" }
}
Write-Host "ynoez instalado em $Target\ynoez.exe" -ForegroundColor Green
