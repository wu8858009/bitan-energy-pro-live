# 部署到 Render.com + MongoDB Atlas

本文件說明如何把「碧潭能源管理系統 PRO」（ASP.NET Core 後端 + MongoDB 資料庫）部署到 Render.com，並用 MongoDB Atlas 作為資料庫。

架構：**單一 Render Web Service** 同時服務靜態前端（`index.html`）與 `/api`（同源，瀏覽器 Cookie 驗證不用處理 CORS）。資料庫是外部的 MongoDB Atlas 叢集。

```
瀏覽器 ──同源 fetch /api/...──▶  Render Web Service (backend/BiTanEnergyApi)
                                        │
                                        ▼
                                  MongoDB Atlas（站點、讀數、內嵌照片中繼資料）
                                  Render 磁碟（錶況照片檔案，需另掛 Disk，見下方注意事項）
```

---

## 1. 建立 MongoDB Atlas 叢集

1. 到 [cloud.mongodb.com](https://cloud.mongodb.com) 建立專案（例如「水電瓦斯表」），在專案內「Database」→「Build a Database」選 **M0 免費層**，選擇雲端供應商與地區（建議選離 Render 服務區域近的，例如都選 Singapore 或都選美東）。
2. 建立資料庫使用者（Database Access）：記下帳號密碼，之後會用在連線字串裡。
3. 網路存取（Network Access）：新增 IP 存取清單項目 `0.0.0.0/0`（允許任何來源）。因為 Render 的一般方案沒有固定對外 IP，暫時只能全開；若要收緊，需額外購買 Render 的「Static Outbound IP」附加功能，再把 Atlas 白名單改成只允許那組固定 IP。
4. 叢集建好後按「Connect」→「Drivers」→選 .NET，複製連線字串（`mongodb+srv://<user>:<password>@xxxxx.mongodb.net/?retryWrites=true&w=majority`）。

---

## 2. 在 Render 建立 Web Service

1. Render Dashboard →「New」→「Web Service」→ 選你的 GitHub repo（`bitan-energy-pro-live`）。
2. 設定：
   - **Language**：**Docker**（Render 沒有原生 .NET 執行環境，.NET 專案一定要用 Docker 部署；repo 根目錄已經有一份 `Dockerfile` 處理好建置與啟動了）
   - **Dockerfile Path**：`./Dockerfile`
   - **Docker Build Context Directory**：`.`（repo 根目錄，不是 `backend/BiTanEnergyApi`——因為 Dockerfile 需要同時拿到後端程式碼和 `index.html`/`manifest.json`/`icons/`）
   - 不需要另外填 Build/Start Command，Dockerfile 裡的 `ENTRYPOINT` 已經包辦
   - **Instance Type**：先用 Free 測試即可，正式使用建議升級付費方案（Free 方案閒置一段時間會休眠，第一個請求會慢）
3. 環境變數（Environment → Add Environment Variable）：

   | Key | Value |
   |---|---|
   | `ASPNETCORE_ENVIRONMENT` | `Production` |
   | `MongoDb__ConnectionString` | 上一步拿到的 `mongodb+srv://...` 連線字串 |
   | `MongoDb__DatabaseName` | `BiTanEnergy` |
   | `Admin__InitialUsername` | `admin`（或自訂） |
   | `Admin__InitialPassword` | 自訂一組強密碼，**部署後請立即登入並考慮更換** |
   | `Uploads__RootPath` | 見下方「照片儲存與 Render Disk」 |

   （雙底線 `__` 對應 `appsettings.json` 裡的巢狀設定鍵，例如 `MongoDb__ConnectionString` = `MongoDb:ConnectionString`。）

4. 儲存後 Render 會自動開始建置並部署。

### 照片儲存與 Render Disk（重要）

Render 的檔案系統預設是**暫時性的**——服務重啟或重新部署後，寫入磁碟的檔案會消失。拍照上傳的照片實體檔案是存在磁碟（`Uploads:RootPath`），如果不處理，每次重新部署都會遺失所有照片檔案（資料庫裡的照片紀錄還在，但檔案打不開）。

解法：在 Render 該服務的「Disks」設定加掛一個 **Persistent Disk**（付費方案才能加），掛載路徑例如 `/var/data`，然後把環境變數 `Uploads__RootPath` 設成 `/var/data/uploads`。若暫時只是測試、可以接受照片會不見，Free 方案不掛 Disk 也能先跑起來（`Uploads__RootPath` 留空，會退回用預設的 `App_Data/uploads`，一樣是暫時性的）。

---

## 3. 驗證部署

部署完成後，用瀏覽器開啟 Render 給的網址（例如 `https://xxx.onrender.com`），應該會看到登入畫面。用 `Admin__InitialPassword` 設定的帳號密碼登入，測試：新增站點、切換月份、輸入本期讀數、拍照上傳、匯出 CSV/PDF、匯出完整備份、匯入備份還原、清除全部資料、登出。

---

## 4. 更新程式

Render 預設會在 GitHub repo 的預設分支有新的 commit 時自動重新部署（Auto-Deploy，可在服務設定裡開關）。也可以在 Render Dashboard 手動按「Manual Deploy」。

---

## 5. 日常維運備忘

- **備份**：「選單 → 匯出完整備份」只包含站點與各月讀數，**不含照片檔案**。照片實際存放在 Render Disk（若有掛載）或暫時性磁碟，請額外考慮 MongoDB Atlas 內建的自動備份（付費層有）或定期 `mongodump` 保護資料庫本身。
- **忘記密碼**：目前沒有「忘記密碼」流程。若管理員密碼遺失，需要到 Atlas 的 Collections 瀏覽器裡刪除 `adminUsers` collection 裡的那筆文件，讓應用程式下次啟動時用環境變數 `Admin__InitialUsername`/`Admin__InitialPassword` 重新建立（記得改一組新密碼後再重啟服務）。
- **自訂網域**：Render 服務設定裡的「Custom Domains」可以掛自己的網域，記得同步更新 DNS。
