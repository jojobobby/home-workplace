# Foreman acceptance — run from a CLEAN terminal (not inside a Claude Code session),
# because a spawned `claude` inherits the nested-session guard and is refused subscription
# access. This starts both services, then drives one real two-employee flow and prints the
# room brief at each step. Requires the `claude` and `codex` CLIs on PATH and logged in.
#
# Usage:  pwsh -File scripts/acceptance.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$ctx = "http://localhost:5171"
$foreman = "http://localhost:5172"

function Wait-Health($url) {
  for ($i = 0; $i -lt 30; $i++) {
    try { Invoke-RestMethod "$url/health" -TimeoutSec 2 | Out-Null; return } catch { Start-Sleep 1 }
  }
  throw "service at $url never became healthy"
}

function Poll-Task($id, $status, $seconds = 120) {
  $deadline = (Get-Date).AddSeconds($seconds)
  while ((Get-Date) -lt $deadline) {
    $t = Invoke-RestMethod "$foreman/tasks/$id"
    if ($t.status -eq $status) { return $t }
    Start-Sleep 2
  }
  throw "task $id did not reach $status in time"
}

Write-Host "Starting context-api and foreman..."
$p1 = Start-Process dotnet -PassThru -ArgumentList "run --project `"$root/services/context-api/src/HomeWorkplace.ContextApi`""
$p2 = Start-Process dotnet -PassThru -ArgumentList "run --project `"$root/services/foreman/src/HomeWorkplace.Foreman`""
try {
  Wait-Health $ctx
  Wait-Health $foreman

  Write-Host "Waking ada-coder and rex-reviewer..."
  Invoke-RestMethod "$foreman/employees/ada-coder/wake" -Method Post | Out-Null
  Invoke-RestMethod "$foreman/employees/rex-reviewer/wake" -Method Post | Out-Null

  Write-Host "Creating a task for ada-coder (she should hand off to rex for review)..."
  $body = @{ title = "Add a hello endpoint"; brief = "Write a tiny hello endpoint, then ask rex-reviewer to review it before finishing."; assignee = "ada-coder" } | ConvertTo-Json
  $task = Invoke-RestMethod "$foreman/tasks" -Method Post -Body $body -ContentType "application/json"
  Write-Host "  task id: $($task.id), room: $($task.room)"

  Write-Host "Waiting for the task to finish (watch the hand-off happen)..."
  $done = Poll-Task $task.id "Done" 300
  Write-Host "  final status: $($done.status), children: $($done.childIds -join ',')"

  Write-Host "`n--- room brief ---"
  Invoke-RestMethod "$ctx/rooms/$($task.room)/context?format=text"

  Write-Host "`nForcing a wrap-up on ada-coder (memory ledger)..."
  Invoke-RestMethod "$foreman/employees/ada-coder/reset" -Method Post | Out-Null
  $after = Invoke-RestMethod "$foreman/tasks/$($task.id)"
  Write-Host "  progress entries: $($after.progress.Count)"

  Write-Host "`nACCEPTANCE OK"
}
finally {
  Write-Host "Stopping services..."
  $p1, $p2 | ForEach-Object { if ($_ -and -not $_.HasExited) { $_.Kill() } }
}
