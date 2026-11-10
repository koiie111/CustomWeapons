# Проверки игрового слоя без движка

`Runtime.csproj` компилирует реальные `CustomWeaponsPlugin.cs` и `PluginConfig.cs`
с управляемыми заменами границы CSS. Core используется из production-проекта.
Тесты вызывают зарегистрированные hooks/events/timers; MySQL заменяется только
через `IAccessRepository`. Сервер, production DB и файлы предпочтений не нужны.

```powershell
dotnet restore tests/Runtime/Runtime.csproj --locked-mode
dotnet run --project tests/Runtime/Runtime.csproj -c Release --no-restore
python tests/verify_profiles.py
```

Проверяются feature gates, отсутствие двойных регистраций после смены карты,
все projectile aliases, ранний capture и отложенное сопоставление thrower,
передача нестандартной модели и обычная вторая flash, discovery после выдачи
другим плагином, removal после drop/death, identity слота, safety retry,
map-scoped DB result, отказ DB и unload с отложенным callback.

`-- map-only` запускает отдельную регрессию старого DB result. На исходном
коде до исправления она падает с `Old-map read cannot publish catalog`.

Заменены native handles, валидность сущностей, SetModel, precache, меню, transport
и расписание callbacks. Suite не доказывает CSS ABI, реальный порядок событий,
клиентский вид/анимации или тайминги гранат. Эти проверки остаются игровыми.

## Одинаковый synthetic harness

Создать baseline checkout `c52b926dab2e4c234d22e901b71d1709929837e7` вне web root.
Передать абсолютные пути к обоим baseline-проектам: иначе Core будет взят из
кандидата и сравнение аллокаций `GrenadeTracker` станет неверным.

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet run --project tests/Runtime/Runtime.csproj -c Release -p:PluginSource=C:/work/cw-baseline/src/CustomWeapons -p:CoreProject=C:/work/cw-baseline/src/CustomWeapons.Core/CustomWeapons.Core.csproj -- bench baseline.json
dotnet run --project tests/Runtime/Runtime.csproj -c Release -- bench candidate.json
```

32 connected, 0/1/8/32 holders, по одному оружию, доступные публичные модели.
2 000 warmup, 3 × 10 000 вызовов каждого пути; отдельно production OnTick и
настоящий callback safety timer, включая его inventory scan и prune. Это
steady state между пятиисекундными forced sweeps; cold population и изменения
проверяются функционально. Прогон не включает cadence/полный tick, spawn,
background DB/JSON, native overhead и аллокации реального GetPlayers.
Числа нужны для сравнения повторной работы, а не обещания live-ускорения.

Raw — `docs/performance/review-20261110/{baseline,candidate}.json`. Counts/scans
и чтения definition доступны для каждого окна. Статистика времени — медиана
трёх окон; bytes/call относятся к этому managed fixture. Production-сборки
проверяются отдельно против настоящего CounterStrikeSharp.API 1.0.375.
