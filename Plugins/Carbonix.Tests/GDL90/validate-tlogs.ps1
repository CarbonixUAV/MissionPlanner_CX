# Converts the validation flights and compares their frame logs against the baseline
# kept beside them. Run after any change under Plugins/Carbonix/GDL90.
#
#   validate-tlogs.ps1 -Root "<folder with the tlogs>"          compare
#   validate-tlogs.ps1 -Root "<folder>" -Refresh                 rewrite the baseline
#
# -Root defaults to $env:CBX_GDL90_VALIDATION, then to the tlog-validation folder in the
# Carbonix Dropbox. The folder holds the tlogs and a "baseline" subfolder; -Refresh
# writes it and records the commit in its report.txt. -NoBuild skips the test project
# build.
param(
    [string]$Root = $env:CBX_GDL90_VALIDATION,
    [switch]$Refresh,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

if (-not $Root) {
    $Root = Join-Path $env:USERPROFILE 'Carbonix Company Dropbox\Carbonix Software\04_Project\30_MissionPlanner_GDL90\tlog-validation'
}
if (-not (Test-Path $Root)) {
    throw "No validation folder at $Root. Pass -Root <folder holding the validation tlogs> or set CBX_GDL90_VALIDATION."
}
$Root = (Resolve-Path $Root).Path

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$project = Join-Path $repo 'Plugins\Carbonix.Tests\Carbonix.Tests.csproj'
$assembly = Join-Path $repo 'Plugins\Carbonix.Tests\bin\Debug\net472\Carbonix.Tests.dll'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products * -property installationPath
if (-not $vs) { throw 'Visual Studio not found.' }
$msbuild = Join-Path $vs 'MSBuild\Current\Bin\MSBuild.exe'
$vstest = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe'

if (-not $NoBuild) {
    & $msbuild $project -nologo -v:m
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}

$env:CBX_GDL90_VALIDATION = $Root
if ($Refresh) { $env:CBX_GDL90_VALIDATION_REFRESH = '1' } else { $env:CBX_GDL90_VALIDATION_REFRESH = '' }

& $vstest $assembly --TestCaseFilter:"TestCategory=Validation" --logger:"console;verbosity=detailed"
exit $LASTEXITCODE
