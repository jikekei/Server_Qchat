param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'linux-x64')]
    [string]$Runtime,

    [Parameter(Mandatory)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Output directory already exists: $output" }

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$runtimeName = 'runtime'
$work = Join-Path ([IO.Path]::GetDirectoryName($output)) ('.publish-' + [Guid]::NewGuid().ToString('N'))
$bot = Join-Path $work 'bot'
$daemon = Join-Path $work 'daemon'
$extension = if ($Runtime -eq 'win-x64') { '.exe' } else { '' }
$botEntrypoint = "Server_Qcha.Bot$extension"
$daemonEntrypoint = "Server_Qcha.Daemon$extension"

try {
    foreach ($app in @('Bot', 'Daemon')) {
        $project = "bot/src/Server_Qcha.$app/Server_Qcha.$app.csproj"
        $publishDirectory = Join-Path $work $app.ToLowerInvariant()
        dotnet publish (Join-Path $root $project) -c Release -r $Runtime --self-contained true -o $publishDirectory "-p:ReleaseRuntimeDirectory=$runtimeName"
        if ($LASTEXITCODE -ne 0) { throw "Publish failed for Server_Qcha.$app ($Runtime)" }
    }

    $runtimeDirectory = Join-Path $bot $runtimeName
    New-Item -ItemType Directory -Path $runtimeDirectory | Out-Null

    # The Bot owns the deployment configuration and web assets.
    Get-ChildItem -LiteralPath $bot -File | Where-Object {
        $_.Name -ne $botEntrypoint -and $_.Name -notin @('appsettings.json', 'appsettings.Example.json')
    } | Move-Item -Destination $runtimeDirectory
    Get-ChildItem -LiteralPath $daemon -File | Where-Object {
        $_.Name -ne $daemonEntrypoint -and $_.Name -notlike 'appsettings*.json'
    } | Copy-Item -Destination $runtimeDirectory
    Copy-Item -LiteralPath (Join-Path $daemon $daemonEntrypoint) -Destination $bot

    if ($Runtime -eq 'win-x64') { Copy-Item -Path (Join-Path $PSScriptRoot '*.bat') -Destination $bot }
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE'), (Join-Path $root 'README.md') -Destination $bot

    $localOverride = Join-Path $runtimeDirectory 'appsettings.Local.json'
    if (Test-Path -LiteralPath $localOverride) { Remove-Item -LiteralPath $localOverride }

    if ($Runtime -eq 'linux-x64' -and $IsLinux) {
        chmod +x (Join-Path $bot $botEntrypoint) (Join-Path $bot $daemonEntrypoint) (Join-Path $runtimeDirectory 'createdump')
        if ($LASTEXITCODE -ne 0) { throw 'Failed to set executable permissions' }
    }

    Move-Item -LiteralPath $bot -Destination $output
} finally {
    if (Test-Path -LiteralPath $work) {
        $resolvedWork = (Resolve-Path -LiteralPath $work).Path
        if ($resolvedWork -eq $work) { Remove-Item -LiteralPath $resolvedWork -Recurse -Force }
    }
}
