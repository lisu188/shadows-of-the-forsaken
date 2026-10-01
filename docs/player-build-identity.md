# Tożsamość builda Windows — #24

`tools/player_build_manifest.py` tworzy powtarzalny `BUILD-INFO.json` wiążący player z pełnym commitem, archiwum wejść Unity, zweryfikowanymi zmianami importera i hashami każdego pliku wynikowego. Używa Python 3.9+ i Git, bez nowych zależności. Nie uruchamia Unity, nie zmienia scen ani plików silnika.

Receipt jest dodatkowym plikiem; nie zwiększa raportowanych `engine_file_count` ani `engine_bytes`. SHA-256 samego receipt należy zachować oddzielnie w raporcie dostawy. To lokalne powiązanie dowodów, nie podpis kompilatora ani dowód uruchomienia gry. Sukces builda nie zalicza rozgrywki, czasu przejścia, audio lub testu użytkownika.

## Kolejność

1. Wybierz pełny commit i osobny checkout zgodnie z [procedurą Windows](unity-testing.md#build-windows-z-czystego-checkoutu). Zachowaj oddzielną, czystą kopię źródłową; Unity może zmienić serializację plików w checkoutcie kompilacji.
2. Zapisz archiwum `Assets`, `Packages`, `ProjectSettings`, wykonane polecenie, hash runnera, stan wejść przed uruchomieniem i rzeczywisty kod zakończenia. Log i dowody trzymaj poza katalogiem playera.
3. Po zakończeniu edytora zamroź te trzy katalogi źródłowe do ZIP. `capture` porówna archiwum z rzeczywistym checkoutem po buildzie oraz zapisze hashe gotowych plików silnika. Nie uruchamiaj równolegle edytora ani innego procesu zmieniającego te pliki.
4. Jeżeli import zmienił źródła, osobno przejrzyj każdą dokładną parę hashy. `create` odrzuca niezrecenzowaną różnicę, zmiany C#, pakietów i usunięcia źródeł.
5. `create` dodaje receipt tylko po pełnej walidacji. `verify` sprawdza dostarczony katalog względem niezależnie zachowanego hasha receipt.

Nie są potrzebne datowane kolektory z `artifacts/validation`. Poniższy ogólny runner wykonuje istniejące `CastlePlayerBuild.BuildForBatch`; nie przebudowuje sceny. Zapisz blok jako `run-castle-build.ps1` poza checkoutem i zachowaj jego niezmieniony plik. Podaj istniejący pusty katalog dowodów i istniejący pusty katalog playera. `SourceRepo` jest czystym checkoutem wybranego commita, `Project` osobną kopią kompilacji tego samego commita. Przed pierwszym importem sprawdź brak cache zgodnie z procedurą Windows, jeżeli zgłaszasz test czystego projektu.

```powershell
param(
    [Parameter(Mandatory=$true)][string]$SourceRepo,
    [Parameter(Mandatory=$true)][string]$Project,
    [Parameter(Mandatory=$true)][string]$Evidence,
    [Parameter(Mandatory=$true)][string]$Player,
    [Parameter(Mandatory=$true)][string]$Commit
)
$ErrorActionPreference = 'Stop'
$SourceRepo = (Resolve-Path $SourceRepo).Path
$Project = (Resolve-Path $Project).Path
$Evidence = (Resolve-Path $Evidence).Path
$Player = (Resolve-Path $Player).Path
if ($SourceRepo -eq $Project) { throw 'Use a separate source checkout.' }
if ((Get-ChildItem -Force $Evidence).Count -or (Get-ChildItem -Force $Player).Count) {
    throw 'Evidence and player directories must be empty.'
}
foreach ($repo in @($SourceRepo, $Project)) {
    $head = git -C $repo rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $head -ne $Commit -or $Commit -notmatch '^[0-9a-f]{40}$') {
        throw 'Source commit mismatch.'
    }
    $dirty = git -C $repo status --porcelain -- Assets Packages ProjectSettings
    if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Dirty Unity inputs.' }
}
$sourceArchive = Join-Path $Evidence 'source.zip'
git -C $SourceRepo archive --format=zip --output=$sourceArchive $Commit -- Assets Packages ProjectSettings
if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
$sourceHashes = @{}
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
    Get-ChildItem (Join-Path $Project $folder) -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($Project.Length + 1).Replace('\', '/')
        $sourceHashes[$relative] = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$cli = 'C:\Program Files\Unity Hub\resources\unity.exe'
$editor = 'C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe'
$log = Join-Path $Evidence 'editor.log'
$env:SHADOWS_PLAYER_OUTPUT = Join-Path $Player 'ShadowsOfTheForsaken.exe'
$arguments = @('run', $Project, '--editor-path', $editor, '--timeout', '3600',
    '--no-tail', '--non-interactive', '--no-log-proxy', '--log-file', $log,
    '--', '-executeMethod', 'CastlePlayerBuild.BuildForBatch', '-disableaudio')
$runnerHash = (Get-FileHash $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()
& $cli @arguments
$buildExit = $LASTEXITCODE
$record = @{
    mode='build'; command=@($cli) + $arguments; runner_sha256=$runnerHash;
    source_sha256=$sourceHashes; player_output=$env:SHADOWS_PLAYER_OUTPUT;
    exit_code=$buildExit; stopped=$null
}
$record | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $Evidence 'execution.json') -Encoding UTF8
if ($buildExit -ne 0) { throw 'Build failed; retained evidence is not a deliverable.' }
Push-Location $Project
try {
    python -m zipfile -c (Join-Path $Evidence 'imported.zip') Assets Packages ProjectSettings
    if ($LASTEXITCODE -ne 0) { throw 'Imported source archive failed.' }
} finally { Pop-Location }
```

Limit miejsca i czas działania edytora pozostają obowiązkiem operatora/runnera; narzędzie receipt niczego nie usuwa. Przerwanego builda nie wolno zmieniać w sukces przez edycję `execution.json`. Zachowaj nieudane logi oddzielnie i użyj nowego pustego katalogu dla ponowienia.

Z czystej kopii zawierającej nowe narzędzie, po zakończeniu powyższego runnera:

```powershell
python tools/player_build_manifest.py capture `
  --execution C:\castle-evidence\execution.json --editor-log C:\castle-evidence\editor.log `
  --runner C:\run-castle-build.ps1 --project C:\castle-clean `
  --imported-archive C:\castle-evidence\imported.zip --player-dir C:\castle-player `
  --output C:\castle-evidence\completion.json
if ($LASTEXITCODE -ne 0) { throw 'Completion capture refused.' }

python tools/player_build_manifest.py create `
  --repo C:\castle-source --commit <FULL_COMMIT> `
  --source-archive C:\castle-evidence\source.zip --imported-archive C:\castle-evidence\imported.zip `
  --completion C:\castle-evidence\completion.json `
  --execution C:\castle-evidence\execution.json --editor-log C:\castle-evidence\editor.log `
  --runner C:\run-castle-build.ps1 --player-dir C:\castle-player
if ($LASTEXITCODE -ne 0) { throw 'Receipt refused.' }
```

Podstaw pełny commit zamiast `<FULL_COMMIT>`. `create` wymaga `HEAD` wskazanego repozytorium równego temu commitowi i wszystkich rzeczywistych plików trzech katalogów źródeł zgodnych z Git — również pliki ignorowane i zmiany staged są sprawdzane. Zmiany dokumentacji poza tymi katalogami nie są wejściem Unity.

## Dokładny przegląd zmian importera

Jeżeli archiwa mają różne mapy plików, dodaj `--review C:\castle-evidence\import-review.json`. Format jest jawny, bez automatycznej akceptacji różnic:

```json
{
  "schema_version": 1,
  "source_commit": "<40 lowercase hex>",
  "source_archive_sha256": "<64 lowercase hex>",
  "imported_archive_sha256": "<64 lowercase hex>",
  "unreviewed_pairs": 0,
  "unresolved_findings": 0,
  "differences": [
    {
      "path": "Assets/Example.mat",
      "source_sha256": "<exact committed payload hash>",
      "imported_sha256": "<exact frozen imported payload hash>",
      "reason": "Describe the reviewed serialized change and why it preserves the intended build."
    }
  ]
}
```

Każda różnica musi wystąpić dokładnie raz; hashe są sprawdzane względem obu archiwów. Dozwolone kategorie do indywidualnego przeglądu: istniejące pliki `.mat`, `.unity`, `.asset`, `.meta` pod `Assets`; nowe pliki `.meta` (`source_sha256: null`); oraz dokładnie `ProjectSettings/ProjectAuditorSettings.asset`, `ProjectSettings/ProjectSettings.asset` i `ProjectSettings/SceneTemplateSettings.json`. Ostatni z tych plików może również zostać dodany przez import — wymaga wtedy dokładnej pary z `source_sha256: null`. Sama nazwa/kategoria nie oznacza akceptacji. C#, manifest pakietów, pozostała konfiguracja, nowe nie-meta zasoby i usunięcia wymagają poprawnego commita oraz nowego builda.

Mapa wejściowa procesu może łączyć pliki w stanie zapisanym w commicie z plikami już zaimportowanymi, lecz wyłącznie w granicach dokładnie przejrzanych par. Musi zawierać wszystkie ścieżki z commita i nie może zawierać ścieżek nieobecnych w końcowym archiwum importu. Hash każdej obecnej ścieżki musi być równy jej końcowemu hashowi importu albo — jeżeli ścieżka istniała w commicie — jej hashowi z commita. Dozwolony nowy plik importera może być jeszcze nieobecny przed buildem; jeśli już istnieje, musi mieć dokładny końcowy hash. Brak pliku z commita, dodatkowa nieznana ścieżka, `null` lub trzeci, nieprzejrzany hash powodują odmowę. Wszystkie dotychczasowe ograniczenia kategorii i pełnego przeglądu różnic nadal obowiązują.

Mapa wejściowa w `completion.json` musi być identyczna z mapą zapisaną przez runner w `execution.json`. Receipt zachowuje ją jako `pre_build_source_sha256`, osobno od końcowej mapy `imported_source_sha256` i archiwum źródeł commita. Dzięki temu częściowo zaimportowany checkout nie wymaga ponownego builda tylko dla formatu receipt; żaden pośredni, trzeci stan pliku nie jest automatycznie akceptowany. Stare manifesty o innym schemacie zachowaj jako historyczne dowody; nie zastępuj hashy w istniejącym raporcie.

## Weryfikacja dostarczonego katalogu

Zachowaj `receipt_sha256` wydrukowany przez `create` poza katalogiem playera, np. w raporcie PR. Następnie:

```powershell
python tools/player_build_manifest.py verify --player-dir C:\delivered-castle `
  --receipt-sha256 <RETAINED_RECEIPT_SHA256> --commit <FULL_COMMIT> `
  --source-archive C:\castle-evidence\source.zip
if ($LASTEXITCODE -ne 0) { throw 'Delivery identity mismatch.' }
```

Weryfikacja odrzuca brakujące, dodatkowe, zmienione lub przemianowane pliki, również o niezmienionym rozmiarze; dowiązania i reparse points nie są akceptowane. Receipt przechowuje własny manifest plików silnika, więc nie wymaga datowanego pliku dowodowego z repo. Pola `source_commit`, `source_archive_sha256`, `unity` i `result` zachowują interfejs poprzedniego BUILD-INFO oraz raportu czasu trasy.

Ponowne `create` z identycznymi dowodami jest idempotentne. Odmienny lub częściowo zapisany BUILD-INFO nigdy nie jest nadpisywany. Jeżeli zapis przerwano, zachowaj wadliwy plik do analizy i dopiero po ustaleniu przyczyny przygotuj nowy katalog dostawy. Nie używaj weryfikacji jako dowodu autentyczności dowolnego receipt, którego oczekiwany hash pochodzi z tego samego niezaufanego katalogu.

## Testy narzędzia

```sh
python3 -B -m unittest discover -s tests -p 'test_player_build_manifest.py' -v
```

Fixture'y tworzą małe lokalne repozytoria i syntetyczne pliki playera/logów. Testują mieszaną mapę dokładnych hashy przed buildem oraz odmowę trzeciego hasha, brakującej/dodatkowej ścieżki, `null` i rozbieżnych map runnera/completion. Sprawdzają również błędny commit, brudne źródła, inne archiwum, podmieniony player, przerwany proces, błędny przegląd importu i nadpisanie receipt. Nie wykonują ani nie symulują zaliczonego testu Unity.
