# AGENTS.md — TuringMonitor

Guia de referência rápida para agentes de IA trabalharem neste repositório.
Leia isto antes de iniciar qualquer tarefa. Mantenha este arquivo atualizado quando
o comportamento do sistema mudar significativamente.

## Visão geral

Monitor de sistema .NET 10 (Native AOT) para Turing Smart Screen 3.5" (Revisão A) no Linux.
Roda como serviço systemd (`turing-monitor.service`), iniciado no boot. Exibe telemetria
(CPU, GPU NVIDIA, RAM, rede) e clima no LCD via porta serial, com engine de temas JSON.

## Comandos essenciais

- **Build (debug):** `dotnet build` (na raiz do repo)
- **Build/instalar serviço (release, Native AOT, systemd):** `chmod +x install.sh && ./install.sh`
- **Ver logs do serviço:** `journalctl -u turing-monitor -f`
- **Reiniciar após mudança:** `sudo systemctl restart turing-monitor`
- **Não há suíte de testes automatizados** — valide com `dotnet build` e teste manual no hardware.
- **Sempre rode `dotnet build` após editar .cs** — deve compilar com 0 warnings e 0 errors.

## Arquitetura

```
Program.cs            → DI setup (IDisplay, ITelemetry, ILayoutManager, Worker)
Worker.cs             → loop produtor (1s) + consumer (desenha no LCD)
LinuxTelemetry.cs     → leitura de /proc, sysfs, nvidia-smi, e APIs de clima
TuringSmartScreenDriver.cs → driver serial do LCD (IDisplay)
LayoutManager.cs      → carrega/gerencia temas JSON (live reload)
TuringMonitorOptions.cs → opções de config (Theme, ThemesRoot, OpenWeatherApiKey)
install.sh            → build Native AOT + instalação systemd
Assets/Themes/        → temas (cada tema tem background.png, theme.json, Icons/, IconCache/)
```

**Fluxo de telemetria**: `Worker.ExecuteAsync` roda um loop a cada 1s que coleta CPU/GPU/RAM/rede/clima
via `ITelemetry` e empurra um `TelemetrySnapshot` em um `Channel<TelemetrySnapshot>` bounded(1)
com `DropOldest`. `RunConsumerAsync` consponde o channel, compara com o snapshot anterior
(delta por fonte — ver `HasChanged`), e só redesenha elementos modificados (overlap-aware).
A cada 30s (`ForceRedrawIntervalSec`) um redraw completo é forçado.

**Live reload de temas**: `Worker` chama `_layout.ReloadIfNeeded()` a cada iteração; se o
`theme.json` mudou no disco, recarrega e chama `ConfigureWeather` para reaplicar a config de
clima do tema.

## Clima (weather) — leia com atenção

Esta é a área mais sensível. Detalhes que agentes devem saber:

### Provedores
- **Open-Meteo** (`openmeteo`, padrão, sem API key): `api.open-meteo.com/v1/forecast` com `current=temperature_2m,weather_code,is_day`. Códigos WMO são mapeados para códigos de ícone OpenWeather via `MapWmoToOwm`.
- **OpenWeather** (`openweather` ou alias `openweathermap`, requer key): `api.openweathermap.org/data/2.5/weather` (Current Weather Data 2.5). Retorna códigos de ícone nativos OWM.

Seleção por `theme.json:WeatherApi`. A key do OpenWeather é resolvida como
`appsettings.json:OpenWeatherApiKey` (prioridade) ?? `theme.json:OpenWeatherApiKey`.
Recomendado colocar em `appsettings.local.json` (gitignored).

### Comportamento de fallback
- Key ausente/inválida (HTTP 401) com `openweather` → loga `ERROR`, fallback **permanente** para Open-Meteo nesta sessão (`_openWeatherFailedPermanent = true`).
- Falha transitória OpenWeather (timeout/5xx/rede) → mantém `openweather`, usa último cache, não mistura provedores.
- `WeatherApi` desconhecido → loga `ERROR`, usa `openmeteo`.

### Agendamento e retry (IMPLEMENTAÇÃO ATUAL — diferente do que parece intuitivo)

- **TTL do cache: 30 minutos contínuos desde a última leitura bem-sucedida** (`WeatherCacheTtlMinutes = 30.0`). **Não** é alinhado a slots de relógio (:00/:30). Razão: ambas APIs retornam condições instantâneas, não em buckets de 30 min; alinhar a slots fixos só descartaria dados válidos recém-buscados e aumentaria a fragilidade (perder um slot = esperar 30 min). Não mude para horário fixo sem discutir com o usuário.
- **Retry com backoff exponencial** (`FetchWithRetryAsync`): ao disparar um fetch, tenta até 6 vezes (1 inicial + 5 retries) com delays `WeatherRetryBackoff = [5s, 10s, 20s, 40s, 80s]`. Sucesso → para e atualiza cache. Falha total → `_nextAllowedFetch = now + 80s` para não hammering a cada segundo.
- **`GetWeatherAsync`** (chamado a cada 1s pelo Worker):
  - Se `_weatherFetching` em flight → retorna cache atual (não dispara novo fetch).
  - Se `_lastWeather == null` → dispara `FetchWithRetryAsync` imediatamente se `now >= _nextAllowedFetch`.
  - Se `_lastWeather != null` → dispara `FetchWithRetryAsync` se `now - _lastWeatherUpdate >= 30 min`.
  - Tudo é fire-and-forget; `GetWeatherAsync` nunca bloqueia o loop de 1s.
- **`FetchOpenMeteoAsync`/`FetchOpenWeatherAsync`** retornam `Task<bool>`: `true` = sucesso e cache atualizado; `false` = falha transitória (caller faz retry/backoff). **Não** mexem em `_weatherFetching` nem `_lastWeatherUpdate` — isso é responsabilidade de `FetchWithRetryAsync`. A exceção é o caso 401 do OpenWeather, que faz fallback imediato chamando `FetchOpenMeteoAsync` dentro do mesmo attempt.

### Persistência do cache (importante para o boot)
- A última leitura bem-sucedida (temp, ícone, timestamp) é salva em `/var/lib/turing-monitor/weather.json` (`PersistWeather`), com fallback para `$HOME/.turing-monitor-weather.json` se `/var/lib/turing-monitor` não for gravável.
- Carregada no startup em `LoadPersistedWeather` (chamado do construtor). **Só reusa se tiver menos de 6 horas** — mais velho que isso é descartado e prefere refazer o fetch.
- Isso resolve o problema clássico de "display em 0°C no primeiro boot" — depois da primeira execução bem-sucedida, o serviço sempre mostra o último valor conhecido enquanto o primeiro fetch retry tenta (com backoff, tipicamente resolve em poucos segundos no boot).

### Estado interno relevante (campos em `LinuxTelemetry`)
- `_lastWeather` / `_lastWeatherUpdate`: o cache em memória + timestamp.
- `_weatherFetching` (volatile): gate para não disparar dois fetches concorrentes.
- `_nextAllowedFetch`: gate após esgotar todos retries (evita hammering).
- `_openWeatherFailedPermanent` (volatile): uma vez true, só reseta reiniciando o serviço.
- `_weatherApi` / `_openWeatherApiKey` / `_lastLoggedWeatherApi`: config resolvida por `ConfigureWeather`.

### Logs
- Retries individuais: nível `Debug` (para não spammar o journal). Se quiser ver, ajuste o nível do logger `LinuxTelemetry` no `appsettings.json` para `Debug`.
- "Falhou após N tentativas": `Warning`.
- Sucesso: `Information` com temp/ícone.

## Configuração e arquivos de estado

- `appsettings.json` / `appsettings.Development.json` / `appsettings.local.json` (gitignored): config de runtime.
- `/var/lib/turing-monitor/weather.json`: cache persistido de clima (criado em runtime, dono root se serviço roda como root).
- `/etc/systemd/system/turing-monitor.service`: unit do systemd, `After=network.target`, `Restart=always`, `RestartSec=5`. Sem `User=` (roda como root por padrão).

## Convenções do código

- C# 13 / .NET 10, `async`/`await`, `using` sem chaves (statement), records para DTOs.
- Source generation para JSON (`JsonSerializerContext`) — sempre adicione novos tipos serializáveis ao `WeatherJsonContext` (ou equivalente) com `[JsonSerializable]`.
- Sempre trate exceções em leituras de telemetria — o serviço não pode cair por causa de um sensor indisponível (padrão: try/catch, logar, retornar 0 ou valor padrão).
- Exceções no loop principal do Worker: logar `Error`, nunca rethrow (exceto `OperationCanceledException`).
- Não adicione comentários ao código a menos que o usuário peça explicitamente.
- Mantenha o estilo existente (espaçamento, nomeação, estrutura de arquivos por responsabilidade).

## Armadilhas conhecidas

- **Não mude o cache para horário fixo** sem discutir — veja "Agendamento e retry" acima.
- `nvidia-smi` é chamado por processo externo com timeout de 5s; se falhar, retorna `(0,0,0,0,0)` — não derruba o serviço.
- O `Channel<TelemetrySnapshot>` é bounded(1) com `DropOldest` — snapshots antigos são descartados se o consumer estiver lento. Não aumente o bound sem motivo (pode atrasar telemetria).
- `ForceRedrawIntervalSec = 30`: a cada 30s um redraw completo é forçado para corrigir artefatos. Ajuste com cuidado (redraw completo é caro).
- Live reload de tema é via timestamp do arquivo `theme.json`; salvar o arquivo aciona reload na próxima iteração.