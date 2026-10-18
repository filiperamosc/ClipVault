# ClipVault

Aplicativo multiplataforma para **Windows** e **Android** desenvolvido com **.NET 8 MAUI**.

## 🚀 Tecnologias

- [.NET 8 MAUI](https://learn.microsoft.com/dotnet/maui/)
- C# 12 / XAML
- Plataformas-alvo:
  - Windows Desktop (`net8.0-windows10.0.19041.0`)
  - Android (`net8.0-android`)

## 🛠️ Como executar

### Pré-requisitos
- .NET 8 SDK com carga de trabalho `maui` instalada (`dotnet workload install maui`)
- (Opcional para Android) Android SDK / emulador

### Executar no Windows Desktop
```bash
dotnet run -f net8.0-windows10.0.19041.0
```

### Compilar para Android
```bash
dotnet build -f net8.0-android
```
