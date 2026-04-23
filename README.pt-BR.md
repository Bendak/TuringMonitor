# TuringMonitor (Turing Smart Screen Linux)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Language: en-US](https://img.shields.io/badge/Language-en--US-blue.svg)](README.md)

Um monitor de sistema de alta performance para Linux, desenvolvido com **.NET 10** e **Native AOT**, projetado especificamente para telas LCD USB de 3.5" (Turing Smart Screen / Revisão A).

## 🚀 Principais Características

- **Native AOT:** Binário nativo com pegada de memória ultra baixa (< 20MB).
- **Engine de Temas:** Sistema de layout baseado em JSON totalmente customizável com suporte a transparência real.
- **Live Reload:** Calibre seu layout em tempo real—salve o JSON e veja as mudanças no LCD instantaneamente.
- **Delta Updates:** Renderização inteligente que envia apenas pixels modificados para o hardware.
- **Telemetria Completa:** Dados em tempo real para CPU, GPU (NVIDIA), RAM, Rede e Clima.

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

## ⚖️ Licença

Este projeto está licenciado sob a **Licença MIT**. Criado por **bendak**.
