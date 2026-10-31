[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$OutputPath,
  [int]$ChunkCount = 16,
  [string]$Url = 'https://github.com/Lolia-FRP/lolia-frp/releases/download/v0.71.2/LoliaFrp_windows_amd64.zip',
  [string]$ExpectedHash = '208a9a4c6d4d6f0a3c07cd2257c821b8cf3d7e49b809b34e3d5f2367dcb9127e'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($ChunkCount -lt 1 -or $ChunkCount -gt 64) { throw 'ChunkCount must be between 1 and 64.' }

$headers = & curl.exe -sIL $Url
if ($LASTEXITCODE -ne 0) { throw 'Unable to read the FRP release headers.' }
$total = 0L
foreach ($line in $headers) {
  if ($line -match '^Content-Length:\s*(\d+)\s*$') { $total = [long]$Matches[1] }
}
if ($total -le 0) { throw 'The FRP release did not report a valid Content-Length.' }

$workRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$work = [IO.Path]::Combine($workRoot, 'LoliaFrp-download-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null
$processes = @()
$ranges = @()
try {
  $chunkSize = [math]::Floor($total / $ChunkCount)
  for ($i = 0; $i -lt $ChunkCount; $i++) {
    $start = [long]($i * $chunkSize)
    $end = if ($i -eq $ChunkCount - 1) { $total - 1 } else { [long](($i + 1) * $chunkSize - 1) }
    $path = [IO.Path]::Combine($work, ('part-{0:d2}.bin' -f $i))
    $ranges += [pscustomobject]@{ Start = $start; End = $end; Path = $path }
  }
  $downloads = @()
  for ($i = 0; $i -lt $ChunkCount; $i++) {
    $range = $ranges[$i]
    $arguments = @('-L','--fail','--retry','3','--retry-delay','2','--connect-timeout','20','-sS','-r',"$($range.Start)-$($range.End)",$Url,'-o',$range.Path)
    $downloads += [pscustomobject]@{ Range = $range; Process = Start-Process -FilePath curl.exe -ArgumentList $arguments -WindowStyle Hidden -PassThru }
  }

  for ($round = 0; $round -lt 4; $round++) {
    $deadline = [DateTime]::UtcNow.AddMinutes(10)
    while (@($downloads | Where-Object { -not $_.Process.HasExited }).Count -gt 0) {
      if ([DateTime]::UtcNow -ge $deadline) {
        @($downloads | Where-Object { -not $_.Process.HasExited }) | ForEach-Object { $_.Process.Kill($true) }
        throw 'Parallel FRP download timed out after 10 minutes.'
      }
      Start-Sleep -Seconds 1
    }

    $invalid = @()
    foreach ($download in $downloads) {
      $expected = $download.Range.End - $download.Range.Start + 1
      $actual = if (Test-Path -LiteralPath $download.Range.Path) { (Get-Item -LiteralPath $download.Range.Path).Length } else { -1 }
      if ($download.Process.ExitCode -ne 0 -or $actual -ne $expected) { $invalid += $download }
    }
    if (@($invalid).Count -eq 0) { break }
    if ($round -eq 3) { throw "FRP download chunks failed after retries: $((@($invalid) | ForEach-Object { $_.Range.Path }) -join ', ')" }

    foreach ($download in $invalid) {
      $arguments = @('-L','--fail','--retry','3','--retry-delay','2','--connect-timeout','20','-sS','-r',"$($download.Range.Start)-$($download.Range.End)",$Url,'-o',$download.Range.Path)
      $download.Process = Start-Process -FilePath curl.exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    }
  }

  $directory = Split-Path -Parent $OutputPath
  if ($directory) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
  $output = [IO.FileStream]::new($OutputPath, [IO.FileMode]::Create, [IO.FileAccess]::Write)
  try {
    foreach ($range in $ranges) {
      $input = [IO.FileStream]::new($range.Path, [IO.FileMode]::Open, [IO.FileAccess]::Read)
      try { $input.CopyTo($output) } finally { $input.Dispose() }
    }
  } finally { $output.Dispose() }

  $hash = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($hash -ne $ExpectedHash) { throw "FRP SHA256 mismatch. Expected $ExpectedHash, got $hash." }
  Write-Host "Downloaded and verified FRP package: $OutputPath"
} finally {
  if (Test-Path -LiteralPath $work) {
    $resolvedWork = (Resolve-Path -LiteralPath $work).Path
    if ($resolvedWork.StartsWith($workRoot, [StringComparison]::OrdinalIgnoreCase)) {
      try { Remove-Item -LiteralPath $resolvedWork -Recurse -Force }
      catch { Write-Warning "Could not clean temporary FRP download directory: $resolvedWork" }
    }
  }
}
