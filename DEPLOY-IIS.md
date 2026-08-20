# 部署到 Windows Server 2019 + IIS10 + SQL Server 2014

本文件說明如何把「碧潭能源管理系統 PRO」（含新的 ASP.NET Core 後端）部署到公司內部的 Windows Server 2019，資料存進 SQL Server 2014，透過 IIS10 對外提供服務。

架構：同一個 IIS 網站下掛兩個部分（同源，瀏覽器 Cookie 驗證不用處理 CORS）：

- 網站根目錄：靜態檔案（`index.html`、`manifest.json`、`icons/`），IIS 直接當靜態網站伺服器。
- `/api`：IIS 下掛一個「應用程式」，指到 ASP.NET Core Web API 的發佈輸出，透過 ASP.NET Core Module（ANCM）代理進 Kestrel。

---

## 1. 伺服器端準備

### 1.1 安裝 IIS10

「伺服器管理員」→「新增角色及功能」→勾選「Web伺服器 (IIS)」角色，其餘用預設值即可（含「靜態內容」、「預設文件」等基本功能）。

### 1.2 安裝 .NET 8 Hosting Bundle

下載並安裝 **.NET 8.0 Hosting Bundle**（讓 IIS 能透過 ASP.NET Core Module 代理 ASP.NET Core 應用程式）。安裝完成後，在系統管理員權限的命令提示字元執行以下指令，讓 IIS 重新載入模組：

```
net stop was /y
net start w3svc
```

### 1.3 確認 SQL Server 2014 可連線

確認伺服器（或內網可連到的另一台主機）上已有可用的 SQL Server 2014 執行個體，並記下連線用的主機名稱／執行個體名稱。

---

## 2. 建立資料庫

1. 用 SQL Server Management Studio (SSMS) 連上 SQL Server 2014，建立一個新資料庫，例如 `BiTanEnergy`。
2. 建立一個給應用程式專用的 SQL 登入帳號（建議用 SQL 驗證，而非 Windows 驗證，因為 IIS 應用程式集區身分通常不是網域帳號），並在 `BiTanEnergy` 資料庫給予 `db_datareader` + `db_datawriter` 權限。
3. 開啟並執行 [`backend/BiTanEnergyApi/Migrations/deploy.sql`](backend/BiTanEnergyApi/Migrations/deploy.sql)（此腳本是用 `dotnet ef migrations script --idempotent` 產生，可重複執行不會出錯），建立所有資料表。

---

## 3. 發佈後端

在開發機（已安裝 .NET 8 SDK）上執行：

```
cd backend/BiTanEnergyApi
dotnet publish -c Release -o C:\publish\BiTanEnergyApi
```

將 `C:\publish\BiTanEnergyApi` 整個資料夾複製到伺服器上，例如 `D:\BiTanEnergy\api\`。

### 3.1 設定 appsettings.Production.json

在 `D:\BiTanEnergy\api\` 底下，依照 [`backend/BiTanEnergyApi/appsettings.Production.json.example`](backend/BiTanEnergyApi/appsettings.Production.json.example) 建立一份 `appsettings.Production.json`，填入：

- `ConnectionStrings:Default`：實際的 SQL Server 2014 連線字串
- `Uploads:RootPath`：伺服器上要存放照片的資料夾（例如 `D:\BiTanEnergy\uploads`），應用程式集區身分需要對這個資料夾有讀寫權限
- `Admin:InitialUsername` / `Admin:InitialPassword`：第一次啟動時自動建立的管理員帳號密碼（**建立後請立即登入並妥善保管，這組密碼只在資料庫的 AdminUsers 表是空的時候才會用來建立帳號**）
- `Database:AutoMigrate`：正式環境建議設為 `false`（因為 schema 已經用 `deploy.sql` 建好了）

---

## 4. IIS 設定

### 4.1 靜態網站（根目錄）

1. IIS 管理員 → 新增網站，實體路徑指到專案根目錄（含 `index.html`、`manifest.json`、`icons/` 的那個資料夾，例如 `D:\BiTanEnergy\www\`；只需要複製這幾個檔案/資料夾過去，不需要 `backend/`）。
2. 設定繫結（Binding），例如 Port 80，或搭配憑證用 443。

### 4.2 API 應用程式（掛在 /api）

1. 在剛剛建立的網站上按右鍵 → 「新增應用程式」。
2. 別名（Alias）填 `api`。
3. 應用程式集區：新建一個集區，**「.NET CLR 版本」選「沒有 Managed 程式碼」**（ASP.NET Core 不需要傳統 CLR，交給 ANCM 處理）。
4. 實體路徑指到 `D:\BiTanEnergy\api\`（就是步驟 3 發佈出來的資料夾）。
5. 確認此應用程式集區的身分（預設是 `ApplicationPoolIdentity`）對 `Uploads:RootPath` 設定的資料夾（例如 `D:\BiTanEnergy\uploads`）有完整讀寫權限：檔案總管 → 該資料夾 → 內容 → 安全性 → 新增 `IIS AppPool\<集區名稱>` 並給予「修改」權限。

### 4.3 重新啟動並測試

```
iisreset
```

用瀏覽器開啟 `http://<伺服器位址>/`，應該會看到登入畫面。用 `appsettings.Production.json` 裡設定的帳號密碼登入，測試：新增站點、切換月份、輸入本期讀數、拍照上傳、匯出 CSV/PDF、匯出完整備份。

---

## 5. 防火牆

若外部裝置（如手機抄錶）需要連線，記得在 Windows Server 的「進階安全性 Windows Defender 防火牆」開放對應的 Port（例如 80/443）的輸入規則。

---

## 6. 日常維運備忘

- **備份**：「選單 → 匯出完整備份」只包含站點與各月讀數，**不含照片檔案**。照片實際存放在 `Uploads:RootPath` 資料夾，請額外用伺服器的檔案備份機制（如排程複製、Windows Server Backup）保護這個資料夾，並定期備份 SQL Server 資料庫本身。
- **更新程式**：重新 `dotnet publish` 後，用新的輸出覆蓋 `D:\BiTanEnergy\api\`（先在 IIS 停用該應用程式或直接覆蓋，ANCM 會自動偵測檔案變更並回收處理程序），靜態檔案（`index.html` 等）若有更新也一併覆蓋 `D:\BiTanEnergy\www\`。
- **忘記密碼**：目前沒有「忘記密碼」流程，若管理員密碼遺失，需要直接在資料庫刪除 `AdminUsers` 表中的那筆帳號，讓應用程式下次啟動時用 `appsettings.Production.json` 裡的 `Admin:InitialUsername`/`Admin:InitialPassword` 重新建立（記得改一組新密碼後再重啟）。
