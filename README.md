# 碧潭能源管理系統 PRO

水電瓦斯抄錶、用量趨勢分析與異常警示的管理工具。前端是單頁 HTML/CSS/JS（`index.html`），後端是 ASP.NET Core Web API + MongoDB（Atlas），資料集中存放在資料庫，可供多站點、多裝置共用；需要管理員帳號密碼登入才能讀寫資料。

## 功能

- **多站點抄錶管理**：新增／編輯／刪除站點，記錄群組、位置、錶號
- **水／電／瓦斯**三種類型，自動計算「本期 − 上期」用量，並即時驗證輸入是否合理（讀數倒退、用量暴增會跳出提醒）
- **月份切換**：依月份記錄抄錶讀數，自動帶入上月讀數作為本期上期
- **拍照存證**：可直接呼叫手機相機拍攝錶況照片，照片存在伺服器
- **圖表分析**：近 6／12 期用量趨勢長條圖、水電瓦斯佔比圓餅圖、異常用量警示、用量排行榜，並可依群組篩選
- **報表匯出**：本期報表匯出 CSV（Excel 可直接開啟）、列印 / 匯出 PDF、圖表分析報告匯出
- **資料備份**：匯出／匯入完整 JSON 備份（站點與各月讀數；照片檔案另外存放，不含在 JSON 備份內）
- **深色模式**：適合夜間抄錶使用
- **響應式版面**：手機、平板、桌機皆自動適配
- **管理員登入**：需要帳號密碼登入才能存取任何站點/抄錶資料

## 架構

```
瀏覽器 (index.html)  ──同源 fetch /api/...──▶  ASP.NET Core Web API (backend/BiTanEnergyApi)
                                                        │
                                                        ▼
                                                  MongoDB Atlas（站點、讀數、內嵌照片中繼資料）
                                                  伺服器磁碟（錶況照片檔案）
```

- 前端：純原生 HTML / CSS / JavaScript，Canvas 2D 手繪圖表，無外部框架或建置流程
- 後端：ASP.NET Core Web API（.NET 8）+ MongoDB.Driver，Cookie 驗證
- 資料庫：MongoDB（開發與正式環境皆使用同一個 MongoDB Atlas 免費層叢集，用不同資料庫名稱區隔）
- 部署目標：Render.com，詳見 [DEPLOY-RENDER.md](DEPLOY-RENDER.md)

## 快速開始（本機開發）

需要先安裝 [.NET 8 SDK](https://dotnet.microsoft.com/download) 與一個 MongoDB Atlas 免費層叢集（見 [DEPLOY-RENDER.md](DEPLOY-RENDER.md) 第 1 節）。

本機開發用的 Mongo 連線字串**不要**寫進 `appsettings.Development.json`（會進 git），改用 .NET 的 user-secrets：

```
cd backend/BiTanEnergyApi
dotnet user-secrets init
dotnet user-secrets set "MongoDb:ConnectionString" "mongodb+srv://<user>:<password>@xxxxx.mongodb.net/?retryWrites=true&w=majority"
dotnet run --urls http://localhost:5241
```

啟動後端後，直接用瀏覽器開 `http://localhost:5241/`（後端會同時把 `index.html` 服務出來，同源不用處理 CORS）。

預設會用 `appsettings.Development.json` 裡的 `Admin:InitialUsername` / `Admin:InitialPassword`（`admin` / `ChangeMe123!`）自動建立第一個管理員帳號，正式環境請務必改成自己的密碼。

## 部署到正式環境

正式環境目標是 **Render.com + MongoDB Atlas**，完整步驟（建立 Atlas 叢集、Render Web Service 設定、環境變數）請見 [DEPLOY-RENDER.md](DEPLOY-RENDER.md)。

## 檔案結構

```
.
├── index.html                      # 前端主程式（含所有邏輯與樣式）
├── manifest.json                   # PWA 設定檔
├── icons/
│   ├── icon-192.png
│   └── icon-512.png
├── backend/
│   └── BiTanEnergyApi/             # ASP.NET Core Web API
│       ├── Controllers/            # Auth / Sites / Readings / Backup
│       ├── Data/                   # MongoContext、索引初始化、種子管理員帳號
│       ├── Models/                 # Site / MonthlyReading / ReadingPhoto / AdminUser
│       └── appsettings*.json       # Mongo 連線字串、上傳路徑、管理員帳號設定
├── DEPLOY-RENDER.md                # Render.com + MongoDB Atlas 部署步驟
└── README.md
```

## 資料儲存說明

- 站點清單、每月抄錶讀數：存在 MongoDB（照片中繼資料內嵌在對應的月份讀數文件裡）
- 錶況照片：以檔案形式存在伺服器磁碟（`Uploads:RootPath` 設定的資料夾），資料庫只存相對路徑
- 深色模式偏好：純前端 UI 設定，仍留在瀏覽器 `localStorage`
- 「選單 → 匯出完整備份」匯出站點與各月讀數的 JSON（**不含照片檔案**），照片請透過伺服器檔案備份機制另外保護

## 授權

本專案為內部工具，授權條款請依實際需求自行加上 `LICENSE` 檔案（例如 MIT License）。
