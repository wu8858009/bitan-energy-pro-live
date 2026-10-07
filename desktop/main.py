"""碧潭能源管理系統 — 電腦版（Python + PySide6，打包成免安裝 .exe）

視窗裡顯示的就是正式站網頁，資料跟手機/瀏覽器版是同一份（連到同一個後端）。
登入狀態保存在本機，下次開啟不用重新登入。
"""

import os
import sys
import tempfile
import uuid

from PySide6.QtCore import Qt, QPointF, QSize, QUrl
from PySide6.QtGui import QAction, QIcon, QPainter
from PySide6.QtPdf import QPdfDocument
from PySide6.QtPdfWidgets import QPdfView
from PySide6.QtPrintSupport import QPrinter, QPrinterInfo
from PySide6.QtWebEngineCore import QWebEnginePage, QWebEngineProfile
from PySide6.QtWebEngineWidgets import QWebEngineView
from PySide6.QtWidgets import (QApplication, QComboBox, QDialog, QFileDialog, QFrame, QHBoxLayout, QLabel,
                               QMainWindow, QMenuBar, QMessageBox, QPushButton, QSpinBox, QVBoxLayout)

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
        # 網頁的「列印 / 存PDF」按鈕：先把報表存成暫存 PDF，再開列印預覽（見 _on_pdf_ready）
        self.printToPdf(os.path.join(tempfile.gettempdir(), f"bitan-report-{uuid.uuid4().hex}.pdf"))

    def _on_pdf_ready(self, path, success):
        parent = self.parent()
        if success:
            show_print_preview(path, parent)
        else:
            QMessageBox.warning(parent, APP_TITLE, "無法產生列印用的 PDF，請再試一次。")
        try:
            os.remove(path)
        except OSError:
            pass


class PrintPreviewDialog(QDialog):
    # 電腦版標準的列印預覽：左側是印表機、份數與列印按鈕，右側是預覽與翻頁、縮放
    def __init__(self, pdf_path, parent=None):
        super().__init__(parent)
        self.setWindowTitle("列印")
        self.resize(1100, 720)
        # 明確指定顏色，不跟隨 Windows 的深色模式（否則左側文字會看不清楚）
        self.setStyleSheet("""
            QDialog { background: #f3f4f6; }
            QLabel { color: #111827; font-size: 14px; }
            QComboBox, QSpinBox { background: #ffffff; color: #111827; border: 1px solid #cbd5e1;
                border-radius: 6px; padding: 5px 8px; font-size: 14px; }
            QComboBox QAbstractItemView { background: #ffffff; color: #111827; selection-background-color: #2563eb; }
            QPushButton { background: #ffffff; color: #111827; border: 1px solid #cbd5e1;
                border-radius: 8px; padding: 8px; font-size: 14px; }
            QPushButton#printBtn { background: #2563eb; color: #ffffff; border: none; font-weight: 700; }
            QPushButton#printBtn:hover { background: #1d4ed8; }
        """)
        self.pdf = QPdfDocument(self)
        self.pdf.load(pdf_path)

        # ---- 左側：印表機與份數（自動選好目前的預設印表機）----
        side = QFrame()
        side.setFixedWidth(260)
        side_layout = QVBoxLayout(side)
        side_layout.setContentsMargins(18, 18, 18, 18)
        side_layout.setSpacing(10)

        self.printer_box = QComboBox()
        self.printer_names = [p.printerName() for p in QPrinterInfo.availablePrinters()]
        self.printer_box.addItems(self.printer_names)
        default_name = QPrinterInfo.defaultPrinter().printerName()
        if default_name in self.printer_names:
            self.printer_box.setCurrentIndex(self.printer_names.index(default_name))
        self.copies = QSpinBox()
        self.copies.setRange(1, 99)
        self.copies.setValue(1)
        self.page_info = QLabel()

        side_layout.addWidget(QLabel("印表機"))
        side_layout.addWidget(self.printer_box)
        side_layout.addSpacing(6)
        side_layout.addWidget(QLabel("份數"))
        side_layout.addWidget(self.copies)
        side_layout.addSpacing(6)
        side_layout.addWidget(self.page_info)
        side_layout.addStretch(1)
        print_btn = QPushButton("列印")
        print_btn.setObjectName("printBtn")
        print_btn.setDefault(True)
        print_btn.setMinimumHeight(38)
        print_btn.clicked.connect(self._print)
        cancel_btn = QPushButton("取消")
        cancel_btn.setMinimumHeight(38)
        cancel_btn.clicked.connect(self.reject)
        side_layout.addWidget(print_btn)
        side_layout.addWidget(cancel_btn)

        # ---- 右側：預覽工具列（翻頁、縮放）＋預覽 ----
        self.view = QPdfView(self)
        self.view.setDocument(self.pdf)
        self.view.setPageMode(QPdfView.PageMode.SinglePage)
        self.view.setZoomMode(QPdfView.ZoomMode.FitInView)
        self.nav = self.view.pageNavigator()
        self.nav.currentPageChanged.connect(self._update_page_info)

        prev_btn = QPushButton("◀")
        next_btn = QPushButton("▶")
        self.page_label = QLabel()
        self.zoom_box = QComboBox()
        self.zoom_box.addItems(["整頁", "符合寬度", "50%", "100%", "150%", "200%"])
        for btn in (prev_btn, next_btn):
            btn.setFixedWidth(36)
        prev_btn.clicked.connect(lambda: self.nav.jump(max(0, self.nav.currentPage() - 1), QPointF()))
        next_btn.clicked.connect(lambda: self.nav.jump(min(self.pdf.pageCount() - 1, self.nav.currentPage() + 1), QPointF()))
        self.zoom_box.currentIndexChanged.connect(self._apply_zoom)

        toolbar = QHBoxLayout()
        toolbar.addWidget(prev_btn)
        toolbar.addWidget(next_btn)
        toolbar.addWidget(self.page_label)
        toolbar.addStretch(1)
        toolbar.addWidget(QLabel("縮放"))
        toolbar.addWidget(self.zoom_box)

        right = QVBoxLayout()
        right.addLayout(toolbar)
        right.addWidget(self.view, 1)

        root = QHBoxLayout(self)
        root.setContentsMargins(0, 0, 0, 0)
        root.setSpacing(0)
        root.addWidget(side)
        root.addLayout(right, 1)

        self._update_page_info(0)

    def _update_page_info(self, page):
        total = self.pdf.pageCount()
        self.page_label.setText(f"第 {page + 1} 頁 / 共 {total} 頁")
        self.page_info.setText(f"共 {total} 頁")

    def _apply_zoom(self, index):
        modes = {
            0: (QPdfView.ZoomMode.FitInView, 1.0),
            1: (QPdfView.ZoomMode.FitToWidth, 1.0),
            2: (QPdfView.ZoomMode.Custom, 0.5),
            3: (QPdfView.ZoomMode.Custom, 1.0),
            4: (QPdfView.ZoomMode.Custom, 1.5),
            5: (QPdfView.ZoomMode.Custom, 2.0),
        }
        mode, factor = modes[index]
        self.view.setZoomMode(mode)
        if mode == QPdfView.ZoomMode.Custom:
            self.view.setZoomFactor(factor)

    def _print(self):
        name = self.printer_box.currentText()
        info = next((p for p in QPrinterInfo.availablePrinters() if p.printerName() == name), None)
        printer = QPrinter(info, QPrinter.PrinterMode.HighResolution) if info else QPrinter(QPrinter.PrinterMode.HighResolution)
        printer.setCopyCount(self.copies.value())
        painter = QPainter(printer)
        area = printer.pageRect(QPrinter.Unit.DevicePixel)
        for i in range(self.pdf.pageCount()):
            if i > 0:
                printer.newPage()
            # 依比例放進可列印範圍，不變形
            page = self.pdf.pagePointSize(i)
            scale = min(area.width() / page.width(), area.height() / page.height())
            size = QSize(int(page.width() * scale), int(page.height() * scale))
            painter.drawImage(area.topLeft(), self.pdf.render(i, size))
        painter.end()
        self.accept()

    def done(self, result):
        self.pdf.close()
        super().done(result)


def show_print_preview(pdf_path, parent):
    PrintPreviewDialog(pdf_path, parent).exec()


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
