"""碧潭能源管理系統 — 電腦版（Python + PySide6，打包成免安裝 .exe）

視窗裡顯示的就是正式站網頁，資料跟手機/瀏覽器版是同一份（連到同一個後端）。
登入狀態保存在本機，下次開啟不用重新登入。
"""

import os
import sys

from PySide6.QtCore import Qt, QUrl
from PySide6.QtGui import QAction, QIcon
from PySide6.QtPrintSupport import QPrintDialog, QPrinter
from PySide6.QtWebEngineCore import QWebEnginePage, QWebEngineProfile
from PySide6.QtWebEngineWidgets import QWebEngineView
from PySide6.QtWidgets import QApplication, QDialog, QFileDialog, QMainWindow, QMenuBar

# 正式站網址：網頁版與電腦版共用同一個網址。
SITE_URL = "https://bitan-energy-pro-live.onrender.com"
APP_TITLE = "碧潭能源管理系統"
# 網頁依 User-Agent 判斷是電腦版，據此隱藏網頁內重複的標題列（見 index.html）。
USER_AGENT_TAG = "BiTanEnergyDesktop/2.0"

# 持有 window.open() 開出來的視窗，避免還沒顯示就被回收。
_popups = []


def resource_path(name):
    # 打包成 exe 後，資源會解壓到 sys._MEIPASS；開發時就是這個資料夾。
    base = getattr(sys, "_MEIPASS", os.path.dirname(os.path.abspath(__file__)))
    return os.path.join(base, name)


class BrowserPage(QWebEnginePage):
    def __init__(self, profile, parent=None):
        super().__init__(profile, parent)
        self.featurePermissionRequested.connect(self._on_feature_requested)
        self.printRequested.connect(self._on_print_requested)

    def createWindow(self, window_type):
        # window.open()（匯出報告、列印預覽）：開一個新視窗來顯示
        popup = PopupWindow(self.profile())
        _popups.append(popup)
        popup.destroyed.connect(lambda *_: _forget_popup(popup))
        return popup.view.page()

    def chooseFiles(self, mode, old_files, accepted_mimes):
        # <input type="file">（上傳照片）：跳出檔案選擇視窗
        filters = "圖片 (*.jpg *.jpeg *.png *.heic *.webp);;所有檔案 (*)"
        if mode == QWebEnginePage.FileSelectionMode.FileSelectOpenMultiple:
            paths, _ = QFileDialog.getOpenFileNames(self.parent(), "選擇檔案", "", filters)
        else:
            path, _ = QFileDialog.getOpenFileName(self.parent(), "選擇檔案", "", filters)
            paths = [path] if path else []
        return paths

    def _on_feature_requested(self, origin, feature):
        # 掃條碼要用相機：只允許本站要求相機／麥克風，其他網站一律拒絕
        camera_features = (
            QWebEnginePage.Feature.MediaVideoCapture,
            QWebEnginePage.Feature.MediaAudioVideoCapture,
        )
        allowed = feature in camera_features and origin.host() == QUrl(SITE_URL).host()
        policy = (QWebEnginePage.PermissionPolicy.PermissionGrantedByUser if allowed
                  else QWebEnginePage.PermissionPolicy.PermissionDeniedByUser)
        self.setFeaturePermission(origin, feature, policy)

    def _on_print_requested(self):
        # 網頁的「列印 / 存PDF」按鈕：顯示系統列印對話框（可選「Microsoft Print to PDF」存成 PDF）
        printer = QPrinter(QPrinter.PrinterMode.HighResolution)
        dialog = QPrintDialog(printer, self.parent())
        if dialog.exec() == QDialog.DialogCode.Accepted:
            self.print(printer, lambda ok: None)


def _forget_popup(popup):
    if popup in _popups:
        _popups.remove(popup)


class PopupWindow(QMainWindow):
    def __init__(self, profile):
        super().__init__()
        self.setWindowTitle(APP_TITLE)
        self.setWindowIcon(QIcon(resource_path("app.ico")))
        self.resize(900, 700)
        self.setAttribute(Qt.WidgetAttribute.WA_DeleteOnClose)
        self.view = QWebEngineView(self)
        self.view.setPage(BrowserPage(profile, self.view))
        self.view.titleChanged.connect(self.setWindowTitle)
        self.setCentralWidget(self.view)
        self.show()


# 工具欄選單：把網頁「選單」裡的功能放到視窗上方。點選後在網頁裡按下對應的原始按鈕，
# 所以功能與網頁版完全一樣；隱藏的項目（例如非管理員看不到的帳號管理）會自動略過。
MENU_GROUPS = [
    ("檔案", [
        ("匯出報表（CSV）", "mExportCsv"),
        ("列印 / 存PDF", "mExportPdf"),
        ("會計報表（PDF）", "mExportAccounting"),
        None,
        ("匯出備份", "mExportJson"),
        ("匯入還原", "mImportJson"),
        None,
        ("登出", "mLogout"),
    ]),
    ("管理", [
        ("帳號管理", "mAccountsBtn"),
        ("門市管理", "mStoresBtn"),
        None,
        ("操作日誌", "mAuditLogBtn"),
        ("登入安全", "mLoginSecurityBtn"),
    ]),
    ("設定", [
        ("設定（姓名、顯示）", "mSettingsBtn"),
        ("費用與預算", "mPriceSettingsBtn"),
        ("深色模式", "mDarkToggle"),
        ("修改密碼", "mChangePasswordBtn"),
        None,
        ("清除本期資料", "mClearMonth"),
        ("清除全部資料", "mClearAll"),
    ]),
]

# 點擊網頁裡的按鈕；所在區塊被隱藏（沒有權限、或該功能不適用）時什麼都不做
CLICK_JS = """(function(id){
  var el = document.getElementById(id);
  if(!el) return;
  for(var n = el; n && !n.classList.contains('overlay'); n = n.parentElement){
    if(n.hidden || (n.style && n.style.display === 'none')) return;
  }
  if(getComputedStyle(el).display === 'none') return;
  el.click();
})("__ID__");"""

MENU_BAR_STYLE = """
QMenuBar { background: #0b1120; color: #e2e8f0; padding: 2px 6px; }
QMenuBar::item { background: transparent; padding: 6px 12px; border-radius: 6px; }
QMenuBar::item:selected { background: #1c3150; }
QMenu { background: #141c2c; color: #e2e8f0; border: 1px solid #243044; padding: 4px; }
QMenu::item { padding: 7px 26px 7px 18px; border-radius: 6px; }
QMenu::item:selected { background: #2563eb; color: #ffffff; }
QMenu::item:disabled { color: #64748b; }
QMenu::separator { height: 1px; background: #243044; margin: 4px 8px; }
"""


def build_menu_bar(view):
    bar = QMenuBar()
    bar.setStyleSheet(MENU_BAR_STYLE)
    for title, items in MENU_GROUPS:
        menu = bar.addMenu(title)
        for item in items:
            if item is None:
                menu.addSeparator()
                continue
            label, element_id = item
            action = QAction(label, menu)
            js = CLICK_JS.replace("__ID__", element_id)
            action.triggered.connect(lambda checked=False, js=js: view.page().runJavaScript(js))
            menu.addAction(action)
    return bar


class MainWindow(QMainWindow):
    def __init__(self, profile):
        super().__init__()
        self.setWindowTitle(APP_TITLE)
        self.setWindowIcon(QIcon(resource_path("app.ico")))
        self.setMinimumSize(760, 560)
        self.view = QWebEngineView(self)
        self.view.setPage(BrowserPage(profile, self.view))
        # 網頁標題（含版本與登入人員）同步到視窗標題列，只保留一條標題列
        self.view.titleChanged.connect(self.setWindowTitle)
        self.setCentralWidget(self.view)
        self.setMenuBar(build_menu_bar(self.view))
        self.view.load(QUrl(SITE_URL))


def on_download(item, parent):
    # 匯出 CSV、備份 JSON 等下載：讓使用者選擇存檔位置
    default_path = os.path.join(os.path.expanduser("~"), "Downloads", item.downloadFileName())
    path, _ = QFileDialog.getSaveFileName(parent, "儲存檔案", default_path)
    if not path:
        item.cancel()
        return
    item.setDownloadDirectory(os.path.dirname(path))
    item.setDownloadFileName(os.path.basename(path))
    item.accept()


def create_profile(app):
    # 具名 profile：cookie 與快取存在本機，登入狀態下次開啟仍然有效
    profile = QWebEngineProfile("BiTanEnergyDesktop", app)
    profile.setPersistentCookiesPolicy(QWebEngineProfile.PersistentCookiesPolicy.ForcePersistentCookies)
    # 網頁快取只放在記憶體，每次開啟都向伺服器取最新版網頁，不會停在舊版（登入的 cookie 仍保存在本機）
    profile.setHttpCacheType(QWebEngineProfile.HttpCacheType.MemoryHttpCache)
    profile.setHttpUserAgent(f"{profile.httpUserAgent()} {USER_AGENT_TAG}")
    return profile


def main():
    app = QApplication(sys.argv)
    app.setApplicationName(APP_TITLE)
    app.setWindowIcon(QIcon(resource_path("app.ico")))

    profile = create_profile(app)
    window = MainWindow(profile)
    profile.downloadRequested.connect(lambda item: on_download(item, window))

    # 視窗大小不超過螢幕可用範圍（扣掉工作列與標題列），再置中，避免上緣被切掉
    screen = app.primaryScreen().availableGeometry()
    window.resize(min(1360, screen.width() - 40), min(860, screen.height() - 60))
    window.show()

    frame = window.frameGeometry()
    frame.moveCenter(screen.center())
    frame.moveTop(max(frame.top(), screen.top()))
    window.move(frame.topLeft())

    sys.exit(app.exec())


if __name__ == "__main__":
    main()
