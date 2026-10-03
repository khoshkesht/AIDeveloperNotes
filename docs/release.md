# انتشار و به‌روزرسانی Publisher.Job روی Windows Server

این اسکریپت را در PowerShell با **Run as administrator** اجرا کنید. اجرای مجدد آن امن است:

- اگر سرویس `Publisher.Job` وجود داشته باشد، آن را متوقف می‌کند.
- فقط فایل‌های لازم برای `Publisher.Job` را با Git sparse-checkout دریافت می‌کند؛ پوشه‌های `docs`، `Sources`، `Publisher.WinForms` و `.vs` وارد working tree سرور نمی‌شوند.
- اگر سورس در `C:\PublisherJob\source` وجود داشته باشد، همان clone را با `git pull --ff-only` به‌روزرسانی می‌کند؛ در غیر این صورت clone جدید می‌گیرد.
- `config.json` و state/history ارسال‌ها را قبل از publish در `C:\PublisherJob\backup` نگه می‌دارد و پس از آن برمی‌گرداند. این backup در هر اجرا جایگزین می‌شود.
- اگر سرویس از قبل وجود نداشته باشد، آن را ایجاد و در پایان اجرا می‌کند. خروجی فعلی برنامه به صورت native Windows Service اجرا می‌شود.

> `git pull --ff-only` عمداً در صورت وجود تغییر local در سورس متوقف می‌شود تا تغییری ناخواسته overwrite نشود. ابتدا آن تغییر را commit، stash یا بررسی کنید و سپس اسکریپت را دوباره اجرا کنید.

```powershell
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $false
}

$rootPath = 'C:\PublisherJob'
$sourcePath = Join-Path $rootPath 'source'
$appPath = Join-Path $rootPath 'app'
$serviceName = 'Publisher.Job'
$repositoryUrl = 'https://github.com/khoshkesht/AIDeveloperNotes'
$projectPath = Join-Path $sourcePath 'Publisher\Publisher.Job\Publisher.Job.csproj'
$backupPath = Join-Path $rootPath 'backup'
$gitSafeSourcePath = $sourcePath.Replace('\', '/')
$sparsePaths = @(
    'Publisher/Publisher.Job',
    'Publisher/Posts',
    'Publisher/Pics',
    'Publisher/Promp'
)

function Invoke-NativeCommand {
    param([scriptblock]$Command, [string]$Description)

    # Git writes ordinary progress messages to stderr. Merge both streams so
    # PowerShell does not treat successful progress output as an exception.
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & $Command 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    $output | ForEach-Object { Write-Host $_ }

    if ($exitCode -ne 0) {
        $outputText = ($output | Out-String).Trim()
        throw "$Description failed with exit code $exitCode.`n$outputText"
    }
}

function Set-PublisherSparseCheckout {
    Invoke-NativeCommand { git -c "safe.directory=$gitSafeSourcePath" -C $sourcePath sparse-checkout init --cone } 'git sparse-checkout init'
    Invoke-NativeCommand { git -c "safe.directory=$gitSafeSourcePath" -C $sourcePath sparse-checkout set --cone $sparsePaths } 'git sparse-checkout set'
    Invoke-NativeCommand { git -c "safe.directory=$gitSafeSourcePath" -C $sourcePath sparse-checkout reapply } 'git sparse-checkout reapply'

    if (-not (Test-Path (Join-Path $sourcePath 'Publisher'))) {
        throw "Sparse checkout did not populate '$sourcePath\Publisher'."
    }
}

# 1. Stop the existing service, if it is installed.
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $service -and $service.Status -ne 'Stopped') {
    Stop-Service -Name $serviceName -Force
    $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

# 2. Clone once; on later releases update the same working copy.
New-Item -ItemType Directory -Force -Path $rootPath | Out-Null
if (Test-Path (Join-Path $sourcePath '.git')) {
    Invoke-NativeCommand { git -c "safe.directory=$gitSafeSourcePath" -C $sourcePath fetch --prune origin } 'git fetch'
    Invoke-NativeCommand { git -c "safe.directory=$gitSafeSourcePath" -C $sourcePath pull --ff-only } 'git pull'
    Set-PublisherSparseCheckout
}
elseif (Test-Path $sourcePath) {
    throw "'$sourcePath' exists but is not a Git clone. Rename or remove it after checking its contents."
}
else {
    Invoke-NativeCommand { git clone --filter=blob:none --sparse $repositoryUrl $sourcePath } 'git clone'
    Set-PublisherSparseCheckout
}

# 3. Save runtime-only configuration and delivery history before publish.
if (Test-Path $backupPath) {
    Remove-Item -LiteralPath $backupPath -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $backupPath | Out-Null
if (Test-Path $appPath) {
    @('config.json', 'posted.txt', 'zoho-cliq-posted.txt') |
        ForEach-Object {
            $file = Join-Path $appPath $_
            if (Test-Path $file) {
                Copy-Item -LiteralPath $file -Destination $backupPath -Force
            }
        }

    Get-ChildItem -Path $appPath -Filter '*-state.json' -File -ErrorAction SilentlyContinue |
        Copy-Item -Destination $backupPath -Force
}

# 4. Build a self-contained Windows x64 release.
Invoke-NativeCommand { dotnet restore $projectPath } 'dotnet restore'
Invoke-NativeCommand {
    dotnet publish $projectPath -c Release -r win-x64 --self-contained true -o $appPath
} 'dotnet publish'

if (-not (Test-Path (Join-Path $appPath 'Publisher.Job.exe'))) {
    throw "Publish completed but '$appPath\Publisher.Job.exe' was not found."
}

# 5. Restore the server-specific runtime files.
@('config.json', 'posted.txt', 'zoho-cliq-posted.txt') |
    ForEach-Object {
        $file = Join-Path $backupPath $_
        if (Test-Path $file) {
            Copy-Item -LiteralPath $file -Destination $appPath -Force
        }
    }

Get-ChildItem -Path $backupPath -Filter '*-state.json' -File -ErrorAction SilentlyContinue |
    Copy-Item -Destination $appPath -Force

# 6. Create the service on the first release, then start it.
if ($null -eq (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) {
    New-Service `
        -Name $serviceName `
        -DisplayName 'Publisher Job Worker' `
        -BinaryPathName ('"' + (Join-Path $appPath 'Publisher.Job.exe') + '"') `
        -StartupType Automatic

    & sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/5000/restart/5000
    if ($LASTEXITCODE -ne 0) {
        throw "Could not configure recovery options for service '$serviceName'."
    }
}

Start-Service -Name $serviceName
Get-Service -Name $serviceName
```

پس از انتشار، تنظیمات فعال در `C:\PublisherJob\app\config.json` هستند. برای Zoho، مقدار `startPostNumber` را در همین فایل تغییر دهید؛ مقدار `150` یعنی ارسال از `Post_150.txt` به بعد.

برای بررسی وضعیت پس از اجرا:

```powershell
Stop-Service -Name 'Publisher.Job'
Set-Location C:\PublisherJob\app
.\Publisher.Job.exe --status
Start-Service -Name 'Publisher.Job'
```

```run

Stop-Service -Name 'Publisher.Job'

Set-Location C:\PublisherJob\app
.\Publisher.Job.exe --run-zoho-cliq-data-provider-once

Start-Service -Name 'Publisher.Job'
```
