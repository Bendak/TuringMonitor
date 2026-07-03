# Implementação — Tracking

Branch: `feat/implementation-plans`
Base: `main` (a5de07a)

## Status geral

| Plano | Título | Status | Build | Testado |
|-------|--------|--------|-------|---------|
| #01   | Error Handling      | ✅ Implementado | ✅ | ☐ |
| #02   | Dependency Injection| ✅ Implementado | ✅ | ☐ |
| #03   | Concurrency/Race    | ✅ Implementado | ✅ | ☐ |
| #04   | Serial Reconnect    | ✅ Implementado | ✅ | ☐ |
| #05   | Change Detection    | ✅ Implementado | ✅ | ☐ |
| #06   | Performance         | ✅ Implementado | ✅ | ☐ |
| #07   | Bugs Minor          | ✅ Implementado | ✅ | ☐ |
| #08   | install.sh          | ✅ Implementado | ✅ | ☐ |
| #09   | Architecture        | ⏸️ Futuro (não nesta fase) | — | — |

Legenda: ✅ = feito / ☐ = pendente de validação humana / ⏸️ = não iniciado

## Detalhes por plano

### #01 — Error Handling
- `LinuxTelemetry`, `TuringSmartScreenDriver`, `LayoutManager` agora recebem `ILogger<T>`.
- Todos os `catch { }` vazios foram substituídos por logs com nível apropriado:
  - `LogError` para falha de comunicação serial / init LCD.
  - `LogWarning` para falhas de leitura de sensor, reconexão, StopAsync.
  - `LogDebug` para falhas de path detection (cpu temp, net iface, gpu name).
- **Arquivos:** `TuringSmartScreenDriver.cs`, `LinuxTelemetry.cs`, `LayoutManager.cs`, `Worker.cs`
- **Build:** ✅
- **Testar:** rodar sem LCD conectado, confirmar logs de erro; simular theme.json inválido.

### #02 — Dependency Injection
- Interfaces criadas: `IDisplay`, `ITelemetry`, `ILayoutManager`.
- `Program.cs` registra tudo via DI; `Worker` recebe dependências via construtor.
- `TuringMonitorOptions` (Theme, ThemesRoot) bindado via `IOptions<T>`.
- `ThemesRoot` padrão usa `AppContext.BaseDirectory` (não mais `Directory.GetCurrentDirectory()`).
- `LayoutManager` registrado via factory que resolve options e dependências.
- **Arquivos novos:** `IDisplay.cs`, `ITelemetry.cs`, `ILayoutManager.cs`, `TuringMonitorOptions.cs`
- **Arquivos:** `Program.cs`, `Worker.cs`, `LayoutManager.cs`, `TuringSmartScreenDriver.cs`, `LinuxTelemetry.cs`
- **Build:** ✅
- **Testar:** subir serviço e confirmar que LCD inicializa; criar FakeDisplay temporário para validar fluxo sem hardware.

### #03 — Concurrency/Race
- `LayoutManager` adiciona `object _themeLock`.
- `ReloadIfNeeded` carrega JSON/bg fora do lock, atribui dentro do lock.
- `DrawBackground` e `DrawElement` copiam/clone dentro do lock; `DisplayBitmap` fora.
- `Worker.RunConsumerAsync` copia referência de `Theme` para variável local.
- **Arquivos:** `LayoutManager.cs`, `Worker.cs`
- **Build:** ✅
- **Testar:** editar `theme.json` ao vivo; stress test (10x em 1s); confirmar sem crash.

### #04 — Serial Reconnect
- `IDisplay.EnsureOpen()` + backoff exponencial (1s→2s→4s→...→30s).
- `EnsureOpen` reconfigura orientation, brightness, clear após reconexão.
- `Reconnected` event disparado em reconexão bem-sucedida; `Worker` assina e redesenha background.
- Re-detecção de porta (USB pode enumerar diferente após reconexão).
- `_initializing` flag evita recursão durante reconfiguração.
- Falhas de `Write` capturadas, marcam desconexão e tentam reconexão.
- **Arquivos:** `TuringSmartScreenDriver.cs`, `IDisplay.cs`, `Worker.cs`
- **Build:** ✅
- **Testar:** desconectar cabo USB durante execução; reconectar; reconectar em porta diferente.

### #05 — Change Detection
- `HasChanged` centralizado em `Worker` com thresholds por source.
- Cobre: CpuLoad (0.5f), CpuTemp (1.0f), CpuClock (50 MHz), CpuPower (0.5W),
  RamPercent (0.1f), GpuLoad (1.0f), GpuTemp (1.0f), GpuPower (0.5W),
  VramPercent (0.5f), NetIn/Out (1.0 Mbps), WeatherTemp (0.5f), strings (!=).
- Default `_ => !Equals(...)` em vez de `_ => true`.
- Safety net: redraw forçado a cada 30s (`ForceRedrawIntervalSec`).
- **Arquivos:** `Worker.cs`
- **Build:** ✅
- **Testar:** observar elementos estáticos não redesenham; gerar carga de CPU → update imediato.

### #06 — Performance
- `ConvertToRgb565` usa `ArrayPool<byte>.Shared.Rent` + try/finally return.
- `GetGpuStats` tem cache de 3s (evita spawn de nvidia-smi a cada tick).
- `GetCpuClock` tenta `/sys/.../scaling_cur_freq` antes de `/proc/cpuinfo`.
- `GetRamUsage` sai do loop quando 4 campos encontrados.
- `ParseKb` reescrito com `Span<char>` (sem Regex).
- **Arquivos:** `LayoutManager.cs`, `LinuxTelemetry.cs`
- **Build:** ✅
- **Testar:** comparar CPU clock com `lscpu`; comparar RAM com `free -h`; medir tempo de iteração.

### #07 — Bugs Minor
- **7.1** `SeedCpuUsage()` no construtor de `LinuxTelemetry` inicializa `_last*` com leitura de `/proc/stat`.
- **7.2** Comentário explicando inversão PWM em `SetBrightness`.
- **7.3** `FindActiveNetInterface` filtra por `/sys/class/net/<iface>/operstate == "up"`.
- **7.4** `StopAsync` verifica `_lcd.IsOpen` antes de chamar `SetBrightness(0)` / `Clear()`, e loga em caso de erro.
- **7.5** `log.log` já ignorado por `*.log` no `.gitignore` (confirmado via `git check-ignore`).
- **7.6** AMD/Intel GPU: documentado como limitação (não implementado nesta fase).
- **Arquivos:** `LinuxTelemetry.cs`, `TuringSmartScreenDriver.cs`, `Worker.cs`
- **Build:** ✅
- **Testar:** primeiro snapshot de CPU não é 100%; máquina com docker não pega docker0.

### #08 — install.sh
- `$USER` corrigido para `"${SUDO_USER:-$USER}"` (preserva usuário real com sudo).
- `cp publish/*.so` substituído por `cp -r "$PUBLISH_DIR"/*` (robusto).
- Limpeza idempotente de `INSTALL_DIR` antes de copiar (com guard de segurança).
- Verificação de build output antes de copiar.
- `appsettings.Development.json` não copiado (apenas produção).
- **Arquivo:** `install.sh`
- **Build (sintaxe):** ✅ `bash -n` OK
- **Testar:** rodar `./install.sh` 2x; `systemctl status turing-monitor`; `journalctl -u turing-monitor -n 50`.

## Notas de implementação

- `AddHttpClient<LinuxTelemetry>` foi removido (precisaria de `Microsoft.Extensions.Http`).
  `LinuxTelemetry` mantém `HttpClient` interno com construtor opcional — suficiente para singleton.
- NativeAOT publish validado: `dotnet publish -c Release -r linux-x64 --self-contained true` ✅
- `Theme` continua mutável (`List<ThemeElement>`); a referência é trocada atomicamente dentro do lock.
  Se houver mutação in-place da lista em código futuro, será necessário revisitar.

## Próximos passos (após validação humana)
1. Push do branch.
2. Período de testes de alguns dias em ambiente real.
3. PR para `main`.