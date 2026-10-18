# ClipVault 📋🔒

**ClipVault** é um gerenciador moderno, performático e silencioso de área de transferência (Clipboard Manager) para **Windows Desktop** e **Android**, desenvolvido com **.NET 8 MAUI** e **WinUI 3**.

---

## ✨ Funcionalidades Principais

1. **System Tray Nativo (Bandeja do Sistema)**:
   - Roda silenciosamente em segundo plano com ícone na bandeja do sistema.
   - Menu de contexto nativo com ações rápidas: *Abrir ClipVault*, *Pausar/Retomar Monitoramento*, *Limpar Histórico*, *Configurações* e *Sair*.
   - Fechar a janela (`X`) apenas oculta o aplicativo na bandeja sem encerrar o processo.
   - Clique duplo ou simples no ícone da bandeja restaura a janela imediatamente ao primeiro plano.

2. **Monitoramento em Tempo Real do Clipboard**:
   - Escuta eventos da área de transferência de forma assíncrona e em segundo plano.
   - **Textos, URLs e Código**: Detecção automática de linguagem, contagem de caracteres e geração de preview.
   - **Imagens e Print Screen**: Captura instantânea de capturas de tela e imagens copiadas, salvando como arquivos PNG com deduplicação criptográfica via hash SHA-256.
   - **Supressão de Duplicatas**: Evita repetições consecutivas do mesmo conteúdo ou imagem.

3. **Interface Moderna Fluent Design (Windows 11)**:
   - Modo escuro polido, tipografia clara e cantos arredondados.
   - Filtros rápidos por abas: *Todos*, *Textos*, *Imagens* e *Fixados*.
   - Busca instantânea inteligente (por texto, snippet de código ou data).
   - Ações rápidas em 1 clique por card: *Copiar de Volta* (com toast discreto de sucesso), *Fixar no Topo* (📌) e *Excluir* (🗑️).

4. **Diretório Customizável & Banco de Dados SQLite Local**:
   - Persistência estruturada em banco SQLite (`clipvault.db3`) de alta performance via `sqlite-net-pcl`.
   - Permite alterar a pasta de destino dos arquivos e imagens a qualquer momento via seletor nativo de pastas (*FolderPicker*).

5. **Política de Autocleaning (Limpeza Automática)**:
   - Limpeza por **Tempo/Idade**: Exclui automaticamente itens não fixados com mais de X dias (ex: 3, 7, 15, 30 dias).
   - Limpeza por **Limite de Armazenamento**: Exclui os itens não fixados mais antigos caso a pasta exceda o tamanho limite configurado em MB/GB.
   - Execução em background não bloqueante na inicialização e a cada 24 horas. Itens fixados **nunca** são removidos pela rotina automática.

6. **Inicialização Automática com o Windows**:
   - Opção para iniciar o aplicativo automaticamente em segundo plano ao ligar o computador (configurável nas Configurações ou pelo instalador).

---

## 🏗️ Arquitetura e Estrutura de Código

```
ClipVault/
├── Models/
│   ├── ClipboardItem.cs         # Entidade SQLite com metadados, hashes e formatos
│   └── AppConfig.cs             # Modelo de configurações persistidas
├── Services/
│   ├── IClipboardService.cs     # Listener assíncrono de área de transferência
│   ├── ClipboardService.cs      # Captura WinRT de Textos e Bitmaps e cópia reversa
│   ├── IDatabaseService.cs      # Camada de dados SQLite com índices e queries
│   ├── DatabaseService.cs       # Implementação SQLite-net-pcl
│   ├── ISettingsService.cs      # Gerenciamento de preferências e registro do Windows
│   ├── SettingsService.cs       # Persistência JSON e HKCU Startup Registry
│   ├── IStorageService.cs       # Cálculo de hashes SHA-256 e gravação de PNGs
│   ├── StorageService.cs        # Gerenciamento de diretórios e arquivos físicos
│   ├── ITrayService.cs          # Interface de controle do System Tray
│   ├── TrayService.cs           # Win32 Shell_NotifyIconW, Subclassing e menus
│   ├── IAutoCleanService.cs     # Interface de expurgo automático de dados
│   └── AutoCleanService.cs      # Timer periódico e semáforo assíncrono
├── ViewModels/
│   ├── ClipboardItemViewModel.cs# ViewModel individual para reatividade
│   ├── MainViewModel.cs         # MVVM Toolkit (filtros, busca, toasts, coleções)
│   └── SettingsViewModel.cs     # Configurações de diretório, limpeza e startup
├── Views/
│   ├── MainPage.xaml(.cs)       # UI Fluent Design com CollectionView virtualizada
│   └── SettingsPage.xaml(.cs)   # Tela de opções de armazenamento e retenção
├── installer/
│   ├── publish.ps1              # Script para publicação de binário Release
│   └── installer.iss            # Script de compilação de instalador Inno Setup
└── GEMINI.md                    # Regras de engenharia do Antigravity
```

---

## 🚀 Como Executar em Modo de Desenvolvimento

Para rodar a aplicação no Windows Desktop:

```powershell
dotnet run --project C:\Users\filip\source\repos\ClipVault -f net8.0-windows10.0.19041.0
```

---

## 📦 Como Gerar o Executável e Instalador .EXE

### 1. Gerar o executável autocontido (Self-Contained)
Execute o script incluído:
```powershell
.\installer\publish.ps1
```
Ou manualmente via CLI:
```powershell
dotnet publish -f net8.0-windows10.0.19041.0 -c Release -r win10-x64 --self-contained true -p:WindowsPackageType=None -p:Platform=x64
```
O arquivo gerado estará em `bin\Release\net8.0-windows10.0.19041.0\win10-x64\publish\ClipVault.exe`.

### 2. Gerar o instalador `Setup.exe` (Inno Setup)
1. Instale o Inno Setup se ainda não tiver:
   ```powershell
   winget install JRSoftware.InnoSetup
   ```
2. Compile o instalador:
   ```powershell
   & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" .\installer\installer.iss
   ```
O instalador pronto será gerado em `bin\Installer\ClipVault_Setup_v1.0.0.exe`.
