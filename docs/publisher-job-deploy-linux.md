# راهنمای Deploy و اجرای Publisher.Job روی Linux

این سند وضعیت فعلی `Publisher.Job` را پوشش می‌دهد: ارسال پست‌های روزانه، تولید مقاله از RSS با Groq، و `TelegramDataProvider` که هر ۳۰ دقیقه در بازه ۶ صبح تا ۱۲ شب اجرا می‌شود.

## مسیرهای مهم

- سورس روی سرور: `/opt/AIDeveloperNotes`
- خروجی publish و محل اجرای سرویس: `/opt/publisher-job`
- پروژه worker: `Publisher/Publisher.Job/Publisher.Job.csproj`
- فایل اجرای worker: `/opt/publisher-job/Publisher.Job.dll`
- config واقعی runtime: `/opt/publisher-job/config.json`

فولدر `Publisher/publish/` دیگر منبع معتبر نیست و از repo حذف/ignore شده است.

## نصب پیش‌نیازها

```bash
git --version
dotnet --info
```

اگر لازم بود:

```bash
sudo apt-get update
sudo apt-get install -y git dotnet-sdk-10.0
```

## دریافت سورس

```bash
cd /opt
sudo git clone https://github.com/YOUR_USER/YOUR_REPO.git AIDeveloperNotes
sudo chown -R "$USER:$USER" /opt/AIDeveloperNotes
cd /opt/AIDeveloperNotes
git status
git log -1 --oneline
```

## Build و Publish

```bash
cd /opt/AIDeveloperNotes
dotnet restore Publisher/Publisher.Job/Publisher.Job.csproj
dotnet build Publisher/Publisher.Job/Publisher.Job.csproj -c Release
dotnet publish Publisher/Publisher.Job/Publisher.Job.csproj -c Release -o /opt/publisher-job
```

خروجی را چک کنید:

```bash
ls -la /opt/publisher-job
ls -la /opt/publisher-job/Publisher.Job.dll
ls -la /opt/publisher-job/config.json
```

## تنظیم config

فایل runtime را روی سرور ویرایش کنید:

```bash
nano /opt/publisher-job/config.json
python3 -m json.tool /opt/publisher-job/config.json
```

نمونه ساختار فعلی:

```json
{
  "bots": [
    {
      "name": "bot1",
      "botId": "YOUR_TELEGRAM_BOT_TOKEN"
    }
  ],
  "proxy": {
    "address": "socks5://127.0.0.1:4567",
    "username": "",
    "password": ""
  },
  "dailyJob": {
    "enabled": true,
    "time": "06:00",
    "postCount": 3,
    "botId": "YOUR_TELEGRAM_BOT_TOKEN",
    "chatId": "-1000000000000",
    "useProxy": false
  },
  "groqArticleJob": {
    "enabled": true,
    "time": "06:30",
    "promptPath": "Promp/Groq-MakeArticle.md",
    "downloadImages": false,
    "feeds": [
      {
        "url": "https://aijourn.com/feed/"
      }
    ],
    "botId": "YOUR_TELEGRAM_BOT_TOKEN",
    "chatId": "-1000000000000",
    "useProxy": false
  },
  "telegramDataProvider": {
    "enabled": true,
    "startTime": "06:00",
    "endTime": "23:59",
    "intervalMinutes": 30,
    "useProxy": false,
    "sendDelayBetweenChannelsSeconds": 30,
    "channels": [
      {
        "url": "https://telegram.me/khabarfouri",
        "promptPath": "Promp/TelegramNews/Telegram-DataProvider-AkharinKhabar.md",
        "postLimit": 10,
        "maxAgeMinutes": 30,
        "botId": "YOUR_TELEGRAM_BOT_TOKEN",
        "chatId": "-1000000000000",
        "useProxy": false
      }
    ]
  },
  "groq": {
    "baseUrl": "https://api.groq.com/openai/v1",
    "apiKey": "",
    "apiKeyEnvironmentVariable": "GROQ_API_KEY",
    "model": "llama-3.3-70b-versatile",
    "maxCompletionTokens": 2048,
    "temperature": 0.7,
    "timeoutSeconds": 300,
    "systemPrompt": "You generate Telegram-ready Persian posts. Return only the final Telegram post text. Use Telegram-compatible HTML only when formatting is needed."
  }
}
```

نکته‌ها:

- `channels` سطح بالا دیگر وجود ندارد. هر job یا source channel باید `chatId` خودش را داشته باشد.
- برای `telegramDataProvider.channels[*].maxAgeMinutes`:
  - مقدار مثبت یعنی فقط پست‌های جدیدتر از همان تعداد دقیقه.
  - مقدار `0` یا منفی یعنی فیلتر زمان خاموش است.
- `sendDelayBetweenChannelsSeconds` وقفه بین ارسال‌های موفق TelegramDataProvider است.
- کلید Groq را بهتر است در config ننویسید و با env بدهید.

## Timezone

```bash
date
timedatectl
sudo timedatectl set-timezone Asia/Tehran
timedatectl
```

## اجرای دستی و status

```bash
cd /opt/publisher-job
dotnet Publisher.Job.dll --status
dotnet Publisher.Job.dll --run-posts-once
dotnet Publisher.Job.dll --run-groq-once
dotnet Publisher.Job.dll --run-telegram-data-provider-once
```

`--run-once` هنوز alias قدیمی `--run-posts-once` است.

## رفتار jobها

سرویس long-running است و هر ۱ دقیقه config و زمان را چک می‌کند.

`DailyPostsJob`:

- روزی یک بار بعد از `dailyJob.time` اجرا می‌شود.
- state و lock:
  - `/opt/publisher-job/daily-job-state.json`
  - `/opt/publisher-job/daily-job.lock`
- `posted.txt` را از کنار `Publisher.Job.dll` می‌خواند و می‌نویسد.

`ArticleJob`:

- روزی یک بار بعد از `groqArticleJob.time` اجرا می‌شود.
- RSS feedها را می‌خواند و Groq فقط خلاصه‌سازی می‌کند.
- state و lock:
  - `/opt/publisher-job/groq-article-job-state.json`
  - `/opt/publisher-job/groq-article-job.lock`

`TelegramDataProviderJob`:

- با `telegramDataProvider.enabled` فعال می‌شود.
- فقط بین `startTime` و `endTime` اجرا می‌شود.
- فاصله اجراها با `intervalMinutes` کنترل می‌شود.
- برای هر source channel، latest public posts را می‌خواند، با prompt مربوط خلاصه می‌کند، نام channel را بدون `@` آخر پیام می‌گذارد، و به `chatId` همان channel می‌فرستد.
- state و lock:
  - `/opt/publisher-job/telegram-data-provider-job-state.json`
  - `/opt/publisher-job/telegram-data-provider-job.lock`

## systemd service

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

## Update از GitHub و publish مجدد

قبل از publish مجدد، runtime config و stateهای مهم را نگه دارید:

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

## عیب‌یابی سریع

```bash
sudo systemctl status publisher-job
journalctl -u publisher-job --since today
cd /opt/publisher-job
dotnet Publisher.Job.dll --status
date
timedatectl
python3 -m json.tool config.json
```

چیزهایی که باید چک شوند:

- سرویس `active (running)` باشد.
- timezone درست باشد.
- فایل runtime config همان `/opt/publisher-job/config.json` باشد.
- token و `chatId` درست باشند.
- promptها در `/opt/publisher-job/Promp/` وجود داشته باشند.
- permission روی `/opt/publisher-job` برای user سرویس درست باشد.
- state فایل مربوط job مانع اجرای دوباره نشده باشد.
