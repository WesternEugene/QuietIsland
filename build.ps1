param([bool]$SelfContained = $true, [switch]$SkipTests, [switch]$Diagnose)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$islandDotnet = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $islandDotnet)) {
    $installedSdk = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($installedSdk -and (& $installedSdk.Source --list-sdks | Select-String '^10\.')) { $islandDotnet = $installedSdk.Source }
    else {
        New-Item -ItemType Directory -Force -Path '.tools' | Out-Null
        Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile '.tools\dotnet-install.ps1'
        & '.\.tools\dotnet-install.ps1' -Channel '10.0' -InstallDir (Join-Path $PSScriptRoot '.tools\dotnet') -NoPath
        if ($LASTEXITCODE -ne 0) { throw 'Не удалось установить локальный .NET SDK.' }
    }
}
if (-not $SkipTests) {
    & $islandDotnet test 'tests\QuietIsland.Tests\QuietIsland.Tests.csproj' -c Release --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Тесты .NET не прошли.' }
    if (Get-Command node -ErrorAction SilentlyContinue) { node --test 'integrations\protocol.test.mjs'; if ($LASTEXITCODE -ne 0) { throw 'Тесты интеграций не прошли.' } }
}
& $islandDotnet publish 'src\QuietIsland\QuietIsland.csproj' -c Release -r win-x64 --self-contained $SelfContained.ToString().ToLowerInvariant() -o 'artifacts\QuietIsland' --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Сборка не прошла.' }
Copy-Item -LiteralPath 'README.md','THIRD_PARTY_NOTICES.md' -Destination 'artifacts\QuietIsland'
Copy-Item -LiteralPath 'integrations' -Destination 'artifacts\QuietIsland' -Recurse -Force
Copy-Item -LiteralPath 'docs','packaging' -Destination 'artifacts\QuietIsland' -Recurse -Force
if ($Diagnose) {
    $islandReport = Join-Path $PSScriptRoot 'artifacts\diagnostics.json'
    $islandCheck = Start-Process -FilePath (Join-Path $PSScriptRoot 'artifacts\QuietIsland\QuietIsland.exe') -ArgumentList @('--diagnostics','--render','--ui-checks','--motion-checks',('"' + $islandReport + '"')) -WindowStyle Hidden -PassThru
    if (-not $islandCheck.WaitForExit(55000)) { throw 'Диагностика не завершилась за 55 секунд.' }
    if ($islandCheck.ExitCode -ne 0) { throw 'Диагностика завершилась с ошибкой.' }
    $report = Get-Content -LiteralPath $islandReport -Raw | ConvertFrom-Json
    $islandFailedUi = @($report.uiRegressions.PSObject.Properties | Where-Object { $_.Value -ne $true })
    if ($islandFailedUi.Count -gt 0) { throw ('Проверки интерфейса не прошли: ' + ($islandFailedUi.Name -join ', ')) }
    $islandFailedWorkflows = @($report.desktopWorkflows.PSObject.Properties | Where-Object { $_.Value -ne $true })
    if (-not $report.desktopWorkflows -or $islandFailedWorkflows.Count -gt 0) { throw ('Проверки рабочих сценариев не прошли: ' + ($islandFailedWorkflows.Name -join ', ')) }
    $islandMotionChecks = @('stableNativeBounds','noLayoutPerAnimationFrame','transparentBoundsPassThrough','visibleNotchReceivesInput','compactTextStaysInsideSurface','reversalKeepsPanelContent','layoutChangeAnimates','capsOnAnimates','capsOffAnimates','inputAnimationsSettle')
    $islandFailedMotion = @($islandMotionChecks | Where-Object { $report.motion.$_ -ne $true })
    if ($islandFailedMotion.Count -gt 0) { throw ('Проверки движения не прошли: ' + ($islandFailedMotion -join ', ')) }
    $islandMusicChecks = @('playingDetected','pauseDetected','waveMovesWhilePlaying','silenceStaysStill','amplitudeFollowsAudio','spectrumFollowsAudio','staleAudioExpires','coverDetected','lowerCornersReact','animationStops')
    $islandFailedMusic = @($islandMusicChecks | Where-Object { $report.musicLifecycle.$_ -ne $true })
    if ($islandFailedMusic.Count -gt 0) { throw ('Проверки музыки не прошли: ' + ($islandFailedMusic -join ', ')) }
    if (-not ($report.everything.found -and $report.ocr.passed -and $report.qr.passed -and $report.bridge.started -and $report.bridge.rejectsUnauthenticated -and $report.bridge.explicitDenial -and $report.aiProtocol.payloadValid -and $report.aiProtocol.streamedAnswer -and $report.uiRegressions.pillHitArea -and $report.uiRegressions.aiDropPreserved -and $report.uiRegressions.hiddenTimerFires -and $report.recordingMux.fullVideoPreserved -and $report.musicLifecycle.playingDetected -and $report.musicLifecycle.waveMovesWhilePlaying -and $report.musicLifecycle.animationStops)) { throw 'Один из основных интеграционных тестов не прошёл. См. artifacts/diagnostics.json.' }
}
Write-Output 'Готово: artifacts\QuietIsland\QuietIsland.exe'
