param([string]$BaseUri='http://localhost:5121')
$ErrorActionPreference='Stop'
# Development demonstration: authorized operator reports task facts; this does not simulate an external provider.
. "$PSScriptRoot/execution-activation-demo.ps1" -BaseUri $BaseUri
$taskNow=[DateTimeOffset]::UtcNow
$taskRunStart=[DateTimeOffset]::new([long]([Math]::Floor($taskNow.UtcTicks / [decimal]10) * 10),[TimeSpan]::Zero).AddSeconds(10)
$taskDefinition.horizon=@{start=$taskRunStart.ToString('o');end=$taskRunStart.AddMinutes(4).ToString('o')}
$taskDefinition.sites[0].openWindows=@(@{start=$taskRunStart.ToString('o');end=$taskRunStart.AddMinutes(4).ToString('o')})
foreach($taskMilestone in $taskDefinition.milestones){$taskMilestone.dueAt=$taskRunStart.AddMinutes(4).ToString('o')}
foreach($taskItem in $taskDefinition.tasks){$taskItem.durationMinutes=1;$taskItem.notBefore=$taskRunStart.ToString('o');$taskItem.dueAt=$taskRunStart.AddMinutes(4).ToString('o');$taskItem.licenses=@(@{product=$taskProduct;seats=1})}
$taskProjectState=Send-Temporal Get "/api/v1/projects/$taskProject"
$taskProjectState=Send-Temporal Put "/api/v1/projects/$taskProject/definition" $taskDefinition -Version $taskProjectState.version
$taskRunRequest=$taskActivationScenarioRequest.Clone();$taskRunRequest.id=[Guid]::NewGuid();$taskRunRequest.expectedProjectVersion=$taskProjectState.version;$taskRunRequest.gridMinutes=1
$taskRunScenario=Send-Temporal Post $taskRoute $taskRunRequest 201
$taskActivationPlan=Send-Temporal Post $taskPlanRoute @{id=[Guid]::NewGuid();scenarioId=$taskRunScenario.id;expectedProjectVersion=$taskProjectState.version} 201
$taskActivationPlan=Send-Temporal Post "$taskPlanRoute/$($taskActivationPlan.id)/approve" @{reviewedDigest=$taskActivationPlan.digest} -Version 1
$taskActivationRoute="$taskPlanRoute/$($taskActivationPlan.id)/activation"
Send-Temporal Post $taskActivationRoute @{reviewedDigest=$taskActivationPlan.digest} 202 -Version 2 | Out-Null
$taskActive=Wait-Activation Active
$taskTasksRoute="$taskPlanRoute/$($taskActivationPlan.id)/tasks"
$taskSchedule=@($taskRunScenario.evidence.result.tasks | Sort-Object { [DateTimeOffset]$_.period.start })
$taskFirst=$taskSchedule[0];$taskSecond=$taskSchedule[1]
$taskEarly=Send-Temporal Post "$taskTasksRoute/$($taskFirst.taskId)/start" @{commandId=[Guid]::NewGuid()} 422 -Version 1
if($taskEarly.rejectionCode -ne 'execution_task_start_window'){throw 'Early start must retain a negative decision'}
function Wait-TaskWindow([DateTimeOffset]$Start){while([DateTimeOffset]::UtcNow -lt $Start){Start-Sleep -Milliseconds 100}}
function Task-CompletionTime{$taskValue=[DateTimeOffset]::UtcNow;[DateTimeOffset]::new([long]([Math]::Floor($taskValue.UtcTicks / [decimal]10) * 10),[TimeSpan]::Zero).ToString('o')}
Wait-TaskWindow $taskFirst.period.start
$taskStartCommand=@{commandId=[Guid]::NewGuid()}
$taskStarted=Send-Temporal Post "$taskTasksRoute/$($taskFirst.taskId)/start" $taskStartCommand -Version 1
$taskStartReplay=Send-Temporal Post "$taskTasksRoute/$($taskFirst.taskId)/start" $taskStartCommand -Version 1
if($taskStarted.recordedAt -ne $taskStartReplay.recordedAt){throw 'Start replay changed the original fact'}
Send-Temporal Post "$taskActivationRoute/cancel" @{reason='operator_cancelled'} 422 -Version $taskActive.activation.version | Out-Null
Wait-TaskWindow $taskSecond.period.start
$taskDependency=Send-Temporal Post "$taskTasksRoute/$($taskSecond.taskId)/start" @{commandId=[Guid]::NewGuid()} 422 -Version 1
if($taskDependency.rejectionCode -ne 'execution_task_dependencies_incomplete'){throw 'Incomplete dependency allowed work'}
$taskCompletionCommand=@{commandId=[Guid]::NewGuid();effectiveAt=(Task-CompletionTime)}
$taskFinished=Send-Temporal Post "$taskTasksRoute/$($taskFirst.taskId)/complete" $taskCompletionCommand -Version 2
if(!$taskFinished.outsideReservation){throw 'True late completion was not retained as an overrun'}
$taskFinishReplay=Send-Temporal Post "$taskTasksRoute/$($taskFirst.taskId)/complete" $taskCompletionCommand -Version 2
if($taskFinished.recordedAt -ne $taskFinishReplay.recordedAt){throw 'Completion replay changed original evidence'}
Send-Temporal Post "$taskTasksRoute/$($taskSecond.taskId)/start" @{commandId=[Guid]::NewGuid()} -Version 1 | Out-Null
Send-Temporal Post "$taskTasksRoute/$($taskSecond.taskId)/complete" @{commandId=[Guid]::NewGuid();effectiveAt=(Task-CompletionTime)} -Version 2 | Out-Null
$taskCompleted=Wait-Activation Completed
$taskHistory=Send-Temporal Get $taskTasksRoute
if(@($taskHistory | Where-Object {$_.task.state -ne 'Completed'}).Count -ne 0 -or @($taskHistory | ForEach-Object {$_.facts}).Count -ne 6){throw 'Task facts/terminal state incomplete'}
$taskLicense=Send-Temporal Get "/api/v1/licenses/$taskProduct"
if($taskLicense.temporalReservedPeak -ne 0){throw 'Completed work did not release license ownership'}
Write-Output 'Task execution evidence: dependent task starts, exact original command decisions, real ownership checks, actual late completion retained, unsafe cancellation rejected, all reported work completed and original capacity released. Financial closure and adaptive replanning remain later gates.'
