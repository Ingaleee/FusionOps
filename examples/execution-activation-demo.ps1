param([string]$BaseUri='http://localhost:5121')
$ErrorActionPreference='Stop'
# Development-only review/reservation walkthrough. Production requires signed project operators and separate approval.
. "$PSScriptRoot/execution-plan-demo.ps1" -BaseUri $BaseUri
$taskProjectState=Send-Temporal Get "/api/v1/projects/$taskProject"
foreach($taskItem in $taskDefinition.tasks){$taskItem.materials=@()}
$taskProjectState=Send-Temporal Put "/api/v1/projects/$taskProject/definition" $taskDefinition -Version $taskProjectState.version
$taskActivationScenarioRequest=$taskPlanScenarioRequest.Clone();$taskActivationScenarioRequest.id=[Guid]::NewGuid();$taskActivationScenarioRequest.expectedProjectVersion=$taskProjectState.version
$taskActivationScenario=Send-Temporal Post $taskRoute $taskActivationScenarioRequest 201
$taskActivationPlan=Send-Temporal Post $taskPlanRoute @{id=[Guid]::NewGuid();scenarioId=$taskActivationScenario.id;expectedProjectVersion=$taskProjectState.version} 201
$taskActivationPlan=Send-Temporal Post "$taskPlanRoute/$($taskActivationPlan.id)/approve" @{reviewedDigest=$taskActivationPlan.digest} -Version 1
$taskActivationRoute="$taskPlanRoute/$($taskActivationPlan.id)/activation"
Send-Temporal Post $taskActivationRoute @{reviewedDigest=$taskActivationPlan.digest} 428 | Out-Null
Send-Temporal Post $taskActivationRoute @{reviewedDigest=$taskActivationPlan.digest} 412 -Version 1 | Out-Null
$taskActivation=Send-Temporal Post $taskActivationRoute @{reviewedDigest=$taskActivationPlan.digest} 202 -Version 2
function Wait-Activation([string]$State){
    $taskDeadline=[DateTimeOffset]::UtcNow.AddSeconds(90)
    do{
        try{$taskCurrent=Send-Temporal Get $taskActivationRoute}catch{Start-Sleep -Milliseconds 200;continue}
        if($taskCurrent.activation.state -eq $State){return $taskCurrent}
        if($taskCurrent.activation.state -in @('NeedsAttention','Rejected','Compensated') -and $State -ne $taskCurrent.activation.state){throw "Activation reached $($taskCurrent.activation.state): $($taskCurrent.activation.failureCode) $($taskCurrent.activation.lastErrorCode)"}
        Start-Sleep -Milliseconds 200
    }while([DateTimeOffset]::UtcNow -lt $taskDeadline)
    throw 'Activation did not converge within the walkthrough limit'
}
$taskActive=Wait-Activation Active
if(@($taskActive.activation.resourceOwnership).Count -ne 3 -or @($taskActive.decisions).Count -ne 3){throw 'Missing confirmed task interval ownership/decisions'}
$taskOwned=Send-Temporal Get "/api/v1/projects/allocations?ids=$taskProject"
if(@($taskOwned.$taskProject).Count -ne 3){throw 'Task intervals were not reserved individually'}
$taskLicense=Send-Temporal Get "/api/v1/licenses/$taskProduct"
if($taskLicense.allocatedSeats -ne 0 -or $taskLicense.temporalReservedPeak -ne 1){throw 'License ownership was collapsed into permanent allocation'}
Send-Temporal Get $taskActivationRoute -Tenant $taskForeignTenant -Expected 404 | Out-Null
Send-Temporal Post "$taskActivationRoute/cancel" @{reason='operator_cancelled'} 412 -Version 1 | Out-Null
Send-Temporal Post "$taskActivationRoute/cancel" @{reason='operator_cancelled'} -Version $taskActive.activation.version | Out-Null
$taskCompensated=Wait-Activation Compensated
$taskLicense=Send-Temporal Get "/api/v1/licenses/$taskProduct"
if($taskLicense.temporalReservedPeak -ne 0 -or @($taskCompensated.decisions).Count -ne 6){throw 'Compensation/history incomplete'}
$taskOriginal=Send-Temporal Post $taskActivationRoute @{reviewedDigest=$taskActivationPlan.digest} 202 -Version 2
if($taskOriginal.state -ne 'Compensated'){throw 'A delayed activation retry restarted compensated effects'}
Write-Output 'Activation evidence: exact approved version, three actual task/resource intervals, one future seat, original decisions, hosted worker, stale/foreign access checks, ownership compensation and original replay. Task starts/completions remain a separate lifecycle.'
