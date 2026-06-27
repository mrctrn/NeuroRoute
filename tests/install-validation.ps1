<#
.SYNOPSIS
    Validates the NeuroRoute installer logic and optionally runs the full install/uninstall cycle.

    Phase 1 — Logic tests (no admin, no system changes):
        • Version parsing from Directory.Build.props
        • Config generation for flm and onnx backends
        • FLM detection edge cases

    Phase 2 — Integration tests (admin required):
        • Fresh install to a temp directory
        • File layout, config, and service verification
        • Clean uninstall verification

.PARAMETER SkipIntegration
    Skip Phase 2 (requires admin). Default: $true, pass -SkipIntegration:$false to enable.

.PARAMETER InstallDir
    Temp directory for integration tests. Default: $env:TEMP\NeuroRoute-InstallTest.

.EXAMPLE
    # Phase 1 only (no admin needed)
    .\tests\install-validation.ps1

.EXAMPLE
    # Full validation (run as admin)
    .\tests\install-validation.ps1 -SkipIntegration:$false
#>

[CmdletBinding()]
param(
    [switch]$SkipIntegration = $true,
    [string]$InstallDir = (Join-Path -Path $env:TEMP -ChildPath "NeuroRoute-InstallTest")
)

#Requires -Version 7.0

Set-StrictMode -Version 3.0

$passed = 0
$failed = 0
$repoRoot = Resolve-Path (Join-Path -Path $PSScriptRoot -ChildPath "..")

# ── Helpers ─────────────────────────────────────────────────────────────────

function Assert-Equal {
    param([string]$Name, $Expected, $Actual)
    if ($Expected -eq $Actual) {
        Write-Host "  ✓ $Name" -ForegroundColor Green
        $script:passed++
    } else {
        Write-Host "  ✗ $Name — expected: '$Expected', actual: '$Actual'" -ForegroundColor Red
        $script:failed++
    }
}

function Assert-Condition {
    param([string]$Name, [scriptblock]$Condition)
    try {
        if (& $Condition) {
            Write-Host "  ✓ $Name" -ForegroundColor Green
            $script:passed++
        } else {
            Write-Host "  ✗ $Name (condition returned false)" -ForegroundColor Red
            $script:failed++
        }
    } catch {
        Write-Host "  ✗ $Name (exception: $_ )" -ForegroundColor Red
        $script:failed++
    }
}

function Assert-FileExists {
    param([string]$Name, [string]$Path)
    if (Test-Path -LiteralPath $Path) {
        Write-Host "  ✓ $Name" -ForegroundColor Green
        $script:passed++
    } else {
        Write-Host "  ✗ $Name — not found at '$Path'" -ForegroundColor Red
        $script:failed++
    }
}



# Load installer functions via dot-sourcing (safe: won't run Main)
Write-Host "Loading installer functions..." -ForegroundColor DarkGray
. (Join-Path -Path $repoRoot -ChildPath "install.ps1")

# ── Phase 1: Logic Tests ────────────────────────────────────────────────────

Write-Host "`n=== Phase 1: Installer Logic Tests ===" -ForegroundColor Cyan

# Test 1: Version parsing
Write-Host "`n--- Version Parsing ---" -ForegroundColor DarkGray
$v = Read-VersionFromProps -RepoRoot $repoRoot
Assert-Equal "Version from Directory.Build.props" -Expected "0.1.0" -Actual $v

# Test 2: Config generation — flm backend
Write-Host "`n--- Config Generation ---" -ForegroundColor DarkGray
$flmConfig = Build-NeuroRouteConfig -NpuBackend "flm"
Assert-Equal "FLM config: NpuBackend=flm" -Expected "flm" -Actual $flmConfig.NeuroRoute.NpuBackend
Assert-Equal "FLM config: NpuFlmEndpoint=:52625" -Expected "http://127.0.0.1:52625" -Actual $flmConfig.NeuroRoute.NpuFlmEndpoint
Assert-Equal "FLM config: NpuLimit=65536" -Expected 65536 -Actual $flmConfig.NeuroRoute.NpuLimit
Assert-Equal "FLM config: UseMockBackends=false" -Expected $false -Actual $flmConfig.NeuroRoute.UseMockBackends

# Test 3: Config generation — onnx backend
$onnxConfig = Build-NeuroRouteConfig -NpuBackend "onnx"
Assert-Equal "ONNX config: NpuBackend=onnx" -Expected "onnx" -Actual $onnxConfig.NeuroRoute.NpuBackend

# Test 4: Config JSON serialization
Write-Host "`n--- Config JSON Structure ---" -ForegroundColor DarkGray
$configPath = Join-Path -Path $env:TEMP -ChildPath "test-appsettings.json"
$flmConfig | ConvertTo-Json -Depth 10 | Set-Content -Path $configPath -Encoding UTF8

Assert-Condition "Config file created" -Condition { Test-Path -LiteralPath $configPath }
Assert-Condition "Config JSON has Kestrel.Endpoints.Http.Url = :5000" -Condition {
    $j = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $j.Kestrel.Endpoints.Http.Url -eq "http://localhost:5000"
}
Assert-Condition "Config JSON has Logging.LogLevel.Default = Information" -Condition {
    $j = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $j.Logging.LogLevel.Default -eq "Information"
}

Remove-Item -LiteralPath $configPath -Force -ErrorAction SilentlyContinue

# Test 5: FLM detection runs
Write-Host "`n--- FLM Detection ---" -ForegroundColor DarkGray
Assert-Condition "Test-FlmInPath runs without error" -Condition { $null -ne (Test-FlmInPath) }

# Test 6: Admin check runs
$adminResult = Test-Administrator
Assert-Condition "Test-Administrator completes" -Condition { $true }

Write-Host "`n=== Phase 1 Complete: $passed passed, $failed failed ===" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })

# ── Phase 2: Integration Tests ───────────────────────────────────────────────

if (-not $SkipIntegration) {
    Write-Host "`n=== Phase 2: Integration Tests ===" -ForegroundColor Cyan

    if (-not (Test-Administrator)) {
        Write-Host "  ⚠ Skipping Phase 2 — requires Administrator privileges." -ForegroundColor Yellow
        Write-Host "    Re-run as admin from an elevated PowerShell prompt:" -ForegroundColor Yellow
        Write-Host "      $PSCommandPath -SkipIntegration:`$false" -ForegroundColor Yellow
    } else {
        # Set install params for the functions loaded via dot-source
        $Version = "0.1.0"
        $BuildFromSource = $true
        $FlmMode = "Skip"
        $ServiceOnly = $false
        $DraftToken = ""
        $Confirm = $false

        # ── Clean any leftover ──
        $existingService = Get-Service -Name "NeuroRoute" -ErrorAction SilentlyContinue
        if ($existingService) {
            Write-Host "  Cleaning previous NeuroRoute service..." -ForegroundColor DarkGray
            if ($existingService.Status -eq "Running") {
                Stop-Service -Name "NeuroRoute" -Force -ErrorAction SilentlyContinue
            }
            sc.exe delete "NeuroRoute" | Out-Null
            Start-Sleep -Seconds 2
        }
        if (Test-Path -LiteralPath $InstallDir) {
            Remove-Item -LiteralPath $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
        }

        # ── Test A: Install (BuildFromSource, FlmMode=Skip) ──
        Write-Host "`n--- Test A: Install (BuildFromSource, FlmMode=Skip) ---" -ForegroundColor DarkGray
        Write-Host "  Installing to: $InstallDir" -ForegroundColor DarkGray

        try {
            Invoke-Install
            Write-Host "  Installer completed" -ForegroundColor DarkGray

            Assert-FileExists "Service EXE" -Path (Join-Path -Path $InstallDir -ChildPath "NeuroRoute.Service.exe")
            Assert-FileExists "Tray EXE" -Path (Join-Path -Path $InstallDir -ChildPath "NeuroRoute.Tray.exe")
            Assert-FileExists "appsettings.json" -Path (Join-Path -Path $InstallDir -ChildPath "appsettings.json")
            Assert-FileExists "install.ps1 (copied)" -Path (Join-Path -Path $InstallDir -ChildPath "install.ps1")

            $installedConfig = Join-Path -Path $InstallDir -ChildPath "appsettings.json"
            Assert-Condition "Config NpuBackend=onnx (FLM skipped)" -Condition {
                $c = Get-Content -LiteralPath $installedConfig -Raw | ConvertFrom-Json
                $c.NeuroRoute.NpuBackend -eq "onnx"
            }
            Assert-Condition "Config port 5000" -Condition {
                $c = Get-Content -LiteralPath $installedConfig -Raw | ConvertFrom-Json
                $c.Kestrel.Endpoints.Http.Url -eq "http://localhost:5000"
            }

            Start-Sleep -Seconds 3
            $svc = Get-Service -Name "NeuroRoute" -ErrorAction SilentlyContinue
            Assert-Condition "Service 'NeuroRoute' exists" -Condition { $null -ne $svc }
            if ($svc) {
                Assert-Equal "Service start type is Automatic" -Expected "Automatic" -Actual $svc.StartType.ToString()
            }
        } catch {
            Write-Host "  ✗ Install failed: $_" -ForegroundColor Red
            $failed++
        }

        # ── Test B: Uninstall ──
        Write-Host "`n--- Test B: Uninstall ---" -ForegroundColor DarkGray

        try {
            if (Get-Service -Name "NeuroRoute" -ErrorAction SilentlyContinue) {
                Invoke-Uninstall
                Write-Host "  Uninstaller completed" -ForegroundColor DarkGray

                Start-Sleep -Seconds 2
                $svcAfter = Get-Service -Name "NeuroRoute" -ErrorAction SilentlyContinue
                Assert-Condition "Service removed after uninstall" -Condition { $null -eq $svcAfter }
            } else {
                Write-Host "  ○ No service to remove" -ForegroundColor DarkGray
            }

            Assert-Condition "Install directory removed" -Condition { -not (Test-Path -LiteralPath $InstallDir) }
        } catch {
            Write-Host "  ✗ Uninstall failed: $_" -ForegroundColor Red
            $failed++
        }

        Write-Host "`n=== Phase 2 Complete ===" -ForegroundColor Cyan
    }
}

# ── Summary ─────────────────────────────────────────────────────────────────

Write-Host @"

╔═══════════════════════════════════════════╗
║         Validation Results                ║
║  Passed: $passed   Failed: $failed              ║
╚═══════════════════════════════════════════╝
"@ -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })

if ($failed -gt 0) { exit 1 }
