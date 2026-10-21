# IFZ Co-op Updater

Кооп-сборка **Infection Free Zone 0.26.10.8** на базе
[IFZ Multiplayer RC7](https://www.nexusmods.com/infectionfreezone/mods/96) от **dbh4you**
(Nexus: `infectionfreezone/mods/96`), с патчами под актуальный билд игры.

## Что делает updater

1. Проверяет `manifest.json` в этом репозитории.
2. Скачивает **BepInEx 5.4.23.2 x64** с официального GitHub-релиза и сверяет SHA-256.
3. Скачивает архив мода из раздела Releases этого репозитория и сверяет SHA-256.
4. Делает резервную копию заменяемых файлов в `_coop_backup_<дата>` внутри папки игры.
5. Устанавливает/обновляет мод, пишет `steam_appid.txt` = 480, создаёт ярлык и `Запуск кооп.bat`.

Игра в комплект не входит.

## Установка

1. Скачай `IFZ-Co-op-Updater.exe` из [Releases](../../releases/latest).
2. Запусти, укажи папку игры (где лежит `Infection Free Zone.exe`).
3. Нажми **Установить / Обновить**.
4. Играй через ярлык «Infection Free Zone (Co-op)» или `Запуск кооп.bat` — **не** через кнопку Play в Steam.

Steam должен быть запущен и залогинен: игра стартует как Spacewar (AppID 480), поэтому
Steam-лобби и P2P работают без проброса портов.

## Как играть вместе

**Хост:** `MULTIPLAYER → HOST CAMPAIGN → HOST` → вкладка `VOTES`: город → `GO` → выбрать → `LOCK`
→ `HOST GAME` → `ПРОДОЛЖИТЬ` → выбрать здание под штаб → `Visibility: Public` (или Friends + инвайт).

**Друг:** `MULTIPLAYER → SERVER BROWSER → REFRESH → IFZ Multiplayer Campaign → JOIN`
(или `DIRECT CONNECT <IP>:27555`).

* Штаб каждого игрока — не ближе **200 м** к чужому.
* У всех должна быть одна версия игры и мода.

## Что пропатчено относительно RC7

1. **Совместимость с 0.26.10.8.** Игра добавила параметр `EconomyConfig` в
   `MainResourcesContainer..ctor`, из-за чего у входящего игрока падало основание штаба
   (`Method not found`). Мост `EconomyConfigBridge` берёт настоящий конфиг из Zenject и
   подставляет его в вызов; сам мод перепатчен Cecil-скриптом (`patcher/`).
2. **Список отрядов и туман.** У хоста в ванильном списке отрядов оставался отряд
   необнаруженного игрока. Harmony-патч скрывает строку до обнаружения.
3. **Server Browser.** Мод запрашивал у Steam список лобби без серверного фильтра; под
   Spacewar Steam отдаёт максимум 50 случайных лобби, и лобби IFZ могло не попасть в
   выборку. Добавлен фильтр `ifzmp_proto = 31`.

## Структура

```
manifest.json      версия мода, URL и SHA-256 архивов
updater/           исходники updater'а (net472 WinForms)
bridge/            BepInEx-плагин: мост EconomyConfig + фиксы UI/лобби
patcher/           Cecil-патчер мода под новый билд игры
```

## Лицензии и credits

* **BepInEx** — LGPL-2.1, https://github.com/BepInEx/BepInEx
* **IFZ Multiplayer RC7** — автор **dbh4you** (Discord: `st_kia`),
  https://www.nexusmods.com/infectionfreezone/mods/96. Мод распространяется здесь
  в пропатченном виде для игры с друзьями; если автор попросит убрать — релиз будет снят.
* **Патчи и updater** — этот репозиторий.
* **Infection Free Zone** — © JutsuGames / Games Operators. Файлы игры не входят.

Не является официальным продуктом JutsuGames. Используй на свой риск.
