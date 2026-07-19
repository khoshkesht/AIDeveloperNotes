# Publisher.Job Linux Deploy And Runtime Guide

This document describes the current `Publisher.Job` worker. The worker uses Hangfire for scheduling and runs these jobs:

- `DailyPostsJob`
- RSS/Groq article job
- `TelegramDataProviderJob`

## Runtime Paths

- Source on server: `/opt/AIDeveloperNotes`
- Published runtime folder: `/opt/publisher-job`
- Worker project: `Publisher/Publisher.Job/Publisher.Job.csproj`
- Runtime executable: `/opt/publisher-job/Publisher.Job.dll`
- Runtime config: `/opt/publisher-job/config.json`

`Publisher/publish/` is generated output, not a source of truth. It is ignored by git.

## Prerequisites

```bash
git --version
dotnet --info
```

If needed:

```bash
sudo apt-get update
sudo apt-get install -y git dotnet-sdk-10.0
```

`Publisher/NuGet.Config` must include `nuget.org`, because `Publisher.Job` depends on Hangfire packages.

## Build And Publish

```bash
cd /opt/AIDeveloperNotes
dotnet restore Publisher/Publisher.Job/Publisher.Job.csproj
dotnet build Publisher/Publisher.Job/Publisher.Job.csproj -c Release
dotnet publish Publisher/Publisher.Job/Publisher.Job.csproj -c Release -o /opt/publisher-job
```

Check the output:

```bash
ls -la /opt/publisher-job
ls -la /opt/publisher-job/Publisher.Job.dll
ls -la /opt/publisher-job/config.json
```

## Config

Edit the runtime config on the server:

```bash
nano /opt/publisher-job/config.json
python3 -m json.tool /opt/publisher-job/config.json
```

Important scheduling settings:

```json
{
  "hangfire": {
    "workerCount": 1
  },
  "dailyJob": {
    "enabled": true,
    "cron": "0 6 * * *"
  },
  "groqArticleJob": {
    "enabled": true,
    "cron": "30 6 * * *"
  },
  "telegramDataProvider": {
    "enabled": true,
    "cron": "*/30 15-20 * * *",
    "sendDelayBetweenChannelsSeconds": 30
  },
  "groq": {
    "apiKeys": [
      "YOUR_GROQ_API_KEY_1",
      "YOUR_GROQ_API_KEY_2"
    ],
    "apiKeyEnvironmentVariable": "GROQ_API_KEY"
  }
}
```

Notes:

- `hangfire.workerCount` controls how many Hangfire workers run in the process.
- Every scheduled job is registered from its own Hangfire cron expression.
- `TelegramDataProviderJob` can be scheduled directly with ranges, for example `*/30 18-23 * * *`.
- Hangfire storage is currently in-memory. Recurring jobs are registered from config every time the worker starts. Hangfire queue state is not persisted across restarts.
- App-level state files such as `posted.txt`, `daily-job-state.json`, and lock files are still stored beside `Publisher.Job.dll`.
- Top-level `channels` no longer exists. Each job or source channel must have its own `chatId`.
- For `telegramDataProvider.channels[*].maxAgeMinutes`, `0` or a negative value disables the age filter.
- `groq.apiKeys` can contain multiple Groq keys. Each Groq request randomly picks one key.
- The old `groq.apiKey` setting still works as a fallback, but `groq.apiKeys` is preferred for multiple keys.
- Prefer environment/secret storage instead of storing real Groq keys in `config.json`.

## Timezone

Hangfire uses the local timezone configured on the server.

```bash
date
timedatectl
sudo timedatectl set-timezone Asia/Tehran
timedatectl
```

## Manual Commands

```bash
cd /opt/publisher-job
dotnet Publisher.Job.dll --status
dotnet Publisher.Job.dll --run-posts-once
dotnet Publisher.Job.dll --run-groq-once
dotnet Publisher.Job.dll --run-telegram-data-provider-once
```

`--run-once` is still a backward-compatible alias for `--run-posts-once`.

## Job Behavior

When started without arguments, `Publisher.Job` starts a long-running Hangfire worker and registers recurring jobs from `config.json`.

`DailyPostsJob`:

- Runs from `dailyJob.cron`.
- Uses `daily-job-state.json` and `daily-job.lock`.
- Reads and writes `posted.txt` beside the running `Publisher.Job.dll`.

RSS/Groq article job:

- Runs from `groqArticleJob.cron`.
- Reads configured RSS feeds and uses Groq only for summarization.
- Uses `groq-article-job-state.json` and `groq-article-job.lock`.

`TelegramDataProviderJob`:

- Runs from `telegramDataProvider.cron`.
- Reads latest public Telegram posts for each configured source channel.
- Summarizes with the configured prompt.
- Appends the source channel name without `@`.
- Sends each result to that source channel's own `chatId`.
- Waits `sendDelayBetweenChannelsSeconds` between successful channel sends.
- Uses `telegram-data-provider-job-state.json` and `telegram-data-provider-job.lock`.

## systemd Service

```bash
sudo nano /etc/systemd/system/publisher-job.service
```

```ini
[Unit]
Description=Publisher Telegram Worker
After=network-online.target
Wants=network-online.target

[Service]
WorkingDirectory=/opt/publisher-job
ExecStart=/usr/bin/dotnet /opt/publisher-job/Publisher.Job.dll
Restart=always
RestartSec=10
User=www-data
Environment=GROQ_API_KEY=YOUR_GROQ_API_KEY

[Install]
WantedBy=multi-user.target
```

```bash
sudo chown -R www-data:www-data /opt/publisher-job
sudo systemctl daemon-reload
sudo systemctl enable publisher-job
sudo systemctl start publisher-job
sudo systemctl status publisher-job
journalctl -u publisher-job -f
```

## Update From Git And Republish

Keep runtime config and state before republishing:

```bash
sudo systemctl stop publisher-job
cp /opt/publisher-job/config.json /tmp/publisher-job-config.json
cp /opt/publisher-job/posted.txt /tmp/publisher-job-posted.txt

cd /opt/AIDeveloperNotes
git pull
dotnet publish Publisher/Publisher.Job/Publisher.Job.csproj -c Release -o /opt/publisher-job

cp /tmp/publisher-job-config.json /opt/publisher-job/config.json
cp /tmp/publisher-job-posted.txt /opt/publisher-job/posted.txt
sudo chown -R www-data:www-data /opt/publisher-job
sudo systemctl start publisher-job
sudo systemctl status publisher-job
```

## Quick Troubleshooting

```bash
sudo systemctl status publisher-job
journalctl -u publisher-job --since today
cd /opt/publisher-job
dotnet Publisher.Job.dll --status
date
timedatectl
python3 -m json.tool config.json
```

Check these first:

- The service is `active (running)`.
- Server timezone is correct.
- Runtime config is `/opt/publisher-job/config.json`.
- `dotnet Publisher.Job.dll --status` shows the expected Hangfire cron values.
- Tokens and `chatId` values are correct.
- Prompt files exist under `/opt/publisher-job/Promp/`.
- The service user can write to `/opt/publisher-job`.
- The relevant state or lock file is not blocking a run.

### Groq 401 Unauthorized

If a manual run fails with `Groq API failed with 401 Unauthorized` and `invalid_api_key`,
the worker reached Groq successfully but sent an invalid key.

Check the runtime keys on the server:

```bash
cd /opt/publisher-job
python3 -m json.tool config.json
sudo systemctl show publisher-job --property=Environment
```

Fix or remove revoked keys from `groq.apiKeys` and update `GROQ_API_KEY` in
`/etc/systemd/system/publisher-job.service` if that environment variable is used.
After changing the service file:

```bash
sudo systemctl daemon-reload
sudo systemctl restart publisher-job
cd /opt/publisher-job
dotnet Publisher.Job.dll --run-groq-once
```

When multiple keys are configured, the worker retries another configured key after an
`invalid_api_key` response. If all keys are invalid, replace them with active Groq keys.
