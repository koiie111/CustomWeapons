# Changelog

## 1.0.4-rc.4 — 2026-11-10

- Refresh first-person models by replacing an owned weapon after model selection, pickup or equip when its cached model differs. Same-entity redeploy failed the client check and is not used.
- Detach the exact old handle through item services, verify inventory removal and await serial invalidation before granting the replacement. Preserve ammo, appearance, modes, mesh, recoil and stricter attack cooldowns while retaining the new item ID.
- Restore appearance once after deferred give hooks without rolling back shots fired in that frame. Coalesce work, cache refreshed handles and limit retire/grant/settlement to four work units per tick, without a tick listener or repeating timer.
- Cancel deferred work across session/pawn/owner/map/round changes. Keep grenade/projectile behavior and O4 fast paths. Add schema-sized economy-vector access for CSS 375, embedded attribute names and packaged gamedata.
- Add 129 linked-source checks, a frozen perf.2/current callback comparison, 25 native state-replacement checks and single-client selection/pickup acceptance. Other clients, full-player load and extended lifecycle/WeaponPaints scenarios remain unverified for this build.

## 1.0.3-perf.2 — 2026-11-10

- Reject MySQL results from an earlier map and request a fresh snapshot before publishing access or applying models.
- Skip definition lookup/normalization for already painted handles during non-forced inventory applications.
- Add production callback checks, a baseline/candidate synthetic harness, and the 20 original CSSProfiler exports with an offline integrity/distribution verifier.

## 1.0.3-perf.1 — 2026-11-10

- When no grenade category is configured, omit the projectile entity-spawn listener as well as the existing tick listener. Gun and knife model application and entity-deletion cleanup remain registered.
- Record a paired empty-server CSSProfiler comparison with the pre-O4 baseline and the current main build in `docs/PERFORMANCE-CSSPROFILER-20261110.md`.

## 1.0.2 — 2026-09-28

- Исправлена ранняя инициализация: игровые события, таймеры и перебор игроков запускаются только после OnMapStart; горячая загрузка — через отложенный callback. Холодный Load больше не вызывает GetPlayers до готовности globals.
- Устранён путь к аварии при подключении после неудачной загрузки: в CSS 375 отложенные игровые hooks могут пережить освобождение managed callback. CustomWeapons больше не регистрирует эти hooks во время холодного Load.
- Выгрузка после неполной загрузки не обращается к игрокам; отложенные операции проверяют готовность карты и остановку плагина.
- При отсутствии ручного выбора автоматически применяется первый доступный и precached скин категории в порядке конфигурации. Проверяются сервер, срок доступа и каталог. Ручной выбор, включая стандартную модель, имеет приоритет и сохраняется как раньше.
- Автовыбор не записывается поверх предпочтений. Недоступный сохранённый скин не заменяется другим; выбор сохраняется до восстановления доступа или ручного изменения.
- Добавлены регрессионные тесты жизненного цикла и автовыбора. Исправление требует проверки подключения на живом сервере; нативный CS2 не запускается в CI.

## 1.0.1 — 2026-09-28

- Минимальная версия CounterStrikeSharp снижена с 376 до 375.
- Плагин пересобран с CounterStrikeSharp.API 1.0.375; .NET 10 сохранён.
- Исправлен отказ загрузки на сервере с API 375. Конфигурация и данные совместимы с 1.0.0.

## 1.0.0 — 2026-09-28

- Самостоятельный плагин CounterStrikeSharp на .NET 10.
- Центральное меню, ручной cooldown, шесть скинов в примере конфигурации.
- Чтение существующих cw_access/cw_skins без изменения схемы и записей.
- Глобальные и серверные доступы, сроки действия, JSON-сохранение выбора.
- Сохранение модели экземпляра при передаче и истечении доступа.
- Поддержка конфигурируемых классов оружия, ножей и стандартных гранат.
- Автоматические тесты, MySQL-интеграция в CI, установочный ZIP и SHA-256.

Игровая проверка на живом CS2-сервере и с пользовательскими моделями не выполнена. Это не подтверждение совместимости конкретных ресурсов и анимаций.
