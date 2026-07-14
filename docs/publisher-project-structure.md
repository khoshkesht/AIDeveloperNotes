# Publisher Project Structure

The `Publisher` folder is split into two app projects plus shared runtime content.

## Projects

- `Publisher/Publisher.WinForms/Publisher.WinForms.csproj`
  - Windows Forms UI app.
  - Reads its own source config from `Publisher/Publisher.WinForms/config.json`.
  - WinForms publish profiles may create a local publish output, but this is not the runtime source for `Publisher.Job`.

- `Publisher/Publisher.Job/Publisher.Job.csproj`
  - Console/worker app for scheduled and manual jobs.
  - Contains:
    - daily posts job
    - RSS/Groq article job
    - Telegram data provider job
  - Reads source config from `Publisher/Publisher.Job/config.json`.
  - During build/publish, that config is copied beside `Publisher.Job.dll`.

## Shared Content

- `Publisher/Posts/`
  - Source text posts for `DailyPostsJob`.
- `Publisher/Pics/`
  - Images for daily posts and downloaded news images.
- `Publisher/Promp/`
  - Prompt files copied into job output.
- `Publisher/posted.txt`
  - Seed/source copy for posted state. Runtime state is read beside the executing `Publisher.Job.dll`.

## Removed Local Publish Folder

`Publisher/publish/` was a generated publish output and is no longer part of the source tree. It is ignored by git. Do not use it as a config source.

For `Publisher.Job`, the active config is whichever `config.json` sits beside the running `Publisher.Job.dll`:

- Local source config: `Publisher/Publisher.Job/config.json`
- Local debug runtime copy: `Publisher/Publisher.Job/bin/Debug/net10.0/config.json`
- Server runtime config: `/opt/publisher-job/config.json`

## Build Commands

```powershell
dotnet build Publisher/Publisher.WinForms/Publisher.WinForms.csproj
dotnet build Publisher/Publisher.Job/Publisher.Job.csproj
dotnet build Publisher/Publisher.slnx
```

## Linux Publish Command

```powershell
dotnet publish Publisher/Publisher.Job/Publisher.Job.csproj -c Release -o /opt/publisher-job
```
