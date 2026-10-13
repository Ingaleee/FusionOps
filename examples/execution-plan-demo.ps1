param([string]$BaseUri='http://localhost:5121')
$ErrorActionPreference='Stop'
# Development-only walkthrough; production uses verified project roles and a separate approver.
. "$PSScriptRoot/temporal-planning-demo.ps1" -BaseUri $BaseUri
$taskPlanScenarioRequest=$taskRequest.Clone();$taskPlanScenarioRequest.id=[Guid]::NewGuid();$taskPlanScenarioRequest.expectedProjectVersion=2
$taskPlanScenario=Send-Temporal Post $taskRoute $taskPlanScenarioRequest 201
$taskPlanRoute="/api/v1/projects/$taskProject/execution-plans"
$taskPlanRequest=@{id=[Guid]::NewGuid();scenarioId=$taskPlanScenario.id;expectedProjectVersion=2}
$taskPlan=Send-Temporal Post $taskPlanRoute $taskPlanRequest 201
if($taskPlan.state -ne 'AwaitingApproval' -or $taskPlan.totalCost -ne 60 -or $taskPlan.evidence.scenario.digest -ne $taskPlanScenario.digest){throw 'Plan lost its original reviewed evidence'}
Send-Temporal Post "$taskPlanRoute/$($taskPlan.id)/approve" @{reviewedDigest='wrong'} 422 -Version 1 | Out-Null
Send-Temporal Post "$taskPlanRoute/$($taskPlan.id)/approve" @{reviewedDigest=$taskPlan.digest} 428 | Out-Null
$taskApproved=Send-Temporal Post "$taskPlanRoute/$($taskPlan.id)/approve" @{reviewedDigest=$taskPlan.digest} -Version 1
if($taskApproved.state -ne 'Approved' -or $taskApproved.version -ne 2){throw 'Reviewed decision was not recorded'}
Send-Temporal Post "$taskPlanRoute/$($taskPlan.id)/reject" @{reviewedDigest=$taskPlan.digest;reason='Stale command'} 412 -Version 1 | Out-Null
Send-Temporal Get "$taskPlanRoute/$($taskPlan.id)" -Tenant $taskForeignTenant -Expected 404 | Out-Null
$taskDefinition.budgetAmount=3000
Send-Temporal Put "/api/v1/projects/$taskProject/definition" $taskDefinition -Version 2 | Out-Null
$taskRetainedPlan=Send-Temporal Post $taskPlanRoute $taskPlanRequest 201
if($taskRetainedPlan.digest -ne $taskPlan.digest){throw 'Original plan proposal replay changed'}
$taskDelayRequest=$taskPlanScenarioRequest.Clone();$taskDelayRequest.id=[Guid]::NewGuid();$taskDelayRequest.expectedProjectVersion=3
$taskDelayRequest.assumptions=@{unavailable=@(@{resourceId=$taskMachine;period=@{start=$taskStart.ToString('o');end=$taskStart.AddHours(2).ToString('o')}});additionalStock=@{};additionalLicenseSeats=@{}}
$taskDelayScenario=Send-Temporal Post $taskRoute $taskDelayRequest 201
$taskChangedPlan=Send-Temporal Post $taskPlanRoute @{id=[Guid]::NewGuid();scenarioId=$taskDelayScenario.id;expectedProjectVersion=3;previousPlanId=$taskPlan.id} 201
if($taskChangedPlan.state -ne 'AwaitingApproval' -or $taskChangedPlan.evidence.diff.finishDeltaMinutes -ne 60 -or $taskChangedPlan.evidence.diff.costDelta -ne 0){throw 'New version lost the reviewed schedule diff'}
$taskHistory=Send-Temporal Get "$taskPlanRoute/$($taskPlan.id)"
if(@($taskHistory.decisions).Count -ne 2){throw 'Approval history is incomplete'}
$taskPools=Send-Temporal Get "/api/v1/licenses/$taskProduct"
$taskBookings=Send-Temporal Get "/api/v1/projects/allocations?ids=$taskProject"
if($taskPools.allocatedSeats -ne 0 -or @($taskBookings.$taskProject).Count -ne 0){throw 'Review unexpectedly acquired real capacity'}
Write-Output 'Reviewed execution evidence: exact digest/ETag, retained original proposal and decisions, +60-minute alternative awaiting fresh review; approval has not acquired capacity or started tasks.'
