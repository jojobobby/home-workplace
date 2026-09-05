# Hiring templates

Each folder here is a **role** the hiring stand offers. Hiring one creates
`employees/<name>-<role>/` from it, with the brain (vendor + model) you picked and the name
you typed. The company starts with nobody hired; `employees/` is empty until you hire.

```
hiring/engineer/
├── template.json   # role, description, tools, schedule, typical tokens per run, runs per day
├── skills.md       # how this role works (becomes the employee's skills.md)
└── life.md         # who they are, in the second person (becomes life.md)
```

`template.json`:

```json
{
  "id": "engineer",
  "role": "Software engineer",
  "description": "Builds features test-first, small commits, posts progress to the room.",
  "effort": "low",
  "claudeAllowedTools": ["Bash(dotnet *)", "Read", "Edit", "Write", "Glob", "Grep"],
  "codexSandbox": "workspace-write",
  "schedule": { "wake": "09:00", "sleep": "20:00" },
  "maxRunMinutes": 30,
  "typicalTokensPerRun": { "in": 60000, "out": 8000 },
  "runsPerDay": 6
}
```

`typicalTokensPerRun` × the model's list price (`Foreman:Pricing` in
`services/foreman/src/HomeWorkplace.Foreman/appsettings.json`) gives the approximate
cost per run the stand shows; × `runsPerDay` gives the per-day figure. On a subscription
those dollars are notional. The brains on offer are `Foreman:Brains`; edit both to match
what your subscriptions unlock.

Letting someone go moves their folder to `employees/.former/<id>-<timestamp>`.
