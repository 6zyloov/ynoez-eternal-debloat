[CmdletBinding()]
param([string]$OutputPath)

$ErrorActionPreference = 'Stop'

function Get-CimSafe {
    param([string]$ClassName, [string]$Namespace = 'root/cimv2')
    try { @(Get-CimInstance -Namespace $Namespace -ClassName $ClassName -ErrorAction Stop) }
    catch { @() }
}

$board = Get-CimSafe 'Win32_BaseBoard' | Select-Object -First 1
$bios = Get-CimSafe 'Win32_BIOS' | Select-Object -First 1
$cpu = Get-CimSafe 'Win32_Processor' | Select-Object -First 1
$os = Get-CimSafe 'Win32_OperatingSystem' | Select-Object -First 1

$memory = @(
    Get-CimSafe 'Win32_PhysicalMemory' | ForEach-Object {
        [pscustomobject]@{
            CapacityGB = if ($_.Capacity) { [math]::Round(([double]$_.Capacity / 1GB), 2) } else { $null }
            SpeedMHz = $_.Speed
            ConfiguredClockSpeedMHz = $_.ConfiguredClockSpeed
            Manufacturer = $_.Manufacturer
            PartNumber = ([string]$_.PartNumber).Trim()
            BankLabel = $_.BankLabel
        }
    }
)

$network = @(
    Get-CimSafe 'Win32_NetworkAdapter' | Where-Object { $_.PhysicalAdapter -eq $true } |
    ForEach-Object {
        [pscustomobject]@{
            Name = $_.Name
            Manufacturer = $_.Manufacturer
            NetEnabled = $_.NetEnabled
            SpeedBitsPerSecond = $_.Speed
            PNPDeviceID = $_.PNPDeviceID
            MACAddress = $_.MACAddress
        }
    }
)

$drivers = @(
    Get-CimSafe 'Win32_PnPSignedDriver' |
    Where-Object { ($_.DeviceName -match 'AMD|Realtek') -or ($_.DriverProviderName -match 'AMD|Realtek') } |
    Select-Object DeviceName, DriverProviderName, DriverVersion, DriverDate, InfName, DeviceID
)

$secureBoot = 'Unknown'
try {
    if (Get-Command Confirm-SecureBootUEFI -ErrorAction SilentlyContinue) {
        $secureBoot = [bool](Confirm-SecureBootUEFI -ErrorAction Stop)
    }
} catch { $secureBoot = 'Unavailable: ' + $_.Exception.Message }

$tpm = $null
try {
    if (Get-Command Get-Tpm -ErrorAction SilentlyContinue) {
        $t = Get-Tpm -ErrorAction Stop
        $tpm = [pscustomobject]@{ Present = $t.TpmPresent; Ready = $t.TpmReady; Enabled = $t.TpmEnabled; Activated = $t.TpmActivated }
    }
} catch { $tpm = [pscustomobject]@{ Status = 'Unavailable'; Reason = $_.Exception.Message } }

$dg = Get-CimSafe 'Win32_DeviceGuard' 'root/Microsoft/Windows/DeviceGuard' | Select-Object -First 1

$report = [pscustomobject]@{
    SchemaVersion = 1
    GeneratedAt = (Get-Date).ToString('o')
    ReadOnly = $true
    Computer = $env:COMPUTERNAME
    OperatingSystem = if ($os) { [pscustomobject]@{ Caption = $os.Caption; Version = $os.Version; BuildNumber = $os.BuildNumber; Architecture = $os.OSArchitecture } } else { $null }
    Motherboard = if ($board) { [pscustomobject]@{ Manufacturer = $board.Manufacturer; Product = $board.Product; Version = $board.Version } } else { $null }
    BIOS = if ($bios) { [pscustomobject]@{ Manufacturer = $bios.Manufacturer; SMBIOSBIOSVersion = $bios.SMBIOSBIOSVersion; Version = $bios.Version; ReleaseDate = $bios.ReleaseDate } } else { $null }
    CPU = if ($cpu) { [pscustomobject]@{ Name = $cpu.Name; Manufacturer = $cpu.Manufacturer; Cores = $cpu.NumberOfCores; LogicalProcessors = $cpu.NumberOfLogicalProcessors; MaxClockMHz = $cpu.MaxClockSpeed } } else { $null }
    Memory = $memory
    PhysicalNetworkAdapters = $network
    RelevantSignedDrivers = $drivers
    Security = [pscustomobject]@{
        SecureBoot = $secureBoot
        TPM = $tpm
        DeviceGuard = if ($dg) { [pscustomobject]@{ VBSStatus = $dg.VirtualizationBasedSecurityStatus; ServicesConfigured = @($dg.SecurityServicesConfigured); ServicesRunning = @($dg.SecurityServicesRunning) } } else { $null }
    }
}

$json = $report | ConvertTo-Json -Depth 8
if ($OutputPath) {
    $full = [System.IO.Path]::GetFullPath($OutputPath)
    $parent = [System.IO.Path]::GetDirectoryName($full)
    if (-not [System.IO.Directory]::Exists($parent)) { throw "A pasta de destino não existe: $parent" }
    Set-Content -LiteralPath $full -Value $json -Encoding UTF8
}
Write-Output $json
