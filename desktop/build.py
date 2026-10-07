"""打包成免安裝的單一 exe。

用法（在 desktop 資料夾內）：
    python build.py

產出：desktop/dist/碧潭能源管理系統.exe
"""

import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))

cmd = [
    sys.executable, "-m", "PyInstaller",
    "--noconfirm", "--clean", "--onefile", "--windowed",
    "--name", "碧潭能源管理系統",
    "--icon", "app.ico",
    "--add-data", f"app.ico{os.pathsep}.",
    "main.py",
]

result = subprocess.run(cmd, cwd=HERE)
if result.returncode == 0:
    print("完成：", os.path.join(HERE, "dist", "碧潭能源管理系統.exe"))
sys.exit(result.returncode)
