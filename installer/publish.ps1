# Script de automação para compilar e gerar o executável portátil (Self-Contained Single-File)
$version = "1.1.4"
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "    ClipVault v$version - Publicação Portátil " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "..\ClipVault.csproj"
$portableDir = Join-Path $PSScriptRoot "..\portable"

if (-not (Test-Path $portableDir)) {
    New-Item -ItemType Directory -Path $portableDir | Out-Null
}

Write-Host "`n[1/3] Publicando executável portátil (Single-File / Self-Contained / x64)..." -ForegroundColor Yellow
dotnet publish $projectPath `
    -f net8.0-windows10.0.19041.0 `
    -p:TargetFrameworks=net8.0-windows10.0.19041.0 `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:WindowsPackageType=None `
    -p:Platform=x64 `
    -o $portableDir

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n[2/3] Gerando binário versionado..." -ForegroundColor Yellow
    $baseExe = Join-Path $portableDir "ClipVault.exe"
    $versionedExe = Join-Path $portableDir "ClipVault-v$version.exe"

    if (Test-Path $baseExe) {
        Move-Item -Path $baseExe -Destination $versionedExe -Force
        $oldUnderscoreExe = Join-Path $portableDir "ClipVault_v$version.exe"
        if (Test-Path $oldUnderscoreExe) {
            Remove-Item -Path $oldUnderscoreExe -Force
        }
    }

    Write-Host "`n[3/3] Publicação portátil concluída com sucesso!" -ForegroundColor Green
    Write-Host "Pasta Portátil: $portableDir" -ForegroundColor White
    Write-Host "Executável Versionado: $versionedExe" -ForegroundColor Green
    Write-Host "`nInstruções de Uso:" -ForegroundColor Cyan
    Write-Host "• Basta copiar o arquivo 'ClipVault-v$version.exe' para qualquer computador ou pendrive." -ForegroundColor Gray
    Write-Host "• Não requer instalação: o banco SQLite (clipvault.db) e configurações (clipvault_config.json) são gerados na mesma pasta do executável." -ForegroundColor Gray
    Write-Host "• Ao substituir por uma versão futura na mesma pasta, seus dados e configurações são mantidos automaticamente!" -ForegroundColor Gray
} else {
    Write-Host "`nErro ao publicar o projeto portátil." -ForegroundColor Red
}
