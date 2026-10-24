param([Parameter(Mandatory = $true)][string]$Executable)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Executable).Path
$root = Join-Path $env:RUNNER_TEMP ("pcl-updater-smoke-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$source = Join-Path $root 'downloaded launcher.exe'
$target = Join-Path $root 'installed launcher.exe'
Copy-Item -LiteralPath $exe -Destination $source
[IO.File]::WriteAllText($target, 'old executable placeholder')
$expected = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
# Exercise both the bootstrap protocol used by old clients and the verified protocol.
foreach ($verified in @($false, $true)) {
    [IO.File]::WriteAllText($target, 'old executable placeholder')
    $arguments = @('update', '2147483647', ('"' + $target + '"'), ('"' + $source + '"'), 'false')
    if ($verified) { $arguments += $expected }
    $updater = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Hidden -PassThru
    if (!$updater.WaitForExit(45000)) {
        Stop-Process -Id $updater.Id -Force
        $log = Join-Path $root 'Log/LastPending_Update.log'
        if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log }
        throw 'Updater executable did not exit within 45 seconds'
    }
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
        $log = Join-Path $root 'Log/LastPending_Update.log'
        if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log }
        throw "Updater executable failed replacement (verified=$verified, exit=$($updater.ExitCode))"
    }
    Write-Output "PASS: executable updater replaced target and hash matches (verified=$verified)"
}
