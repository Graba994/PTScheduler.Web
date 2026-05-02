$host.UI.RawUI.WindowTitle = "PTScheduler — Git Push"
Set-Location $PSScriptRoot

Write-Host ""
Write-Host "=== PTScheduler Git Push ===" -ForegroundColor Cyan

# Show current status
$status = git status --short
if ($status) {
    Write-Host "`nZmiany do wypchnięcia:" -ForegroundColor Yellow
    git status --short
} else {
    Write-Host "`nBrak nowych zmian." -ForegroundColor Green
    Read-Host "`nNaciśnij Enter aby zakończyć"
    exit
}

Write-Host ""
$msg = Read-Host "Opis zmian (Enter = 'Update')"
if ([string]::IsNullOrWhiteSpace($msg)) { $msg = "Update" }

git add .
git commit -m $msg
git push

Write-Host ""
if ($LASTEXITCODE -eq 0) {
    Write-Host "Gotowe! Kod wypchnięty na GitHub." -ForegroundColor Green
} else {
    Write-Host "Wystąpił błąd. Sprawdź powyższe komunikaty." -ForegroundColor Red
}

Read-Host "`nNaciśnij Enter aby zakończyć"
