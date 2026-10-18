# Script de automação para compilar e empacotar o ClipVault (Windows Desktop)
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "       ClipVault - Build & Publish        " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "..\ClipVault.csproj"
$outputDir = Join-Path $PSScriptRoot "..\bin\Release\net8.0-windows10.0.19041.0\win10-x64\publish"

Write-Host "`n[1/3] Limpando compilações anteriores..." -ForegroundColor Yellow
dotnet clean $projectPath -c Release -f net8.0-windows10.0.19041.0

Write-Host "`n[2/3] Publicando executável autocontido (Self-Contained)..." -ForegroundColor Yellow
dotnet publish $projectPath `
    -f net8.0-windows10.0.19041.0 `
    -c Release `
    -r win10-x64 `
    --self-contained true `
    -p:WindowsPackageType=None `
    -p:Platform=x64

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n[3/3] Publicação concluída com sucesso!" -ForegroundColor Green
    Write-Host "Pasta de saída: $outputDir" -ForegroundColor White
    Write-Host "Executável: $outputDir\ClipVault.exe" -ForegroundColor Green
    Write-Host "`nPara gerar o instalador Setup.exe:" -ForegroundColor Cyan
    Write-Host "1. Baixe o Inno Setup: winget install JRSoftware.InnoSetup" -ForegroundColor Gray
    Write-Host "2. Compile o arquivo: ISCC.exe `"$PSScriptRoot\installer.iss`"" -ForegroundColor Gray
} else {
    Write-Host "`nErro ao publicar o projeto." -ForegroundColor Red
}
