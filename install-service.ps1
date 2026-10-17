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
    if (Test-Path (Join-Path $PSScriptRoot 'frpc.exe')) {
        Copy-Item (Join-Path $PSScriptRoot 'frpc.exe') (Join-Path $target 'Engine/frpc.exe') -Force
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
