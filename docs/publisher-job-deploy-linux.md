# راهنمای Deploy و اجرای Publisher.Job روی Linux

این سند مراحل دریافت کد از GitHub، publish، تنظیم `config.json`، ساخت سرویس `systemd`، اجرای سرویس، و update بعدی از GitHub را پوشش می‌دهد.

فرض‌های این راهنما:

- سرور Linux از نوع Ubuntu/Debian است.
- مسیر سورس روی سرور: `/opt/AIDeveloperNotes`
- مسیر خروجی publish و اجرای سرویس: `/opt/publisher-job`
- نام سرویس: `publisher-job`
- پروژه job: `Publisher/Publisher.Job/Publisher.Job.csproj`
- اجرای برنامه با .NET انجام می‌شود: `dotnet Publisher.Job.dll`

مقادیر زیر را با مقدار واقعی خودتان جایگزین کنید:

```bash
GITHUB_REPO_URL="https://github.com/YOUR_USER/YOUR_REPO.git"
BOT_TOKEN="YOUR_TELEGRAM_BOT_TOKEN"
CHAT_ID="-1000000000000"
```

## 1. نصب پیش‌نیازها روی سرور

ابتدا وضعیت نصب `git` و `dotnet` را چک کنید:

```bash
git --version
dotnet --info
```

اگر نصب نبودند:

```bash
sudo apt-get update
sudo apt-get install -y git
```

برای این پروژه `dotnet-sdk-10.0` لازم است، چون پروژه با `net10.0` ساخته شده است:

```bash
sudo apt-get install -y dotnet-sdk-10.0
```

اگر package پیدا نشد، repository رسمی Microsoft برای Ubuntu/Debian باید روی سرور اضافه شود. بعد از نصب، دوباره چک کنید:

```bash
dotnet --info
```

## 2. دریافت کد از GitHub

اگر سورس قبلاً روی سرور clone نشده است:

```bash
cd /opt
sudo git clone https://github.com/YOUR_USER/YOUR_REPO.git AIDeveloperNotes
sudo chown -R "$USER:$USER" /opt/AIDeveloperNotes
cd /opt/AIDeveloperNotes
```

اگر repository خصوصی است، از SSH یا GitHub token استفاده کنید. نمونه SSH:

```bash
cd /opt
sudo git clone git@github.com:YOUR_USER/YOUR_REPO.git AIDeveloperNotes
sudo chown -R "$USER:$USER" /opt/AIDeveloperNotes
cd /opt/AIDeveloperNotes
```

وضعیت branch و آخرین commit را چک کنید:

```bash
git status
git branch --show-current
git log -1 --oneline
```

## 3. Build و Publish

از مسیر سورس:

```bash
cd /opt/AIDeveloperNotes
dotnet restore Publisher/Publisher.Job/Publisher.Job.csproj
dotnet build Publisher/Publisher.Job/Publisher.Job.csproj -c Release
dotnet publish Publisher/Publisher.Job/Publisher.Job.csproj -c Release -o /opt/publisher-job
```

خروجی publish را چک کنید:

```bash
ls -la /opt/publisher-job
ls -la /opt/publisher-job/Publisher.Job.dll
```

باید فایل‌های زیر را ببینید:

```text
Publisher.Job.dll
Publisher.Job.runtimeconfig.json
config.json
Posts/
Pics/
posted.txt
```

## 4. تنظیم config

فایل config خروجی publish را روی سرور ویرایش کنید:

```bash
nano /opt/publisher-job/config.json
```

نمونه حداقلی:

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
  "channels": [
    {
      "title": "main",
      "chatId": "-1000000000000"
    }
  ],
  "dailyJob": {
    "enabled": true,
    "time": "09:00",
    "postCount": 2,
    "botId": "YOUR_TELEGRAM_BOT_TOKEN",
    "chatId": "-1000000000000",
    "useProxy": false
  }
}
```

نکات مهم:

- اگر روی سرور proxy لازم ندارید، مقدار `useProxy` را `false` بگذارید.
- اگر `enabled` برابر `false` باشد، سرویس اجرا می‌شود ولی job روزانه کاری نمی‌کند.
- زمان `09:00` بر اساس timezone خود سرور است.
- `botId` و `chatId` داخل `dailyJob` باید با bot و channel واقعی مطابق باشند.
- token بات را در repository عمومی commit نکنید.

اعتبار JSON را چک کنید:

```bash
python3 -m json.tool /opt/publisher-job/config.json
```

اگر خطا داد، اول config را درست کنید. فایل باید با `{` شروع شود.

## 5. تنظیم timezone سرور

ساعت و timezone را چک کنید:

```bash
date
timedatectl
```

اگر باید بر اساس ساعت ایران اجرا شود:

```bash
sudo timedatectl set-timezone Asia/Tehran
timedatectl
```

## 6. تست دستی قبل از ساخت سرویس

ابتدا وضعیت config و state را ببینید:

```bash
cd /opt/publisher-job
dotnet Publisher.Job.dll --status
```

خروجی باید مواردی شبیه این داشته باشد:

```text
Enabled: True
Scheduled time: 09:00
Server now: ...
Last run date: ...
Unposted posts: ...
Should run now: True/False
```

برای اجرای فوری بدون توجه به ساعت و بدون تغییر `daily-job-state.json`:

```bash
cd /opt/publisher-job
dotnet Publisher.Job.dll --run-once
```

اگر ارسال موفق باشد، فایل `posted.txt` آپدیت می‌شود.

## 7. ساخت سرویس systemd

مسیر `dotnet` را پیدا کنید:

```bash
which dotnet
```

معمولاً خروجی این است:

```text
/usr/bin/dotnet
```

فایل سرویس را بسازید:

```bash
sudo nano /etc/systemd/system/publisher-job.service
```

محتوا:

```ini
[Unit]
Description=Publisher Telegram Daily Job
After=network-online.target
Wants=network-online.target

[Service]
WorkingDirectory=/opt/publisher-job
ExecStart=/usr/bin/dotnet /opt/publisher-job/Publisher.Job.dll
Restart=always
RestartSec=10
User=www-data

[Install]
WantedBy=multi-user.target
```

اگر خروجی `which dotnet` چیز دیگری بود، مقدار `ExecStart` را مطابق همان اصلاح کنید.

دسترسی فایل‌ها را برای user سرویس تنظیم کنید:

```bash
sudo chown -R www-data:www-data /opt/publisher-job
```

## 8. اجرای سرویس

سرویس را reload، enable و start کنید:

```bash
sudo systemctl daemon-reload
sudo systemctl enable publisher-job
sudo systemctl start publisher-job
```

وضعیت سرویس:

```bash
sudo systemctl status publisher-job
```

لاگ زنده:

```bash
journalctl -u publisher-job -f
```

لاگ‌های امروز:

```bash
journalctl -u publisher-job --since today
```

اگر سرویس پیدا نشد:

```bash
ls -la /etc/systemd/system/publisher-job.service
sudo systemctl daemon-reload
```

اگر permission error روی `posted.txt` یا `daily-job-state.json` داشتید:

```bash
sudo chown -R www-data:www-data /opt/publisher-job
sudo systemctl restart publisher-job
```

## 9. رفتار job روزانه

سرویس به صورت long-running اجرا می‌شود و هر 1 دقیقه config و زمان را چک می‌کند.

job فقط وقتی اجرا می‌شود که همه شرط‌های زیر برقرار باشند:

- `dailyJob.enabled` برابر `true` باشد.
- ساعت سرور از `dailyJob.time` عبور کرده باشد.
- `daily-job-state.json` نشان ندهد که امروز قبلاً اجرا شده است.
- حداقل یک پست ارسال‌نشده در `Posts` وجود داشته باشد.
- برای پست انتخاب‌شده، تصویر متناظر در `Pics` وجود داشته باشد.

نمونه نام‌گذاری:

```text
Posts/Post_011.txt
Pics/011.png
```

پس از اجرای زمان‌بندی‌شده، فایل زیر ساخته/آپدیت می‌شود:

```bash
/opt/publisher-job/daily-job-state.json
```

اگر این فایل تاریخ امروز را داشته باشد، job تا فردا دوباره اجرا نمی‌شود.

## 10. آپدیت از GitHub و publish مجدد

برای update بعدی، config واقعی سرور را حفظ کنید. چون publish می‌تواند `config.json` را overwrite کند.

```bash
sudo systemctl stop publisher-job
cp /opt/publisher-job/config.json /tmp/publisher-job-config.json
cp /opt/publisher-job/posted.txt /tmp/publisher-job-posted.txt
```

کد جدید را بگیرید:

```bash
cd /opt/AIDeveloperNotes
git pull
```

publish مجدد:

```bash
dotnet publish Publisher/Publisher.Job/Publisher.Job.csproj -c Release -o /opt/publisher-job
```

config و posted واقعی سرور را برگردانید:

```bash
cp /tmp/publisher-job-config.json /opt/publisher-job/config.json
cp /tmp/publisher-job-posted.txt /opt/publisher-job/posted.txt
sudo chown -R www-data:www-data /opt/publisher-job
```

سرویس را اجرا کنید:

```bash
sudo systemctl start publisher-job
sudo systemctl status publisher-job
```

یا اگر سرویس در حال اجرا بود و فقط restart لازم داشت:

```bash
sudo systemctl restart publisher-job
sudo systemctl status publisher-job
```

لاگ را چک کنید:

```bash
journalctl -u publisher-job --since "10 minutes ago"
```

## 11. چک‌لیست عیب‌یابی سریع

اگر ساعت 9 اتفاقی نیفتاد:

```bash
sudo systemctl status publisher-job
journalctl -u publisher-job --since today
cd /opt/publisher-job
dotnet Publisher.Job.dll --status
date
timedatectl
cat config.json
```

مواردی که باید بررسی شوند:

- سرویس وجود دارد و `active (running)` است.
- `dailyJob.enabled` برابر `true` است.
- timezone سرور درست است.
- `daily-job-state.json` تاریخ امروز را از اجرای قبلی ثبت نکرده است.
- `Posts` پست ارسال‌نشده دارد.
- `Pics` عکس متناظر با شماره پست را دارد.
- `posted.txt` به اشتباه همه پست‌ها را ارسال‌شده نشان نمی‌دهد.
- token بات و `chatId` درست هستند.

برای تست فوری:

```bash
cd /opt/publisher-job
dotnet Publisher.Job.dll --run-once
```

## 12. تنظیم و تست Groq Article Job

این job مستقل از job ارسال پست‌های `Posts` است و state/lock جدا دارد:

```text
/opt/publisher-job/groq-article-job-state.json
/opt/publisher-job/groq-article-job.lock
```

فایل prompt باید در خروجی publish وجود داشته باشد:

```bash
ls -la /opt/publisher-job/Promp/Groq-MakeArticle.md
cat /opt/publisher-job/Promp/Groq-MakeArticle.md
```

در `config.json` بخش‌های زیر را تنظیم کنید:

```json
"groqArticleJob": {
  "enabled": true,
  "time": "09:30",
  "promptPath": "Promp/Groq-MakeArticle.md",
  "botId": "YOUR_TELEGRAM_BOT_TOKEN",
  "chatId": "-1000000000000",
  "useProxy": false
},
"groq": {
  "baseUrl": "https://api.groq.com/openai/v1",
  "apiKey": "",
  "apiKeyEnvironmentVariable": "GROQ_API_KEY",
  "model": "llama-3.3-70b-versatile",
  "maxCompletionTokens": 2048,
  "temperature": 0.7,
  "timeoutSeconds": 300
}
```

بهتر است Groq API key را داخل config ننویسید و به صورت environment variable به سرویس بدهید.

فایل سرویس را باز کنید:

```bash
sudo nano /etc/systemd/system/publisher-job.service
```

داخل بخش `[Service]` این خط را اضافه کنید:

```ini
Environment=GROQ_API_KEY=YOUR_GROQ_API_KEY
```

بعد سرویس را reload و restart کنید:

```bash
sudo systemctl daemon-reload
sudo systemctl restart publisher-job
```

تست دستی job دوم:

```bash
cd /opt/publisher-job
dotnet Publisher.Job.dll --run-groq-once
```

دیدن وضعیت هر دو job:

```bash
cd /opt/publisher-job
dotnet Publisher.Job.dll --status
```

اگر فقط job دوم خطا بدهد، job اول تحت تاثیر قرار نمی‌گیرد. لاگ‌ها:

```bash
journalctl -u publisher-job --since today
```
