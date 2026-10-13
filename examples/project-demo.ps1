param([string]$BaseUri='http://localhost:5121')
$ErrorActionPreference='Stop'
$taskBase=$BaseUri.TrimEnd('/')
$taskTenant=[Guid]::NewGuid()
$taskOtherTenant=[Guid]::NewGuid()
$taskProject=[Guid]::NewGuid()
$taskSite=[Guid]::NewGuid()
$taskDesign=[Guid]::NewGuid()
$taskInstall=[Guid]::NewGuid()
$taskStart=[DateTimeOffset]::new([DateTime]::UtcNow.Date.AddDays(1).AddHours(8),[TimeSpan]::Zero)
$taskDefinition=@{
    name='Multi-site installation';horizon=@{start=$taskStart.ToString('o');end=$taskStart.AddDays(3).ToString('o')};budgetAmount=5000;currency='USD'
    sites=@(@{id=$taskSite;name='Customer site';openWindows=@(@{start=$taskStart.ToString('o');end=$taskStart.AddDays(3).ToString('o')})})
    tasks=@(
        @{id=$taskDesign;name='Design';siteId=$taskSite;durationMinutes=60;notBefore=$taskStart.ToString('o');dueAt=$taskStart.AddDays(3).ToString('o');dependencies=@();resources=@();materials=@();licenses=@()},
        @{id=$taskInstall;name='Install';siteId=$taskSite;durationMinutes=120;notBefore=$taskStart.ToString('o');dueAt=$taskStart.AddDays(3).ToString('o');dependencies=@($taskDesign);resources=@();materials=@(@{sku='connector';quantity=2});licenses=@()}
    )
    milestones=@(@{id=[Guid]::NewGuid();name='Handover';dueAt=$taskStart.AddDays(3).ToString('o');requiredTasks=@($taskInstall)})
}
function Send-Project([string]$Method,[string]$Path,$Body=$null,[Nullable[long]]$Version=$null,[Guid]$Tenant=$taskTenant,[int]$Expected=200){
    $taskHeaders=@{'X-Tenant-Id'=$Tenant.ToString('D')}
    if($null -ne $Version){$taskHeaders['If-Match']='"{0}"' -f $Version}
    $taskParameters=@{Uri="$taskBase$Path";Method=$Method;Headers=$taskHeaders;TimeoutSec=15;SkipHttpErrorCheck=$true}
    if($null -ne $Body){$taskParameters.ContentType='application/json';$taskParameters.Body=$Body | ConvertTo-Json -Depth 16}
    $taskResponse=Invoke-WebRequest @taskParameters
    if([int]$taskResponse.StatusCode -ne $Expected){throw "Expected $Expected, got $($taskResponse.StatusCode): $($taskResponse.Content)"}
    if($taskResponse.Content){$taskResponse.Content | ConvertFrom-Json}
}
$taskCreated=Send-Project Post '/api/v1/projects' @{id=$taskProject;definition=$taskDefinition} -Expected 201
if($taskCreated.version -ne 1 -or $taskCreated.definitionVersion -ne 1){throw 'Invalid initial project version'}
Send-Project Post '/api/v1/projects' @{id=$taskProject;definition=$taskDefinition} -Expected 201 | Out-Null
$taskMembership=Send-Project Put "/api/v1/projects/$taskProject/members" @{subject='reviewer';role='Approver'} 1
if($taskMembership.version -ne 2 -or $taskMembership.definitionVersion -ne 1){throw 'Membership must have its own aggregate epoch'}
$taskDefinition.budgetAmount=6000
$taskRevised=Send-Project Put "/api/v1/projects/$taskProject/definition" $taskDefinition 2
if($taskRevised.version -ne 3 -or $taskRevised.definitionVersion -ne 2){throw 'Invalid definition revision'}
Send-Project Put "/api/v1/projects/$taskProject/definition" $taskDefinition 2 -Expected 412 | Out-Null
$taskCycle=$taskDefinition | ConvertTo-Json -Depth 16 | ConvertFrom-Json -AsHashtable
$taskCycle.tasks[0].dependencies=@($taskInstall)
Send-Project Put "/api/v1/projects/$taskProject/definition" $taskCycle 3 -Expected 422 | Out-Null
Send-Project Get "/api/v1/projects/$taskProject" -Tenant $taskOtherTenant -Expected 404 | Out-Null
Send-Project Delete "/api/v1/projects/$taskProject/members/system" -Version 3 -Expected 422 | Out-Null
$taskHistory=@(Send-Project Get "/api/v1/projects/$taskProject/revisions")
if($taskHistory.Count -ne 2 -or $taskHistory[0].definition.budgetAmount -ne 6000 -or $taskHistory[1].definition.budgetAmount -ne 5000){throw 'Historical project evidence was lost'}
$taskFinal=Send-Project Get "/api/v1/projects/$taskProject"
if($taskFinal.version -ne 3){throw 'Rejected changes altered the project'}
Write-Output "Project ${taskProject}: idempotent creation, independent membership/definition versions, retained history, stale ETag/cycle/foreign tenant/owner removal rejected."
Write-Output "Tenant $taskTenant; current version $($taskFinal.version); definition version $($taskFinal.definitionVersion); digest $($taskFinal.definitionDigest)."
