# System Monitor LCD Daemon (.NET 10)

Um monitor de sistema leve e moderno para Linux, desenvolvido em **.NET 10** com **Native AOT**, projetado especificamente para displays LCD chineses de 3.5" (Turing Smart Screen / Revision A).

## 🚀 Destaques
- **Native AOT:** Binário nativo com consumo de memória baixíssimo (< 20MB).
- **Theme-Driven:** Totalmente customizável via arquivos JSON (coordenadas, cores, fontes).
- **Live Reload:** Calibração em tempo real: altere o `theme.json` e veja as mudanças no LCD instantaneamente.
- **Delta Updates:** Atualizações inteligentes que enviam apenas os pixels modificados pela porta serial.
- **Telemetria Real:** Coleta dados diretamente do `/proc` e `/sys` (CPU, RAM, Temp, Power).

## 🛠️ Requisitos
- Linux (Testado no Nobara/Fedora)
- Display LCD 3.5" USB (VID:PID 1a86:5722)
- .NET 10 SDK (para compilação)

## 🎨 Temas
Os temas ficam localizados em `Assets/Themes/[Nome]`. Cada tema possui:
- `background.png`: Imagem de fundo (480x320).
- `theme.json`: Configurações de posicionamento e sensores.

### Exemplo de Configuração
```json
{
  "Type": "ProgressBar",
  "Source": "CpuLoad",
  "X": 25, "Y": 110, "Width": 220, "Height": 18,
  "Blocks": 12, "ShowPercentage": true
}
```

## 🏗️ Como Rodar
```bash
# Dar permissão para a porta serial
sudo chmod 666 /dev/ttyACM0

# Rodar o projeto
cd LcdDisplay
dotnet run
```

## ⚖️ Licença
Este projeto está sob a licença **MIT**. Veja o arquivo [LICENSE](LICENSE) para detalhes.
