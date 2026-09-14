# 碧潭能源管理系統 — 桌面版（免安裝）

這是一個很薄的 Windows 桌面外殼，開啟後直接顯示正式站 `https://bitan-energy-pro-live.onrender.com` 的內容——資料跟手機/瀏覽器版是同一份（都是連到同一個後端 + MongoDB），沒有另外離線儲存資料。

技術上是 .NET 8 WinForms + WebView2（用 Windows 內建的 Edge 引擎顯示網頁），不是 Electron，所以檔案小很多、開啟速度快。

## 使用者需求

- Windows 10/11
- Microsoft Edge WebView2 執行環境（絕大多數 Windows 10/11 都已內建；如果沒有，程式啟動時會跳出提示並附下載連結）

## 建置免安裝版 exe

```
cd desktop/BiTanEnergyDesktop
dotnet publish -c Release
```

產出的檔案在：
```
desktop/BiTanEnergyDesktop/bin/Release/net8.0-windows/win-x64/publish/碧潭能源管理系統.exe
```

這個 `.exe`（連同同資料夾裡的幾個 `.xml` 說明檔，可以不管它們，不影響執行）就是免安裝版，複製到任何 Windows 電腦上雙擊就能開，不用先裝 .NET。檔案本身會比較大（約 150+ MB），因為裡面包含了完整的 .NET 執行環境。

## 修改網址

如果之後正式站網址換了（例如換了自訂網域），改 `MainForm.cs` 裡的 `SiteUrl` 常數即可，重新 `dotnet publish` 一次。
