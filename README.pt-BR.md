# System Monitor LCD Daemon (.NET 10)

Um monitor de sistema ultra-leve para Linux, desenvolvido em **.NET 10** com **Native AOT**, otimizado para displays LCD chineses de 3.5" (Turing Smart Screen / Revision A).

## 🚀 Destaques
- **Native AOT:** Binário nativo de alta performance com consumo de memória < 20MB.
- **Pro Theme Engine:** Sistema de temas via JSON com suporte a transparência real (Alpha Blending).
- **Live Reload:** Altere o layout no JSON e veja as mudanças no LCD instantaneamente sem reiniciar o serviço.
- **Delta Updates:** Envia apenas os pixels modificados para o hardware, garantindo fluidez.
- **Telemetria Completa:** Coleta profunda de dados de CPU, GPU (NVIDIA), RAM, Rede e Clima.

---

## 🎨 Guia do theme.json

O arquivo de tema reside em `Assets/Themes/[Nome]/theme.json`.

### Configurações Globais
| Campo | Descrição |
| :--- | :--- |
| `Background` | Nome do arquivo de imagem de fundo (480x320). |
| `FontPath` | Caminho para a fonte `.ttf` no sistema. |
| `DebugMode` | `true` desenha bordas vermelhas nos elementos para calibração. |
| `Latitude` / `Longitude` | Coordenadas para a previsão do tempo. |

### Tipos de Elementos
- **`Text`**: Renderiza strings formatadas.
- **`ProgressBar`**: Barra de progresso segmentada.
- **`Gauge`**: Arco (velocímetro) segmentado de 180°.
- **`Icon`**: Carrega PNGs dinâmicos da pasta `Icons/` baseados no sensor.

### Propriedades dos Elementos
| Propriedade | Descrição |
| :--- | :--- |
| `Source` | Fonte do dado (veja a lista abaixo). |
| `X`, `Y` | Posição no display (0-479, 0-319). |
| `Width`, `Height` | Dimensões da área de atualização. |
| `Color` | Cor do elemento (Hex: `#00ffff` ou nome: `cyan`). |
| `OffColor` | Cor dos blocos inativos (ou `transparent`). |
| `Alignment` | Alinhamento do texto (`Left`, `Center`, `Right`). |
| `Multiplier` | Multiplicador para o valor bruto (ex: `0.001` para converter MHz em GHz). |
| `Format` | Máscara de formatação C# (ex: `{0:F1} GHz` ou `{0:HH:mm}`). |
| `Blocks` | Número de segmentos para barras e gauges. |
| `ShowPercentage` | (ProgressBar) Exibe o texto `%` alinhado à direita. |

---

## 📊 Fontes de Dados (Sources)

### CPU
- `CpuName`: Nome do processador (filtrado).
- `CpuLoad`: Uso total em %.
- `CpuTemp`: Temperatura real (Tctl/Package).
- `CpuClock`: Frequência máxima atual (MHz).
- `CpuPower`: Consumo em Watts.

### GPU (NVIDIA)
- `GpuModel`: Nome curto (ex: RTX 4090).
- `GpuLoad`: Uso do núcleo em %.
- `GpuTemp`: Temperatura da GPU.
- `GpuPower`: Consumo em Watts.
- `VramString`: Texto formatado "Used / Total GB".
- `VramPercent`: Porcentagem de uso da VRAM.

### Outros
- `RamString`: Texto formatado "Used / Total GB".
- `RamPercent`: Porcentagem de uso da memória RAM.
- `NetInString` / `NetOutString`: Velocidade de Rede (ex: "500 Mbps").
- `WeatherTemp`: Temperatura externa atual.
- `WeatherIcon`: ID do ícone de clima (Dia/Noite).
- `DateTime`: Objeto de tempo completo para formatos de hora e data.

---

## 🏗️ Como Rodar

1.  **Permissões:** `sudo chmod 666 /dev/ttyACM0`
2.  **Execução:** `dotnet run` dentro da pasta `LcdDisplay`.
3.  **Produção (AOT):** `dotnet publish -c Release`

## ⚖️ Licença
Este projeto está sob a licença **MIT**.
