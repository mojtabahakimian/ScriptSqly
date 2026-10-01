# Database-free regression for caught SQL failures in the CLI.
# Run after building ScriptSqly.Runner: pwsh -File tests/ReportMigrationResult.Tests.ps1
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
$runnerRoot = Split-Path -Parent $PSScriptRoot
$runnerDll = Join-Path $runnerRoot "bin/$Configuration/net8.0/ScriptSqly.Runner.dll"
if (-not (Test-Path -LiteralPath $runnerDll)) {
    throw 'Build ScriptSqly.Runner before running this regression test.'
}

$runnerSource = [IO.File]::ReadAllText((Join-Path $runnerRoot 'Program.cs'))
if ($runnerSource -notmatch 'var result = ScriptSqly\.Migrations\.ScriptSqly\.RunTracked\(' -or
    $runnerSource -notmatch 'return ReportMigrationResult\(result\);') {
    throw 'The CLI must report the tracked migration result.'
}

$coreAssembly = [Reflection.Assembly]::LoadFrom((Join-Path (Split-Path $runnerDll) 'ScriptSqly.Core.dll'))
$runnerAssembly = [Reflection.Assembly]::LoadFrom($runnerDll)
$resultType = $coreAssembly.GetType('ScriptSqly.Migrations.MigrationExecutionResult', $true)
$failureType = $coreAssembly.GetType('ScriptSqly.Migrations.MigrationFailure', $true)
$reportMethod = $runnerAssembly.GetType('ScriptSqly.Runner.Program', $true).GetMethod(
    'ReportMigrationResult', [Reflection.BindingFlags]'NonPublic,Static')

function Invoke-ResultReport($Result) {
    $originalOutput = [Console]::Out
    $capture = [IO.StringWriter]::new()
    try {
        [Console]::SetOut($capture)
        $exitCode = $reportMethod.Invoke($null, [object[]]@($Result))
        return [pscustomobject]@{ ExitCode = $exitCode; Output = $capture.ToString() }
    }
    finally {
        [Console]::SetOut($originalOutput)
        $capture.Dispose()
    }
}

$success = [Activator]::CreateInstance($resultType)
$resultType.GetProperty('Executed').SetValue($success, 4)
$resultType.GetProperty('Skipped').SetValue($success, 2)
$successReport = Invoke-ResultReport $success
if ($successReport.ExitCode -ne 0 -or $successReport.Output -notmatch 'completed successfully' -or
    $successReport.Output -notmatch 'Executed: 4; skipped: 2; failed: 0') {
    throw 'A successful migration must report its counters and exit zero.'
}

$failure = [Activator]::CreateInstance($resultType)
$failure.Errors.Add([Activator]::CreateInstance($failureType,
    [object[]]@('ALTER TABLE dbo.Example ADD Id int', 2705, 'Column already exists')))
$failureReport = Invoke-ResultReport $failure
if ($failureReport.ExitCode -ne 2 -or $failureReport.Output -match 'completed successfully' -or
    $failureReport.Output -notmatch 'failed: 1' -or
    $failureReport.Output -notmatch 'ALTER TABLE dbo.Example ADD Id int' -or
    $failureReport.Output -notmatch 'SQL error 2705: Column already exists') {
    throw 'A caught SQL failure must report its command and error and exit nonzero.'
}

$helpOutput = (& dotnet $runnerDll --help) -join "`n"
if ($LASTEXITCODE -ne 0 -or $helpOutput -notmatch 'Usage:') {
    throw 'The help option must remain available and exit zero.'
}

$previewOutput = (& dotnet $runnerDll --conn='Server=localhost;Database=SafirTestPreview;User Id=preview;Password=FakePreviewSecret' -custom --type=2 --preview) -join "`n"
if ($LASTEXITCODE -ne 0 -or $previewOutput -notmatch 'Validating parameters only' -or
    $previewOutput -notmatch 'isCustomCall: True' -or $previewOutput -notmatch 'type: 2' -or
    $previewOutput -match 'FakePreviewSecret') {
    throw 'Preview options must work without connecting and keep the password masked.'
}

# Parsing fails before a connection is created, so this cannot execute SQL.
$invalidOutput = (& dotnet $runnerDll --conn 'invalid-connection-string') -join "`n"
if ($LASTEXITCODE -ne 2 -or $invalidOutput -notmatch 'Database migration failed' -or
    $invalidOutput -match 'completed successfully') {
    throw 'An unhandled migration exception must also exit nonzero.'
}

Write-Output 'PASS: CLI success, caught SQL failure, exception, help and preview (no database connection).'
exit 0
