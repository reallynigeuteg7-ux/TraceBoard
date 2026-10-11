param([string]$Output = (Join-Path $PSScriptRoot '..\TraceBoard-Windows'))
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = 'D:\Codex\tools\dotnet-home'
$env:NUGET_PACKAGES = 'D:\Codex\tools\nuget'
$env:TEMP = 'D:\Codex\tools\temp'
$env:TMP = $env:TEMP
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME,$env:NUGET_PACKAGES,$env:TEMP | Out-Null
$dotnet = 'D:\Codex\tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
Push-Location $PSScriptRoot
try {
    & $dotnet publish 'TraceBoard.csproj' -c Release -r win-x64 --self-contained true -o $Output -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось собрать TraceBoard.' }
} finally { Pop-Location }
