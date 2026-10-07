# 碧潭能源管理系統 — 電腦版（免安裝 .exe）

這是一個很薄的 Windows 桌面外殼，開啟後直接顯示正式站 `https://bitan-energy-pro-live.onrender.com` 的內容——資料跟手機/瀏覽器版是同一份（都是連到同一個後端 + MongoDB），沒有另外離線儲存資料。

技術上是 Python + PySide6（Qt WebEngine，內建 Chromium 網頁引擎），用 PyInstaller 打包成單一 `.exe`。

## 使用者需求

- Windows 10/11
- 不用安裝 Python，也不用安裝任何程式；雙擊 `.exe` 即可開啟

## 檔案

| 檔案 | 用途 |
| --- | --- |
| `main.py` | 桌面版程式（視窗、列印、下載、相機權限等） |
| `build.py` | 打包成免安裝 exe |
| `requirements.txt` | 打包需要的套件版本 |
| `app.ico` | 視窗與 exe 圖示 |

## 開發時執行

```
cd desktop
pip install -r requirements.txt
python main.py
```

## 建置免安裝版 exe

```
cd desktop
python build.py
```

產出的檔案在：

```
desktop/dist/碧潭能源管理系統.exe
```

這個 `.exe` 就是免安裝版，複製到任何 Windows 電腦上雙擊就能開。檔案約 220 MB，因為裡面包含了完整的 Qt 與 Chromium 網頁引擎；第一次開啟會比較慢一點（幾秒）。

## 功能對應

- **列印 / 存 PDF**：報表的「列印 / 存PDF」按鈕會跳出系統列印對話框，選擇「Microsoft Print to PDF」即可存成 PDF。
- **下載檔案**：匯出 CSV、備份 JSON 會跳出「儲存檔案」視窗讓你選位置。
- **選擇照片**：拍照上傳會跳出檔案選擇視窗。
- **掃條碼**：允許本站使用相機，其他網站一律拒絕。
- **登入狀態**：登入後會保存在本機，下次開啟不用重新登入。
- **視窗標題**：網頁標題（版本與登入人員）會同步到視窗標題列，只保留一條標題列。

## 修改網址

如果之後正式站網址換了（例如換了自訂網域），改 `main.py` 裡的 `SITE_URL` 常數，重新執行 `python build.py` 即可。
