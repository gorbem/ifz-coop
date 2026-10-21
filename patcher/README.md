# Patcher

`patch_economy.cs` — Cecil-скрипт, который правит `IFZ.Multiplayer.GameIntegration.dll`
под игру 0.26.10.8:

* находит `PlayerEconomyState::.ctor`;
* вставляет `call EconomyConfigBridge.Bridge::Get()` перед аргументом `int`;
* перенаправляет `newobj` на новый 5-аргументный конструктор
  `MainResourcesContainer(CharactersConfig, FoodConsumingConfig, EconomyConfig, int, bool)`.

Запуск (нужен .NET 10 SDK, пакет Mono.Cecil подтянется автоматически):

```
dotnet run patch_economy.cs -- <mod GameIntegration.dll> <EconomyConfigBridge.dll> <Ifz.dll> <out.dll> <ManagedDir>
```

После патча положи `out.dll` в `BepInEx/plugins/IFZMultiplayer/` вместе с остальными
девятью DLL мода и положи `EconomyConfigBridge.dll` в `BepInEx/plugins/EconomyConfigBridge/`.
