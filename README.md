# ClipVault

Gerenciador de área de transferência (Clipboard Manager) de alta performance para **Windows Desktop**, construído sobre a arquitetura **.NET 8 MAUI** e **WinUI 3**. Desenvolvido com foco em execução portátil, desacoplamento em camadas (MVVM), persistência estruturada local e processamento assíncrono de visão computacional e OCR nativo.

---

## 🚀 Destaques da Versão 1.1.1

- **Visão Computacional Local Multi-Label**: Inferência neural executada localmente via ONNX Runtime (`MobileNetV2`), com cálculo de probabilidades normalizadas (Softmax) e extração de múltiplos rótulos semânticos (Top-K) em português com limiares de confiança adaptativos.
- **OCR Nativo do Windows**: Reconhecimento óptico de caracteres em background integrado diretamente ao subsistema `Windows.Media.Ocr`, extraindo textos de prints, diagramas e capturas de tela.
- **Busca Unificada Insensível a Acentos (Ignore Diacritics)**: Indexação vetorial normalizada em SQLite (`System.Globalization`), permitindo consultas imediatas que ignoram acentuação, caracteres diacríticos e caixa alta/baixa tanto para textos quanto para metadados de OCR e tags visuais.
- **Visualizador Interativo de Mídia e Texto**: Modal dedicado com suporte a gestos de toque e controles de mouse para Zoom (Pinch/Pan), inspeção de texto OCR com cópia seletiva e leitor de textos longos com quebra de linha automática.
- **Modo Grade Responsivo**: Cálculo dinâmico do número de colunas (`Span`) no `CollectionView` baseado na largura real da janela em tempo de execução.
- **Portabilidade Real (Single-File)**: Executável independente autocontido (`portable\ClipVault_v1.1.1.exe`) que opera sem necessidade de instalação prévia, mantendo o banco de dados (`clipvault.db`) e configurações (`clipvault_config.json`) co-localizados no diretório do binário.

---

## 🛠️ Tecnologias Utilizadas

| Componente | Tecnologia | Finalidade |
| :--- | :--- | :--- |
| **Plataforma / UI** | .NET 8 MAUI / WinUI 3 | Interface nativa Windows Desktop com Fluent Design |
| **Padrão de Arquitetura**| MVVM (CommunityToolkit.Mvvm) | Desacoplamento estrito entre apresentação e lógica de domínio |
| **Inferência Neural** | Microsoft.ML.OnnxRuntime (v1.31) | Execução local de redes neurais ONNX com aceleração de CPU |
| **Reconhecimento de Texto** | Windows.Media.Ocr (Windows SDK) | Motor nativo de OCR do sistema operacional |
| **Banco de Dados** | SQLite / sqlite-net-pcl | Persistência relacional local de alta velocidade com índices |
| **Integração com SO** | Win32 API / Comctl32 / User32 | System Tray, janelas sem bordas, subclassing de HWND e ícones nativos |

---

## 📋 Funcionalidades Detalhadas

### 1. Monitoramento do Clipboard e Debounce Rígido
- Escuta contínua de eventos via APIs de baixo nível do Windows (`Windows.ApplicationModel.DataTransfer.Clipboard`).
- Tratamento estrito contra múltiplos disparos gerados pela Ferramenta de Captura do Windows (*Snipping Tool*), utilizando janela temporal de debounce de 500 ms e cálculo prévio de hash criptográfico (SHA-256) em buffer de memória.
- Capturas com hash idêntico ao último registro são sumariamente descartadas antes de qualquer alocação em disco.

### 2. Reconhecimento Óptico de Caracteres (OCR)
- Toda imagem capturada da área de transferência é despachada de maneira não bloqueante em `Task.Run` para o motor `WindowsOcrService`.
- Suporte a idiomas instalados no perfil do usuário, indexando o texto extraído na coluna `ExtractedText`.

### 3. Visão Computacional Multi-Label (ONNX)
- Processamento do tensor de entrada padronizado para $1 \times 3 \times 224 \times 224$ com normalização estatística ImageNet ($\mu = [0.485, 0.456, 0.406]$, $\sigma = [0.229, 0.224, 0.225]$).
- Aplicação de função Softmax sobre os logits para obtenção de probabilidades estocásticas.
- Extração de predições primárias e secundárias significativas, mapeando centenas de sinônimos em português (animais, paisagens, veículos, tecnologia, alimentos e objetos).

### 4. Motor de Busca de Alta Performance
- Execução direta de consultas SQL parametrizadas (`LIKE ?`) sem inferência em tempo de digitação, mantendo o consumo de CPU em níveis mínimos.
- Normalização de diacríticos (`RemoveDiacritics`) aplicada simultaneamente ao termo digitado e à coluna indexada `NormalizedSearchContent`.

### 5. Integração com System Tray e Ciclo de Vida
- Ícone persistente na bandeja do sistema com menu de contexto (Abrir, Pausar/Retomar, Limpar Histórico, Configurações, Sobre e Sair).
- Fechamento da janela principal (`X`) minimiza a aplicação para a bandeja.
- Injeção de ícone nativo na barra de título do WinUI 3 via `AppWindow.SetIcon()`.

### 6. Políticas de Retenção e Limpeza Automática
- Configuração de expurgo por tempo (3, 7, 15 ou 30 dias) e por teto de armazenamento em disco (MB/GB).
- Itens marcados como fixados (📌) são permanentemente protegidos contra remoção automática.

---

## 📁 Estrutura do Projeto

```
ClipVault/
├── Models/
│   ├── AppConfig.cs             # Modelo de configuração serializado em JSON
│   └── ClipboardItem.cs         # Entidade SQLite com colunas de OCR, Tags e busca
├── Services/
│   ├── AppEnvironment.cs        # Resolução de diretórios, metadados e ícones
│   ├── AutoCleanService.cs      # Serviço em background para expurgo programado
│   ├── ClipboardService.cs      # Listener assíncrono do clipboard com debounce
│   ├── DatabaseService.cs       # Camada de acesso a dados SQLite com busca insensível
│   ├── IOcrService.cs           # Contrato para motores de OCR
│   ├── IVisionClassifierService.cs # Contrato para classificação de imagem
│   ├── SettingsService.cs       # Leitura e escrita de preferências no disco
│   ├── StorageService.cs        # Armazenamento físico de bitmaps PNG e hashes
│   ├── TextNormalizationHelper.cs # Normalização unicode e remoção de acentos
│   ├── TrayService.cs           # Controle Win32 da bandeja do sistema e subclassing
│   ├── VisionClassifierService.cs # Pipeline ONNX Runtime com MobileNetV2 e Softmax
│   └── WindowsOcrService.cs     # Implementação nativa Windows.Media.Ocr
├── ViewModels/
│   ├── ClipboardItemViewModel.cs# ViewModel de apresentação para itens individuais
│   ├── MainViewModel.cs         # ViewModel principal (busca, filtros, ordenação)
│   └── SettingsViewModel.cs     # ViewModel de configurações de retenção e caminhos
├── Views/
│   ├── AboutPage.xaml(.cs)      # Janela informativa sobre versão e autoria
│   ├── ItemViewerPage.xaml(.cs) # Visualizador fullscreen com zoom/pan e leitor de texto
│   ├── MainPage.xaml(.cs)       # Interface principal com alternância Lista/Grade
│   └── SettingsPage.xaml(.cs)   # Interface de configurações de diretório e retenção
├── Resources/
│   ├── AppIcon/                 # Ícones (.ico, .svg)
│   └── Models/
│       ├── mobilenet_v2.onnx    # Rede neural pré-treinada em formato ONNX
│       └── imagenet_pt.txt      # Mapeamento semântico de rótulos em português
└── installer/
    └── publish.ps1              # Script de automação para compilação portátil Release
```

---

## 🔨 Compilação e Publicação

### Pré-requisitos
- .NET 8.0 SDK (x64) com suporte a .NET MAUI
- Windows 10 (versão 1809 / Build 17763) ou Windows 11

### Compilação em Modo de Desenvolvimento
```powershell
dotnet build -f net8.0-windows10.0.19041.0 -c Debug
```

### Publicação do Binário Portátil (Single-File / Self-Contained)
Execute o script automatizado:
```powershell
powershell -ExecutionPolicy Bypass -File .\installer\publish.ps1
```
O executável final independente será gerado em:
```
portable\ClipVault_v1.1.1.exe
```

---

## 📄 Licença e Autoria

- **Versão**: 1.1.1
- **Data de Lançamento**: Outubro de 2026
- **Autor**: Filipe R. Carvalho
