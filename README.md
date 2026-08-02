# INFINIX Loader

Публичный снимок исходного кода Windows-лоадера INFINIX.

Репозиторий содержит:

- WPF-интерфейс и анимации запуска;
- обнаружение Steam и установленных версий Counter-Strike;
- обработку `libraryfolders.vdf` и Steam-манифестов;
- отдельную проверку установленной и смонтированной beta-ветки `csgo_legacy`;
- точный запуск `csgo.exe` из legacy-установки вместо стандартного CS2 из App 730;
- подготовку профиля и каталога Lua-библиотек;
- код запуска игры и подключения встроенного модуля;
- smoke-тесты основной логики.

Готовая сборка находится в корне репозитория: [`infinix loader.exe`](./infinix%20loader.exe).

## Требования

- Windows 10 или новее;
- .NET 8 SDK;
- архитектура `win-x86` для релизной сборки.

## Сборка

```powershell
dotnet restore LoaderNL.sln
dotnet build LoaderNL.sln -c Release
```

## Smoke-тесты

```powershell
dotnet run `
  --project tests\LoaderNL.Core.SmokeTests\LoaderNL.Core.SmokeTests.csproj `
  -c Release
```

## Публикация автономного EXE

```powershell
dotnet publish src\LoaderNL.App\LoaderNL.App.csproj `
  -c Release `
  -r win-x86 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o artifacts\publish
```

Манифест приложения использует `requireAdministrator`, поэтому опубликованная сборка запрашивает повышение прав через UAC. Для запуска App 730 Legacy Steam должен полностью установить и смонтировать beta-ветку `csgo_legacy`.

## Поддержка

[Discord INFINIX](https://discord.gg/infinixleague)
