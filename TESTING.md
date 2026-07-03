# Passos de Teste — Validação da Implementação

Este documento descreve os passos para validar manualmente cada plano implementado.
Execute na ordem indicada. Marque cada item conforme validar.

## Pré-requisitos
- .NET 10 SDK instalado.
- LCD Turing Smart Screen Revision A conectado (para testes com hardware) OU máquina de teste Linux.
- Branch atual: `feat/implementation-plans`.

---

## 1. Build e sintaxe

- [ ] `dotnet build` — deve compilar com 0 erros e 0 warnings.
- [ ] `dotnet publish -c Release -r linux-x64 --self-contained true` — deve gerar binário NativeAOT.
- [ ] `bash -n install.sh` — deve retornar "Syntax OK".

---

## 2. Plano #01 — Error Handling (sem LCD)

**Objetivo:** confirmar que erros aparecem nos logs em vez de serem engolidos.

1. Rodar o serviço sem LCD conectado:
   ```bash
   dotnet run
   ```
2. **Esperado:** logs `LogError` indicando falha ao abrir porta serial `/dev/ttyACM0`.
3. **Esperado:** serviço NÃO crasha — continua rodando (loop producer).
4. Simular `theme.json` inválido (editar e colocar JSON inválido):
   - **Esperado:** `LogWarning` "Failed to reload theme".
   - **Esperado:** serviço continua rodando; LCD mantém último estado válido.
5. Remover `background.png` temporariamente e editar `theme.json` para recarregar:
   - **Esperado:** `LogWarning` e serviço continua.

- [ ] Validado: erros de init LCD aparecem no log.
- [ ] Validado: theme.json inválido gera warning, não crash.
- [ ] Validado: serviço continua após erros.

---

## 3. Plano #02 — Dependency Injection

**Objetivo:** confirmar que DI não quebrou o fluxo.

1. Rodar com LCD conectado:
   ```bash
   dotnet run
   ```
2. **Esperado:** "LCD Initialized successfully." no log.
3. **Esperado:** LCD mostra background e elementos como antes.
4. Confirmar que `Theme` do `appsettings.json` é respeitado:
   - Mudar `"Theme": "Default"` → `"Theme": "Outro"` (se existir) e reiniciar.
   - **Esperado:** carrega tema correto.

- [ ] Validado: LCD inicializa via DI.
- [ ] Validado: tema do appsettings.json é respeitado.

---

## 4. Plano #03 — Concurrency/Race

**Objetivo:** confirmar que editar theme.json ao vivo não causa crash.

1. Rodar serviço com LCD conectado.
2. Editar `Assets/Themes/Default/theme.json` ao vivo (mudar cor de um elemento, salvar).
3. **Esperado:** transição limpa, background atualiza, sem exceção no log.
4. **Stress test:** salvar `theme.json` 10x em 1 segundo (pode usar script):
   ```bash
   for i in $(seq 1 10); do touch Assets/Themes/Default/theme.json; sleep 0.1; done
   ```
5. **Esperado:** sem `NullReferenceException`, sem `ObjectDisposedException`, sem crash.

- [ ] Validado: reload ao vivo sem crash.
- [ ] Validado: stress test sem exceção.

---

## 5. Plano #04 — Serial Reconnect

**Objetivo:** confirmar reconexão automática após desconexão USB.

1. Rodar serviço com LCD conectado e funcionando.
2. **Desconectar** o cabo USB do LCD durante execução.
   - **Esperado:** logs `LogWarning` de tentativa de reconexão.
   - **Esperado:** serviço NÃO crasha.
3. **Reconectar** o cabo.
   - **Esperado:** dentro de ~5-10s, LCD volta a desenhar.
   - **Esperado:** log "Serial reconnected on /dev/ttyACM0".
   - **Esperado:** log "LCD reconnected; redrawing background".
4. (Avançado) Reconectar em porta diferente (ex.: `/dev/ttyACM1`):
   - **Esperado:** log "Serial port changed: /dev/ttyACM0 -> /dev/ttyACM1".
   - **Esperado:** LCD funciona na nova porta.

- [ ] Validado: desconexão gera logs sem crash.
- [ ] Validado: reconexão restaura LCD.
- [ ] Validado: mudança de porta detectada.

---

## 6. Plano #05 — Change Detection

**Objetivo:** confirmar que elementos estáticos não redesenham e dinâmicos atualizam.

1. Rodar serviço com LCD e tema com elementos `CpuName`, `GpuModel`, `CpuLoad`.
2. Observar LCD por ~10s com carga de CPU estável.
   - **Esperado:** `CpuName` e `GpuModel` NÃO redraw (ficam estáticos).
   - **Esperado:** `CpuLoad` atualiza quando muda > 0.5%.
3. Gerar carga de CPU (ex.: `stress -c 1` ou rodar algo pesado):
   - **Esperado:** `CpuLoad` atualiza imediatamente.
4. Esperar ~30s.
   - **Esperado:** redraw forçado de todos os elementos (safety net).

- [ ] Validado: elementos estáticos não redesenham.
- [ ] Validado: elementos dinâmicos atualizam.
- [ ] Validado: redraw forçado a cada 30s.

---

## 7. Plano #06 — Performance

**Objetivo:** confirmar otimizações e valores corretos.

1. Comparar CPU clock do LCD com `lscpu` ou `cat /proc/cpuinfo`:
   - **Esperado:** valor similar (pode usar sysfs agora).
2. Comparar RAM do LCD com `free -h`:
   - **Esperado:** valor similar.
3. Verificar cache de nvidia-smi (se GPU NVIDIA):
   - Observar que `nvidia-smi` não é chamado a cada segundo (cache 3s).
   - Pode usar `strace -p $(pgrep TuringMonitor) -e trace=execve` ou similar.
4. Confirmar CPU clock lendo `/sys/devices/system/cpu/cpufreq/policy0/scaling_cur_freq`:
   - **Esperado:** valor em kHz/1000 ≈ MHz mostrado no LCD.

- [ ] Validado: CPU clock coerente com sysfs/lscpu.
- [ ] Validado: RAM coerente com free -h.
- [ ] Validado: nvidia-smi não dispara a cada segundo (cache).

---

## 8. Plano #07 — Bugs Minor

**Objetivo:** confirmar fixes de bugs menores.

1. **Primeira leitura de CPU:**
   - Rodar serviço e observar primeiro snapshot.
   - **Esperado:** NÃO é 100% artificial (era o bug anterior).
   - **Esperado:** valor realista desde o primeiro tick.
2. **Net interface:**
   - Rodar em máquina com Docker instalado (tem `docker0`, `virbr0`).
   - Verificar log/valor no LCD.
   - **Esperado:** NÃO pega `docker0` (filtra por `operstate == "up"`).
3. **StopAsync:**
   - Parar serviço com `Ctrl+C` sem LCD conectado (init falhou).
   - **Esperado:** `LogWarning` em vez de `catch { }` silencioso.
   - **Esperado:** serviço para sem travar.
4. **Brightness:** apenas comentário — sem teste funcional necessário.

- [ ] Validado: primeiro snapshot de CPU não é 100%.
- [ ] Validado: net interface correta (não pega docker/virbr).
- [ ] Validado: StopAsync loga warning sem travar.

---

## 9. Plano #08 — install.sh

**Objetivo:** confirmar instalação robusta e idempotente.

1. Rodar `bash -n install.sh`:
   - **Esperado:** "Syntax OK".
2. Rodar `./install.sh` com dotnet 10 instalado:
   - **Esperado:** builda, copia, instala serviço.
3. `systemctl status turing-monitor`:
   - **Esperado:** serviço ativo/running.
4. `journalctl -u turing-monitor -n 50`:
   - **Esperado:** logs normais, sem erros.
5. Rodar `./install.sh` **2x seguidas**:
   - **Esperado:** segunda rodada limpa dir, reinstala, sem artefatos antigos.
   - **Esperado:** serviço reinicia corretamente.
6. Em VM/container sem dotnet:
   - **Esperado:** mensagem "❌ .NET 10 SDK is required".
7. Verificar grupo dialout:
   - **Esperado:** `usermod -a -G dialout "${SUDO_USER:-$USER}"` — preserva usuário real.

- [ ] Validado: install.sh roda com sucesso.
- [ ] Validado: idempotente (2x sem lixo).
- [ ] Validado: serviço ativo após install.
- [ ] Validado: erro claro sem dotnet.

---

## Checklist final (antes de push)

- [ ] Todos os itens acima validados.
- [ ] `dotnet build` sem erros.
- [ ] `dotnet publish` AOT sem erros.
- [ ] Sem regressões visuais no LCD (comparar com `main`).
- [ ] Commit no branch `feat/implementation-plans`.
- [ ] Push (após validação completa).
- [ ] Período de testes de alguns dias em ambiente real.
- [ ] PR para `main`.