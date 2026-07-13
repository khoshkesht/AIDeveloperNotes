# Publisher Project Structure

The `Publisher` folder is split into separate app projects plus shared runtime content:

- `Publisher/Publisher.WinForms/Publisher.WinForms.csproj`
  - Windows Forms UI app.
  - Contains `Program.cs`, `MainForm*`, `Groq/`, and WinForms publish profiles.
  - The legacy `Groq/` source is kept with the WinForms project folder but excluded from the WinForms build because the UI does not reference it.
  - Reads its source config from `Publisher/Publisher.WinForms/config.json`, copied beside the WinForms executable.
- `Publisher/Publisher.Job/Publisher.Job.csproj`
  - Console/worker app for scheduled and manual jobs.
  - Contains the daily posts job, RSS article job, and Telegram data provider job.
  - Reads its source config from `Publisher/Publisher.Job/config.json`, copied beside `Publisher.Job.dll`.
- `Publisher/Posts/`, `Publisher/Pics/`, `Publisher/Promp/`, `Publisher/posted.txt`
  - Shared runtime content copied into app outputs by project files where needed.

Build commands:

```powershell
dotnet build Publisher/Publisher.WinForms/Publisher.WinForms.csproj
dotnet build Publisher/Publisher.Job/Publisher.Job.csproj
dotnet build Publisher/Publisher.slnx
```

Linux deployment for the job still uses:

```powershell
dotnet publish Publisher/Publisher.Job/Publisher.Job.csproj -c Release -o /opt/publisher-job
```
