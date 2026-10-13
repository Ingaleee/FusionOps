param([string]$BaseUri = 'http://localhost:5121')
$ErrorActionPreference = 'Stop'
$taskBase = $BaseUri.TrimEnd('/')
$taskTenantA = [Guid]::NewGuid()
$taskTenantB = [Guid]::NewGuid()
$taskProduct = "tenant-demo-$([Guid]::NewGuid().ToString('N'))"
$taskFrom = [DateTimeOffset]::UtcNow.AddDays(1)
$taskFrom = [DateTimeOffset]::new([long]([Math]::Floor($taskFrom.UtcTicks / [decimal]10) * 10), [TimeSpan]::Zero)

function Send-Tenant([Guid]$Tenant, [string]$Method, [string]$Path, $Body = $null, $AdditionalHeaders = @{}) {
    $taskHeaders = @{'X-Tenant-Id'=$Tenant.ToString('D')}
    foreach ($taskKey in $AdditionalHeaders.Keys) { $taskHeaders[$taskKey]=$AdditionalHeaders[$taskKey] }
    $taskParameters = @{Uri="$taskBase$Path"; Method=$Method; Headers=$taskHeaders; TimeoutSec=10}
    if ($null -ne $Body) { $taskParameters.ContentType='application/json'; $taskParameters.Body=$Body | ConvertTo-Json -Depth 8 }
    Invoke-RestMethod @taskParameters
}
function Assert-ForeignStatus([Guid]$Tenant, [string]$Method, [string]$Path, [int]$Expected, $Body=$null) {
    $taskParameters = @{Uri="$taskBase$Path"; Method=$Method; Headers=@{'X-Tenant-Id'=$Tenant.ToString('D')}; TimeoutSec=10; SkipHttpErrorCheck=$true}
    if ($null -ne $Body) { $taskParameters.ContentType='application/json'; $taskParameters.Body=$Body | ConvertTo-Json -Depth 8 }
    $taskResponse=Invoke-WebRequest @taskParameters
    if ([int]$taskResponse.StatusCode -ne $Expected) { throw "Expected $Expected for foreign access; got $($taskResponse.StatusCode)." }
}
function Wait-TenantWorkflow([Guid]$Tenant, [Guid]$Workflow, [string]$Expected) {
    $taskDeadline=[DateTimeOffset]::UtcNow.AddSeconds(60)
    do {
        $taskState=Send-Tenant $Tenant Get "/api/v1/workflows/$Workflow"
        if ($taskState.state -eq $Expected) { return $taskState }
        if ($taskState.state -in @('Failed','NeedsAttention','Cancelled','Compensated','Succeeded')) { throw "Unexpected workflow state $($taskState.state)." }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $taskDeadline)
    throw 'Tenant workflow did not converge.'
}
$taskRecords=@()
foreach ($taskTenant in @($taskTenantA,$taskTenantB)) {
    $taskRate=if ($taskTenant -eq $taskTenantA) {25} else {99}
    $taskCapacity=if ($taskTenant -eq $taskTenantA) {5} else {9}
    $taskResource=[Guid]::NewGuid()
    Send-Tenant $taskTenant Post '/api/v1/catalog/resources' @{id=$taskResource;name='Tenant engineer';kind='Human';hourlyRate=$taskRate;currency='USD';skills=@{csharp=4}} | Out-Null
    Send-Tenant $taskTenant Post "/api/v1/licenses/$taskProduct/pool?totalSeats=$taskCapacity" | Out-Null
    $taskQuote=Send-Tenant $taskTenant Post '/api/v1/planning/quotes' @{
        projectId=[Guid]::NewGuid();from=$taskFrom.ToString('o');to=$taskFrom.AddHours(2).ToString('o');currency='USD';candidateIds=@($taskResource)
        slots=@(@{id=[Guid]::NewGuid();name='Lead';kind='Human';requiredSkills=@{csharp=4}})
    }
    if ($taskQuote.evidence.result.totalCost.amount -ne $taskRate*2) { throw 'Incorrect tenant quote price.' }
    $taskWorkflow=[Guid]::NewGuid()
    Send-Tenant $taskTenant Post '/api/v1/workflows' @{workflowId=$taskWorkflow;quoteId=$taskQuote.id;licenses=@(@{product=$taskProduct;seats=2})} | Out-Null
    Send-Tenant $taskTenant Post "/api/v1/workflows/$taskWorkflow/approve" $null @{'If-Match'='"1"'} | Out-Null
    $taskSuccess=Wait-TenantWorkflow $taskTenant $taskWorkflow 'Succeeded'
    $taskRecords+=@{tenant=$taskTenant;resource=$taskResource;quote=$taskQuote.id;workflow=$taskWorkflow;version=$taskSuccess.version;cost=$taskQuote.evidence.result.totalCost.amount;capacity=$taskCapacity}
}
$taskOwnedA=$taskRecords[0]
Assert-ForeignStatus $taskTenantB Get "/api/v1/catalog/resources/$($taskOwnedA.resource)" 404
Assert-ForeignStatus $taskTenantB Get "/api/v1/planning/quotes/$($taskOwnedA.quote)" 404
Assert-ForeignStatus $taskTenantB Get "/api/v1/workflows/$($taskOwnedA.workflow)" 404
Assert-ForeignStatus $taskTenantB Post '/api/v1/allocate' 409 @{projectId=[Guid]::NewGuid();resourceIds=@($taskOwnedA.resource);periodFrom=$taskFrom.ToString('o');periodTo=$taskFrom.AddHours(2).ToString('o')}
foreach ($taskRecord in $taskRecords) {
    $taskPool=Send-Tenant $taskRecord.tenant Get "/api/v1/licenses/$taskProduct"
    if ($taskPool.totalSeats -ne $taskRecord.capacity -or $taskPool.allocatedSeats -ne 2) { throw 'License ownership leaked between tenants.' }
    Send-Tenant $taskRecord.tenant Post "/api/v1/workflows/$($taskRecord.workflow)/cancel" $null @{'If-Match'=('"{0}"' -f $taskRecord.version)} | Out-Null
    Wait-TenantWorkflow $taskRecord.tenant $taskRecord.workflow 'Compensated' | Out-Null
    if ((Send-Tenant $taskRecord.tenant Get "/api/v1/licenses/$taskProduct").allocatedSeats -ne 0) { throw 'Compensation failed.' }
}
Write-Output 'Two tenants: independent USD 50 / USD 198 quotes, same product with separate capacity, foreign access rejected, both reservations compensated.'
$taskRecords | Select-Object tenant,resource,quote,workflow,cost,capacity | ConvertTo-Json -Depth 4
