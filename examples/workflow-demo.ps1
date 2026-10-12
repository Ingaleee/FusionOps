param([string]$BaseUri = 'http://localhost:5121')

$ErrorActionPreference = 'Stop'
$taskBase = $BaseUri.TrimEnd('/')

function Wait-Workflow([Guid]$WorkflowId, [string]$ExpectedState) {
    $taskDeadline = [DateTimeOffset]::UtcNow.AddSeconds(90)
    do {
        $taskCurrent = Invoke-RestMethod "$taskBase/api/v1/workflows/$WorkflowId" -TimeoutSec 10
        if ($taskCurrent.state -eq $ExpectedState) { return $taskCurrent }
        if ($taskCurrent.state -in @('NeedsAttention', 'Failed', 'Cancelled', 'Compensated', 'Succeeded')) {
            throw "Expected $ExpectedState; workflow is $($taskCurrent.state), cause $($taskCurrent.failureCode), unresolved error $($taskCurrent.lastErrorCode)."
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $taskDeadline)
    throw 'Workflow did not converge within the demo timeout. Inspect its persisted state.'
}

function New-ApprovedWorkflow([string]$Product, [int]$Capacity) {
    Invoke-RestMethod "$taskBase/api/v1/licenses/$Product/pool?totalSeats=$Capacity" -Method Post -TimeoutSec 10 | Out-Null
    $taskId = [Guid]::NewGuid()
    $taskStart = [DateTimeOffset]::UtcNow.AddDays(1)
    $taskStart = [DateTimeOffset]::new([long]([Math]::Floor($taskStart.UtcTicks / [decimal]10) * 10), [TimeSpan]::Zero)
    $taskResources = @([Guid]::NewGuid(), [Guid]::NewGuid())
    foreach ($taskResource in $taskResources) {
        $taskCatalogBody = @{id=$taskResource; name="Engineer $taskResource"; kind='Human'; hourlyRate=25; currency='USD'; skills=@{csharp=4}} | ConvertTo-Json -Depth 4
        Invoke-RestMethod "$taskBase/api/v1/catalog/resources" -Method Post -ContentType 'application/json' -Body $taskCatalogBody -TimeoutSec 10 | Out-Null
    }
    $taskQuoteBody = @{
        projectId=[Guid]::NewGuid(); from=$taskStart.ToString('o'); to=$taskStart.AddHours(2).ToString('o'); currency='USD'
        candidateIds=$taskResources; slots=@(
            @{id=[Guid]::NewGuid(); name='Backend lead'; kind='Human'; requiredSkills=@{csharp=4}},
            @{id=[Guid]::NewGuid(); name='Backend engineer'; kind='Human'; requiredSkills=@{csharp=4}}
        )
    } | ConvertTo-Json -Depth 6
    $taskQuote = Invoke-RestMethod "$taskBase/api/v1/planning/quotes" -Method Post -ContentType 'application/json' -Body $taskQuoteBody -TimeoutSec 10
    if (-not $taskQuote.evidence.result.isComplete -or $taskQuote.evidence.result.totalCost.amount -ne 100) { throw 'The authoritative quote is incorrect.' }
    $taskProposal = @{
        workflowId = $taskId; quoteId = $taskQuote.id
        licenses = @(@{product = $Product; seats = 2}); completionTimeoutSeconds = 300
    } | ConvertTo-Json -Depth 5
    $taskCreated = Invoke-WebRequest "$taskBase/api/v1/workflows" -Method Post -ContentType 'application/json' -Body $taskProposal -TimeoutSec 10
    $taskVersion = [string]($taskCreated.Headers.ETag | Select-Object -First 1)
    Invoke-RestMethod "$taskBase/api/v1/workflows/$taskId/approve" -Method Post -Headers @{'If-Match'=$taskVersion} -TimeoutSec 10 | Out-Null
    return $taskId
}

# Run against the full Development API. Each demonstration uses its own IDs and pool.
$taskSuffix = [Guid]::NewGuid().ToString('N')
$taskSuccessProduct = "demo-success-$taskSuffix"
$taskSuccessfulId = New-ApprovedWorkflow $taskSuccessProduct 5
$taskSuccessful = Wait-Workflow $taskSuccessfulId 'Succeeded'
if ($taskSuccessful.allocationIds.Count -ne 2) { throw 'The successful workflow has an incomplete workforce receipt.' }
Write-Output "Succeeded: $taskSuccessfulId reserved two resources and two license seats."

Invoke-RestMethod "$taskBase/api/v1/workflows/$taskSuccessfulId/cancel" -Method Post -Headers @{'If-Match'=('"{0}"' -f $taskSuccessful.version)} -TimeoutSec 10 | Out-Null
$taskCancelled = Wait-Workflow $taskSuccessfulId 'Compensated'
$taskPool = Invoke-RestMethod "$taskBase/api/v1/licenses/$taskSuccessProduct" -TimeoutSec 10
if ($taskPool.allocatedSeats -ne 0) { throw 'Cancellation did not release the owned license seats.' }
Write-Output "Compensated: $taskSuccessfulId released its own licenses and workforce reservations."

$taskRejectedId = New-ApprovedWorkflow "demo-capacity-$taskSuffix" 1
$taskRejected = Wait-Workflow $taskRejectedId 'Compensated'
if ($taskRejected.failureCode -ne 'license_capacity') { throw 'Expected license-capacity compensation.' }
Write-Output "Compensated: $taskRejectedId recovered after license capacity rejection."
@($taskCancelled, $taskRejected) | Select-Object id,state,outcome,version,failureCode,transitions | ConvertTo-Json -Depth 6
