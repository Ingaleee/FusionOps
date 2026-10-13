param(
    [ValidateSet('ReservingResources','ReservingLicenses')][string]$Boundary='ReservingResources',
    [ValidatePattern('^fusionops-[a-z0-9-]+-verify$')][string]$Project='fusionops-execution-recovery-verify',
    [switch]$SkipBuild,
    [switch]$KeepStack
)
$ErrorActionPreference='Stop'
$taskRepoRoot=Split-Path $PSScriptRoot -Parent
$taskComposeSource=Join-Path $taskRepoRoot 'infra/compose.execution-verification.yml'
$taskArtifacts=Join-Path $taskRepoRoot 'artifacts'
New-Item -ItemType Directory -Path $taskArtifacts -Force | Out-Null
$taskCompose=Join-Path $taskArtifacts "$Project-compose.yml"
$taskFault=Join-Path $taskArtifacts "$Project-fault.yml"
$taskDemoLog=Join-Path $taskArtifacts "$Project-$Boundary-demo.log"
$taskReportPath=Join-Path $taskArtifacts "$Project-$Boundary-report.json"
Copy-Item -LiteralPath $taskComposeSource -Destination $taskCompose
$taskDemoJob=$null
function Compose([string[]]$Arguments){
    & docker compose -p $Project -f $taskCompose @Arguments
    if($LASTEXITCODE -ne 0){throw "Verification compose failed: $($Arguments -join ' ')"}
}
function Check-Owner([string]$Container){
    $taskOwner=(& docker inspect $Container --format '{{ index .Config.Labels "com.docker.compose.project" }}').Trim()
    if($LASTEXITCODE -ne 0 -or $taskOwner -ne $Project){throw 'Verification container ownership mismatch'}
}
try{
    if(!$SkipBuild){
        & docker build -f (Join-Path $taskRepoRoot 'FusionOps.Presentation/Dockerfile') -t fusionops-api:activation-verification $taskRepoRoot
        if($LASTEXITCODE -ne 0){throw 'API image build failed'}
    }
    try{Compose -Arguments @('up','-d','--wait','--wait-timeout','180')}
    catch{
        $taskRabbit="$Project-rabbit-1";Check-Owner $taskRabbit
        $taskRabbitLog=(& docker logs $taskRabbit --tail 20 2>&1 | Out-String)
        if(!$taskRabbitLog.Contains('.erlang.cookie: eacces')){throw}
        & docker run --rm --user 0 --volumes-from $taskRabbit --entrypoint chown rabbitmq:3-management-alpine 100:101 /var/lib/rabbitmq/.erlang.cookie
        if($LASTEXITCODE -ne 0){throw 'Verification cookie repair failed'}
        Compose -Arguments @('up','-d','--wait','--wait-timeout','180')
    }
    $taskBinding=(& docker compose -p $Project -f $taskCompose port api 80).Trim()
    if($taskBinding -notmatch '^127\.0\.0\.1:(\d+)$'){throw 'Expected loopback verification API'}
    $taskPort=$Matches[1];$taskBaseUri="http://$taskBinding"
    # Keep the allocated loopback port stable when replacing the killed API container.
    $taskSource=Get-Content -LiteralPath $taskCompose -Raw
    if(!$taskSource.Contains('127.0.0.1::80')){throw 'Expected ephemeral verification port contract'}
    Set-Content -LiteralPath $taskCompose -Value $taskSource.Replace('127.0.0.1::80',"127.0.0.1:${taskPort}:80") -NoNewline -Encoding utf8
    Set-Content -LiteralPath $taskFault -Value "services:`n  api:`n    environment:`n      Execution__FailureLab__Enabled: 'true'`n      Execution__FailureLab__PauseAfter: '$Boundary'`n" -NoNewline -Encoding utf8
    & docker compose -p $Project -f $taskCompose -f $taskFault up -d --no-deps api
    if($LASTEXITCODE -ne 0){throw 'Failure-lab API start failed'}
    $taskApi="$Project-api-1";Check-Owner $taskApi
    $taskReadyDeadline=[DateTimeOffset]::UtcNow.AddSeconds(30)
    do{try{$taskReady=Invoke-WebRequest "$taskBaseUri/health/ready" -TimeoutSec 2 -SkipHttpErrorCheck;if($taskReady.StatusCode -eq 200){break}}catch{};Start-Sleep -Milliseconds 100}while([DateTimeOffset]::UtcNow -lt $taskReadyDeadline)
    if($taskReady.StatusCode -ne 200){throw 'Failure-lab API not ready'}
    $taskDemoPath=Join-Path $taskRepoRoot 'examples/execution-activation-demo.ps1'
    $taskDemoJob=Start-Job -ScriptBlock {param($Path,$Uri,$Log); & pwsh -NoProfile -File $Path -BaseUri $Uri *> $Log;if($LASTEXITCODE -ne 0){throw "Recovery demo failed ($LASTEXITCODE)"}} -ArgumentList $taskDemoPath,$taskBaseUri,$taskDemoLog
    $taskMarkerDeadline=[DateTimeOffset]::UtcNow.AddSeconds(30);$taskMarker=$null
    do{
        $taskLines=(& docker logs $taskApi --tail 100 2>&1 | Out-String)
        if($taskLines -match "FUSIONOPS_FAILURE_LAB_PARTICIPANT_COMMITTED ([0-9a-f-]{36}) $Boundary"){$taskMarker=$Matches[1];break}
        if($taskDemoJob.State -in @('Failed','Completed')){Receive-Job $taskDemoJob;Get-Content $taskDemoLog -Tail 8;throw 'Demo ended before commit boundary'}
        Start-Sleep -Milliseconds 100
    }while([DateTimeOffset]::UtcNow -lt $taskMarkerDeadline)
    if(!$taskMarker){throw 'Participant commit marker absent'}
    Check-Owner $taskApi
    & docker kill --signal KILL $taskApi
    if($LASTEXITCODE -ne 0){throw 'Verification worker kill failed'}
    $taskLostState=& docker exec "$Project-pg-1" psql -U fusionops -d fusionops -t -A -c "SELECT row_to_json(x) FROM (SELECT `"Id`",`"State`",`"LeaseEpoch`",`"Version`",`"ResourceOwnershipJson`" FROM ops_execution_activations WHERE `"Id`"='$taskMarker') x;"
    if($LASTEXITCODE -ne 0){throw 'Lost-commit state read failed'}
    $taskLost=$taskLostState | ConvertFrom-Json
    if($taskLost.State -ne $Boundary){throw 'Coordinator already advanced past the killed boundary'}
    # Replace only this API with ordinary configuration; existing stores and receipts survive.
    Compose -Arguments @('up','-d','--no-deps','api')
    $taskDemoDeadline=[DateTimeOffset]::UtcNow.AddSeconds(100)
    do{if($taskDemoJob.State -in @('Completed','Failed','Stopped')){break};Start-Sleep -Milliseconds 200}while([DateTimeOffset]::UtcNow -lt $taskDemoDeadline)
    Receive-Job $taskDemoJob
    if($taskDemoJob.State -ne 'Completed'){Get-Content $taskDemoLog -Tail 12;throw 'Recovery demo failed or timed out'}
    Get-Content $taskDemoLog -Tail 1
    $taskFinalRaw=& docker exec "$Project-pg-1" psql -U fusionops -d fusionops -t -A -c "SELECT row_to_json(x) FROM (SELECT `"Id`",`"State`",`"LeaseEpoch`",`"Version`",`"Digest`",`"ResourceOwnershipJson`" FROM ops_execution_activations WHERE `"Id`"='$taskMarker') x;"
    if($LASTEXITCODE -ne 0){throw 'Recovered state read failed'}
    $taskFinal=$taskFinalRaw | ConvertFrom-Json
    if($taskFinal.State -ne 'Compensated' -or $taskFinal.LeaseEpoch -le $taskLost.LeaseEpoch -or @($taskFinal.ResourceOwnershipJson | ConvertFrom-Json).Count -ne 3){throw 'Recovery/fencing/ownership invariant failed'}
    $taskReport=@{boundary=$Boundary;planId=$taskMarker;apiKilled=$true;before=$taskLost;after=$taskFinal;demoLog=$taskDemoLog;scope='Local Development API process killed after actual owning-store commit; original lease/receipts retained; activation and compensation verified. No region/provider/financial recovery claim.'}
    $taskReport | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $taskReportPath -Encoding utf8
    Write-Output "Verified $Boundary recovery; report: $taskReportPath"
}finally{
    if($taskDemoJob){if($taskDemoJob.State -eq 'Running'){Stop-Job $taskDemoJob};Remove-Job $taskDemoJob}
    if(!$KeepStack){Compose -Arguments @('down','--volumes','--remove-orphans')}
}
