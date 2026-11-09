/**
 * 碧潭能源管理系統 雲端後端（Google Apps Script ＋ Google 試算表 ＋ Google 雲端硬碟）
 *
 * 取代原本的 Render 伺服器＋MongoDB：網頁把每個 API 請求包成
 *   POST { m: "GET"|"POST"|"PUT"|"DELETE", p: "/sites/…?month=…", t: 登入權杖, b: 內容 }
 * 送到這個「網頁應用程式」，這裡照原本後端的規則處理後回傳 { s: HTTP 狀態碼, b: 內容 }。
 * 路徑、欄位、權限、錯誤訊息都跟原本的 ASP.NET 後端一樣，前端只換了傳送方式。
 *
 * 部署：執行身分＝我、誰可以存取＝所有人。資料都存在這份試算表裡，請勿手動修改各工作表內容。
 */

const APP_VERSION = 1;
const TOKEN_DAYS = 14;                // 跟原本 Cookie 的有效期一樣
const ONLINE_WINDOW_MS = 90 * 1000;   // 雲端版每 60 秒回報一次，超過這個時間沒回報就視為離線
const AUDIT_KEEP = 1000;              // 操作日誌最多保留幾筆（畫面最多顯示 500 筆）
const CHUNK = 45000;                  // 每格上限 5 萬字，大型 JSON 分段存在同一列的多個欄位
const MAX_FAILED_ATTEMPTS = 5;
const LOCKOUT_MINUTES = 15;
const MAX_PHOTO_BYTES = 20000000;

// ---------------------------------------------------------------- 進入點

function doGet() {
  return out_({ s: 200, b: { ok: true, app: "BiTanEnergy", ver: APP_VERSION } });
}

function doPost(e) {
  let req = {};
  try { req = JSON.parse((e && e.postData && e.postData.contents) || "{}"); } catch (x) {}
  let res;
  try {
    res = route_(String(req.m || "GET").toUpperCase(), String(req.p || ""), req.b == null ? {} : req.b, String(req.t || ""));
  } catch (err) {
    res = err && err.httpStatus ? { s: err.httpStatus, b: { message: err.message } }
      : { s: 500, b: { message: "雲端發生錯誤：" + (err && err.message ? err.message : err) } };
  } finally {
    releaseLock_();
  }
  return out_(res);
}

function out_(o) {
  return ContentService.createTextOutput(JSON.stringify(o)).setMimeType(ContentService.MimeType.JSON);
}

function fail_(status, message) {
  const e = new Error(message);
  e.httpStatus = status;
  throw e;
}

function forbid_() { fail_(403, "您沒有權限執行這個動作"); }
function notFound_() { fail_(404, "找不到這筆資料"); }

const ok_ = (b) => ({ s: 200, b: b === undefined ? null : b });

// ---------------------------------------------------------------- 路由

// [方法, 路徑, 權限("public"|"user"|"admin"), 處理函式]
const ROUTES = [
  ["POST", /^\/auth\/login$/, "public", (c) => login_(c)],
  ["POST", /^\/setup\/import$/, "public", (c) => setupImport_(c)],

  ["POST", /^\/auth\/logout$/, "user", () => ok_()],
  ["GET", /^\/auth\/me$/, "user", (c) => ok_(Object.assign(meOut_(c.user), { token: createToken_(c.user) }))],
  ["POST", /^\/auth\/change-password$/, "user", (c) => changePassword_(c)],
  ["POST", /^\/auth\/heartbeat$/, "user", (c) => heartbeat_(c)],
  ["POST", /^\/auth\/offline$/, "user", (c) => offline_(c)],
  ["PUT", /^\/auth\/display-name$/, "user", (c) => updateDisplayName_(c)],

  ["GET", /^\/accounts$/, "admin", () => getAccounts_()],
  ["POST", /^\/accounts$/, "admin", (c) => createAccount_(c)],
  ["PUT", /^\/accounts\/([^/]+)$/, "admin", (c) => updateAccount_(c, c.args[0])],
  ["DELETE", /^\/accounts\/([^/]+)$/, "admin", (c) => deleteAccount_(c, c.args[0])],
  ["POST", /^\/accounts\/([^/]+)\/unlock$/, "admin", (c) => unlockAccount_(c, c.args[0])],

  ["GET", /^\/audit-logs$/, "admin", (c) => getAuditLogs_(c)],

  ["GET", /^\/backup\/export$/, "admin", () => exportBackup_()],
  ["POST", /^\/backup\/import$/, "admin", (c) => importBackup_(c)],
  ["POST", /^\/backup\/clear-all$/, "admin", (c) => clearAll_(c)],

  ["GET", /^\/sites$/, "user", (c) => getSites_(c)],
  ["POST", /^\/sites$/, "user", (c) => createSite_(c)],
  ["PUT", /^\/sites\/([^/]+)$/, "user", (c) => updateSite_(c, c.args[0])],
  ["DELETE", /^\/sites\/([^/]+)$/, "user", (c) => deleteSite_(c, c.args[0])],

  ["GET", /^\/stores$/, "admin", () => getStores_()],
  ["POST", /^\/stores$/, "admin", (c) => createStore_(c)],
  ["PUT", /^\/stores\/([^/]+)$/, "admin", (c) => updateStore_(c, c.args[0])],
  ["DELETE", /^\/stores\/([^/]+)$/, "admin", (c) => deleteStore_(c, c.args[0])],

  ["GET", /^\/readings\/all$/, "user", (c) => getAllReadings_(c)],
  ["GET", /^\/readings$/, "user", (c) => getReadingsByMonth_(c)],
  ["DELETE", /^\/readings$/, "admin", (c) => deleteMonth_(c)],
  ["PUT", /^\/readings\/([^/]+)\/prev-override$/, "user", (c) => setPrevOverride_(c, c.args[0])],
  ["PUT", /^\/readings\/([^/]+)$/, "user", (c) => upsertReading_(c, c.args[0])],
  ["POST", /^\/readings\/([^/]+)\/photos$/, "user", (c) => uploadPhoto_(c, c.args[0])],

  ["POST", /^\/photos\/batch$/, "user", (c) => getPhotos_(c)],
  ["DELETE", /^\/photos\/([^/]+)$/, "user", (c) => deletePhoto_(c, c.args[0])],

  ["GET", /^\/daily-readings\/([^/]+)$/, "user", (c) => getDaily_(c, c.args[0])],
  ["PUT", /^\/daily-readings\/([^/]+)$/, "user", (c) => upsertDaily_(c, c.args[0])],
  ["DELETE", /^\/daily-readings\/([^/]+)$/, "user", (c) => deleteDaily_(c, c.args[0])],
];

function parseQuery_(s) {
  const q = {};
  String(s || "").split("&").forEach((kv) => {
    if (!kv) return;
    const i = kv.indexOf("=");
    const k = i < 0 ? kv : kv.slice(0, i);
    q[decodeURIComponent(k)] = i < 0 ? "" : decodeURIComponent(kv.slice(i + 1));
  });
  return q;
}

function route_(method, fullPath, body, token) {
  const q = fullPath.indexOf("?");
  const path = (q < 0 ? fullPath : fullPath.slice(0, q)).replace(/^\/api(?=\/)/, "");
  const query = parseQuery_(q < 0 ? "" : fullPath.slice(q + 1));
  for (const [m, re, level, handler] of ROUTES) {
    if (m !== method) continue;
    const match = re.exec(path);
    if (!match) continue;
    const ctx = { body: body, query: query, args: match.slice(1).map(decodeURIComponent), user: null, token: token };
    if (level !== "public") {
      ctx.user = authenticate_(token);
      if (level === "admin" && ctx.user.role !== "Admin") forbid_();
    }
    return handler(ctx);
  }
  fail_(404, "找不到這個功能");
}

// ---------------------------------------------------------------- 試算表儲存層
// 每種資料一張工作表，一筆一列：A 欄＝鍵、B 欄＝保留、C 欄之後＝內容 JSON 分段。
// 每格都加 ~ 前綴並設成純文字，避免數字、日期或 = 開頭的內容被試算表自動轉換。

let ss_ = null;
const memo_ = {};

function book_() {
  return ss_ || (ss_ = SpreadsheetApp.getActive());
}

function sheet_(name) {
  const b = book_();
  return b.getSheetByName(name) || b.insertSheet(name);
}

function cellIn_(v) { return "~" + v; }
function cellOut_(v) { return v === "" || v == null ? "" : String(v).slice(1); }

// 讀出整張表：[{ key, row, data }]；keysOnly 只讀 A 欄（只要找列號時不必解析內容）
function readAll_(name, keysOnly) {
  const cacheKey = name + (keysOnly ? ":k" : "");
  if (memo_[cacheKey]) return memo_[cacheKey];
  const s = sheet_(name), n = s.getLastRow();
  const list = [];
  if (n > 0) {
    const cols = keysOnly ? 1 : Math.max(3, s.getLastColumn());
    const values = s.getRange(1, 1, n, cols).getValues();
    for (let i = 0; i < values.length; i++) {
      const r = values[i];
      const key = cellOut_(r[0]);
      if (!key) continue;
      const rec = { key: key, row: i + 1 };
      if (!keysOnly) rec.data = parseChunks_(r);
      list.push(rec);
    }
  }
  memo_[cacheKey] = list;
  return list;
}

function parseChunks_(r) {
  let txt = "";
  for (let c = 2; c < r.length; c++) txt += cellOut_(r[c]);
  return txt ? JSON.parse(txt) : null;
}

// 新工作表只有 1000 列 × 26 欄，寫到外面會出錯：不夠就先加大
function ensureSize_(s, rows, cols) {
  const maxR = s.getMaxRows(), maxC = s.getMaxColumns();
  if (rows > maxR) s.insertRowsAfter(maxR, rows - maxR + 500);
  if (cols > maxC) s.insertColumnsAfter(maxC, cols - maxC);
}

function dataOf_(name) {
  return readAll_(name).map((r) => r.data);
}

function findRec_(name, key) {
  return readAll_(name).find((r) => r.key === String(key)) || null;
}

function rowCells_(key, data) {
  const txt = JSON.stringify(data);
  const row = [cellIn_(String(key)), cellIn_("")];
  for (let i = 0; i < txt.length; i += CHUNK) row.push(cellIn_(txt.slice(i, i + CHUNK)));
  return row;
}

function put_(name, key, data) {
  const s = sheet_(name);
  const row = rowCells_(key, data);
  const existing = (memo_[name] || readAll_(name, true)).find((r) => r.key === String(key));
  let rowIndex;
  if (existing) {
    rowIndex = existing.row;
    const width = Math.max(row.length, s.getLastColumn());
    while (row.length < width) row.push("");
  } else {
    rowIndex = s.getLastRow() + 1;
  }
  ensureSize_(s, rowIndex, row.length);
  const range = s.getRange(rowIndex, 1, 1, row.length);
  range.setNumberFormat("@");
  range.setValues([row]);
  invalidate_(name);
}

// 大量新增（匯入用）：一次寫進去，比一筆一筆快很多。呼叫前工作表應已清空。
function bulkAppend_(name, items) {
  if (!items.length) return;
  const s = sheet_(name);
  const rows = items.map((it) => rowCells_(it.key, it.data));
  const width = rows.reduce((w, r) => Math.max(w, r.length), 3);
  rows.forEach((r) => { while (r.length < width) r.push(""); });
  const start = s.getLastRow() + 1;
  ensureSize_(s, start + rows.length - 1, width);
  const range = s.getRange(start, 1, rows.length, width);
  range.setNumberFormat("@");
  range.setValues(rows);
  invalidate_(name);
}

function remove_(name, key) {
  const rec = readAll_(name, true).find((r) => r.key === String(key));
  if (!rec) return false;
  sheet_(name).deleteRow(rec.row);
  invalidate_(name);
  return true;
}

// 一次刪掉多筆（由下往上刪，列號才不會跑掉）
function removeMany_(name, keys) {
  const want = {};
  keys.forEach((k) => { want[String(k)] = 1; });
  const rows = readAll_(name, true).filter((r) => want[r.key]).map((r) => r.row).sort((a, b) => b - a);
  const s = sheet_(name);
  rows.forEach((r) => s.deleteRow(r));
  if (rows.length) invalidate_(name);
}

function clear_(name) {
  sheet_(name).clear();
  invalidate_(name);
}

function invalidate_(name) {
  delete memo_[name];
  delete memo_[name + ":k"];
}

// ---------------------------------------------------------------- 鎖、編號、時間

let lock = null;

// 會寫入資料的動作一律先上鎖，避免兩台裝置同時存檔互相蓋掉。
function lock_() {
  if (lock) return;
  const l = LockService.getScriptLock();
  if (!l.tryLock(30000)) fail_(503, "雲端忙碌中，請稍後再試");
  lock = l;
  for (const k in memo_) delete memo_[k]; // 等鎖期間別人可能改過，重新讀
}

function releaseLock_() {
  if (!lock) return;
  const l = lock;
  lock = null;
  l.releaseLock();
}

// 跟 MongoDB ObjectId 一樣是 24 位十六進位、前 8 位是時間，新資料排序一定在舊資料後面
function newId_() {
  const secs = Math.floor(Date.now() / 1000).toString(16);
  return (secs + Utilities.getUuid().replace(/-/g, "")).slice(0, 24).toLowerCase();
}

function nowIso_() {
  return new Date().toISOString();
}

function blank_(s) { return s == null || String(s).trim() === ""; }
function isMonth_(s) { return /^\d{4}-(0[1-9]|1[0-2])$/.test(String(s || "")); }
function isDate_(s) { return /^\d{4}-\d{2}-\d{2}$/.test(String(s || "")); }
function numOrNull_(v) { return v === null || v === undefined || v === "" ? null : Number(v); }

function requireMonth_(c) {
  if (!isMonth_(c.query.month)) fail_(400, "月份格式錯誤");
  return c.query.month;
}

// ---------------------------------------------------------------- 一次性搬家（MongoDB → 試算表）

// 把原本 MongoDB 匯出的全部資料寫進來。只有在還沒有任何帳號時才允許，搬完就自動失效。
function setupImport_(c) {
  lock_();
  if (readAll_("Users", true).length) fail_(403, "雲端已經有資料，不能再匯入");
  const b = c.body || {};
  if (!(b.users || []).some((u) => u.role === "Admin")) fail_(400, "匯入資料裡沒有管理員帳號");
  ["Users", "Stores", "Sites", "Readings", "Daily", "AuditLogs"].forEach(clear_);
  bulkAppend_("Users", (b.users || []).map((u) => ({ key: u.id, data: u })));
  bulkAppend_("Stores", (b.stores || []).map((s) => ({ key: s.id, data: s })));
  bulkAppend_("Sites", (b.sites || []).map((s) => ({ key: s.id, data: s })));
  bulkAppend_("Readings", (b.readings || []).map((r) => ({ key: readingKey_(r.siteId, r.monthKey), data: r })));
  bulkAppend_("Daily", (b.dailyReadings || []).map((r) => ({ key: r.siteId + "|" + r.date, data: r })));
  bulkAppend_("AuditLogs", (b.auditLogs || []).slice(-AUDIT_KEEP).map((a) => ({ key: a.id, data: a })));
  return ok_({ message: "匯入完成", users: (b.users || []).length, sites: (b.sites || []).length, readings: (b.readings || []).length });
}

// ---------------------------------------------------------------- 密碼（與原本 ASP.NET PasswordHasher V3 相同：PBKDF2-HMAC-SHA512）
// Apps Script 沒有內建 PBKDF2，也沒辦法呼叫 10 萬次原生 HMAC，所以用 JS 寫。舊密碼可以直接沿用。

const K512_ = Int32Array.from([
  0x428a2f98, 0xd728ae22, 0x71374491, 0x23ef65cd, 0xb5c0fbcf, 0xec4d3b2f, 0xe9b5dba5, 0x8189dbbc,
  0x3956c25b, 0xf348b538, 0x59f111f1, 0xb605d019, 0x923f82a4, 0xaf194f9b, 0xab1c5ed5, 0xda6d8118,
  0xd807aa98, 0xa3030242, 0x12835b01, 0x45706fbe, 0x243185be, 0x4ee4b28c, 0x550c7dc3, 0xd5ffb4e2,
  0x72be5d74, 0xf27b896f, 0x80deb1fe, 0x3b1696b1, 0x9bdc06a7, 0x25c71235, 0xc19bf174, 0xcf692694,
  0xe49b69c1, 0x9ef14ad2, 0xefbe4786, 0x384f25e3, 0x0fc19dc6, 0x8b8cd5b5, 0x240ca1cc, 0x77ac9c65,
  0x2de92c6f, 0x592b0275, 0x4a7484aa, 0x6ea6e483, 0x5cb0a9dc, 0xbd41fbd4, 0x76f988da, 0x831153b5,
  0x983e5152, 0xee66dfab, 0xa831c66d, 0x2db43210, 0xb00327c8, 0x98fb213f, 0xbf597fc7, 0xbeef0ee4,
  0xc6e00bf3, 0x3da88fc2, 0xd5a79147, 0x930aa725, 0x06ca6351, 0xe003826f, 0x14292967, 0x0a0e6e70,
  0x27b70a85, 0x46d22ffc, 0x2e1b2138, 0x5c26c926, 0x4d2c6dfc, 0x5ac42aed, 0x53380d13, 0x9d95b3df,
  0x650a7354, 0x8baf63de, 0x766a0abb, 0x3c77b2a8, 0x81c2c92e, 0x47edaee6, 0x92722c85, 0x1482353b,
  0xa2bfe8a1, 0x4cf10364, 0xa81a664b, 0xbc423001, 0xc24b8b70, 0xd0f89791, 0xc76c51a3, 0x0654be30,
  0xd192e819, 0xd6ef5218, 0xd6990624, 0x5565a910, 0xf40e3585, 0x5771202a, 0x106aa070, 0x32bbd1b8,
  0x19a4c116, 0xb8d2d0c8, 0x1e376c08, 0x5141ab53, 0x2748774c, 0xdf8eeb99, 0x34b0bcb5, 0xe19b48a8,
  0x391c0cb3, 0xc5c95a63, 0x4ed8aa4a, 0xe3418acb, 0x5b9cca4f, 0x7763e373, 0x682e6ff3, 0xd6b2b8a3,
  0x748f82ee, 0x5defb2fc, 0x78a5636f, 0x43172f60, 0x84c87814, 0xa1f0ab72, 0x8cc70208, 0x1a6439ec,
  0x90befffa, 0x23631e28, 0xa4506ceb, 0xde82bde9, 0xbef9a3f7, 0xb2c67915, 0xc67178f2, 0xe372532b,
  0xca273ece, 0xea26619c, 0xd186b8c7, 0x21c0c207, 0xeada7dd6, 0xcde0eb1e, 0xf57d4f7f, 0xee6ed178,
  0x06f067aa, 0x72176fba, 0x0a637dc5, 0xa2c898a6, 0x113f9804, 0xbef90dae, 0x1b710b35, 0x131c471b,
  0x28db77f5, 0x23047d84, 0x32caab7b, 0x40c72493, 0x3c9ebe0a, 0x15c9bebc, 0x431d67c4, 0x9c100d4c,
  0x4cc5d4be, 0xcb3e42b6, 0x597f299c, 0xfc657e2a, 0x5fcb6fab, 0x3ad6faec, 0x6c44198c, 0x4a475817,
]);
const IV512_ = [0x6a09e667, 0xf3bcc908, 0xbb67ae85, 0x84caa73b, 0x3c6ef372, 0xfe94f82b, 0xa54ff53a, 0x5f1d36f1,
  0x510e527f, 0xade682d1, 0x9b05688c, 0x2b3e6c1f, 0x1f83d9ab, 0xfb41bd6b, 0x5be0cd19, 0x137e2179];
const W512_ = new Int32Array(160);

// H：16 個 32 位元字（高、低交錯）；W 的前 32 個字＝一個 128 位元組區塊。64 位元運算用高低兩半手動進位。
function compress512_(H, W) {
  for (let t = 32; t < 160; t += 2) {
    let h = W[t - 30], l = W[t - 29];
    const s0h = ((h >>> 1) | (l << 31)) ^ ((h >>> 8) | (l << 24)) ^ (h >>> 7);
    const s0l = ((l >>> 1) | (h << 31)) ^ ((l >>> 8) | (h << 24)) ^ ((l >>> 7) | (h << 25));
    h = W[t - 4]; l = W[t - 3];
    const s1h = ((h >>> 19) | (l << 13)) ^ ((l >>> 29) | (h << 3)) ^ (h >>> 6);
    const s1l = ((l >>> 19) | (h << 13)) ^ ((h >>> 29) | (l << 3)) ^ ((l >>> 6) | (h << 26));
    const lo = (s1l >>> 0) + (W[t - 13] >>> 0) + (s0l >>> 0) + (W[t - 31] >>> 0);
    W[t + 1] = lo | 0;
    W[t] = (s1h + W[t - 14] + s0h + W[t - 32] + ((lo / 4294967296) | 0)) | 0;
  }
  let ah = H[0], al = H[1], bh = H[2], bl = H[3], ch = H[4], cl = H[5], dh = H[6], dl = H[7];
  let eh = H[8], el = H[9], fh = H[10], fl = H[11], gh = H[12], gl = H[13], hh = H[14], hl = H[15];
  for (let t = 0; t < 160; t += 2) {
    const S1h = ((eh >>> 14) | (el << 18)) ^ ((eh >>> 18) | (el << 14)) ^ ((el >>> 9) | (eh << 23));
    const S1l = ((el >>> 14) | (eh << 18)) ^ ((el >>> 18) | (eh << 14)) ^ ((eh >>> 9) | (el << 23));
    const chh = (eh & fh) ^ (~eh & gh), chl = (el & fl) ^ (~el & gl);
    const t1l = (hl >>> 0) + (S1l >>> 0) + (chl >>> 0) + (K512_[t + 1] >>> 0) + (W[t + 1] >>> 0);
    const t1h = (hh + S1h + chh + K512_[t] + W[t] + ((t1l / 4294967296) | 0)) | 0;
    const S0h = ((ah >>> 28) | (al << 4)) ^ ((al >>> 2) | (ah << 30)) ^ ((al >>> 7) | (ah << 25));
    const S0l = ((al >>> 28) | (ah << 4)) ^ ((ah >>> 2) | (al << 30)) ^ ((ah >>> 7) | (al << 25));
    const majh = (ah & bh) ^ (ah & ch) ^ (bh & ch), majl = (al & bl) ^ (al & cl) ^ (bl & cl);
    const t2l = (S0l >>> 0) + (majl >>> 0);
    const t2h = (S0h + majh + ((t2l / 4294967296) | 0)) | 0;
    hh = gh; hl = gl; gh = fh; gl = fl; fh = eh; fl = el;
    const el2 = (dl >>> 0) + (t1l >>> 0);
    eh = (dh + t1h + ((el2 / 4294967296) | 0)) | 0; el = el2 | 0;
    dh = ch; dl = cl; ch = bh; cl = bl; bh = ah; bl = al;
    const al2 = ((t1l | 0) >>> 0) + (t2l >>> 0);
    ah = (t1h + t2h + ((al2 / 4294967296) | 0)) | 0; al = al2 | 0;
  }
  const add = [ah, al, bh, bl, ch, cl, dh, dl, eh, el, fh, fl, gh, gl, hh, hl];
  for (let i = 0; i < 16; i += 2) {
    const lo = (H[i + 1] >>> 0) + (add[i + 1] >>> 0);
    H[i] = (H[i] + add[i] + ((lo / 4294967296) | 0)) | 0;
    H[i + 1] = lo | 0;
  }
}

// 從狀態 H0（之前已處理 prefixLen 位元組）繼續雜湊 bytes 並補齊結尾，回傳新的 16 個字
function sha512From_(H0, bytes, prefixLen) {
  const H = Int32Array.from(H0);
  const total = prefixLen + bytes.length;
  const padded = new Uint8Array(Math.ceil((bytes.length + 17) / 128) * 128);
  padded.set(bytes);
  padded[bytes.length] = 0x80;
  const bits = total * 8;
  padded[padded.length - 4] = (bits >>> 24) & 255; padded[padded.length - 3] = (bits >>> 16) & 255;
  padded[padded.length - 2] = (bits >>> 8) & 255; padded[padded.length - 1] = bits & 255;
  padded[padded.length - 5] = Math.floor(bits / 0x100000000) & 255;
  for (let off = 0; off < padded.length; off += 128) {
    for (let i = 0; i < 32; i++) {
      const p = off + i * 4;
      W512_[i] = (padded[p] << 24) | (padded[p + 1] << 16) | (padded[p + 2] << 8) | padded[p + 3];
    }
    compress512_(H, W512_);
  }
  return H;
}

function wordsToBytes_(H, len) {
  const out = new Uint8Array(len);
  for (let i = 0; i < len; i++) out[i] = (H[i >> 2] >>> (24 - (i % 4) * 8)) & 255;
  return out;
}

function hmacStates512_(keyBytes) {
  const key = keyBytes.length > 128 ? wordsToBytes_(sha512From_(IV512_, keyBytes, 0), 64) : keyBytes;
  const k = new Uint8Array(128);
  k.set(key);
  const state = (pad) => {
    for (let i = 0; i < 32; i++) {
      W512_[i] = ((k[i * 4] ^ pad) << 24) | ((k[i * 4 + 1] ^ pad) << 16) | ((k[i * 4 + 2] ^ pad) << 8) | (k[i * 4 + 3] ^ pad);
    }
    const H = Int32Array.from(IV512_);
    compress512_(H, W512_);
    return H;
  };
  return { inner: state(0x36), outer: state(0x5c) };
}

function pbkdf2Sha512_(passwordBytes, salt, iterations, dkLen) {
  const st = hmacStates512_(passwordBytes);
  const out = new Uint8Array(dkLen);
  for (let block = 1, done = 0; done < dkLen; block++) {
    const msg = new Uint8Array(salt.length + 4);
    msg.set(salt);
    msg[salt.length] = block >>> 24; msg[salt.length + 1] = (block >>> 16) & 255;
    msg[salt.length + 2] = (block >>> 8) & 255; msg[salt.length + 3] = block & 255;
    let U = sha512From_(st.outer, wordsToBytes_(sha512From_(st.inner, msg, 128), 64), 128);
    const T = Int32Array.from(U);
    // 之後每一輪的輸入固定 64 位元組：內外層各只要壓縮一個區塊
    for (let it = 1; it < iterations; it++) {
      for (let pass = 0; pass < 2; pass++) {
        for (let i = 0; i < 16; i++) W512_[i] = U[i];
        W512_[16] = 0x80000000;
        for (let i = 17; i < 31; i++) W512_[i] = 0;
        W512_[31] = (128 + 64) * 8;
        const H = Int32Array.from(pass === 0 ? st.inner : st.outer);
        compress512_(H, W512_);
        U = H;
      }
      for (let i = 0; i < 16; i++) T[i] ^= U[i];
    }
    const bytes = wordsToBytes_(T, 64);
    for (let i = 0; i < 64 && done < dkLen; i++) out[done++] = bytes[i];
  }
  return out;
}

function utf8_(s) {
  const bin = unescape(encodeURIComponent(s));
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}

const B64_ = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

function b64Encode_(bytes) {
  let s = "";
  for (let i = 0; i < bytes.length; i += 3) {
    const n = (bytes[i] << 16) | ((bytes[i + 1] || 0) << 8) | (bytes[i + 2] || 0);
    s += B64_[n >> 18] + B64_[(n >> 12) & 63] + (i + 1 < bytes.length ? B64_[(n >> 6) & 63] : "=") + (i + 2 < bytes.length ? B64_[n & 63] : "=");
  }
  return s;
}

function b64Decode_(s) {
  s = String(s).replace(/[^A-Za-z0-9+/]/g, "");
  const out = [];
  for (let i = 0; i < s.length; i += 4) {
    const n = (B64_.indexOf(s[i]) << 18) | (B64_.indexOf(s[i + 1]) << 12) | ((B64_.indexOf(s[i + 2]) & 63) << 6) | (B64_.indexOf(s[i + 3]) & 63);
    out.push(n >> 16 & 255);
    if (i + 2 < s.length) out.push(n >> 8 & 255);
    if (i + 3 < s.length) out.push(n & 255);
  }
  return Uint8Array.from(out);
}

function randomBytes_(n) {
  const out = new Uint8Array(n);
  let hex = "";
  while (hex.length < n * 2) hex += Utilities.getUuid().replace(/-/g, "");
  for (let i = 0; i < n; i++) out[i] = parseInt(hex.substr(i * 2, 2), 16);
  return out;
}

const PBKDF2_ITERATIONS = 100000;
const PRF_SHA512 = 2;

function putU32_(a, off, v) { a[off] = v >>> 24; a[off + 1] = (v >>> 16) & 255; a[off + 2] = (v >>> 8) & 255; a[off + 3] = v & 255; }
function getU32_(a, off) { return ((a[off] << 24) | (a[off + 1] << 16) | (a[off + 2] << 8) | a[off + 3]) >>> 0; }

function hashPassword_(password) {
  const salt = randomBytes_(16);
  const key = pbkdf2Sha512_(utf8_(password), salt, PBKDF2_ITERATIONS, 32);
  const out = new Uint8Array(13 + salt.length + key.length);
  out[0] = 1;
  putU32_(out, 1, PRF_SHA512);
  putU32_(out, 5, PBKDF2_ITERATIONS);
  putU32_(out, 9, salt.length);
  out.set(salt, 13);
  out.set(key, 13 + salt.length);
  return b64Encode_(out);
}

function verifyPassword_(password, stored) {
  const a = b64Decode_(stored || "");
  if (a.length < 13 || a[0] !== 1 || getU32_(a, 1) !== PRF_SHA512) return false;
  const iterations = getU32_(a, 5), saltLen = getU32_(a, 9);
  if (!iterations || saltLen < 8 || a.length <= 13 + saltLen) return false;
  const expected = a.subarray(13 + saltLen);
  const actual = pbkdf2Sha512_(utf8_(password), a.subarray(13, 13 + saltLen), iterations, expected.length);
  let diff = 0;
  for (let i = 0; i < expected.length; i++) diff |= expected[i] ^ actual[i];
  return diff === 0;
}

// ---------------------------------------------------------------- 登入權杖（HMAC-SHA256 簽章）

function tokenSecret_() {
  const props = PropertiesService.getScriptProperties();
  let s = props.getProperty("tokenSecret");
  if (!s) {
    s = b64Encode_(randomBytes_(32));
    props.setProperty("tokenSecret", s);
  }
  return s;
}

function sign_(text) {
  return Utilities.base64EncodeWebSafe(Utilities.computeHmacSha256Signature(text, tokenSecret_())).replace(/=+$/, "");
}

function createToken_(user) {
  const body = user.id + "." + (Date.now() + TOKEN_DAYS * 86400000);
  return body + "." + sign_(body);
}

// 每次請求都重新對照帳號（跟原本一樣），管理員改了指派門市或刪掉帳號會立刻生效
function authenticate_(token) {
  const parts = String(token || "").split(".");
  if (parts.length !== 3) fail_(401, "請先登入");
  if (sign_(parts[0] + "." + parts[1]) !== parts[2]) fail_(401, "登入已失效，請重新登入");
  if (Date.now() > Number(parts[1])) fail_(401, "登入已過期，請重新登入");
  const user = userById_(parts[0]);
  if (!user) fail_(401, "帳號不存在");
  return user;
}

// ---------------------------------------------------------------- 帳號與權限

const RANK_ = { View: 0, Edit: 1, Full: 2 };

function users_() { return dataOf_("Users"); }
function userById_(id) { const r = findRec_("Users", id); return r ? r.data : null; }
function saveUser_(u) { put_("Users", u.id, u); }

// null＝不受限；管理員如果也勾選了門市，就跟一般使用者一樣只限定在那些門市
function allowedGroups_(user) {
  const assigned = user.assignedGroups || [];
  if (user.role === "Admin") return assigned.length ? assigned : null;
  return assigned;
}

function permissionOf_(user) {
  return user.role === "Admin" ? "Full" : (user.permissionLevel || "View");
}

function requirePermission_(user, required) {
  if ((RANK_[permissionOf_(user)] || 0) < RANK_[required]) forbid_();
}

function siteOf_(id) { const r = findRec_("Sites", id); return r ? r.data : null; }

function canAccessSite_(user, siteId) {
  const allowed = allowedGroups_(user);
  if (allowed === null) return true;
  const site = siteOf_(siteId);
  return !!site && allowed.indexOf(site.group) >= 0;
}

function requireSiteAccess_(user, siteId) {
  if (!canAccessSite_(user, siteId)) forbid_();
}

// 回傳看得到的站點 Id（對照表）；null＝不受限
function allowedSiteIds_(user) {
  const allowed = allowedGroups_(user);
  if (allowed === null) return null;
  const ids = {};
  dataOf_("Sites").forEach((s) => { if (allowed.indexOf(s.group) >= 0) ids[s.id] = 1; });
  return ids;
}

function meOut_(u) {
  return { username: u.username, displayName: u.displayName || "", role: u.role, assignedGroups: u.assignedGroups || [],
    permissionLevel: permissionOf_(u) };
}

// ---------------------------------------------------------------- /auth

function login_(c) {
  const username = c.body.username, password = c.body.password || "";
  let user = users_().find((u) => u.username === username) || null;
  if (!user) {
    lock_();
    audit_(username, "登入失敗", "帳號不存在");
    fail_(401, "帳號或密碼錯誤");
  }
  if (user.lockedUntil && Date.parse(user.lockedUntil) > Date.now()) {
    fail_(401, "密碼錯誤次數過多，帳號已鎖定，請 " + Math.ceil((Date.parse(user.lockedUntil) - Date.now()) / 60000) + " 分鐘後再試");
  }
  const good = verifyPassword_(password, user.passwordHash); // 很花時間，在上鎖之前做
  lock_();
  user = userById_(user.id);
  if (!user) fail_(401, "帳號或密碼錯誤");
  if (!good) {
    user.failedLoginCount = (user.failedLoginCount || 0) + 1;
    let message = "帳號或密碼錯誤";
    if (user.failedLoginCount >= MAX_FAILED_ATTEMPTS) {
      user.lockedUntil = new Date(Date.now() + LOCKOUT_MINUTES * 60000).toISOString();
      user.failedLoginCount = 0;
      message = "密碼錯誤次數過多，帳號已鎖定 " + LOCKOUT_MINUTES + " 分鐘";
      audit_(user.username, "帳號鎖定", "連續輸入錯誤密碼 " + MAX_FAILED_ATTEMPTS + " 次，鎖定 " + LOCKOUT_MINUTES + " 分鐘");
    }
    saveUser_(user);
    audit_(user.username, "登入失敗", "密碼錯誤");
    fail_(401, message);
  }
  if (user.failedLoginCount || user.lockedUntil) {
    user.failedLoginCount = 0;
    user.lockedUntil = null;
    saveUser_(user);
  }
  markSeen_(user.id);
  audit_(user.username, "登入成功", "");
  return ok_(Object.assign(meOut_(user), { token: createToken_(user) }));
}

function changePassword_(c) {
  if (!verifyPassword_(c.body.currentPassword || "", c.user.passwordHash)) fail_(400, "目前密碼不正確");
  const pw = c.body.newPassword;
  if (blank_(pw) || pw.length < 6) fail_(400, "新密碼至少需要 6 個字元");
  const hash = hashPassword_(pw);
  lock_();
  const u = userById_(c.user.id);
  u.passwordHash = hash;
  saveUser_(u);
  audit_(u.username, "修改密碼", "");
  return ok_();
}

function updateDisplayName_(c) {
  lock_();
  const u = userById_(c.user.id);
  u.displayName = String(c.body.displayName || "").trim();
  saveUser_(u);
  return ok_(meOut_(u));
}

// 在線狀態放在 Apps Script 快取，不寫進試算表（前端每分鐘回報一次，寫表太慢也沒必要）
function markSeen_(id) { CacheService.getScriptCache().put("seen:" + id, nowIso_(), 21600); }
function heartbeat_(c) { markSeen_(c.user.id); return ok_(); }
function offline_(c) { CacheService.getScriptCache().remove("seen:" + c.user.id); return ok_(); }

// ---------------------------------------------------------------- /accounts

function accountOut_(u, seen) {
  const last = seen["seen:" + u.id] || null;
  return { id: u.id, displayName: u.displayName || "", username: u.username, role: u.role, assignedGroups: u.assignedGroups || [],
    permissionLevel: permissionOf_(u), createdAt: u.createdAt, lockedUntil: u.lockedUntil || null, lastSeenAt: last,
    isOnline: !!last && Date.now() - Date.parse(last) < ONLINE_WINDOW_MS };
}

function seenMap_(list) {
  return list.length ? CacheService.getScriptCache().getAll(list.map((u) => "seen:" + u.id)) : {};
}

function getAccounts_() {
  const list = users_().slice().sort((a, b) => (a.createdAt < b.createdAt ? -1 : a.createdAt > b.createdAt ? 1 : 0));
  const seen = seenMap_(list);
  return ok_(list.map((u) => accountOut_(u, seen)));
}

function validateAccount_(b) {
  if (blank_(b.username)) fail_(400, "請輸入帳號名稱");
  if (b.role !== "Admin" && b.role !== "User") fail_(400, "角色不正確");
  if (!Object.prototype.hasOwnProperty.call(RANK_, b.permissionLevel)) fail_(400, "權限等級不正確");
}

function createAccount_(c) {
  const b = c.body;
  if (blank_(b.username)) fail_(400, "請輸入帳號名稱");
  if (blank_(b.password) || b.password.length < 6) fail_(400, "密碼至少需要 6 個字元");
  validateAccount_(b);
  const hash = hashPassword_(b.password);
  lock_();
  if (users_().some((u) => u.username === b.username)) fail_(400, "這個帳號名稱已經存在");
  const u = { id: newId_(), displayName: String(b.displayName || "").trim(), username: b.username, passwordHash: hash, role: b.role,
    assignedGroups: b.assignedGroups || [], permissionLevel: b.role === "Admin" ? "Full" : b.permissionLevel,
    createdAt: nowIso_(), failedLoginCount: 0, lockedUntil: null };
  saveUser_(u);
  audit_(c.user.username, "新增帳號", u.username);
  return ok_(accountOut_(u, {}));
}

function updateAccount_(c, id) {
  const b = c.body;
  validateAccount_(b);
  if (!blank_(b.newPassword) && b.newPassword.length < 6) fail_(400, "密碼至少需要 6 個字元");
  const hash = blank_(b.newPassword) ? null : hashPassword_(b.newPassword);
  lock_();
  const u = userById_(id);
  if (!u) notFound_();
  if (b.username !== u.username && users_().some((x) => x.username === b.username && x.id !== id)) fail_(400, "這個帳號名稱已經存在");
  if (u.role === "Admin" && b.role !== "Admin" && users_().filter((x) => x.role === "Admin").length <= 1) fail_(400, "至少需要保留一個管理員帳號");
  u.displayName = String(b.displayName || "").trim();
  u.username = b.username;
  if (hash) u.passwordHash = hash;
  u.role = b.role;
  u.assignedGroups = b.assignedGroups || [];
  u.permissionLevel = b.role === "Admin" ? "Full" : b.permissionLevel;
  saveUser_(u);
  audit_(c.user.username, "修改帳號", u.username);
  return ok_(accountOut_(u, seenMap_([u])));
}

function deleteAccount_(c, id) {
  if (id === c.user.id) fail_(400, "無法刪除自己目前登入中的帳號");
  lock_();
  const u = userById_(id);
  if (!u) notFound_();
  if (u.role === "Admin" && users_().filter((x) => x.role === "Admin").length <= 1) fail_(400, "至少需要保留一個管理員帳號");
  remove_("Users", id);
  audit_(c.user.username, "刪除帳號", u.username);
  return ok_();
}

function unlockAccount_(c, id) {
  lock_();
  const u = userById_(id);
  if (!u) notFound_();
  u.failedLoginCount = 0;
  u.lockedUntil = null;
  saveUser_(u);
  audit_(c.user.username, "手動解鎖帳號", u.username);
  return ok_(accountOut_(u, seenMap_([u])));
}

// ---------------------------------------------------------------- /audit-logs

function audit_(actor, action, detail) {
  const id = newId_();
  put_("AuditLogs", id, { id: id, actorUsername: actor || "", action: action, detail: detail || "", createdAt: nowIso_() });
  const s = sheet_("AuditLogs"), n = s.getLastRow();
  if (n > AUDIT_KEEP + 100) {
    s.deleteRows(1, n - AUDIT_KEEP);
    invalidate_("AuditLogs");
  }
}

function getAuditLogs_(c) {
  const take = Math.min(500, Math.max(1, parseInt(c.query.limit, 10) || 100));
  return ok_(dataOf_("AuditLogs").slice().sort((a, b) => (a.createdAt < b.createdAt ? 1 : a.createdAt > b.createdAt ? -1 : 0)).slice(0, take));
}

// ---------------------------------------------------------------- /stores

function storesOf_() { return dataOf_("Stores"); }

// 站點用到還沒建立的門市名稱時自動補上，門市管理與帳號指派清單才看得到（原本是每次啟動時補）
function ensureStore_(name) {
  if (blank_(name) || storesOf_().some((s) => s.name === name)) return;
  const id = newId_();
  put_("Stores", id, { id: id, name: name, createdAt: nowIso_() });
}

function storeOut_(s, count) {
  return { id: s.id, name: s.name, createdAt: s.createdAt, siteCount: count };
}

function siteCountOf_(name) {
  return dataOf_("Sites").filter((s) => s.group === name).length;
}

function getStores_() {
  return ok_(storesOf_().slice().sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0)).map((s) => storeOut_(s, siteCountOf_(s.name))));
}

function createStore_(c) {
  const name = String(c.body.name || "").trim();
  if (!name) fail_(400, "請輸入門市名稱");
  lock_();
  if (storesOf_().some((s) => s.name === name)) fail_(400, "這個門市名稱已經存在");
  const store = { id: newId_(), name: name, createdAt: nowIso_() };
  put_("Stores", store.id, store);
  audit_(c.user.username, "新增門市", name);
  return ok_(storeOut_(store, 0));
}

// 重新命名：連帶更新底下站點的門市，以及有指派到這個門市的帳號
function updateStore_(c, id) {
  const newName = String(c.body.name || "").trim();
  if (!newName) fail_(400, "請輸入門市名稱");
  lock_();
  const rec = findRec_("Stores", id);
  if (!rec) notFound_();
  const store = rec.data;
  if (newName !== store.name) {
    if (storesOf_().some((s) => s.name === newName && s.id !== id)) fail_(400, "這個門市名稱已經存在");
    const oldName = store.name;
    store.name = newName;
    put_("Stores", id, store);
    dataOf_("Sites").filter((s) => s.group === oldName).forEach((s) => { s.group = newName; put_("Sites", s.id, s); });
    users_().filter((u) => (u.assignedGroups || []).indexOf(oldName) >= 0).forEach((u) => {
      u.assignedGroups = u.assignedGroups.map((g) => (g === oldName ? newName : g));
      saveUser_(u);
    });
    audit_(c.user.username, "修改門市", oldName + " → " + newName);
  }
  return ok_(storeOut_(store, siteCountOf_(store.name)));
}

function deleteStore_(c, id) {
  lock_();
  const rec = findRec_("Stores", id);
  if (!rec) notFound_();
  const count = siteCountOf_(rec.data.name);
  if (count > 0) fail_(400, "這個門市底下還有 " + count + " 個站點，請先刪除或改指派這些站點的群組後再刪除門市");
  remove_("Stores", id);
  users_().filter((u) => (u.assignedGroups || []).indexOf(rec.data.name) >= 0).forEach((u) => {
    u.assignedGroups = u.assignedGroups.filter((g) => g !== rec.data.name);
    saveUser_(u);
  });
  audit_(c.user.username, "刪除門市", rec.data.name);
  return ok_();
}

// ---------------------------------------------------------------- /sites

function getSites_(c) {
  const allowed = allowedGroups_(c.user);
  return ok_(dataOf_("Sites").filter((s) => allowed === null || allowed.indexOf(s.group) >= 0)
    .sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0)));
}

function validateSite_(b) {
  if (blank_(b.site)) fail_(400, "請輸入站點名稱");
  if ([4, 5, 6].indexOf(Number(b.meterDigits)) < 0) fail_(400, "儀表位數不正確");
}

function createSite_(c) {
  const b = c.body;
  validateSite_(b);
  requirePermission_(c.user, "Edit");
  const allowed = allowedGroups_(c.user);
  if (allowed !== null && allowed.indexOf(b.group) < 0) forbid_();
  lock_();
  const site = { id: newId_(), group: b.group || "", site: b.site, location: b.location || "", meterNo: b.meterNo || "", type: b.type || "water",
    basePrev: Number(b.basePrev) || 0, baseMonth: b.baseMonth || null, meterDigits: Number(b.meterDigits) };
  put_("Sites", site.id, site);
  ensureStore_(site.group);
  audit_(c.user.username, "新增站點", site.group + "／" + site.site);
  return ok_(site);
}

function updateSite_(c, id) {
  const b = c.body;
  validateSite_(b);
  requirePermission_(c.user, "Edit");
  lock_();
  const site = siteOf_(id);
  if (!site) notFound_();
  const allowed = allowedGroups_(c.user);
  if (allowed !== null && (allowed.indexOf(site.group) < 0 || allowed.indexOf(b.group) < 0)) forbid_();
  const basePrev = Number(b.basePrev) || 0;
  site.group = b.group || "";
  site.site = b.site;
  site.location = b.location || "";
  site.meterNo = b.meterNo || "";
  site.type = b.type || "water";
  // 只有「上期讀數」真的被改動時，才把基準月份移到現在選定的月份
  if (site.basePrev !== basePrev) site.baseMonth = b.baseMonth || null;
  site.basePrev = basePrev;
  site.meterDigits = Number(b.meterDigits);
  put_("Sites", id, site);
  ensureStore_(site.group);
  audit_(c.user.username, "修改站點", site.group + "／" + site.site);
  return ok_(site);
}

function deleteSite_(c, id) {
  requirePermission_(c.user, "Full");
  lock_();
  const site = siteOf_(id);
  if (!site) notFound_();
  const allowed = allowedGroups_(c.user);
  if (allowed !== null && allowed.indexOf(site.group) < 0) forbid_();
  const readings = dataOf_("Readings").filter((r) => r.siteId === id);
  const daily = dataOf_("Daily").filter((r) => r.siteId === id);
  removeMany_("Readings", readings.map((r) => readingKey_(r.siteId, r.monthKey)));
  removeMany_("Daily", daily.map((r) => r.siteId + "|" + r.date));
  remove_("Sites", id);
  trashPhotos_(photosOf_(readings));
  audit_(c.user.username, "刪除站點", site.group + "／" + site.site);
  return ok_();
}

// ---------------------------------------------------------------- /readings

function readingKey_(siteId, month) { return siteId + "|" + month; }

function photoOut_(p) { return { id: p.id, url: "/api/photos/" + p.id }; }

function sortedPhotos_(r) {
  return (r.photos || []).slice().sort((a, b) => (a.uploadedAt < b.uploadedAt ? -1 : a.uploadedAt > b.uploadedAt ? 1 : 0)).map(photoOut_);
}

function readingOut_(siteId, r) {
  return { siteId: siteId, curr: r ? (r.curr === undefined ? null : r.curr) : null, prevOverride: r ? (r.prevOverride === undefined ? null : r.prevOverride) : null,
    photos: r ? sortedPhotos_(r) : [] };
}

function photosOf_(readings) {
  const list = [];
  readings.forEach((r) => (r.photos || []).forEach((p) => list.push(p)));
  return list;
}

function getReadingsByMonth_(c) {
  const month = requireMonth_(c);
  const ids = allowedSiteIds_(c.user);
  const bySite = {};
  dataOf_("Readings").forEach((r) => { if (r.monthKey === month) bySite[r.siteId] = r; });
  return ok_(dataOf_("Sites").filter((s) => ids === null || ids[s.id]).map((s) => readingOut_(s.id, bySite[s.id])));
}

function getAllReadings_(c) {
  const ids = allowedSiteIds_(c.user);
  return ok_(dataOf_("Readings").filter((r) => ids === null || ids[r.siteId]).map((r) =>
    Object.assign(readingOut_(r.siteId, r), { monthKey: r.monthKey })));
}

function deleteMonth_(c) {
  const month = requireMonth_(c);
  lock_();
  const ids = allowedSiteIds_(c.user);
  const readings = dataOf_("Readings").filter((r) => r.monthKey === month && (ids === null || ids[r.siteId]));
  removeMany_("Readings", readings.map((r) => readingKey_(r.siteId, r.monthKey)));
  trashPhotos_(photosOf_(readings));
  audit_(c.user.username, "清除本期抄錄資料", month);
  return ok_();
}

function readingForWrite_(siteId, month) {
  const rec = findRec_("Readings", readingKey_(siteId, month));
  return rec ? rec.data : { siteId: siteId, monthKey: month, curr: null, prevOverride: null, updatedAt: nowIso_(), photos: [] };
}

function saveReading_(r) { put_("Readings", readingKey_(r.siteId, r.monthKey), r); }

function upsertReading_(c, siteId) {
  const month = requireMonth_(c);
  requireSiteAccess_(c.user, siteId);
  requirePermission_(c.user, "Edit");
  lock_();
  const r = readingForWrite_(siteId, month);
  r.curr = numOrNull_(c.body.curr);
  r.updatedAt = nowIso_();
  saveReading_(r);
  return ok_(readingOut_(siteId, r));
}

// 這個月專屬的「上期」覆蓋值，只有系統管理員能用（跟一般讀數編輯權限刻意分開）
function setPrevOverride_(c, siteId) {
  const month = requireMonth_(c);
  requireSiteAccess_(c.user, siteId);
  if (c.user.role !== "Admin") forbid_();
  lock_();
  const r = readingForWrite_(siteId, month);
  r.prevOverride = numOrNull_(c.body.value);
  saveReading_(r);
  return ok_(readingOut_(siteId, r));
}

// ---------------------------------------------------------------- 照片（Google 雲端硬碟）
// 照片放在雲端硬碟的「碧潭能源 照片」資料夾，維持私人（沒有公開連結）；讀數資料只記雲端檔案 Id。
// 用 Drive REST API 而不是 DriveApp：DriveApp 要整個雲端硬碟的權限，這裡只需要 drive.file
// （只碰得到這個程式自己建立的檔案）。

const DRIVE_FILES = "https://www.googleapis.com/drive/v3/files";
const DRIVE_UPLOAD = "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id";
const PHOTO_FOLDER = "碧潭能源 照片";
const PHOTO_BATCH_MAX = 8;
const PHOTO_TYPES = { "image/jpeg": ".jpg", "image/png": ".png", "image/webp": ".webp", "image/heic": ".heic" };

function driveOptions_(options) {
  return Object.assign({ headers: { Authorization: "Bearer " + ScriptApp.getOAuthToken() }, muteHttpExceptions: true }, options);
}

function driveCreate_(url, options) {
  const res = UrlFetchApp.fetch(url, driveOptions_(Object.assign({ method: "post" }, options)));
  if (res.getResponseCode() >= 300) throw new Error("Google 雲端硬碟寫入失敗（" + res.getResponseCode() + "）");
  return JSON.parse(res.getContentText()).id;
}

function photoFolderId_() {
  const props = PropertiesService.getScriptProperties();
  let id = props.getProperty("photoFolderId");
  if (!id) {
    id = driveCreate_(DRIVE_FILES + "?fields=id", { contentType: "application/json",
      payload: JSON.stringify({ name: PHOTO_FOLDER, mimeType: "application/vnd.google-apps.folder" }) });
    props.setProperty("photoFolderId", id);
  }
  return id;
}

function uploadPhoto_(c, siteId) {
  const month = requireMonth_(c);
  const type = c.body.contentType, data = String(c.body.data || "");
  if (!data) fail_(400, "沒有收到照片檔案");
  if (!PHOTO_TYPES[type]) fail_(400, "不支援的圖片格式");
  if (data.length * 0.75 > MAX_PHOTO_BYTES) fail_(400, "照片檔案太大");
  requireSiteAccess_(c.user, siteId);
  requirePermission_(c.user, "Edit");

  // 上傳很花時間，在上鎖之前做，才不會卡住別人存檔
  const photo = { id: newId_(), contentType: type, uploadedAt: nowIso_(), fileId: null };
  const boundary = "bitanphoto" + Utilities.getUuid();
  const metadata = JSON.stringify({ name: siteId + "_" + month + "_" + photo.id + PHOTO_TYPES[type], parents: [photoFolderId_()] });
  const body = "--" + boundary + "\r\nContent-Type: application/json; charset=UTF-8\r\n\r\n" + metadata +
    "\r\n--" + boundary + "\r\nContent-Type: " + type + "\r\nContent-Transfer-Encoding: base64\r\n\r\n" + data +
    "\r\n--" + boundary + "--";
  photo.fileId = driveCreate_(DRIVE_UPLOAD, { contentType: "multipart/related; boundary=" + boundary, payload: body });

  lock_();
  const r = readingForWrite_(siteId, month);
  r.photos = (r.photos || []).concat([photo]);
  saveReading_(r);
  return ok_(photoOut_(photo));
}

function findPhoto_(photoId) {
  for (const r of dataOf_("Readings")) {
    const p = (r.photos || []).find((x) => x.id === photoId);
    if (p) return { reading: r, photo: p };
  }
  return null;
}

// 一次取回多張照片的內容（data: 網址）；看不到的站點、已不存在的照片直接略過
function getPhotos_(c) {
  const want = {};
  (c.body.ids || []).slice(0, PHOTO_BATCH_MAX).forEach((id) => { want[id] = 1; });
  const ids = allowedSiteIds_(c.user);
  const found = [];
  dataOf_("Readings").forEach((r) => {
    if (ids !== null && !ids[r.siteId]) return;
    (r.photos || []).forEach((p) => { if (want[p.id] && p.fileId) found.push(p); });
  });
  const photos = {};
  if (found.length) {
    UrlFetchApp.fetchAll(found.map((p) => driveOptions_({ url: DRIVE_FILES + "/" + p.fileId + "?alt=media" }))).forEach((res, i) => {
      if (res.getResponseCode() < 300) photos[found[i].id] = "data:" + found[i].contentType + ";base64," + Utilities.base64Encode(res.getContent());
    });
  }
  return ok_({ photos: photos });
}

// 丟到雲端硬碟的垃圾桶（30 天內還救得回來）
function trashPhotos_(photos) {
  const list = photos.filter((p) => p.fileId);
  if (!list.length) return;
  for (let i = 0; i < list.length; i += 50) {
    UrlFetchApp.fetchAll(list.slice(i, i + 50).map((p) => driveOptions_({ url: DRIVE_FILES + "/" + p.fileId, method: "patch",
      contentType: "application/json", payload: JSON.stringify({ trashed: true }) })));
  }
}

function deletePhoto_(c, photoId) {
  lock_();
  const hit = findPhoto_(photoId);
  if (!hit) notFound_();
  requireSiteAccess_(c.user, hit.reading.siteId);
  requirePermission_(c.user, "Full");
  hit.reading.photos = hit.reading.photos.filter((p) => p.id !== photoId);
  saveReading_(hit.reading);
  trashPhotos_([hit.photo]);
  return ok_();
}

// ---------------------------------------------------------------- /daily-readings

function dailyOut_(r) { return { date: r.date, value: r.value }; }

function getDaily_(c, siteId) {
  const month = requireMonth_(c);
  requireSiteAccess_(c.user, siteId);
  const prefix = month + "-";
  return ok_(dataOf_("Daily").filter((r) => r.siteId === siteId && r.date.indexOf(prefix) === 0)
    .sort((a, b) => (a.date < b.date ? -1 : a.date > b.date ? 1 : 0)).map(dailyOut_));
}

function requireDate_(c) {
  if (!isDate_(c.query.date)) fail_(400, "日期格式錯誤");
  return c.query.date;
}

function upsertDaily_(c, siteId) {
  const date = requireDate_(c);
  requireSiteAccess_(c.user, siteId);
  requirePermission_(c.user, "Edit");
  lock_();
  const r = { siteId: siteId, date: date, value: Number(c.body.value) || 0 };
  put_("Daily", siteId + "|" + date, r);
  return ok_(dailyOut_(r));
}

function deleteDaily_(c, siteId) {
  const date = requireDate_(c);
  requireSiteAccess_(c.user, siteId);
  requirePermission_(c.user, "Edit");
  lock_();
  remove_("Daily", siteId + "|" + date);
  return ok_();
}

// ---------------------------------------------------------------- /backup

function exportBackup_() {
  return ok_({
    sites: dataOf_("Sites").slice().sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0)),
    readings: dataOf_("Readings").map((r) => ({ siteId: r.siteId, monthKey: r.monthKey, curr: r.curr === undefined ? null : r.curr })),
  });
}

// 還原備份：覆蓋所有站點與各月讀數（備份不含照片，原本的照片會一併移到雲端硬碟的垃圾桶）
function importBackup_(c) {
  const b = c.body;
  if (!b || !b.sites) fail_(400, "備份格式不正確");
  lock_();
  const oldPhotos = photosOf_(dataOf_("Readings"));
  ["Readings", "Daily", "Sites"].forEach(clear_);
  const idMap = {};
  // Id 後段用流水號：站點是依 Id 排序顯示，這樣才會維持備份裡的順序
  const stamp = Math.floor(Date.now() / 1000).toString(16);
  const sites = b.sites.map((s, i) => {
    const id = stamp + ("0000000000000000" + i.toString(16)).slice(-16);
    idMap[s.id] = id;
    return { id: id, group: s.group || "", site: s.site || "", location: s.location || "", meterNo: s.meterNo || "", type: s.type || "water",
      basePrev: Number(s.basePrev) || 0, baseMonth: s.baseMonth || null, meterDigits: Number(s.meterDigits) > 0 ? Number(s.meterDigits) : 4 };
  });
  bulkAppend_("Sites", sites.map((s) => ({ key: s.id, data: s })));
  const seen = {};
  const readings = [];
  (b.readings || []).forEach((r) => {
    const siteId = idMap[r.siteId];
    if (!siteId || seen[readingKey_(siteId, r.monthKey)]) return;
    seen[readingKey_(siteId, r.monthKey)] = 1;
    readings.push({ siteId: siteId, monthKey: r.monthKey, curr: numOrNull_(r.curr), prevOverride: null, updatedAt: nowIso_(), photos: [] });
  });
  bulkAppend_("Readings", readings.map((r) => ({ key: readingKey_(r.siteId, r.monthKey), data: r })));
  sites.forEach((s) => ensureStore_(s.group));
  trashPhotos_(oldPhotos);
  audit_(c.user.username, "匯入備份還原", b.sites.length + " 個站點");
  return ok_();
}

function clearAll_(c) {
  lock_();
  const photos = photosOf_(dataOf_("Readings"));
  ["Readings", "Daily", "Sites"].forEach(clear_);
  trashPhotos_(photos);
  audit_(c.user.username, "清除全部資料", "");
  return ok_();
}
