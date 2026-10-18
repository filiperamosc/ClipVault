# ClipVault

Gerenciador de área de transferência (Clipboard Manager) de alta performance para **Windows Desktop**, construído sobre a arquitetura **.NET 8 MAUI** e **WinUI 3**. Desenvolvido com foco em execução portátil, desacoplamento em camadas (MVVM), persistência estruturada local, interface **Windows 11 Fluent Design** e processamento assíncrono de classificação visual e OCR nativo.

---

## Destaques da Versão 1.1.2

- **Interface Windows 11 Fluent Design & Clean UI**: Layout minimalista em modo escuro (`#0F0F12` / `#18181C`), cantos arredondados (`CornerRadius="8"`), tipografia limpa e ícones vetoriais nativos (`Segoe Fluent Icons`).
- **Modo Grade com Proporção Estrutural (`22,*,36`)**: Cards dimensionados para priorizar a visualização do conteúdo principal (`*`), mantendo os controles de ação compactos e exclusivamente baseados em ícones na barra inferior (`36px`).
- **Classificação Visual ONNX Calibrada (`Limiar >= 60%`)**: Inferência local via ONNX Runtime (`MobileNetV2`), com alinhamento exato das 1.001 classes do grafo (`background` offset + 1.000 classes ImageNet), recorte central proporcional (`224x224`), normalização simétrica `[-1, 1]` e filtro rígido de confiança mínima de **60% (`0.60f`)** para eliminar falsos positivos.
- **OCR Nativo do Windows**: Reconhecimento óptico de caracteres em segundo plano integrado ao subsistema `Windows.Media.Ocr`, extraindo textos de capturas de tela e imagens.
- **Busca Unificada Insensível a Acentos (Ignore Diacritics)**: Indexação vetorial normalizada em SQLite (`System.Globalization`), permitindo consultas imediatas que ignoram acentuação e caixa alta/baixa em textos, OCR e categorias visuais.
- **Portabilidade Real (Single-File)**: Executável independente autocontido (`portable\ClipVault_v1.1.2.exe`) que opera sem necessidade de instalação, mantendo o banco de dados (`clipvault.db`) e configurações (`clipvault_config.json`) no mesmo diretório do executável.

---

## Tecnologias Utilizadas

| Componente | Tecnologia | Finalidade |
| :--- | :--- | :--- |
| **Plataforma / UI** | .NET 8 MAUI / WinUI 3 | Interface nativa Windows Desktop com Fluent Design e Segoe Fluent Icons |
| **Padrão de Arquitetura**| MVVM (CommunityToolkit.Mvvm) | Desacoplamento estrito entre apresentação e lógica de domínio |
| **Classificação Visual** | Microsoft.ML.OnnxRuntime (v1.31) | Execução local de modelo ONNX MobileNetV2 com limiar de confiança >= 60% |
| **Reconhecimento de Texto** | Windows.Media.Ocr (Windows SDK) | Motor nativo de OCR do sistema operacional |
| **Banco de Dados** | SQLite / sqlite-net-pcl | Persistência relacional local de alta velocidade com índices |
| **Integração com SO** | Win32 API / Comctl32 / User32 | System Tray, subclassing de HWND e ícones nativos |

---

## Funcionalidades Detalhadas

### 1. Monitoramento do Clipboard e Debounce Rígido
- Escuta contínua de eventos via APIs nativas do Windows (`Windows.ApplicationModel.DataTransfer.Clipboard`).
- Tratamento estrito contra múltiplos disparos gerados pela Ferramenta de Captura do Windows (*Snipping Tool*), utilizando janela temporal de debounce de 500 ms e cálculo prévio de hash criptográfico (SHA-256) em memória.
- Capturas com hash idêntico ao último registro são descartadas antes de qualquer gravação em disco.

### 2. Reconhecimento Óptico de Caracteres (OCR)
- Toda imagem capturada da área de transferência é processada de maneira assíncrona em `Task.Run` pelo serviço `WindowsOcrService`.
- Suporte aos idiomas instalados no sistema operacional, indexando o texto extraído na coluna `ExtractedText`.

### 3. Classificação Visual de Alta Precisão (ONNX)
- Pré-processamento com redimensionamento proporcional e recorte central (`224x224`) para preservar a razão de aspecto original dos objetos.
- Normalização de canais RGB no intervalo `[-1, 1]` e aplicação de função Softmax sobre os logits de saída.
- Filtro de confiança estrito de **60% (`0.60f`)**: predições abaixo de 60% de probabilidade são descartadas para garantir que apenas objetos com alta certeza estatística recebam rótulos no banco de dados.

### 4. Motor de Busca de Alta Performance
- Execução direta de consultas SQL parametrizadas (`LIKE ?`) sem processamento pesado em tempo de digitação.
- Normalização de diacríticos (`RemoveDiacritics`) aplicada ao termo pesquisado e à coluna indexada `NormalizedSearchContent`.

### 5. Integração com System Tray e Ciclo de Vida
- Ícone persistente na bandeja do sistema com menu de contexto nativo (Abrir, Pausar/Retomar, Limpar Histórico, Configurações, Sobre e Sair).
- Fechamento da janela principal (`X`) oculta a aplicação na bandeja do sistema.

---

## Estrutura do Projeto

```
ClipVault/
├── Models/
│   ├── AppConfig.cs                  # Modelo de configuração serializado em JSON
│   └── ClipboardItem.cs              # Entidade SQLite com colunas de OCR, Tags e busca
├── Services/
│   ├── AppEnvironment.cs             # Resolução de diretórios, metadados e ícones
│   ├── AutoCleanService.cs           # Serviço em background para expurgo programado
│   ├── ClipboardService.cs           # Listener assíncrono do clipboard com debounce
│   ├── DatabaseService.cs            # Camada de acesso a dados SQLite com busca insensível
│   ├── IOcrService.cs                # Contrato para motor de OCR
│   ├── IVisionClassifierService.cs   # Contrato para classificador visual
│   ├── SettingsService.cs            # Leitura e escrita de preferências no disco
│   ├── StorageService.cs             # Armazenamento físico de bitmaps PNG e hashes
│   ├── TextNormalizationHelper.cs    # Normalização unicode e remoção de acentos
│   ├── TrayService.cs                # Controle Win32 da bandeja do sistema e subclassing
│   ├── VisionClassifierService.cs    # Pipeline ONNX Runtime com MobileNetV2 (limiar >= 60%)
│   └── WindowsOcrService.cs          # Implementação nativa Windows.Media.Ocr
├── ViewModels/
│   ├── ClipboardItemViewModel.cs     # ViewModel de apresentação para itens individuais
│   ├── MainViewModel.cs              # ViewModel principal (busca, filtros, ordenação)
│   └── SettingsViewModel.cs          # ViewModel de configurações de retenção e caminhos
├── Views/
│   ├── AboutPage.xaml(.cs)           # Janela informativa sobre versão e autoria
│   ├── ItemViewerPage.xaml(.cs)      # Visualizador fullscreen com zoom/pan e leitor de texto
│   ├── MainPage.xaml(.cs)            # Interface principal Fluent Design com Lista e Grade
│   └── SettingsPage.xaml(.cs)        # Interface de configurações de diretório e retenção
├── Resources/
│   ├── AppIcon/                      # Ícones (.ico, .svg)
│   └── Models/
│       ├── mobilenet_v2.onnx         # Rede neural pré-treinada em formato ONNX
│       └── imagenet_pt.txt           # Mapeamento semântico das 1.000 classes em português
└── installer/
    └── publish.ps1                   # Script de automação para compilação portátil Release
```

---

## Compilação e Publicação

### Pré-requisitos
- .NET 8.0 SDK (x64) com workload `.NET MAUI`
- Windows 10 (versão 1809 / Build 17763) ou Windows 11

### Compilação em Modo de Desenvolvimento
```powershell
dotnet build -f net8.0-windows10.0.19041.0 -c Debug
```

### Automação de CI/CD e Releases (GitHub Actions)
O projeto conta com esteira automatizada em `.github/workflows/build-release.yml`:
- **Disparos (Triggers)**: Executado automaticamente a cada push na branch `main` ou na criação de novas tags (`v*`).
- **Ambiente**: Runner Windows (`windows-latest`) com SDK .NET 8 e workload `maui-windows`.
- **Publicação**: Gera o executável Single-File autônomo versionado (`ClipVault_v1.1.2.exe`), anexa como artefato da build e publica automaticamente como **GitHub Release**.

---

## Licença e Autoria

- **Versão**: 1.1.2
- **Data de Lançamento**: Outubro de 2026
- **Autor**: Filipe R. Carvalho
