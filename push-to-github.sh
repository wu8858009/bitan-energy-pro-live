#!/bin/bash
# 使用方式：
#   1. 先在 GitHub 網站上建立一個空的 repository（不要勾選 "Add a README"）
#   2. 把下面 REPO_URL 換成你自己的 repository 網址
#   3. 在這個資料夾下執行： bash push-to-github.sh

REPO_URL="https://github.com/wu8858009/bitan-energy-pro.git"

set -e
git init
git add .
git commit -m "Initial commit: 碧潭能源管理系統 PRO"
git branch -M main
git remote add origin "$REPO_URL"
git push -u origin main

echo ""
echo "✅ 推送完成！"
echo "接著到 GitHub repository 的 Settings → Pages，"
echo "Source 選 main 分支 / root 目錄，儲存後就可以用 GitHub Pages 網址開啟。"
