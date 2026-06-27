<#
.SYNOPSIS
    Installs, upgrades, or uninstalls NeuroRoute as a Windows Service.

.DESCRIPTION
    NeuroRoute is a hybrid NPU-to-GPU routing gateway for local LLM execution.
    This script handles:
      - Downloading or building NeuroRoute
      - FastFlowLM (FLM) detection and installation
      - Windows Service registration
      - Start Menu shortcuts for Dashboard and Tray
      - Clean uninstall

.PARAMETER Version
    Release version to download (e.g. "0.1.0"). Defaults to the value in
    Directory.Build.props at the script's location, or "0.1.0" as fallback.

.PARAMETER BuildFromSource
    Build from local source instead of downloading a release zip. Use this when
    developing or when you don't have a GitHub token for draft release downloads.

.PARAMETER FlmMode
    FastFlowLM installation mode. Valid values:
      - Sideload (default): Install FLM alongside NeuroRoute
      - Global: Install FLM system-wide (C:\Program Files\flm)
      - Skip: Do not install FLM (use ONNX backend)

.PARAMETER ServiceOnly
    Skip Start Menu shortcut creation. Only install the Windows Service.

.PARAMETER InstallDir
    Target installation directory. Default: "$env:ProgramFiles\NeuroRoute"

.PARAMETER Uninstall
    Remove NeuroRoute completely: stop service, delete files, remove shortcuts.

.PARAMETER DraftToken
    GitHub personal access token (with repo scope) for downloading draft
    release assets. Not needed for public releases.

.PARAMETER Confirm
    Prompt before each major operation. Default: $true.

.EXAMPLE
    # Install with defaults (sideload FLM, download release)
    .\install.ps1

.EXAMPLE
    # Install from source, skip FLM
    .\install.ps1 -BuildFromSource -FlmMode Skip

.EXAMPLE
    # Full uninstall
    .\install.ps1 -Uninstall

.EXAMPLE
    # Install specific version from draft release
    .\install.ps1 -Version 0.2.0-beta -DraftToken ghp_xxxx
#>

[CmdletBinding(DefaultParameterSetName = "Install")]
param(
    [Parameter(Position = 0)]
    [string]$Version,

    [Parameter(ParameterSetName = "Install")]
    [switch]$BuildFromSource,

    [Parameter(ParameterSetName = "Install")]
    [ValidateSet("Sideload", "Global", "Skip")]
    [string]$FlmMode = "Sideload",

    [Parameter(ParameterSetName = "Install")]
    [switch]$ServiceOnly,

    [Parameter(ParameterSetName = "Install")]
    [string]$InstallDir = "$env:ProgramFiles\NeuroRoute",

    [Parameter(ParameterSetName = "Uninstall")]
    [switch]$Uninstall,

    [Parameter(ParameterSetName = "Install")]
    [string]$DraftToken = "",

    [Parameter(ParameterSetName = "Install")]
    [switch]$Confirm = $true
)

#Requires -Version 7.0

Set-StrictMode -Version 3.0
$ErrorActionPreference = "Stop"

# ─── Helper Functions ───────────────────────────────────────────────────────────

function Write-Header {
    param([string]$Text)
    Write-Host "`n=== $Text ===" -ForegroundColor Cyan
}

function Write-Step {
    param([string]$Text)
    Write-Host "  $Text" -ForegroundColor DarkGray
}

function Write-Success {
    param([string]$Text)
    Write-Host "  ✓ $Text" -ForegroundColor Green
}

function Write-Warning {
    param([string]$Text)
    Write-Host "  ⚠ $Text" -ForegroundColor Yellow
}

function Write-Error {
    param([string]$Text)
    Write-Host "  ✗ $Text" -ForegroundColor Red
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-FlmInPath {
    $flmPath = Get-Command "flm.exe" -ErrorAction SilentlyContinue
    if ($flmPath) {
        try {
            $versionOutput = & $flmPath.Source --version 2>&1
            return @{
                Found = $true
                Path = $flmPath.Source
                Version = ($versionOutput -join " ").Trim()
            }
        } catch {
            return @{ Found = $true; Path = $flmPath.Source; Version = "unknown" }
        }
    }
    return @{ Found = $false }
}

function Get-FlmInstaller {
    $setupUrl = "https://github.com/FastFlowLM/FastFlowLM/releases/latest/download/flm-setup.exe"
    $setupPath = Join-Path -Path $env:TEMP -ChildPath "flm-setup.exe"

    if (Test-Path -LiteralPath $setupPath) {
        $size = (Get-Item -LiteralPath $setupPath).Length
        if ($size -gt 1MB) {
            Write-Step "Using cached FLM installer at $setupPath ($([math]::Round($size / 1MB, 1)) MB)"
            return $setupPath
        }
        Remove-Item -LiteralPath $setupPath -Force -ErrorAction SilentlyContinue
    }

    Write-Step "Downloading FastFlowLM installer from GitHub..."
    try {
        Invoke-WebRequest -Uri $setupUrl -OutFile $setupPath -UseBasicParsing
        return $setupPath
    } catch {
        Write-Error "FLM download failed: $_"
        return $null
    }
}

function Install-FlmSideload {
    param([string]$TargetDir)

    $setupPath = Get-FlmInstaller
    if (-not $setupPath) { return $false }

    $flmDir = Join-Path -Path $TargetDir -ChildPath "flm"
    Write-Step "Installing FastFlowLM to $flmDir (silent mode)..."
    try {
        $proc = Start-Process -FilePath $setupPath -ArgumentList @("/VERYSILENT", "/DIR=`"$flmDir`"") -Wait -PassThru -NoNewWindow
        if ($proc.ExitCode -ne 0) {
            Write-Warning "FLM installer exited with code $($proc.ExitCode)"
        }
    } catch {
        Write-Error "FLM install failed: $_"
        return $false
    }

    $flmExe = Join-Path -Path $flmDir -ChildPath "flm.exe"
    if (Test-Path -LiteralPath $flmExe) {
        Write-Success "FastFlowLM sideloaded at $flmDir"
        return $true
    }

    Write-Warning "FLM sideload may have failed (flm.exe not found at $flmExe)."
    Write-Step "FLM installer may have fallen back to default install directory."
    return $false
}

function Install-FlmGlobal {
    $setupPath = Get-FlmInstaller
    if (-not $setupPath) { return $false }

    Write-Step "Installing FastFlowLM globally (silent mode)..."
    try {
        $proc = Start-Process -FilePath $setupPath -ArgumentList "/VERYSILENT" -Wait -PassThru -NoNewWindow
        if ($proc.ExitCode -ne 0) {
            Write-Warning "FLM installer exited with code $($proc.ExitCode)"
        }
    } catch {
        Write-Error "FLM global install failed: $_"
        return $false
    }

    $result = Test-FlmInPath
    if ($result.Found) {
        Write-Success "FastFlowLM installed globally ($($result.Path))"
        return $true
    }

    Write-Warning "FLM global install may have failed. Check if flm.exe is in PATH after a reboot."
    return $false
}

function Read-VersionFromProps {
    $propsPath = Join-Path -Path $PSScriptRoot -ChildPath "Directory.Build.props"
    if (Test-Path -LiteralPath $propsPath) {
        $content = Get-Content -LiteralPath $propsPath -Raw
        $match = [regex]::Match($content, '<Version>(.+?)</Version>')
        if ($match.Success) {
            return $match.Groups[1].Value
        }
    }
    return "0.1.0"
}

function Confirm-Step {
    param([string]$Message)
    if (-not $Confirm) { return $true }
    $response = Read-Host "`n$Message (Y/n)"
    return ($response -eq "" -or $response -eq "y" -or $response -eq "Y")
}

function Show-InteractiveFlmChoice {
    param(
        [string]$GlobalVersion,
        [string]$GlobalPath
    )

    Write-Host "`n[ FastFlowLM Detection ]" -ForegroundColor Cyan
    Write-Host "  ⚡ FastFlowLM detected globally:" -ForegroundColor Yellow
    Write-Host "     Version: $GlobalVersion"
    Write-Host "     Path:    $GlobalPath"
    Write-Host ""

    $choices = @(
        [System.Management.Automation.Host.ChoiceDescription]::new("&Sideload", "Install a local copy alongside NeuroRoute (version-pinned, isolated)")
        [System.Management.Automation.Host.ChoiceDescription]::new("&Global", "Use the existing global installation")
        [System.Management.Automation.Host.ChoiceDescription]::new("&Both", "Keep global + sideload a local copy too")
        [System.Management.Automation.Host.ChoiceDescription]::new("S&kip", "Skip FLM entirely (use ONNX NPU backend or GPU only)")
    )

    $choice = $Host.UI.PromptForChoice("FastFlowLM Mode", "How would you like to handle FastFlowLM?", $choices, 0)

    switch ($choice) {
        0 { return "Sideload" }
        1 { return "Global" }
        2 { return "Both" }
        3 { return "Skip" }
    }
    return "Sideload"
}

function Build-NeuroRouteConfig {
    param([string]$NpuBackend)
    return @{
        NeuroRoute = @{
            NpuBackend         = $NpuBackend
            NpuModelPath       = "Models/gemma-4-int4.onnx"
            NpuFlmModelTag     = "gemma4-it:e4b"
            NpuFlmEndpoint     = "http://127.0.0.1:52625"
            GpuEndpoint        = "http://localhost:8080"
            NpuLimit           = 65536
            NpuSlice           = 2048
            GpuMaxRetries      = 3
            GpuTimeoutSeconds  = 300
            UseMockBackends    = $false
        }
        Kestrel = @{
            Endpoints = @{
                Http = @{ Url = "http://localhost:5000" }
            }
        }
        Logging = @{
            LogLevel = @{
                Default               = "Information"
                "Microsoft.AspNetCore" = "Warning"
                NeuroRoute            = "Information"
            }
        }
    }
}

# ─── Uninstall ──────────────────────────────────────────────────────────────────

function Invoke-Uninstall {
    Write-Header "Uninstall NeuroRoute"
    Write-Step "Using install directory: $InstallDir"

    # Stop service
    $service = Get-Service -Name "NeuroRoute" -ErrorAction SilentlyContinue
    if ($service) {
        Write-Step "Stopping NeuroRoute service..."
        if ($service.Status -eq "Running") {
            Stop-Service -Name "NeuroRoute" -Force -ErrorAction SilentlyContinue
        }
        Write-Step "Deleting NeuroRoute service..."
        sc.exe delete "NeuroRoute" | Out-Null
        Write-Success "Service removed"
    } else {
        Write-Step "No NeuroRoute service found"
    }

    # Remove Start Menu shortcuts
    $startMenuPath = Join-Path -Path ([Environment]::GetFolderPath("CommonStartMenu")) -ChildPath "Programs\NeuroRoute"
    if (Test-Path -LiteralPath $startMenuPath) {
        Remove-Item -LiteralPath $startMenuPath -Recurse -Force -ErrorAction SilentlyContinue
        Write-Success "Start Menu shortcuts removed"
    }

    # Remove registry auto-start
    try {
        $runPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
        if (Get-ItemProperty -Path $runPath -Name "NeuroRoute.Tray" -ErrorAction SilentlyContinue) {
            Remove-ItemProperty -Path $runPath -Name "NeuroRoute.Tray" -ErrorAction SilentlyContinue
            Write-Success "Registry auto-start removed"
        }
    } catch {}

    # Remove files
    if (Test-Path -LiteralPath $InstallDir) {
        Write-Step "Removing $InstallDir..."
        Remove-Item -LiteralPath $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Success "Files removed"
    }

    Write-Host "`nNeuroRoute has been uninstalled." -ForegroundColor Green
    Write-Host "Note: If FastFlowLM was installed globally, it was NOT removed." -ForegroundColor DarkGray
    Write-Host "      To uninstall FLM: run 'flm-setup.exe' and choose Remove" -ForegroundColor DarkGray
}

# ─── Install ─────────────────────────────────────────────────────────────────────

function Invoke-Install {
    Write-Header "NeuroRoute Installer v$Version"
    Write-Step "Install directory: $InstallDir"
    Write-Step "FLM mode: $FlmMode"
    Write-Step "Source: $(if ($BuildFromSource) { 'Build from source' } else { 'Download from GitHub' })"

    # Resolve FLM mode (interactive if needed)
    $resolvedFlmMode = $FlmMode
    $globalFlm = Test-FlmInPath
    if ($globalFlm.Found -and $FlmMode -eq "Sideload") {
        Write-Step "FastFlowLM $($globalFlm.Version) detected globally"
        $resolvedFlmMode = Show-InteractiveFlmChoice -GlobalVersion $globalFlm.Version -GlobalPath $globalFlm.Path
        Write-Step "Selected FLM mode: $resolvedFlmMode"
    }

    # ── Phase 1: FLM ──────────────────────────────────────────────────────────
    Write-Header "Phase 1: FastFlowLM"
    $flmInstalled = $false

    if ($resolvedFlmMode -eq "Sideload" -or $resolvedFlmMode -eq "Both") {
        if (Confirm-Step "Install FastFlowLM via sideload?") {
            $flmInstalled = Install-FlmSideload -TargetDir $InstallDir
        }
    }

    if ($resolvedFlmMode -eq "Global" -or $resolvedFlmMode -eq "Both") {
        if ($resolvedFlmMode -eq "Both") { $flmInstalled = $false } # flag tracks sideload
        if (Confirm-Step "Install FastFlowLM globally?") {
            Install-FlmGlobal | Out-Null
        }
    }

    if ($resolvedFlmMode -eq "Skip") {
        Write-Step "Skipping FastFlowLM installation"
    }

    # ── Phase 2: NeuroRoute ───────────────────────────────────────────────────
    Write-Header "Phase 2: NeuroRoute Binary"

    if (-not (Test-Path -LiteralPath $InstallDir)) {
        New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
        Write-Step "Created directory: $InstallDir"
    }

    if ($BuildFromSource) {
        # Build from local source
        $serviceProj = Join-Path -Path $PSScriptRoot -ChildPath "NeuroRoute.Service\NeuroRoute.Service.csproj"
        $trayProj = Join-Path -Path $PSScriptRoot -ChildPath "NeuroRoute.Tray\NeuroRoute.Tray.csproj"

        if (-not (Test-Path -LiteralPath $serviceProj)) {
            Write-Error "Cannot find NeuroRoute.Service.csproj at $serviceProj"
            Write-Error "Run from the repository root, or use download mode."
            exit 1
        }

        if (Confirm-Step "Build NeuroRoute from source?") {
            Write-Step "Building Service..."
            dotnet publish $serviceProj -c Release -r win-x64 --self-contained -o $InstallDir --nologo
            if ($LASTEXITCODE -ne 0) { throw "Service build failed" }
            Write-Success "Service built"

            Write-Step "Building Tray..."
            dotnet publish $trayProj -c Release -r win-x64 --self-contained -o $InstallDir --nologo
            if ($LASTEXITCODE -ne 0) { throw "Tray build failed" }
            Write-Success "Tray built"
        } else {
            Write-Step "Skipping build"
            exit 0
        }
    } else {
        # Download from GitHub release
        if (Confirm-Step "Download NeuroRoute v$Version from GitHub?") {
            $zipUrl = "https://github.com/mrctrn/NeuroRoute/releases/download/v$Version/NeuroRoute-v$Version.zip"
            $zipPath = Join-Path -Path $env:TEMP -ChildPath "NeuroRoute-v$Version.zip"

            Write-Step "Downloading from $zipUrl ..."
            $webParams = @{ Uri = $zipUrl; OutFile = $zipPath; UseBasicParsing = $true }
            if ($DraftToken) {
                $webParams.Headers = @{ Authorization = "Bearer $DraftToken" }
            }
            try {
                Invoke-WebRequest @webParams
            } catch {
                Write-Error "Download failed: $_"
                Write-Warning "The release may be a draft. Use -DraftToken with a GitHub PAT, or use -BuildFromSource."
                exit 1
            }

            Write-Step "Extracting to $InstallDir ..."
            Expand-Archive -Path $zipPath -DestinationPath $InstallDir -Force
            Write-Success "NeuroRoute extracted"
        } else {
            Write-Step "Skipping download"
            exit 0
        }
    }

    # Copy install.ps1 to install dir for future reference / uninstall
    Copy-Item -Path $PSCommandPath -Destination (Join-Path -Path $InstallDir -ChildPath "install.ps1") -Force

    # ── Phase 3: Configuration ───────────────────────────────────────────────
    Write-Header "Phase 3: Configuration"

    $npuBackend = if ($flmInstalled -or $resolvedFlmMode -eq "Global") { "flm" } else { "onnx" }
    $config = Build-NeuroRouteConfig -NpuBackend $npuBackend

    $configPath = Join-Path -Path $InstallDir -ChildPath "appsettings.json"
    $configJson = $config | ConvertTo-Json -Depth 10
    Set-Content -Path $configPath -Value $configJson -Encoding UTF8
    Write-Success "Configuration written to $configPath"

    # ── Phase 4: Service Registration ─────────────────────────────────────────
    Write-Header "Phase 4: Windows Service"

    if (Confirm-Step "Register and start NeuroRoute as a Windows Service?") {
        # Remove existing service if present
        $existing = Get-Service -Name "NeuroRoute" -ErrorAction SilentlyContinue
        if ($existing) {
            Write-Step "Existing NeuroRoute service found, removing..."
            if ($existing.Status -eq "Running") {
                Stop-Service -Name "NeuroRoute" -Force -ErrorAction SilentlyContinue
            }
            sc.exe delete "NeuroRoute" | Out-Null
            Start-Sleep -Seconds 2
        }

        $binPath = "`"$InstallDir\NeuroRoute.Service.exe`" --content-root `"$InstallDir`""
        Write-Step "Creating service with binPath: $binPath"

        sc.exe create "NeuroRoute" binPath=$binPath start=auto | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "sc.exe create failed with exit code $LASTEXITCODE" }

        sc.exe description "NeuroRoute" "Hybrid NPU-to-GPU routing gateway for local LLM execution" | Out-Null

        Write-Step "Starting NeuroRoute service..."
        Start-Service -Name "NeuroRoute" -ErrorAction SilentlyContinue
        if ($LASTEXITCODE -ne 0 -or (Get-Service -Name "NeuroRoute").Status -ne "Running") {
            Write-Warning "Service created but may not have started. Check with: Get-Service NeuroRoute"
            Write-Warning "View errors: Get-WinEvent -LogName NeuroRoute -MaxEvents 10"
        } else {
            Write-Success "NeuroRoute service is running"
        }

        # Wait for health endpoint
        Write-Step "Waiting for health endpoint..."
        $timeout = 30
        $healthy = $false
        for ($i = 0; $i -lt $timeout; $i++) {
            try {
                $response = Invoke-RestMethod -Uri "http://localhost:5000/v1/health" -UseBasicParsing -TimeoutSec 2
                if ($response.status -ne "unhealthy") {
                    $healthy = $true
                    break
                }
            } catch {}
            Start-Sleep -Seconds 1
        }
        if ($healthy) {
            Write-Success "Service health endpoint: http://localhost:5000/v1/health"
        } else {
            Write-Warning "Health endpoint not responding within ${timeout}s. Service may still be starting."
        }
    }

    # ── Phase 5: Start Menu Shortcuts ─────────────────────────────────────────
    if (-not $ServiceOnly) {
        Write-Header "Phase 5: Start Menu Shortcuts"

        if (Confirm-Step "Create Start Menu shortcuts?") {
            $startMenuPath = Join-Path -Path ([Environment]::GetFolderPath("CommonStartMenu")) -ChildPath "Programs\NeuroRoute"
            New-Item -ItemType Directory -Path $startMenuPath -Force | Out-Null

            # Dashboard URL shortcut
            $dashboardContent = "[InternetShortcut]`nURL=http://localhost:5000`n"
            $dashboardUrlPath = Join-Path -Path $startMenuPath -ChildPath "NeuroRoute Dashboard.url"
            Set-Content -Path $dashboardUrlPath -Value $dashboardContent -Encoding ASCII
            Write-Step "  Dashboard URL shortcut created"

            # Tray shortcut
            $wshell = New-Object -ComObject WScript.Shell
            $trayLink = Join-Path -Path $startMenuPath -ChildPath "NeuroRoute Tray.lnk"
            $trayShortcut = $wshell.CreateShortcut($trayLink)
            $trayShortcut.TargetPath = Join-Path -Path $InstallDir -ChildPath "NeuroRoute.Tray.exe"
            $trayShortcut.WorkingDirectory = $InstallDir
            $trayShortcut.Save()
            Write-Step "  Tray shortcut created"

            # Uninstall shortcut
            $uninstallLink = Join-Path -Path $startMenuPath -ChildPath "Uninstall NeuroRoute.lnk"
            $uninstallShortcut = $wshell.CreateShortcut($uninstallLink)
            $uninstallShortcut.TargetPath = "pwsh.exe"
            $uninstallShortcut.Arguments = "-NoProfile -File `"$InstallDir\install.ps1`" -Uninstall"
            $uninstallShortcut.WorkingDirectory = $InstallDir
            $uninstallShortcut.Save()
            Write-Step "  Uninstall shortcut created"

            Write-Success "Start Menu shortcuts created at $startMenuPath"
        }
    }

    # ── Phase 6: Summary ──────────────────────────────────────────────────────
    Write-Header "Installation Complete"

    $status = Get-Service -Name "NeuroRoute" -ErrorAction SilentlyContinue
    $serviceStatus = if ($status) { $status.Status } else { "Not installed" }

    Write-Host @"

  ✓ NeuroRoute Service installed
      Service:  NeuroRoute
      Status:   $serviceStatus
      Binary:   $InstallDir\NeuroRoute.Service.exe
      Port:     http://localhost:5000

$(if ($flmInstalled) {
"  ✓ FastFlowLM sideloaded
      Binary:   $InstallDir\flm\flm.exe
      Server:   http://127.0.0.1:52625
      Models:   $env:USERPROFILE\.flm\models"
} elseif ($resolvedFlmMode -eq "Global") {
"  ✓ FastFlowLM (global installation)
      Use 'flm list' to check available models"
} else {
"  ○ FastFlowLM skipped
      NPU backend: onnx"
})

$(if (-not $ServiceOnly) {
"  ✓ Start Menu: $startMenuPath"
})

  ─────────────────────────────────────────────
  Next steps:

$(if ($flmInstalled) {
"  1. Pull an NPU model (first use):
       flm pull gemma4-it:e4b"
})
  2. Start a GPU backend on http://localhost:8080
       (LM Studio, llama.cpp, vLLM, etc.)
  3. Launch NeuroRoute Tray from Start Menu
  4. Open Dashboard: http://localhost:5000

  To uninstall: .\install.ps1 -Uninstall

"@ -ForegroundColor Green
}

# ─── Main ────────────────────────────────────────────────────────────────────────

function Main {
    # Detect version from Directory.Build.props if not specified
    if (-not $Version) {
        $Version = Read-VersionFromProps
    }

    # Print banner
    Write-Host @"

   ╔═══════════════════════════════════════════╗
   ║         NeuroRoute Installer v$Version        ║
   ║  Hybrid NPU→GPU Routing for Local LLMs    ║
   ╚═══════════════════════════════════════════╝

"@ -ForegroundColor Magenta

    # Elevation check
    if (-not (Test-Administrator)) {
        Write-Warning "Administrator privileges required."
        Write-Step "Restarting as administrator..."
        $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" $($MyInvocation.Line -replace $MyInvocation.InvocationName, '')"
        Start-Process pwsh.exe -Verb RunAs -ArgumentList $arguments
        exit 0
    }

    # OS architecture check
    if (-not [Environment]::Is64BitOperatingSystem) {
        Write-Error "NeuroRoute requires 64-bit Windows."
        exit 1
    }

    # Route to install or uninstall
    if ($Uninstall) {
        Invoke-Uninstall
    } else {
        Invoke-Install
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    Main
}
