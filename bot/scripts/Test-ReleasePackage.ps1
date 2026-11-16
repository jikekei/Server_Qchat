param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'linux-x64')]
    [string]$Runtime,

    [Parameter(Mandatory)]
    [string]$Archive
)

$ErrorActionPreference = 'Stop'
$zip = (Resolve-Path -LiteralPath $Archive).Path
$parent = [IO.Path]::GetDirectoryName($zip)
$name = '.verify-release-' + [Guid]::NewGuid().ToString('N')
$work = Join-Path $parent $name
$windows = $Runtime -eq 'win-x64'
$extension = if ($windows) { '.exe' } else { '' }
$hostLibrary = if ($windows) { 'hostfxr.dll' } else { 'libhostfxr.so' }
$processes = @()

try {
    if ($windows) {
        [IO.Compression.ZipFile]::ExtractToDirectory($zip, $work)
    } else {
        unzip -q $zip -d $work
        if ($LASTEXITCODE -ne 0) { throw "Cannot extract $zip" }
    }

    $bot = "Server_Qcha.Bot$extension"
    $daemon = "Server_Qcha.Daemon$extension"
    foreach ($file in @($bot, $daemon, "runtime/$hostLibrary", 'runtime/Server_Qcha.Bot.dll',
            'runtime/Server_Qcha.Daemon.dll', 'runtime/Server_Qcha.LocalAdmin.Shared.dll',
            'appsettings.json', 'wwwroot/index.html', 'wwwroot/js/modules/useLocalAdmin.js')) {
        if (!(Test-Path -LiteralPath (Join-Path $work $file) -PathType Leaf)) { throw "Package is missing $file" }
    }
    $runtimeDirectory = (Join-Path $work 'runtime') + [IO.Path]::DirectorySeparatorChar
    $strayDll = Get-ChildItem -LiteralPath $work -File -Recurse -Filter '*.dll' |
        Where-Object { !$_.FullName.StartsWith($runtimeDirectory, [StringComparison]::Ordinal) } |
        Select-Object -First 1
    if ($strayDll) { throw "DLL outside runtime/: $($strayDll.FullName)" }

    $testEnvironment = @{
        LocalAdmin__Enabled = 'false'
        LocalAdmin__AutoStartDaemon = 'false'
        LocalAdmin__DaemonUri = 'http://127.0.0.1:19090'
        LocalAdmin__DaemonToken = 'release-smoke-test-token'
        WebPanel__Enabled = 'true'
        WebPanel__Host = '127.0.0.1'
        WebPanel__Port = '18080'
        GoCqHttp__WsBaseUri = 'ws://127.0.0.1:19091/'
        SocketServer__Ports__0 = '19092'
        SocketServer__NotificationPort = '19093'
    }
    $headers = @{ 'X-Daemon-Token' = $testEnvironment.LocalAdmin__DaemonToken }
    $checks = @(
        @{ File = $daemon; Url = 'http://127.0.0.1:19090/api/daemon/ping'; Text = '"status":"ok"' },
        @{ File = $bot; Url = 'http://127.0.0.1:18080/index.html'; Text = '<title>' }
    )
    foreach ($check in $checks) {
        $start = @{
            FilePath = Join-Path $work $check.File
            WorkingDirectory = $work
            Environment = $testEnvironment
            PassThru = $true
            RedirectStandardOutput = Join-Path $work "$($check.File).stdout"
            RedirectStandardError = Join-Path $work "$($check.File).stderr"
        }
        if ($windows) { $start.WindowStyle = 'Hidden' }
        $process = Start-Process @start
        $processes += $process
        $ready = $false
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
        while (!$process.HasExited -and [DateTimeOffset]::UtcNow -lt $deadline) {
            try {
                $response = Invoke-WebRequest -Uri $check.Url -NoProxy -TimeoutSec 2 -Headers $headers
                if ($response.StatusCode -eq 200 -and $response.Content.Contains($check.Text)) {
                    $ready = $true
                    break
                }
            } catch {}
            Start-Sleep -Milliseconds 300
        }
        if (!$ready) {
            Get-Content -LiteralPath $start.RedirectStandardError -Tail 12
            throw "$($check.File) did not become ready at $($check.Url)"
        }
        Write-Host "Verified $($check.File) from $Runtime ZIP."
    }
} finally {
    foreach ($process in $processes) {
        if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
    if (Test-Path -LiteralPath $work) {
        $resolved = (Resolve-Path -LiteralPath $work).Path
        if ([IO.Path]::GetDirectoryName($resolved) -eq $parent -and [IO.Path]::GetFileName($resolved) -eq $name) {
            Remove-Item -LiteralPath $resolved -Recurse
        }
    }
}
