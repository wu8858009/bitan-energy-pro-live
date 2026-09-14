using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BiTanEnergyDesktop;

public class MainForm : Form
{
    // 正式站網址 — 桌面版只是這個網站的外殼，資料都是即時連到伺服器，跟手機/瀏覽器共用同一份資料。
    private const string SiteUrl = "https://bitan-energy-pro-live.onrender.com";

    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };

    public MainForm()
    {
        Text = "碧潭能源管理系統";
        Width = 1360;
        Height = 860;
        MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "app.ico")); } catch { /* icon is cosmetic only */ }

        Controls.Add(_webView);
        Load += MainForm_Load;
    }

    private async void MainForm_Load(object? sender, EventArgs e)
    {
        try
        {
            await _webView.EnsureCoreWebView2Async();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this,
                "這台電腦還沒有安裝 Microsoft Edge WebView2 執行環境（一般 Windows 10/11 都已內建，若缺少請至\n" +
                "https://developer.microsoft.com/microsoft-edge/webview2/ 下載安裝「Evergreen Bootstrapper」）。",
                "無法啟動", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        // 讓「匯出報告/列印」功能開的新視窗（window.open）能正常顯示，而不是被 WebView2 預設吃掉。
        _webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;

        _webView.CoreWebView2.Navigate(SiteUrl);
    }

    private async void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // Must finish creating the popup's CoreWebView2 (async) before assigning e.NewWindow,
        // so hold the deferral open across the await instead of completing it synchronously.
        var deferral = e.GetDeferral();
        try
        {
            var popup = new PopupForm();
            popup.Show(this);
            await popup.InitializeAsync(_webView.CoreWebView2.Environment, e.Uri);
            e.NewWindow = popup.CoreWebView2;
        }
        finally
        {
            deferral.Complete();
        }
    }
}

// 承載 window.open() 開出來的新視窗內容（例如：匯出報告的列印預覽頁）。
internal class PopupForm : Form
{
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    public CoreWebView2 CoreWebView2 => _webView.CoreWebView2;

    public PopupForm()
    {
        Text = "碧潭能源管理系統";
        Width = 900;
        Height = 700;
        Controls.Add(_webView);
    }

    public async Task InitializeAsync(CoreWebView2Environment environment, string? initialUri)
    {
        await _webView.EnsureCoreWebView2Async(environment);
        if (!string.IsNullOrEmpty(initialUri)) _webView.CoreWebView2.Navigate(initialUri);
    }
}
