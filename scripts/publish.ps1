[CmdletBinding()]
param(
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $ProjectRoot 'src\ynoez\ynoez.csproj'
$Output = Join-Path $ProjectRoot 'dist'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'O .NET 10 SDK LTS não foi encontrado. Instale o SDK 10.0 e execute novamente.'
}

Push-Location $ProjectRoot
try {
    $SdkVersion = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $SdkVersion -notmatch '^10\.') {
        throw "O ynoez agora requer o SDK .NET 10 LTS fixado em global.json; SDK detectado: $SdkVersion"
    }

    if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
    New-Item -ItemType Directory -Path $Output -Force | Out-Null

    dotnet publish $Project -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $Output
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou com código $LASTEXITCODE" }

    $Exe = Join-Path $Output 'ynoez.exe'
    if (-not (Test-Path $Exe)) { throw 'O processo de publicação terminou sem produzir ynoez.exe.' }
    Write-Host "Publicado com SDK .NET ${SdkVersion}: $Exe" -ForegroundColor Green
}
finally {
    Pop-Location
}
