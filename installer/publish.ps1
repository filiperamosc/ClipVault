# Script de automação para compilar e gerar o executável portátil (Self-Contained Single-File)
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "    ClipVault - Publicação Portátil .EXE  " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "..\ClipVault.csproj"
$portableDir = Join-Path $PSScriptRoot "..\portable"

if (-not (Test-Path $portableDir)) {
    New-Item -ItemType Directory -Path $portableDir | Out-Null
}

Write-Host "`n[1/2] Publicando executável portátil (Single-File / Self-Contained)..." -ForegroundColor Yellow
dotnet publish $projectPath `
    -f net8.0-windows10.0.19041.0 `
    -p:TargetFrameworks=net8.0-windows10.0.19041.0 `
    -c Release `
    -r win10-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:WindowsPackageType=None `
    -p:Platform=x64 `
    -o $portableDir

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n[2/2] Publicação portátil concluída com sucesso!" -ForegroundColor Green
    Write-Host "Pasta Portátil: $portableDir" -ForegroundColor White
    Write-Host "Executável: $portableDir\ClipVault.exe" -ForegroundColor Green
    Write-Host "`nComo usar:" -ForegroundColor Cyan
    Write-Host "• Basta copiar o arquivo 'ClipVault.exe' para qualquer pasta ou pendrive." -ForegroundColor Gray
    Write-Host "• Ao executar, o ClipVault criará automaticamente o banco de dados e arquivos locais sem exigir instalação." -ForegroundColor Gray
} else {
    Write-Host "`nErro ao publicar o projeto portátil." -ForegroundColor Red
}
