"""
把原本 MongoDB 的全部資料搬到 Google 試算表雲端後端。

用法（連線字串從環境變數讀，不要寫進檔案或指令）：
  set BITAN_MONGO_URI=mongodb+srv://...
  python migrate_from_mongo.py --dry-run 匯出.json
  python migrate_from_mongo.py <Apps Script 網址(…/exec)>

只會「讀」MongoDB；雲端那邊只有在還沒有任何帳號時才接受匯入。
密碼雜湊原封不動搬過去，所有人可以繼續用原本的帳號密碼登入。
照片的實體檔案在 Render 暫時性磁碟上，取不回來，所以舊照片紀錄不搬（會顯示略過幾張）。
"""
import base64
import datetime as dt
import json
import os
import sys
import urllib.request

DB_NAME = "BiTanEnergy"


def iso(v):
    if v is None:
        return None
    if v.tzinfo is None:
        v = v.replace(tzinfo=dt.timezone.utc)
    return v.astimezone(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f")[:-3] + "Z"


def num(v):
    """Decimal128、字串、整數都轉成數字；沒有值回傳 None。"""
    if v is None:
        return None
    if hasattr(v, "to_decimal"):
        v = v.to_decimal()
    return float(v)


def hash_prf(stored):
    """回傳 ASP.NET 密碼雜湊用的演算法代號（2 = SHA-512，雲端只支援這個）；不是 V3 格式回傳 None。"""
    try:
        raw = base64.b64decode(stored)
    except Exception:
        return None
    if len(raw) < 13 or raw[0] != 1:
        return None
    return int.from_bytes(raw[1:5], "big")


def export(db):
    users = [{
        "id": str(u["_id"]),
        "displayName": u.get("DisplayName") or "",
        "username": u["Username"],
        "passwordHash": u.get("PasswordHash") or "",
        "role": u.get("Role") or "Admin",
        "assignedGroups": u.get("AssignedGroups") or [],
        "permissionLevel": u.get("PermissionLevel") or "Full",
        "createdAt": iso(u.get("CreatedAt")),
        "failedLoginCount": 0,
        "lockedUntil": None,
    } for u in db.adminUsers.find({})]

    sites = [{
        "id": str(s["_id"]),
        "group": s.get("Group") or "",
        "site": s.get("Name") or "",
        "location": s.get("Location") or "",
        "meterNo": s.get("MeterNo") or "",
        "type": s.get("Type") or "water",
        "basePrev": num(s.get("BasePrev")) or 0,
        "baseMonth": s.get("BaseMonth"),
        "meterDigits": s.get("MeterDigits") or 4,
    } for s in db.sites.find({}).sort("_id", 1)]

    skipped_photos = 0
    readings = []
    for r in db.monthlyReadings.find({}):
        skipped_photos += len(r.get("Photos") or [])
        readings.append({
            "siteId": r["SiteId"],
            "monthKey": r["MonthKey"],
            "curr": num(r.get("CurrentValue")),
            "prevOverride": num(r.get("PrevOverride")),
            "updatedAt": iso(r.get("UpdatedAt")),
            "photos": [],
        })

    daily = [{"siteId": r["SiteId"], "date": r["Date"], "value": num(r.get("Value")) or 0} for r in db.dailyReadings.find({})]

    stores = [{"id": str(s["_id"]), "name": s["Name"], "createdAt": iso(s.get("CreatedAt"))} for s in db.stores.find({})]
    known = {s["name"] for s in stores}
    now = iso(dt.datetime.now(dt.timezone.utc))
    for i, name in enumerate(sorted({s["group"] for s in sites if s["group"]} - known)):
        stores.append({"id": f"{int(dt.datetime.now().timestamp()):08x}{i:016x}", "name": name, "createdAt": now})

    logs = [{
        "id": str(a["_id"]),
        "actorUsername": a.get("ActorUsername") or "",
        "action": a.get("Action") or "",
        "detail": a.get("Detail") or "",
        "createdAt": iso(a.get("CreatedAt")),
    } for a in db.auditLogs.find({}).sort("CreatedAt", 1)]

    return {"users": users, "stores": stores, "sites": sites, "readings": readings, "dailyReadings": daily, "auditLogs": logs}, skipped_photos


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    uri = os.environ.get("BITAN_MONGO_URI", "")
    if not uri:
        print("請先設定環境變數 BITAN_MONGO_URI")
        sys.exit(1)

    import pymongo  # 放這裡：只測匯出轉換邏輯時不需要安裝

    db = pymongo.MongoClient(uri, serverSelectionTimeoutMS=20000)[DB_NAME]
    data, skipped_photos = export(db)
    print(f"讀到：帳號 {len(data['users'])}、門市 {len(data['stores'])}、站點 {len(data['sites'])}、"
          f"月讀數 {len(data['readings'])}、每日讀數 {len(data['dailyReadings'])}、操作日誌 {len(data['auditLogs'])}；"
          f"略過照片紀錄 {skipped_photos} 筆")

    unsupported = [u["username"] for u in data["users"] if hash_prf(u["passwordHash"]) != 2]
    if unsupported:
        print(f"注意：{len(unsupported)} 個帳號的密碼格式雲端不支援，搬完需由管理員重設：", "、".join(unsupported))

    if sys.argv[1] == "--dry-run":
        out = sys.argv[2] if len(sys.argv) > 2 else "export.json"
        with open(out, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=1)
        print("已寫到", out, "（內含密碼雜湊，用完請刪除）")
        return

    body = json.dumps({"m": "POST", "p": "/setup/import", "t": "", "b": data}, ensure_ascii=False).encode("utf-8")
    req = urllib.request.Request(sys.argv[1], data=body, headers={"Content-Type": "text/plain;charset=utf-8"})
    with urllib.request.urlopen(req, timeout=360) as res:
        reply = json.loads(res.read().decode("utf-8"))
    print("雲端回應：", reply.get("s"), reply.get("b"))
    if reply.get("s") != 200:
        sys.exit(2)


if __name__ == "__main__":
    main()
