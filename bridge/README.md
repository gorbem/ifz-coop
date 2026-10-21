# EconomyConfigBridge

BepInEx-плагин с тремя фиксами для IFZ Multiplayer RC7 на игре 0.26.10.8:

1. `Bridge.Get()` — отдаёт настоящий `Gameplay.EconomyConfig` из Zenject-контейнера.
   Используется перепатченным `IFZ.Multiplayer.GameIntegration.dll` (см. `../patcher/`),
   потому что игра добавила `EconomyConfig` в `MainResourcesContainer..ctor`.
2. `SquadFogPatch` — скрывает в ванильном списке отрядов строку игрока, пока тот не
   обнаружен (мод скрывает только презентацию в мире).
3. `LobbyFilterPatch` — добавляет серверный фильтр `ifzmp_proto=31` к запросу списка
   лобби Steam, иначе под Spacewar (AppID 480) лимит в 50 лобби скрывает лобби IFZ.

Сборка: `dotnet build -c Release -p:GameDir="<папка игры>"`.
