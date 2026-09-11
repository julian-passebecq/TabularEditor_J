param(
    [switch]$HostOnlyDiagnostic,
    [string]$NuGetPath,
    [string]$EvidenceDirectory = (Join-Path $env:TEMP 'pbibench-verification')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$oldLocation = Get-Location
$oldSdk = $env:MSBuildSDKsPath
$oldResolver = $env:MSBuildEnableWorkloadResolver
$oldPath = $env:PATH
$stages = [System.Collections.Generic.List[object]]::new()
$exitCode = 1
function Stage($name, $exe, $arguments) {
    $log = Join-Path $EvidenceDirectory ($name + '.log')
    $priorErrorAction = $ErrorActionPreference
    try {
        # Windows PowerShell wraps native stderr in ErrorRecord; retain the process exit code.
        $ErrorActionPreference = 'Continue'
        & $exe @arguments *> $log
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $priorErrorAction }
    $stages.Add([pscustomobject]@{stage=$name; exitCode=$code; result=$(if ($code -eq 0) {'PASS'} else {'FAIL'}); log=$log})
    Write-Host "$name : exit $code ($log)"
    if ($code -ne 0) { throw "Stage $name failed; dependent stages not run." }
}
try {
    $env:PATH = (($env:PATH -split ';' | ForEach-Object { $_.Trim().Trim('"') } | Where-Object { $_ -and $_ -notmatch '[\r\n]' }) -join ';')
    Set-Location $root
    New-Item -ItemType Directory -Force $EvidenceDirectory | Out-Null
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $sdks = & $dotnet --list-sdks
    $hasNet10 = @($sdks | Where-Object { $_ -match '^10\.' }).Count -gt 0
    $sdks | Set-Content (Join-Path $EvidenceDirectory 'sdks.txt')
    $msbuildCommand = Get-Command msbuild -ErrorAction SilentlyContinue
    $msbuild = if ($msbuildCommand) { $msbuildCommand.Source } else {
        & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    }
    if (!$msbuild) { throw 'BLOCKED_ENV: VS MSBuild is required.' }
    if (!(Test-Path "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\mscorlib.dll")) { throw 'BLOCKED_ENV: .NET Framework 4.8 reference assemblies required.' }
    if (!$NuGetPath) { $NuGetPath = (Get-Command nuget -ErrorAction Stop).Source }
    # Resolve SDK dynamically for legacy VS imports. Restore the caller environment in finally.
    $sdkLine = $sdks | Where-Object { $_ -match '^\d+\.\d+\.\d+ \[' } | Select-Object -Last 1
    if (!$sdkLine) { throw 'BLOCKED_ENV: stable .NET SDK required.' }
    if ($sdkLine -match '^(\S+) \[(.+)\]') { $env:MSBuildSDKsPath = Join-Path (Join-Path $Matches[2] $Matches[1]) 'Sdks' }
    $env:MSBuildEnableWorkloadResolver = 'false'
    Stage 'restore-legacy' $NuGetPath @('restore','TabularEditor.sln','-NonInteractive')
    Stage 'restore-core' $dotnet @('restore','PbiBench.Core/PbiBench.Core.csproj','--nologo')
    Stage 'antlr-debug' $msbuild @('AntlrGrammars/AntlrGrammars.csproj','/m','/t:Rebuild','/p:Configuration=Debug','/p:Platform=AnyCPU','/verbosity:minimal')
    Stage 'host-release' $msbuild @('TabularEditor/TabularEditor.csproj','/m','/t:Rebuild','/p:Configuration=Release','/p:Platform=AnyCPU','/verbosity:minimal')
    if ($HostOnlyDiagnostic -or !$hasNet10) {
        $stages.Add([pscustomobject]@{stage='core-smoke'; result=$(if ($HostOnlyDiagnostic) {'NOT_RUN_DIAGNOSTIC'} else {'BLOCKED_ENV'}); exitCode=$null; log='Requires .NET 10 SDK; target unchanged'})
    } else { Stage 'core-smoke' $dotnet @('run','--project','PbiBench.Core.Smoke/PbiBench.Core.Smoke.csproj','-c','Release') }
    Stage 'host-smoke-build' $dotnet @('build','PbiBench.Host.Smoke/PbiBench.Host.Smoke.csproj','-c','Release','--nologo')
    Stage 'host-smoke-run' (Join-Path $root 'PbiBench.Host.Smoke/bin/Release/net48/PbiBench.Host.Smoke.exe') @()
    $exitCode = if ($hasNet10 -or $HostOnlyDiagnostic) { 0 } else { 2 }
} catch {
    Write-Warning $_.Exception.Message
    $stages.Add([pscustomobject]@{stage='verification'; result='INCOMPLETE'; exitCode=1; log=$_.Exception.Message})
} finally {
    $env:MSBuildSDKsPath = $oldSdk
    $env:MSBuildEnableWorkloadResolver = $oldResolver
    $env:PATH = $oldPath
    Set-Location $oldLocation
    $stages | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $EvidenceDirectory 'summary.json')
    $stages | Format-Table stage,result,exitCode
}
exit $exitCode
