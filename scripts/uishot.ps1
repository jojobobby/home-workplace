# Render one canned UI scene of the office to a PNG without booting the services.
#   .\scripts\uishot.ps1 dialogue            -> shots\dialogue.png at 3x
#   .\scripts\uishot.ps1 tickets -Scale 4
#   .\scripts\uishot.ps1 all                 -> every scene
# Scenes: office, dialogue, overlay-employees, overlay-tasks, textentry, confirm, hiring, hiring-brains, tickets, desk,
#         menu, menu-multiplayer, workplaces, new-workplace, settings-video, settings-controls, pause
param(
    [Parameter(Mandatory = $true)][string]$Scene,
    [int]$Scale = 3,
    [string]$OutDir = (Join-Path $PSScriptRoot "..\shots")
)
$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "..\apps\office\HomeWorkplace.Office"
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force $OutDir | Out-Null

$scenes = if ($Scene -eq "all") { @("office", "dialogue", "overlay-employees", "overlay-tasks", "textentry", "confirm", "hiring", "hiring-brains", "tickets", "desk", "menu", "menu-multiplayer", "workplaces", "new-workplace", "settings-video", "settings-controls", "pause") } else { @($Scene) }
dotnet build $project -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "build failed" }
foreach ($s in $scenes) {
    $png = Join-Path $OutDir "$s.png"
    dotnet run --project $project --no-build -- --scale $Scale --ui-shot $s $png | Out-Null
    if (Test-Path $png) { Write-Output $png } else { Write-Warning "no shot for $s" }
}
