$ErrorActionPreference = 'Stop'
$target = Join-Path $env:ProgramFiles 'LoliaBloom'
$data = Join-Path $env:ProgramData 'LoliaBloom'
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Administrator permission required. Use the installation button in Lolia Bloom and accept Windows UAC.'
    }
    $source = Join-Path $PSScriptRoot 'Service'
    if (-not (Test-Path (Join-Path $source 'Lolia.Service.exe'))) { throw 'Service\Lolia.Service.exe is missing. Extract the complete download first.' }
    $service = Get-Service LoliaBloomService -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne 'Stopped') {
        Stop-Service LoliaBloomService -ErrorAction Stop
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
    New-Item -ItemType Directory -Force -Path (Join-Path $target 'Service'),(Join-Path $target 'Engine'),$data | Out-Null
    Copy-Item (Join-Path $source '*') (Join-Path $target 'Service') -Recurse -Force
    $versionPath = Join-Path $target 'Engine/engine-version.txt'
    $bundledVersion = 'v0.71.2'
    $installedVersion = if (Test-Path $versionPath) { (Get-Content $versionPath -Raw).Trim() } else { '' }
    $keepEngine = $false
    $parsedVersion = $null
    if ([Version]::TryParse($installedVersion.TrimStart('v'), [ref]$parsedVersion)) { $keepEngine = $parsedVersion -ge [Version]'0.71.2' }
    if ((Test-Path (Join-Path $PSScriptRoot 'frpc.exe')) -and (-not $keepEngine -or -not (Test-Path (Join-Path $target 'Engine/frpc.exe')))) {
        Copy-Item (Join-Path $PSScriptRoot 'frpc.exe') (Join-Path $target 'Engine/frpc.exe') -Force
        $bundledVersion | Set-Content $versionPath -Encoding ASCII
    }
    $binPath = '"' + (Join-Path $target 'Service/Lolia.Service.exe') + '"'
    if ($service) {
        $entry = Get-CimInstance Win32_Service -Filter "Name='LoliaBloomService'"
        $result = Invoke-CimMethod -InputObject $entry -MethodName Change -Arguments @{PathName=$binPath;StartMode='Automatic'}
        if ($result.ReturnValue -ne 0) { throw "Service configuration failed: $($result.ReturnValue)" }
    } else { New-Service -Name LoliaBloomService -BinaryPathName $binPath -StartupType Automatic -DisplayName 'Lolia Bloom Service' | Out-Null }
    & sc.exe failure LoliaBloomService reset= 86400 actions= restart/5000/restart/15000/restart/60000
    if ($LASTEXITCODE -ne 0) { throw "Service recovery configuration failed: $LASTEXITCODE" }
    Start-Service LoliaBloomService
    (Get-Service LoliaBloomService).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'LoliaBloomPipe', [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(15000)
        $writer = [IO.StreamWriter]::new($pipe); $writer.AutoFlush = $true
        $reader = [IO.StreamReader]::new($pipe)
        $writer.WriteLine('{"action":"list"}')
        # Windows PowerShell 5.1 has no Task.WaitAsync.
        $replyTask = $reader.ReadLineAsync()
        if (-not $replyTask.Wait(15000)) { throw 'Service started but did not respond within 15 seconds.' }
        $reply = $replyTask.Result | ConvertFrom-Json
        if (-not $reply.ok) { throw $reply.message }
    } finally { $pipe.Dispose() }
    'Service installation and connection verification succeeded.' | Set-Content (Join-Path $data 'install-result.txt') -Encoding UTF8
    exit 0
} catch {
    $message = $_.Exception.Message
    try { New-Item -ItemType Directory -Force -Path $data | Out-Null; $message | Set-Content (Join-Path $data 'install-result.txt') -Encoding UTF8 } catch {}
    Write-Host "ERROR: $message" -ForegroundColor Red
    exit 1
}
