param([string]$BaseUri='http://localhost:5121')
$ErrorActionPreference='Stop'
$taskBase=$BaseUri.TrimEnd('/')
$taskTenant=[Guid]::NewGuid();$taskForeignTenant=[Guid]::NewGuid()
$taskProject=[Guid]::NewGuid();$taskSite=[Guid]::NewGuid();$taskDesign=[Guid]::NewGuid();$taskInstall=[Guid]::NewGuid()
$taskEngineer=[Guid]::NewGuid();$taskMachine=[Guid]::NewGuid();$taskProduct="temporal-$([Guid]::NewGuid().ToString('N'))"
$taskStart=[DateTimeOffset]::new([DateTime]::UtcNow.Date.AddDays(1).AddHours(8),[TimeSpan]::Zero)
function Send-Temporal([string]$Method,[string]$Path,$Body=$null,[int]$Expected=200,[Guid]$Tenant=$taskTenant,[Nullable[long]]$Version=$null){
    $taskHeaders=@{'X-Tenant-Id'=$Tenant.ToString('D')}
    if($null -ne $Version){$taskHeaders['If-Match']='"{0}"' -f $Version}
    $taskParameters=@{Uri="$taskBase$Path";Method=$Method;Headers=$taskHeaders;TimeoutSec=40;SkipHttpErrorCheck=$true}
    if($null -ne $Body){$taskParameters.ContentType='application/json';$taskParameters.Body=$Body | ConvertTo-Json -Depth 24}
    $taskResponse=Invoke-WebRequest @taskParameters
    if([int]$taskResponse.StatusCode -ne $Expected){throw "Expected $Expected; got $($taskResponse.StatusCode): $($taskResponse.Content)"}
    if($taskResponse.Content){$taskResponse.Content | ConvertFrom-Json}
}
Send-Temporal Post '/api/v1/catalog/resources' @{id=$taskEngineer;name='Engineer';kind='Human';hourlyRate=25;currency='USD';skills=@{csharp=4}} 201 | Out-Null
Send-Temporal Post '/api/v1/catalog/resources' @{id=$taskMachine;name='Installation machine';kind='Equipment';hourlyRate=10;currency='USD';skills=@{};equipmentType='machine'} 201 | Out-Null
Send-Temporal Post "/api/v1/licenses/$taskProduct/pool?totalSeats=1" | Out-Null
$taskDefinition=@{
    name='Engineering installation';horizon=@{start=$taskStart.ToString('o');end=$taskStart.AddHours(4).ToString('o')};budgetAmount=1000;currency='USD'
    sites=@(@{id=$taskSite;name='Customer site';openWindows=@(@{start=$taskStart.ToString('o');end=$taskStart.AddHours(4).ToString('o')})})
    tasks=@(
        @{id=$taskDesign;name='Design';siteId=$taskSite;durationMinutes=60;notBefore=$taskStart.ToString('o');dueAt=$taskStart.AddHours(4).ToString('o');dependencies=@();
            resources=@(@{id=[Guid]::NewGuid();name='Designer';kind='Human';requiredSkills=@{csharp=4}});materials=@();licenses=@()},
        @{id=$taskInstall;name='Install';siteId=$taskSite;durationMinutes=60;notBefore=$taskStart.ToString('o');dueAt=$taskStart.AddHours(4).ToString('o');dependencies=@($taskDesign);
            resources=@(@{id=[Guid]::NewGuid();name='Installer';kind='Human';requiredSkills=@{csharp=4}},@{id=[Guid]::NewGuid();name='Machine';kind='Equipment';equipmentType='machine';requiredSkills=@{}});
            materials=@();licenses=@(@{product=$taskProduct;seats=1})}
    );milestones=@(@{id=[Guid]::NewGuid();name='Handover';dueAt=$taskStart.AddHours(4).ToString('o');requiredTasks=@($taskInstall)})
}
Send-Temporal Post '/api/v1/projects' @{id=$taskProject;definition=$taskDefinition} 201 | Out-Null
$taskRoute="/api/v1/projects/$taskProject/scenarios"
$taskRequest=@{id=[Guid]::NewGuid();expectedProjectVersion=1;candidateIds=@($taskEngineer,$taskMachine);gridMinutes=60;maxSearchNodes=100000;maxElapsedMilliseconds=30000}
$taskBaseline=Send-Temporal Post $taskRoute $taskRequest 201
if(!$taskBaseline.isComplete -or !$taskBaseline.searchComplete -or $taskBaseline.totalCost -ne 60 -or [DateTimeOffset]$taskBaseline.finishAt -ne $taskStart.AddHours(2)){throw 'Baseline schedule is incorrect'}
$taskRetry=Send-Temporal Post $taskRoute $taskRequest 201
if($taskRetry.digest -ne $taskBaseline.digest){throw 'Scenario retry changed its retained decision'}
$taskAlternativeRequest=$taskRequest.Clone();$taskAlternativeRequest.id=[Guid]::NewGuid()
$taskAlternativeRequest.assumptions=@{unavailable=@(@{resourceId=$taskMachine;period=@{start=$taskStart.ToString('o');end=$taskStart.AddHours(2).ToString('o')}});additionalStock=@{};additionalLicenseSeats=@{}}
$taskAlternative=Send-Temporal Post $taskRoute $taskAlternativeRequest 201
if(!$taskAlternative.isHypothetical -or !$taskAlternative.isComplete -or [DateTimeOffset]$taskAlternative.finishAt -ne $taskStart.AddHours(3)){throw 'Equipment delay was not reflected in scheduling'}
$taskCompare=Send-Temporal Get "$taskRoute/compare?left=$($taskBaseline.id)&right=$($taskAlternative.id)"
if($taskCompare.finishDeltaMinutes -ne 60 -or $taskCompare.costDelta -ne 0 -or @($taskCompare.changedTasks).Count -ne 1){throw 'Scenario comparison is incorrect'}
$taskDefinition.budgetAmount=2000
Send-Temporal Put "/api/v1/projects/$taskProject/definition" $taskDefinition -Version 1 | Out-Null
$taskReplay=Send-Temporal Post "$taskRoute/$($taskBaseline.id)/replay"
if(!$taskReplay.matchesOriginalDecision -or !$taskReplay.validation.isFeasible){throw 'Historical scenario replay lost original evidence'}
$taskStale=$taskRequest.Clone();$taskStale.id=[Guid]::NewGuid();Send-Temporal Post $taskRoute $taskStale 412 | Out-Null
Send-Temporal Get "$taskRoute/$($taskBaseline.id)" -Tenant $taskForeignTenant -Expected 404 | Out-Null
$taskPools=Send-Temporal Get "/api/v1/licenses/$taskProduct"
$taskBookings=Send-Temporal Get "/api/v1/projects/allocations?ids=$taskProject"
if($taskPools.allocatedSeats -ne 0 -or @($taskBookings.$taskProject).Count -ne 0){throw 'Simulation reserved real capacity'}
Write-Output 'Temporal planning: USD 60, dependent engineer/machine bundle, two-hour baseline, three-hour equipment-delay alternative; stored replay unchanged after project revision, foreign read/stale version denied; no reservations.'
Write-Output "Project $taskProject; tenant $taskTenant; baseline $($taskBaseline.id); alternative $($taskAlternative.id); changed tasks $(@($taskCompare.changedTasks).Count)."
