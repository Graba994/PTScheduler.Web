Set-Location $PSScriptRoot

Write-Host ""
Write-Host "=== PTScheduler Git Push ===" -ForegroundColor Cyan

$status = git status --short
if ($status) {
    Write-Host "`nZmiany:" -ForegroundColor Yellow
    git status --short
} else {
    Write-Host "`nBrak nowych zmian." -ForegroundColor Green
    Read-Host "Nacisnij Enter"
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
    Write-Host "Gotowe! Kod wypchniety na GitHub." -ForegroundColor Green
} else {
    Write-Host "Blad. Sprawdz powyzsze komunikaty." -ForegroundColor Red
}

Read-Host "Nacisnij Enter aby zakonczyc"
