[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $projectRoot 'dist'
$artifactDir = Join-Path $projectRoot 'artifacts'
$statusFile = Join-Path $artifactDir 'SIGNATURE-STATUS.txt'
$certificateBase64 = [Environment]::GetEnvironmentVariable('YNOEZ_SIGNING_CERTIFICATE_BASE64')
$certificatePassword = [Environment]::GetEnvironmentVariable('YNOEZ_SIGNING_CERTIFICATE_PASSWORD')

New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($certificateBase64) -and [string]::IsNullOrWhiteSpace($certificatePassword)) {
    @(
        'Authenticode: NOT SIGNED'
        'Reason: nenhum certificado de assinatura foi configurado nos secrets do repositório.'
        'Integrity: consulte SHA256SUMS.txt e valide os hashes antes de executar os arquivos.'
    ) | Set-Content -LiteralPath $statusFile -Encoding UTF8
    Write-Warning 'Certificado Authenticode ausente. Os executáveis permanecerão sem assinatura; hashes SHA-256 serão publicados.'
    return
}
if ([string]::IsNullOrWhiteSpace($certificateBase64) -or [string]::IsNullOrWhiteSpace($certificatePassword)) {
    throw 'Configure ambos os secrets YNOEZ_SIGNING_CERTIFICATE_BASE64 e YNOEZ_SIGNING_CERTIFICATE_PASSWORD, ou deixe ambos vazios.'
}
if ($env:OS -ne 'Windows_NT') { throw 'A assinatura Authenticode só pode ser executada em um runner Windows.' }

$setupFiles = @(Get-ChildItem -LiteralPath $artifactDir -Filter 'ynoez-setup-*.exe' -File)
$appExecutable = Join-Path $dist 'ynoez.exe'
if ($setupFiles.Count -ne 1) { throw "Esperado exatamente um instalador para assinatura; encontrados: $($setupFiles.Count)." }
if (-not (Test-Path -LiteralPath $appExecutable -PathType Leaf)) { throw "Executável principal ausente: $appExecutable" }

$tempRoot = if (-not [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
$pfxPath = Join-Path $tempRoot 'ynoez-authenticode-signing.pfx'
$certificateStorePath = $null
$securePassword = $null
$pfxBytes = $null

try {
    $pfxBytes = [Convert]::FromBase64String($certificateBase64)
    if ($pfxBytes.Length -lt 1024) { throw 'O certificado PFX codificado parece vazio ou inválido.' }
    [System.IO.File]::WriteAllBytes($pfxPath, $pfxBytes)
    [Array]::Clear($pfxBytes, 0, $pfxBytes.Length)
    $pfxBytes = $null

    $securePassword = ConvertTo-SecureString -String $certificatePassword -AsPlainText -Force
    $importedCertificates = @(Import-PfxCertificate -FilePath $pfxPath -CertStoreLocation 'Cert:\CurrentUser\My' -Password $securePassword -Exportable)
    $certificate = $importedCertificates |
        Where-Object { $_.HasPrivateKey -and ($_.EnhancedKeyUsageList.Value -contains '1.3.6.1.5.5.7.3.3') } |
        Select-Object -First 1
    if (-not $certificate) { throw 'O PFX não contém um certificado válido com chave privada e EKU Code Signing.' }
    $thumbprint = $certificate.Thumbprint
    $certificateStorePath = "Cert:\CurrentUser\My\$thumbprint"

    $windowsKits = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Windows Kits\10\bin'
    $signTool = Get-ChildItem -LiteralPath $windowsKits -Filter 'signtool.exe' -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '[\\/]x64[\\/]signtool\.exe$' } |
        Sort-Object -Property FullName -Descending |
        Select-Object -First 1
    if (-not $signTool) { throw 'signtool.exe x64 não foi encontrado no Windows SDK do runner.' }

    $timestampUrl = 'http://timestamp.digicert.com'
    $filesToSign = @($appExecutable, $setupFiles[0].FullName)
    foreach ($file in $filesToSign) {
        Write-Host "Assinando $(Split-Path -Leaf $file) com Authenticode."
        & $signTool.FullName sign /fd SHA256 /td SHA256 /tr $timestampUrl /sha1 $thumbprint /s My $file
        if ($LASTEXITCODE -ne 0) { throw "signtool sign falhou para '$file' (código $LASTEXITCODE)." }
        & $signTool.FullName verify /pa /all /v $file
        if ($LASTEXITCODE -ne 0) { throw "A verificação Authenticode falhou para '$file' (código $LASTEXITCODE)." }
    }

    @(
        'Authenticode: SIGNED'
        "Certificate subject: $($certificate.Subject)"
        "Certificate thumbprint: $thumbprint"
        "Timestamp authority: $timestampUrl"
        'Verification: signtool verify /pa /all concluiu com sucesso para ynoez.exe e ynoez-setup.'
    ) | Set-Content -LiteralPath $statusFile -Encoding UTF8

    Write-Host 'Assinatura Authenticode e verificação concluídas para os dois executáveis.' -ForegroundColor Green
}
finally {
    if ($pfxBytes) { [Array]::Clear($pfxBytes, 0, $pfxBytes.Length) }
    if ($certificateStorePath -and (Test-Path -LiteralPath $certificateStorePath)) { Remove-Item -LiteralPath $certificateStorePath -Force }
    if (Test-Path -LiteralPath $pfxPath) { Remove-Item -LiteralPath $pfxPath -Force }
    if ($securePassword) { $securePassword.Dispose() }
}
