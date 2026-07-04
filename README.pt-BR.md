# TuringMonitor (Turing Smart Screen Linux)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Language: en-US](https://img.shields.io/badge/Language-en--US-blue.svg)](README.md)

**Um monitor de sistema de alta performance em .NET 10 para Turing Smart Screens de 3.5" no Linux (Turing Smart Screen / Revisão A). Possui Native AOT para consumo de memória ultra baixo e uma engine de temas JSON totalmente customizável.**

## 🚀 Principais Características

- **Native AOT:** Binário nativo com pegada de memória ultra baixa (< 20MB).
- **Engine de Temas:** Sistema de layout baseado em JSON totalmente customizável com suporte a transparência real.
- **Live Reload:** Calibre seu layout em tempo real—salve o JSON e veja as mudanças no LCD instantaneamente.
- **Delta Updates:** Renderização inteligente que envia apenas pixels modificados para o hardware. Redesenho ciente de sobreposições garante que elementos dependentes sejam atualizados corretamente.
- **Telemetria Completa:** Dados em tempo real para CPU, GPU (NVIDIA), RAM, Rede e Clima.
- **Reconexão Automática:** Reconexão automática da porta serial com backoff exponencial e re-detecção de porta. O LCD se recupera sozinho após desconexão/reconexão do USB.
- **Tratamento de Erros Robusto:** Todas as falhas são logadas com severidade apropriada (sem exceções silenciosas). O serviço continua rodando mesmo se sensores ou o LCD estiverem indisponíveis.
- **Weather Não-Bloqueante:** Dados de clima são buscados em background (a cada 30 min) e nunca travam o loop de telemetria. Fallback gracioso se a API estiver indisponível.

---

## 🛠️ Instalação Rápida

### 1. Prerequisitos

- **Nobara Linux / Fedora** (Desenvolvido e testado atualmente no Nobara; outras distros ainda não foram testadas).
- **.NET 10 SDK** (necessário para o build).
- Dependências: `libicu`, `libssl`, `libusb`.
- Uma tela Turing Smart Screen 3.5" (Revisão A).

### 2. Instalar via Script

Fornecemos um script simples que compila o projeto e configura o daemon do systemd:

```bash
chmod +x install.sh
./install.sh
```

O script irá:

1. Compilar o binário usando **Native AOT**.
2. Instalar a aplicação em `/usr/local/bin/TuringMonitor`.
3. Configurar e iniciar um serviço `systemd` (`turing-monitor.service`).
4. Adicionar seu usuário ao grupo `dialout` para acesso à porta serial.

---

## 🎨 Guia de Temas

### Seleção

Para trocar o tema ativo, edite `appsettings.json`:

```json
{
  "Theme": "MeuTemaCustom"
}
```

Os temas ficam em `Assets/Themes/`.

### Criando um Tema Customizado

1. Crie uma nova pasta em `Assets/Themes/[NomeDoTema]`.
2. Adicione um `background.png` (480x320).
3. Crie um arquivo `theme.json`.
4. (Opcional) Adicione uma pasta `Icons/` com ícones de clima (01d.png, etc).

### Configuração Global (`theme.json`)

| Campo | Descrição |
| :--- | :--- |
| `Background` | Nome do arquivo de imagem de fundo. |
| `FontPath` | Caminho de um arquivo de fonte `.ttf` (relativo à raiz). |
| `DebugMode` | Se `true`, desenha caixas vermelhas ao redor dos elementos. |
| `Latitude` / `Longitude` | Coordenadas geográficas para o clima. |
| `WeatherApi` | Provedor de dados de clima. Padrão `openmeteo` (sem key). Opcional: `openweather` ou `openweathermap` (alias) — requer API key. |
| `WeatherIconsSource` | Origem dos ícones de clima. Padrão `local` (usa `Icons/{icon}.png` do tema). Opcional: `online` — baixa ícones OWM para `IconCache/` no primeiro uso. Ícones locais sempre têm prioridade e nunca são sobrescritos. |
| `OpenWeatherApiKey` | API key para OpenWeather quando `WeatherApi=openweather`. Recomendado colocar a key em `appsettings.json` (`OpenWeatherApiKey`) — ela tem prioridade sobre `theme.json`. Veja [Provedores de Clima](#provedores-de-clima). |

### Tipos de Elemento

- `Text`: Renderiza strings formatadas.
- `ProgressBar`: Barra horizontal segmentada.
- `Gauge`: Arco segmentado de 180°.
- `Icon`: Ícones PNG dinâmicos baseados no clima.

---

## 📊 Fontes de Dados

- `CpuName`, `CpuLoad`, `CpuTemp`, `CpuClock`, `CpuPower`
- `GpuModel`, `GpuLoad`, `GpuTemp`, `GpuPower`, `VramString`, `VramPercent`
- `RamString`, `RamPercent`
- `NetInMbps`, `NetOutMbps`, `NetInString`, `NetOutString`
- `WeatherTemp`, `WeatherIcon`
- `DateTime`

### Provedores de Clima

O TuringMonitor suporta dois provedores de dados de clima, selecionáveis por tema via `WeatherApi` em `theme.json`:

| Provedor | valor de `WeatherApi` | API Key | Endpoint |
| :--- | :--- | :--- | :--- |
| **Open-Meteo** (padrão) | `openmeteo` | nenhuma | `api.open-meteo.com/v1/forecast` |
| **OpenWeather** (opcional) | `openweather` (ou alias `openweathermap`) | requerida | `api.openweathermap.org/data/2.5/weather` (Current Weather Data 2.5, plano gratuito ~1000 chamadas/dia) |

**Resolução de API key**: quando `WeatherApi=openweather`, a key é resolvida como `appsettings.json:OpenWeatherApiKey` (prioridade) ?? `theme.json:OpenWeatherApiKey`. Para manter sua key fora da pasta do tema (e fora de qualquer download de tema compartilhado), coloque-a em `appsettings.json` ou, preferencialmente, em `appsettings.local.json` (ignorado pelo git por padrão — veja `.gitignore`):

```json
{
  "OpenWeatherApiKey": "sua-api-key-aqui"
}
```

**Comportamento de fallback**:

- Se a key estiver ausente ou inválida (HTTP 401) com `openweather` configurado → loga `ERROR`, volta **permanentemente** para Open-Meteo nesta sessão.
- Se a chamada OpenWeather falhar transitóriamente (timeout / 5xx / rede) → loga `WARNING`, mantém `openweather` como provedor e usa o último valor em cache. Não mistura provedores numa mesma sessão.
- Se `WeatherApi` for um valor desconhecido → loga `ERROR`, usa `openmeteo`.
- Open-Meteo (`openmeteo`, o padrão) mantém o comportamento atual sem mudanças.

**Ícones**: os códigos de ícone de clima são nativos do OpenWeather (`01d`, `04n`, etc.). Quando `WeatherIconsSource=online`, os ícones são baixados uma vez para `Assets/Themes/[Tema]/IconCache/{icon}.png` e reusados do cache. Um ícone local em `Assets/Themes/[Tema]/Icons/{icon}.png` sempre tem precedência sobre o download online e nunca é sobrescrito. Se o download falhar, o ícone local é usado como fallback; se nenhum existir, um placeholder geométrico é desenhado.

---

## ⚖️ Licença

Este projeto está licenciado sob a **Licença MIT**. Criado por **bendak**.
