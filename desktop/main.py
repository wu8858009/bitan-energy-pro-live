"""碧潭能源管理系統 — 電腦版（Python + PySide6，打包成免安裝 .exe）

視窗裡顯示的就是正式站網頁，資料跟手機/瀏覽器版是同一份（連到同一個後端）。
登入狀態保存在本機，下次開啟不用重新登入。
"""

import os
import sys
import tempfile
import uuid

from PySide6.QtCore import Qt, QUrl
from PySide6.QtGui import QIcon
from PySide6.QtWebEngineCore import QWebEnginePage, QWebEngineProfile
from PySide6.QtWebEngineWidgets import QWebEngineView
from PySide6.QtWidgets import QApplication, QFileDialog, QMainWindow, QMessageBox

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
        self.pdfPrintingFinished.connect(self._on_pdf_ready)

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
        # 網頁的「列印 / 存PDF」按鈕：先把報表存成暫存 PDF，再交給電腦上的 PDF 程式開啟（見 _on_pdf_ready）
        self.printToPdf(os.path.join(tempfile.gettempdir(), f"bitan-report-{uuid.uuid4().hex}.pdf"))

    def _on_pdf_ready(self, path, success):
        parent = self.parent()
        if success:
            open_pdf_for_printing(path)
        else:
            QMessageBox.warning(parent, APP_TITLE, "無法產生列印用的 PDF，請再試一次。")


def open_pdf_for_printing(pdf_path):
    # 列印交給電腦上預設的 PDF 程式：它有列印預覽，列印對話框是 Windows 標準的，預設印表機已自動選好
    os.startfile(pdf_path)


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
