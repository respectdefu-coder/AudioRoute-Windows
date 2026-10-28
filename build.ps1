param([switch]$Test, [string]$TestDirectory = "")
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Install .NET Framework 4.8 or the 4.8 Developer Pack first.' }
$bin = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$references = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Runtime.Serialization.dll')
$executable = Join-Path $bin 'AudioRoute.exe'
$icon = Join-Path $PSScriptRoot 'assets\AudioRoute.ico'
if (-not (Test-Path -LiteralPath $icon)) { & (Join-Path $PSScriptRoot 'tools\generate-icon.ps1') }
$iconResources = @("/win32icon:$icon", "/resource:$icon,AudioRoute.App.ico")
& $compiler /nologo /utf8output /warn:4 /warnaserror+ /optimize+ /target:winexe /platform:anycpu "/out:$executable" "/win32manifest:$PSScriptRoot\app.manifest" $iconResources $references $sources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'app.config') -Destination "$executable.config" -Force
Write-Output "Built: $executable"
if ($Test) {
    $testExe = Join-Path $bin 'AudioRoute.Tests.exe'
    $testSources = @('Models.cs', 'RuleEngine.cs', 'Storage.cs', 'CoreAudio.cs') | ForEach-Object { Join-Path $PSScriptRoot "src\$_" }
    & $compiler /nologo /utf8output /warn:4 /warnaserror+ /target:exe "/out:$testExe" $references $testSources (Join-Path $PSScriptRoot 'tests\EngineTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    if (-not $TestDirectory) { $TestDirectory = Join-Path $env:TEMP 'AudioRouteTests' }
    & $testExe $TestDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    $uiTestExe = Join-Path $bin 'AudioRoute.UiTests.exe'
    & $compiler /nologo /utf8output /warn:4 /warnaserror+ /target:exe /main:UiTests "/out:$uiTestExe" "/win32manifest:$PSScriptRoot\app.manifest" $iconResources $references $sources (Join-Path $PSScriptRoot 'tests\UiTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'UI test compilation failed.' }
    & $uiTestExe (Join-Path $TestDirectory 'ui')
    if ($LASTEXITCODE -ne 0) { throw 'UI integration tests failed.' }
}
