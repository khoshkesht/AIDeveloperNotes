using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Publisher;

public sealed partial class MainForm : Form
{
    private readonly string _basePath = AppContext.BaseDirectory;
    private readonly string _postsPath;
    private readonly string _postedFilePath;

    private AppConfig _config = new();
    private readonly Dictionary<string, PostedEntry> _postedFiles = new(StringComparer.OrdinalIgnoreCase);

    public MainForm()
    {
        _postsPath = Path.Combine(_basePath, "Posts");
        _postedFilePath = Path.Combine(FindProjectRoot(_basePath), "posted.txt");

        InitializeComponent();
        Load += MainForm_Load;
    }

    private static string FindProjectRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Publisher.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return startPath;
    }

    private void MainForm_Load(object? sender, EventArgs e)
    {
        try
        {
            EnsureDefaultFiles();
            LoadConfig();
            LoadPostedFiles();
            LoadPostFiles();
            SetStatus("آماده");
        }
        catch (Exception ex)
        {
            SetStatus("خطا در بارگذاری");
            MessageBox.Show(ex.Message, "خطا", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void EnsureDefaultFiles()
    {
        Directory.CreateDirectory(_postsPath);

        var configPath = Path.Combine(_basePath, "config.json");
        if (!File.Exists(configPath))
        {
            File.WriteAllText(configPath, """
            {
              "bots": [
                {
                  "name": "بات پیش فرض",
                  "botId": "bot_token_here"
                }
              ],
              "proxy": {
                "address": "socks5://127.0.0.1:1080",
                "username": "",
                "password": ""
              },
              "channels": [
                {
                  "title": "کانال پیش فرض",
                  "chatId": "@channel_username_or_chat_id"
                }
              ]
            }
            """, Encoding.UTF8);
        }

        if (!File.Exists(_postedFilePath))
        {
            File.WriteAllText(_postedFilePath, string.Empty, Encoding.UTF8);
        }
    }

    private void LoadConfig()
    {
        var configPath = Path.Combine(_basePath, "config.json");
        var json = File.ReadAllText(configPath, Encoding.UTF8);
        _config = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new AppConfig();
        _config.Proxy ??= new ProxyConfig();

        if (_config.Bots.Count == 0)
        {
            throw new InvalidOperationException("در فایل config.json حداقل یک بات باید تعریف شود.");
        }

        if (_config.Channels.Count == 0)
        {
            throw new InvalidOperationException("در فایل config.json حداقل یک کانال باید تعریف شود.");
        }

        _botCombo.DataSource = _config.Bots;
        _channelCombo.DataSource = _config.Channels;
        _botCombo.SelectedIndex = 0;
        _channelCombo.SelectedIndex = 0;
    }

    private void LoadPostedFiles()
    {
        _postedFiles.Clear();

        foreach (var line in File.ReadLines(_postedFilePath, Encoding.UTF8))
        {
            var parts = line.Split('\t', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var fileName = Path.GetFileName(parts[1].Trim());
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            _postedFiles[fileName] = new PostedEntry(fileName, parts[0]);
        }
    }

    private void LoadPostFiles()
    {
        _postsList.Items.Clear();
        _previewBox.Clear();
        _postTextBox.Clear();
        _imagePathBox.Clear();

        var files = Directory.EnumerateFiles(_postsPath, "*.txt", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file => !_postedFiles.ContainsKey(file.Name))
            .OrderBy(file => GetPostSortKey(file.Name))
            .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(file => new PostFileItem(file.FullName, file.Name, file.LastWriteTime))
            .ToArray();

        _postsList.Items.AddRange(files);
    }

    private void PostsList_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_postsList.SelectedItem is not PostFileItem item)
        {
            return;
        }

        try
        {
            var text = ReadPostContent(item.Path);
            _previewBox.Text = text;
            _postTextBox.Text = text;
            SetStatus($"انتخاب شد: {item.Name}");
        }
        catch (Exception ex)
        {
            SetStatus("خطا در خواندن فایل");
            MessageBox.Show(ex.Message, "خطا", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string ReadPostContent(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length > 0 && IsPostHeaderLine(lines[0]))
        {
            return string.Join(Environment.NewLine, lines.Skip(1));
        }

        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static int GetPostSortKey(string fileName)
    {
        var match = Regex.Match(fileName, @"(?i)^Post_(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var number))
        {
            return number;
        }

        return int.MaxValue;
    }

    private static bool IsPostHeaderLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        return Regex.IsMatch(line.Trim(), "^#Post_\\d+$", RegexOptions.CultureInvariant);
    }

    private async void SendButton_Click(object? sender, EventArgs e)
    {
        if (_postsList.SelectedItem is not PostFileItem selectedFile)
        {
            MessageBox.Show("ابتدا یک فایل را انتخاب کنید.", "ارسال پست", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_botCombo.SelectedItem is not BotConfig bot || string.IsNullOrWhiteSpace(bot.BotId))
        {
            MessageBox.Show("بات انتخاب شده معتبر نیست.", "ارسال پست", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_channelCombo.SelectedItem is not ChannelConfig channel || string.IsNullOrWhiteSpace(channel.ChatId))
        {
            MessageBox.Show("کانال انتخاب شده معتبر نیست.", "ارسال پست", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var postText = _postTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(postText))
        {
            MessageBox.Show("متن پست خالی است.", "ارسال پست", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetBusy(true, "در حال ارسال...");

        try
        {
            var imagePath = _imagePathBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(imagePath) && !File.Exists(imagePath))
            {
                MessageBox.Show("فایل عکس انتخاب شده پیدا نشد.", "ارسال پست", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("ارسال ناموفق");
                return;
            }

            using var httpClient = CreateTelegramHttpClient();
            var result = string.IsNullOrWhiteSpace(imagePath)
                ? await SendTelegramMessageAsync(httpClient, bot.BotId, channel.ChatId, postText)
                : await SendTelegramPhotoPostAsync(httpClient, bot.BotId, channel.ChatId, postText, imagePath);
            if (!result.Success)
            {
                MessageBox.Show(result.ErrorMessage, "خطا در ارسال", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("ارسال ناموفق");
                return;
            }

            MarkAsPosted(selectedFile.Path);
            LoadPostedFiles();
            LoadPostFiles();
            SetStatus("پست با موفقیت ارسال شد.");
            MessageBox.Show("پست با موفقیت ارسال شد.", "ارسال پست", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            SetStatus("ارسال ناموفق");
            MessageBox.Show($"ارسال پیام انجام نشد: {ex.Message}", "خطا در ارسال", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ClearButton_Click(object? sender, EventArgs e)
    {
        _postTextBox.Clear();
    }

    private void SelectImageButton_Click(object? sender, EventArgs e)
    {
        if (_imageDialog.ShowDialog(this) == DialogResult.OK)
        {
            _imagePathBox.Text = _imageDialog.FileName;
        }
    }

    private void ClearImageButton_Click(object? sender, EventArgs e)
    {
        _imagePathBox.Clear();
    }

    private HttpClient CreateTelegramHttpClient()
    {
        if (!_useProxyCheckBox.Checked)
        {
            return new HttpClient();
        }

        var proxyAddress = _config.Proxy.Address.Trim();
        if (string.IsNullOrWhiteSpace(proxyAddress))
        {
            throw new InvalidOperationException("پروکسی فعال است، اما مقدار proxy.address در config.json خالی است.");
        }

        if (!Uri.TryCreate(proxyAddress, UriKind.Absolute, out var proxyUri))
        {
            throw new InvalidOperationException("آدرس پروکسی در config.json باید کامل باشد، مثلا socks5://127.0.0.1:4567.");
        }

        if (!IsSupportedProxyScheme(proxyUri.Scheme))
        {
            throw new InvalidOperationException("نوع پروکسی پشتیبانی نمی‌شود. از http، https، socks4، socks4a یا socks5 استفاده کنید.");
        }

        var proxy = new WebProxy(proxyUri);
        if (!string.IsNullOrWhiteSpace(_config.Proxy.Username))
        {
            proxy.Credentials = new NetworkCredential(_config.Proxy.Username, _config.Proxy.Password);
        }

        var handler = new HttpClientHandler
        {
            Proxy = proxy,
            UseProxy = true
        };

        return new HttpClient(handler, disposeHandler: true);
    }

    private static bool IsSupportedProxyScheme(string scheme)
    {
        return scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
            || scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
            || scheme.Equals("socks4", StringComparison.OrdinalIgnoreCase)
            || scheme.Equals("socks4a", StringComparison.OrdinalIgnoreCase)
            || scheme.Equals("socks5", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<TelegramSendResult> SendTelegramMessageAsync(HttpClient httpClient, string botId, string chatId, string text)
    {
        var url = $"https://api.telegram.org/{botId}/sendMessage";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["chat_id"] = chatId,
            ["parse_mode"] = "HTML",
            ["text"] = text
        });

        using var response = await httpClient.PostAsync(url, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            return TelegramSendResult.Ok();
        }

        var error = ExtractTelegramError(responseBody);
        return TelegramSendResult.Fail($"تلگرام خطا برگرداند ({(int)response.StatusCode} {response.ReasonPhrase}): {error}");
    }

    private async Task<TelegramSendResult> SendTelegramPhotoPostAsync(HttpClient httpClient, string botId, string chatId, string text, string imagePath)
    {
        if (text.Length <= 1024)
        {
            return await SendTelegramPhotoAsync(httpClient, botId, chatId, imagePath, text);
        }

        var photoResult = await SendTelegramPhotoAsync(httpClient, botId, chatId, imagePath, null);
        if (!photoResult.Success)
        {
            return photoResult;
        }

        return await SendTelegramMessageAsync(httpClient, botId, chatId, text);
    }

    private async Task<TelegramSendResult> SendTelegramPhotoAsync(HttpClient httpClient, string botId, string chatId, string imagePath, string? caption)
    {
        var url = $"https://api.telegram.org/{botId}/sendPhoto";
        await using var imageStream = File.OpenRead(imagePath);
        using var content = new MultipartFormDataContent
        {
            { new StringContent(chatId, Encoding.UTF8), "chat_id" },
            { new StringContent("HTML", Encoding.UTF8), "parse_mode" }
        };

        if (!string.IsNullOrWhiteSpace(caption))
        {
            content.Add(new StringContent(caption, Encoding.UTF8), "caption");
        }

        using var imageContent = new StreamContent(imageStream);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue(GetImageContentType(imagePath));
        content.Add(imageContent, "photo", Path.GetFileName(imagePath));

        using var response = await httpClient.PostAsync(url, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            return TelegramSendResult.Ok();
        }

        var error = ExtractTelegramError(responseBody);
        return TelegramSendResult.Fail($"تلگرام در ارسال عکس خطا برگرداند ({(int)response.StatusCode} {response.ReasonPhrase}): {error}");
    }

    private static string GetImageContentType(string imagePath)
    {
        return Path.GetExtension(imagePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    private static string ExtractTelegramError(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "پاسخ خالی از سرور دریافت شد.";
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("description", out var description))
            {
                return description.GetString() ?? responseBody;
            }
        }
        catch (JsonException)
        {
            Debug.WriteLine("Telegram response is not JSON.");
        }

        return responseBody;
    }

    private void MarkAsPosted(string filePath)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}\t{Path.GetFileName(filePath)}{Environment.NewLine}";
        File.AppendAllText(_postedFilePath, line, Encoding.UTF8);
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _sendButton.Enabled = !busy;
        _clearButton.Enabled = !busy;
        _selectImageButton.Enabled = !busy;
        _clearImageButton.Enabled = !busy;
        _postsList.Enabled = !busy;
        _botCombo.Enabled = !busy;
        _channelCombo.Enabled = !busy;
        _useProxyCheckBox.Enabled = !busy;

        if (!string.IsNullOrWhiteSpace(status))
        {
            SetStatus(status);
        }
    }

    private void SetStatus(string status)
    {
        _statusLabel.Text = status;
    }
}

public sealed class AppConfig
{
    public List<BotConfig> Bots { get; set; } = [];
    public List<ChannelConfig> Channels { get; set; } = [];
    public ProxyConfig Proxy { get; set; } = new();
}

public sealed class ProxyConfig
{
    public string Address { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class BotConfig
{
    public string Name { get; set; } = string.Empty;
    public string BotId { get; set; } = string.Empty;

    public override string ToString() => Name;
}

public sealed class ChannelConfig
{
    public string Title { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;

    public override string ToString() => Title;
}

public sealed record PostFileItem(string Path, string Name, DateTime LastWriteTime)
{
    public string DisplayName => Name;
}

public sealed record PostedEntry(string FileName, string PostedAt);

public sealed record TelegramSendResult(bool Success, string ErrorMessage)
{
    public static TelegramSendResult Ok() => new(true, string.Empty);

    public static TelegramSendResult Fail(string errorMessage) => new(false, errorMessage);
}
