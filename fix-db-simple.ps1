$dbPath = "$env:LOCALAPPDATA\ToolBox\snippets.db"
$backupPath = "$dbPath.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

Write-Host "ToolBox 数据库修复工具" -ForegroundColor Cyan
Write-Host ""

if (!(Test-Path $dbPath)) {
    Write-Host "数据库不存在: $dbPath" -ForegroundColor Yellow
    exit 0
}

Write-Host "正在备份数据库..." -ForegroundColor Yellow
Copy-Item $dbPath $backupPath
Write-Host "已备份到: $backupPath" -ForegroundColor Green
Write-Host ""

Write-Host "正在删除数据库..." -ForegroundColor Yellow
try {
    Remove-Item $dbPath -Force
    Write-Host "已删除数据库" -ForegroundColor Green
    Write-Host ""
    Write-Host "修复完成！" -ForegroundColor Green
    Write-Host "现在可以启动 ToolBox，数据库会自动重新创建。" -ForegroundColor Cyan
} catch {
    Write-Host "无法删除数据库: $_" -ForegroundColor Red
    Write-Host "请确保 ToolBox 已完全关闭。" -ForegroundColor Yellow
}
