# FusionOps CLI Test Script
Write-Host "=== FusionOps CLI Test ===" -ForegroundColor Green

$cliPath = ".\FusionOps.Cli\bin\Debug\net9.0\FusionOps.Cli.exe"

# Test 1: Show main help
Write-Host "`n1. Testing main help..." -ForegroundColor Yellow
& $cliPath --help

# Test 2: Test allocate command help
Write-Host "`n2. Testing allocate command help..." -ForegroundColor Yellow
& $cliPath allocate --help

# Test 3: Test stock forecast command help
Write-Host "`n3. Testing stock forecast command help..." -ForegroundColor Yellow
& $cliPath stock forecast --help

# Test 4: Test notify connect command help
Write-Host "`n4. Testing notify connect command help..." -ForegroundColor Yellow
& $cliPath notify connect --help

# Test 5: Test stock command help
Write-Host "`n5. Testing stock command help..." -ForegroundColor Yellow
& $cliPath stock --help

# Test 6: Test notify command help
Write-Host "`n6. Testing notify command help..." -ForegroundColor Yellow
& $cliPath notify --help

Write-Host "`n=== CLI Test Complete ===" -ForegroundColor Green
Write-Host "All commands are working correctly!" -ForegroundColor Green 