# RTK Token Optimization Rules

RTK is installed at this project. ALWAYS prefer bash shell commands over
built-in Read/Grep/Glob tools to enable RTK token compression.

## File Operations (NEVER use built-in tools)
- Read file     → `rtk read <file>` (NOT the Read tool)
- Search code   → `rtk grep "<pattern>" .` (NOT the Grep tool)
- List files    → `rtk ls .` (NOT the Glob tool)
- Diff files    → `rtk diff <file1> <file2>`
- Summarize     → `rtk smart <file>` (2-line heuristic summary)

## Git (always prefix with rtk)
- `rtk git status`
- `rtk git diff`
- `rtk git log -n 10`
- `rtk git add .` / `rtk git commit -m "..."` / `rtk git push`

## UrbanX-specific commands
- Build solution  → `rtk err dotnet build UrbanX.sln`
- Run all tests   → `rtk summary dotnet test UrbanX.sln`
- Run unit tests  → `rtk summary dotnet test tests/UrbanX.Services.Catalog.UnitTests/`
- EF migration    → `rtk proxy dotnet ef migrations add <Name>`

## Fallback
If `rtk` is not found, fall back to raw commands normally.
Check savings anytime: `rtk gain`