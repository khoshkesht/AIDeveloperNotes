# راهنمای استقرار Publisher.Job روی Windows Server

این راهنما سرویس زمان‌بندی‌شدهٔ `Publisher.Job` را روی Windows Server اجرا می‌کند. سرویس، بدون آرگومان، worker مربوط به Hangfire را اجرا و Jobهای فعال در `config.json` را زمان‌بندی می‌کند.

## 1. پیش‌نیازها

- Windows Server 2019 یا جدیدتر، 64 بیتی
- دسترسی Administrator در PowerShell
- Git، فقط در صورتی که می‌خواهید سورس را مستقیماً روی سرور دریافت و به‌روزرسانی کنید

در این راهنما خروجی به صورت self-contained ساخته می‌شود؛ بنابراین نصب .NET Runtime روی سرور لازم نیست.

## 2. دریافت سورس و ساخت Release

PowerShell را با گزینهٔ **Run as administrator** باز کنید و مسیرها را مطابق محیط خودتان تغییر دهید.

```powershell
New-Item -ItemType Directory -Force -Path C:\PublisherJob | Out-Null
Set-Location C:\PublisherJob
git clone <REPOSITORY_URL> source
Set-Location C:\PublisherJob\source

dotnet restore .\Publisher\Publisher.Job\Publisher.Job.csproj
dotnet publish .\Publisher\Publisher.Job\Publisher.Job.csproj `
  -Configuration Release `
  -Runtime win-x64 `
  --self-contained true `
  -Output C:\PublisherJob\app
```

پس از publish، فایل اجرایی `C:\PublisherJob\app\Publisher.Job.exe` باید وجود داشته باشد.

```powershell
Test-Path C:\PublisherJob\app\Publisher.Job.exe
```

## 3. تنظیم config و کلید Zoho Cliq

فایل اجرایی همیشه `config.json` کنار خودش را می‌خواند. بنابراین فایل زیر را ویرایش کنید:

```powershell
notepad C:\PublisherJob\app\config.json
```

نمونهٔ تنظیم Zoho Cliq:

```json
"zohoCliqDataProvider": {
  "enabled": true,
  "cron": "*/30 15-23 * * *",
  "endpoint": "https://<your-domain>/api/v2/channelsbyname/<channel>/message",
  "apiKeyEnvironmentVariable": "ZOHO_CLIQ_API_KEY",
  "useProxy": false,
  "postCount": 2,
  "startPostNumber": 150
}
```

`startPostNumber: 150` یعنی Zoho فقط فایل‌های `Post_150.txt` و بعد از آن را بررسی می‌کند. برای شروع از ابتدا، آن را `1` بگذارید. `postCount` حداکثر تعداد پست موفق در هر اجرا است.

کلید API را داخل Git یا `config.json` قرار ندهید. آن را به صورت environment variable سطح Machine ثبت کنید:

```powershell
setx ZOHO_CLIQ_API_KEY "YOUR_ZOHO_CLIQ_API_KEY" /M
```

برای اعتبارسنجی JSON:

```powershell
Get-Content C:\PublisherJob\app\config.json -Raw | ConvertFrom-Json | Out-Null
```

## 4. تست دستی قبل از ساخت سرویس

ابتدا وضعیت را بررسی کنید:

```powershell
Set-Location C:\PublisherJob\app
.\Publisher.Job.exe --status
```

برای یک اجرای دستی Zoho (بدون توجه به cron):

```powershell
.\Publisher.Job.exe --run-zoho-cliq-data-provider-once
```

ارسال‌های موفق Zoho در `C:\PublisherJob\app\zoho-cliq-posted.txt` ثبت می‌شوند؛ پست‌های تلگرام همچنان از `posted.txt` مستقل هستند.

## 5. ساخت Windows Service

پس از اینکه تست دستی موفق بود، سرویس را با PowerShell Administrator بسازید:

```powershell
New-Service `
  -Name "Publisher.Job" `
  -DisplayName "Publisher Job Worker" `
  -BinaryPathName '"C:\PublisherJob\app\Publisher.Job.exe"' `
  -StartupType Automatic

sc.exe failure "Publisher.Job" reset= 86400 actions= restart/5000/restart/5000/restart/5000
Start-Service -Name "Publisher.Job"
Get-Service -Name "Publisher.Job"
```

سرویس با حساب LocalSystem اجرا می‌شود که امکان نوشتن در `C:\PublisherJob\app` را دارد. اگر حساب سرویس را تغییر دادید، به آن حساب دسترسی Modify روی پوشهٔ `C:\PublisherJob\app` بدهید.

## 6. بررسی لاگ و وضعیت

خروجی Console سرویس ویندوز به صورت پیش‌فرض قابل مشاهده نیست. برای بررسی فوری، سرویس را متوقف کنید و فرمان دستی مرحلهٔ 4 را اجرا کنید. برای لاگ دائمی، خروجی را با یک logger فایل یا ابزار مدیریت سرویس مانند NSSM به فایل هدایت کنید.

وضعیت و تنظیم‌های runtime:

```powershell
Stop-Service -Name "Publisher.Job"
Set-Location C:\PublisherJob\app
.\Publisher.Job.exe --status
Start-Service -Name "Publisher.Job"
```

فایل‌های مهم runtime در همین پوشه هستند:

- `config.json`: تنظیمات فعلی سرویس
- `Posts\`: فایل‌های `Post_<number>.txt`
- `zoho-cliq-posted.txt`: پست‌های موفق Zoho Cliq
- `zoho-cliq-data-provider-job-state.json`: آخرین اجرای Zoho
- `*.lock`: قفل موقت اجرای هم‌زمان

## 7. به‌روزرسانی امن سرویس

قبل از publish جدید، config و state را نگه دارید تا ارسال تکراری رخ ندهد:

```powershell
Stop-Service -Name "Publisher.Job"

$backup = "C:\PublisherJob\backup-$(Get-Date -Format yyyyMMdd-HHmmss)"
New-Item -ItemType Directory -Force -Path $backup | Out-Null
Copy-Item C:\PublisherJob\app\config.json, C:\PublisherJob\app\posted.txt, C:\PublisherJob\app\zoho-cliq-posted.txt -Destination $backup -ErrorAction SilentlyContinue
Copy-Item C:\PublisherJob\app\*-state.json -Destination $backup -ErrorAction SilentlyContinue

Set-Location C:\PublisherJob\source
git pull
dotnet publish .\Publisher\Publisher.Job\Publisher.Job.csproj -c Release -r win-x64 --self-contained true -o C:\PublisherJob\app

Copy-Item $backup\config.json, $backup\posted.txt, $backup\zoho-cliq-posted.txt -Destination C:\PublisherJob\app -Force -ErrorAction SilentlyContinue
Copy-Item $backup\*-state.json -Destination C:\PublisherJob\app -Force -ErrorAction SilentlyContinue

Start-Service -Name "Publisher.Job"
Get-Service -Name "Publisher.Job"
```

## 8. حذف سرویس

```powershell
Stop-Service -Name "Publisher.Job" -ErrorAction SilentlyContinue
sc.exe delete "Publisher.Job"
```

حذف سرویس، فایل‌های `C:\PublisherJob\app` و history ارسال را پاک نمی‌کند.
