[CmdletBinding()]
param(
    [ValidateSet('Application', 'Installer')]
    [string]$Target = 'Installer'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $projectRoot 'dist'
$artifactDir = Join-Path $projectRoot 'artifacts'
$statusFile = Join-Path $artifactDir 'SIGNATURE-STATUS.txt'
$application = Join-Path $dist 'ynoez.exe'
$setupFiles = @(Get-ChildItem -LiteralPath $artifactDir -Filter 'ynoez-setup-*.exe' -File -ErrorAction SilentlyContinue)
$certificateBase64 = [Environment]::GetEnvironmentVariable('YNOEZ_SIGNING_CERTIFICATE_BASE64')
$certificatePassword = [Environment]::GetEnvironmentVariable('YNOEZ_SIGNING_CERTIFICATE_PASSWORD')

New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null

if ($Target -eq 'Application' -and -not (Test-Path -LiteralPath $application -PathType Leaf)) {
    throw "Executável principal ausente: $application"
}
if ($Target -eq 'Installer' -and $setupFiles.Count -ne 1) {
    throw "Esperado exatamente um instalador para assinatura; encontrados: $($setupFiles.Count)."
}

if ([string]::IsNullOrWhiteSpace($certificateBase64) -and [string]::IsNullOrWhiteSpace($certificatePassword)) {
    if ($Target -eq 'Installer') {
        @(
            'Authenticode: NOT SIGNED'
            'Application: unsigned'
            'Installer: unsigned'
            'Reason: nenhum certificado de assinatura foi configurado nos secrets do repositório.'
            'Integrity: consulte SHA256SUMS.txt e valide os hashes antes de executar os arquivos.'
        ) | Set-Content -LiteralPath $statusFile -Encoding UTF8
    }
    Write-Warning "Certificado Authenticode ausente. Etapa $Target ignorada; nenhum estado de assinatura será inventado."
    return
}
if ([string]::IsNullOrWhiteSpace($certificateBase64) -or [string]::IsNullOrWhiteSpace($certificatePassword)) {
    throw 'Configure ambos os secrets YNOEZ_SIGNING_CERTIFICATE_BASE64 e YNOEZ_SIGNING_CERTIFICATE_PASSWORD, ou deixe ambos vazios.'
}
if ($env:OS -ne 'Windows_NT') { throw 'A assinatura Authenticode só pode ser executada em um runner Windows.' }

if ($Target -eq 'Installer') {
    $appSignature = Get-AuthenticodeSignature -LiteralPath $application
    if ($appSignature.Status -ne 'Valid') {
        throw "O instalador só pode ser empacotado/publicado após o executável principal estar assinado e válido. Estado atual: $($appSignature.Status)."
    }
}

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
    $certificate = $null
    foreach ($candidate in $importedCertificates) {
        if (-not $candidate.HasPrivateKey) { continue }
        $ekuExtension = $candidate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' } | Select-Object -First 1
        if (-not $ekuExtension) { continue }
        $eku = [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($ekuExtension, $ekuExtension.Critical)
        if ($eku.EnhancedKeyUsages.Value -contains '1.3.6.1.5.5.7.3.3') {
            $certificate = $candidate
            break
        }
    }
    if (-not $certificate) { throw 'O PFX não contém um certificado com chave privada e EKU Code Signing.' }
    $thumbprint = $certificate.Thumbprint
    $certificateStorePath = "Cert:\CurrentUser\My\$thumbprint"

    $windowsKits = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Windows Kits\10\bin'
    $signTool = Get-ChildItem -LiteralPath $windowsKits -Filter 'signtool.exe' -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '[\\/]x64[\\/]signtool\.exe$' } |
        Sort-Object -Property FullName -Descending |
        Select-Object -First 1
    if (-not $signTool) { throw 'signtool.exe x64 não foi encontrado no Windows SDK do runner.' }

    $fileToSign = if ($Target -eq 'Application') { $application } else { $setupFiles[0].FullName }
    Write-Host "Assinando $(Split-Path -Leaf $fileToSign) com Authenticode."
    & $signTool.FullName sign /fd SHA256 /td SHA256 /tr 'http://timestamp.digicert.com' /sha1 $thumbprint /s My $fileToSign
    if ($LASTEXITCODE -ne 0) { throw "signtool sign falhou para '$fileToSign' (código $LASTEXITCODE)." }

    & $signTool.FullName verify /pa /all /v $fileToSign
    if ($LASTEXITCODE -ne 0) { throw "A verificação Authenticode falhou para '$fileToSign' (código $LASTEXITCODE)." }

    if ($Target -eq 'Installer') {
        $applicationSignature = Get-AuthenticodeSignature -LiteralPath $application
        $installerSignature = Get-AuthenticodeSignature -LiteralPath $fileToSign
        if ($applicationSignature.Status -ne 'Valid' -or $installerSignature.Status -ne 'Valid') {
            throw "A validação final falhou. App=$($applicationSignature.Status); Installer=$($installerSignature.Status)."
        }
        @(
            'Authenticode: SIGNED'
            "Certificate subject: $($certificate.Subject)"
            "Certificate thumbprint: $thumbprint"
            'Application: Valid (assinada antes do empacotamento do instalador)'
            'Installer: Valid'
            'Timestamp authority: http://timestamp.digicert.com'
            'Verification: signtool verify /pa /all concluiu com sucesso.'
        ) | Set-Content -LiteralPath $statusFile -Encoding UTF8
    } else {
        $signature = Get-AuthenticodeSignature -LiteralPath $application
        if ($signature.Status -ne 'Valid') { throw "O executável app não ficou válido após assinatura: $($signature.Status)." }
        Write-Host 'Executável do aplicativo assinado e validado antes do empacotamento.' -ForegroundColor Green
    }
}
finally {
    if ($pfxBytes) { [Array]::Clear($pfxBytes, 0, $pfxBytes.Length) }
    if ($certificateStorePath -and (Test-Path -LiteralPath $certificateStorePath)) { Remove-Item -LiteralPath $certificateStorePath -Force }
    if (Test-Path -LiteralPath $pfxPath) { Remove-Item -LiteralPath $pfxPath -Force }
    if ($securePassword) { $securePassword.Dispose() }
}
