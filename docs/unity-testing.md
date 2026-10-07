# Testy Unity i integralność źródeł — issue #5

## Stan i granice weryfikacji

Po połączeniu gałęzi poziomu z bieżącym `main` oczekiwany pełny zestaw źródłowy obejmuje **187 przypadków EditMode i 228 PlayMode**. Zachowuje wszystkie 173/202 przypadki z `main`, dodaje 14 przypadków `CombatRulesTests` oraz 26 przypadków alternatywnego poziomu, walki, interakcji, HUD-u i napisów. Liczby oznaczają wymagania katalogu, nie wykonanie po scaleniu. Obowiązują wersje z bieżącego `main`: Unity `6000.6.3f1`, Input System `1.20.0`, URP `17.6.0` i Test Framework `1.8.0`. Historyczne wyniki 113/113 i 74/74 gałęzi z 2026-10-06 dotyczyły Unity `6000.0.24f1`; nie potwierdzają tej połączonej konfiguracji. [Raport poziomu](level-verification.md) oddziela te dowody od późniejszej walidacji.

W ramach #5 dodano kod **6 przypadków EditMode i 2 PlayMode**, osobne assembly definitions, walidator źródeł i raportów NUnit, lokalny runner oraz workflow `Unity tests`. W tamtym zadaniu istniejące skrypty, sceny, wersje pakietów i GUID-y nie zostały zmienione. Obecna konfiguracja wskazuje Unity `6000.6.3f1` i pakiety wymienione w README po późniejszej aktualizacji na polecenie właściciela. Dnia 2026-09-30 rzeczywisty Windows Unity CLI potwierdził czysty import, kompilację i rozszerzony zestaw **105/105 EditMode oraz 68/68 PlayMode**; zob. [raport](validation/unity-castle-2026-09-30.md).

Osobna walidacja mechanik #9/#11 przed połączeniem z blockoutem zaliczyła **148/148 EditMode i 89/89 PlayMode**; [raport wspólnych mechanik](validation/shared-gameplay-2026-09-30.md) zawiera również wyniki .NET, zapisane sceny demonstracyjne, obrazy oraz granice odbioru. Po rebase na scalony blockout ponownie wykonano oba pełne zestawy: **153/153 EditMode i 109/109 PlayMode**, bez błędów i pominięć. Raport zawiera dziesięć obrazów z kamer zapisanych scen oraz rozdziela zgodne źródła od normalizacji materiałów przez import Unity.

Pierwsza próba CI w [PR #26](https://github.com/lisu188/shadows-of-the-forsaken/pull/26) wykazała **brak konfiguracji aktywacji Unity**. Etap `Unity activation prerequisite (not tests)` zakończył się błędem, a testy edytora nie wystartowały. Nie jest to wynik testów C# ani dowód błędnej kompilacji. Lokalne raporty obu trybów i log czystego importu są już dostępne; nie konfigurują sekretów GitHub Actions ani nie zamykają automatycznie #5. Historyczny brak aktywacji nie ustala aktualnego stanu zdalnego CI; ten wymaga sprawdzenia bieżącego uruchomienia i dokładnego commita.

`Validate` sprawdza pliki, dokumentację i narzędzia w Pythonie. Zielony wynik tego workflow **nie potwierdza** importu, kompilacji, działania fizyki, kamery, walki lub grafiki. Syntetyczne XML-e i mock procesu w `tests/test_unity_validation.py` testują tylko zachowanie walidatora/runnera; nie są atrapą uruchomienia gry ani dowodem wykonania testów Unity.

## Zakres testów silnika

| Assembly | Przypadki bazowe | Sprawdzane elementy |
| --- | ---: | --- |
| `Shadows.EditMode.Tests` | 6 | Wersja edytora; obecność sceny w Build Settings; import obu skryptów z oryginalnymi GUID-ami; brak Missing Script w scenie; rozwiązywalne shadery materiałów. |
| `Shadows.PlayMode.Tests` | 2 | Załadowanie rzeczywistego `SampleScene.unity`, kilka klatek bez nieoczekiwanych błędów, aktywna główna kamera z AudioListener oraz światło kierunkowe. |

Scena bazowa pozostaje sceną szablonową. Testy nie twierdzą, że zawiera gracza albo gotowy poziom zamku. Testy EditMode sprawdzają import istniejących skryptów przez `MonoScript`, bez przenoszenia ich do nowych assemblies. Assembly testowe nie są dołączane do zwykłych buildów gry. Podstawowe testy PlayMode nie wymagają UnityEditor; edytorowe testy zapisanych scen demo korzystają z niego warunkowo, ponieważ demonstracje celowo nie należą do Build Settings.

Dla [blockoutu #8](castle-layout.md) dodano 5 przypadków EditMode oraz 20 PlayMode: 18 kierunkowych przejść dziewięciu połączeń, skok na dziedzińcu i próba przejścia/skoku przez ścianę zagadki. Raport blockoutu 105/105 i 68/68 zawiera te przypadki oraz wcześniejsze testy rdzeni i komponentów; wszystkie zaliczono w rzeczywistym Unity 2026-09-30. Walidator nadal wymaga obecności nazwanych regresji blockoutu w połączonym zestawie.

Dla [interakcji #9](interactions.md) dodano 17 przypadków wspólnych reguł w EditMode i 15 PlayMode obejmujących wybór celu, zasięg, przeszkody, wejście, sesje oraz fizyczne bramy. [Walka #11](combat.md) dodaje 31 przypadków rdzenia w EditMode i 24 PlayMode dotyczące trafień, zdrowia, wejścia, fizyki i cyklu życia. Dwa dalsze przypadki PlayMode ładują rzeczywiste zapisane sceny [InteractionDemo](../Assets/Interactions/Demo/InteractionDemo.unity) i [CombatDemo](../Assets/Combat/Demo/CombatDemo.unity). Te sceny celowo pozostają poza Build Settings. Walidator wymaga dokładnych tożsamości wszystkich przypadków, w tym argumentów testów parametryzowanych; brak nowego przypadku w raporcie jest błędem walidacji.

Pierwsza lokalna próba PlayMode miała 34 zaliczenia i 34 błędy: bez fokusu Game View domyślne `InputSystem.Update()` kierowało wejście do aktualizacji edytora, więc fixture'y nie dostarczały syntetycznego wejścia graczowi. `PlayerMovementTests` i `CastleLayoutTraversalTests` zapisują teraz, ustawiają na czas testu i przywracają `backgroundBehavior = IgnoreFocus` oraz `editorInputBehaviorInPlayMode = AllDeviceInputAlwaysGoesToGameView`; dodatkowo sprawdzają typ aktualizacji `Manual`. Ponowny pełny PlayMode zaliczył 68/68. Produkcyjne blokady fokusu/pauzy, asercje ruchu, geometria i zależności pozostały bez zmian. Pierwotny nieudany raport jest zachowany w dowodach, a nie zastąpiony wynikiem ponowienia.

## Kontrole bez edytora

Wymagane: Git oraz Python 3.9 lub nowszy, bez dodatkowych bibliotek.

```sh
python3 tools/unity_validation.py project
python3 -m unittest discover -s tests -p 'test_*.py' -v
```

Walidator korzysta z `git ls-files`, więc wykrywa również `.meta` istniejące lokalnie, ale nieśledzone przez Git. Sprawdza brakujące metadane plików i folderów, zerowe/błędne/zdublowane GUID-y, osierocone metadane plików, kolizje wielkości liter, katalogi generowane i zgodność przypiętych wersji z lockfile. Nie naprawia ani nie regeneruje GUID-ów. Lokalne nieśledzone cache nie są uznawane za źródła.

## Rzeczywisty test lokalny

Zainstaluj i aktywuj **Unity 6000.6.3f1**, zamknij projekt w edytorze i uruchom oba tryby oddzielnie. Na Windows, z katalogu repozytorium:

```powershell
python tools/unity_validation.py run --editor "C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe" --mode editmode
python tools/unity_validation.py run --editor "C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe" --mode playmode
```

Na Linux/macOS podaj rzeczywistą ścieżkę do binarnego pliku Unity przez `--editor`. Runner używa już aktywowanego edytora: nie przyjmuje haseł ani nie wykonuje aktywacji. W środowisku Linux bez ekranu potrzebny jest działający serwer wyświetlania, np. Xvfb; same kontrole źródeł tego nie wymagają.

Domyślny limit wynosi 900 sekund (`--timeout`); `--output` pozwala zmienić katalog wyników. Stary XML danego trybu jest usuwany przed uruchomieniem, a brak nowego raportu, błąd procesu, timeout, pominięty test i niepoprawne liczniki kończą polecenie kodem niezerowym. Log edytora i XML trafiają do `artifacts/unity-local`. Runner nie dodaje `-quit` do `-runTests`.

Czysty import należy sprawdzić na **osobnym świeżym checkoutcie**, bez otwierania go wcześniej w Hubie; nie trzeba usuwać `Library` z używanej kopii roboczej. Sam runner lokalny nie kasuje cache i nie deklaruje automatycznie, że uruchomienie było czystym importem.

## Build Windows z czystego checkoutu

Użyj aktywowanego Unity **6000.6.3f1** z modułem Windows Build Support. Poniższy przykład wybiera dokładny commit; nowego checkoutu nie otwieraj wcześniej w edytorze. `CastlePlayerBuild.BuildForBatch` buduje zapisaną scenę `Assets/Scenes/ForsakenCastle.unity` bez uruchamiania buildera sceny.

```powershell
git clone --config core.autocrlf=false --config core.eol=lf --no-checkout https://github.com/lisu188/shadows-of-the-forsaken.git castle-clean
if ($LASTEXITCODE -ne 0) { throw "Nie utworzono świeżego checkoutu." }
git -C castle-clean checkout --detach 8602b8e38f0f933c01efc00b0f42fd50bf71659a
if ($LASTEXITCODE -ne 0) { throw "Nie wybrano wymaganego commita." }
$castleCommit = git -C castle-clean rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $castleCommit -ne "8602b8e38f0f933c01efc00b0f42fd50bf71659a") { throw "Niezgodny commit wejściowy." }
$castleProject = (Resolve-Path .\castle-clean).Path
foreach ($cache in @("Library", "Temp", "UserSettings")) {
    if (Test-Path (Join-Path $castleProject $cache)) { throw "Checkout zawiera lokalny stan Unity: $cache" }
}
$castleOutput = Join-Path (Get-Location).Path "castle-build-8602b8e"
New-Item -ItemType Directory -Path $castleOutput -ErrorAction Stop
$env:SHADOWS_PLAYER_OUTPUT = Join-Path $castleOutput "ShadowsOfTheForsaken.exe"
& "C:\Program Files\Unity Hub\resources\unity.exe" run $castleProject --editor-path "C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe" --timeout 3600 --no-tail --non-interactive --no-log-proxy --log-file "$castleOutput\editor.log" -- -executeMethod CastlePlayerBuild.BuildForBatch -disableaudio
if ($LASTEXITCODE -ne 0) { throw "Build Unity nie został zaliczony." }
```

Przed pierwszym importem sprawdź brak `Library`, `Temp` i `UserSettings`; zachowaj commit, hash wejść, polecenie i log. Sukces musi potwierdzać wpis `Castle Windows build: Succeeded` z zerową liczbą błędów, nie tylko obecność pliku `.exe`. Zachowaj cały katalog wynikowy wraz z `ShadowsOfTheForsaken_Data` i bibliotekami; uruchamia się `ShadowsOfTheForsaken.exe`. Samo zbudowanie nie oznacza wykonania tego uruchomienia ani odbioru rozgrywki. `-disableaudio` ogranicza tę walidację edytora; audio wymaga osobnego odbioru.


## Aktywacja GitHub Actions

Postępuj według [instrukcji aktywacji GameCI](https://game.ci/docs/github/activation/) odpowiedniej dla posiadanej licencji. W repozytorium otwórz **Settings → Secrets and variables → Actions**. Skonfiguruj `UNITY_EMAIL`, `UNITY_PASSWORD` oraz **jeden** z sekretów: `UNITY_LICENSE` dla Personal albo `UNITY_SERIAL` dla Pro. Nie wklejaj wartości do README, issue, komentarza PR, pliku repozytorium ani rozmowy.

Po konfiguracji uruchom workflow `Unity tests` przez **Actions → Unity tests → Run workflow**, wskazując aktualne `main`. Można też ponowić nieudany run. Obecność sekretów jest tylko warunkiem wstępnym; poprawność aktywacji potwierdzi dopiero rzeczywisty proces Unity.

Workflow działa na push do `main`, pull requestach i ręcznie. Nie używa `pull_request_target`; fork PR i Dependabot nie uruchamiają zadań z sekretami. Takie pominięcie nie jest dowodem zaliczenia testów. Dla zmian z zewnętrznego źródła testy z aktywacją uruchamiać dopiero po przeglądzie na zaufanej gałęzi.

Testy obu trybów są wykonywane kolejno, każdy na świeżym runnerze bez cache `Library`. GameCI Test Runner jest przypięty do commita `0ff419b913a3630032cbe0de48a0099b5a9f0ed9` (v4.3.1), z Unity 6000.6.3f1. Raporty `editmode-results.xml` / `playmode-results.xml` i logi edytora są publikowane jako osobne artefakty z identyfikatorem commita. Nie jest wymagany token z prawem zapisu checks; poświadczenia checkoutu nie są utrwalane.

## Zasady odbioru

`tools/unity_validation.py results --mode editmode <raport.xml>` i odpowiednik `--mode playmode` wymagają rzeczywistego raportu NUnit z wynikiem `Passed`, zgodnymi licznikami, zerową liczbą failed/skipped/inconclusive i **całym oczekiwanym zestawem danego trybu**: obecnie 187 EditMode i 228 PlayMode. `EXPECTED_TESTS` zawiera jawne nazwy `Fixture.Metoda` oraz krotki dokładnych sufiksów argumentów zgodnych z przypiętym runnerem NUnit; `EXPECTED_CASES` rozwija je do wymaganych tożsamości przypadków. Walidator porównuje pełne `fullname` z właściwą przestrzenią nazw, zachowując wszystkie argumenty, np. `CombatRulesTests.SwingHitsExactlyOnceAtRepresentativeFrameRates(30)`. Metoda bez argumentów ma pusty sufiks. Nie można zastąpić wymaganych `(30)`, `(60)`, `(120)` wariantami `(31)`, `(32)`, `(33)` nawet przy niezmienionej liczbie zaliczonych przypadków; dodatkowy zielony przypadek również nie zastępuje brakującej tożsamości.

Wymagania wynikają z deklaracji testów w źródłach, a nie z właśnie ocenianego raportu. Dlatego zielony stary zestaw bazowy nie może zastąpić pełnej integracji końcowego poziomu. Sam kod wyjścia zero, pusty raport, brak wyników, częściowy zestaw lub wyłącznie testy innego trybu nie wystarczają. Przy uzasadnionej zmianie źródłowych nazw lub argumentów utrzymać spójność `EXPECTED_TESTS`, testów walidatora i dokumentacji; nie zmniejszać wymagań, aby zaakceptować niepełny wynik. Syntetyczne raporty w testach Pythona sprawdzają odrzucanie zmienionych argumentów liczbowych, tekstowych, enumów, wartości bool i wartości nieskończonych/NaN; nie dowodzą wykonania odpowiadających im przypadków w Unity.

Do zamknięcia #5 należy dołączyć: udany run obu trybów, XML-e, logi importu/kompilacji i potwierdzenie uruchomienia sceny. Przy błędzie aktywacji sprawdzić konfigurację; przy błędzie kompilacji lub asercji poprawić kod. Nie usuwać testów, nie ustawiać `continue-on-error` i nie zmieniać warunku wstępnego na pozorny sukces tylko po to, aby uzyskać zielony status.

Dla Windows x64 istnieje jawny `CastlePlayerBuild.BuildForBatch`, a `FullCastleRouteTests` obejmuje kompletną scenę i reset. Wyniki wykonania oraz pozostały odbiór #24/#22 zapisuje [historyczny raport main](validation/gothic-presentation-2026-10-01.md); obecność kodu nie jest potwierdzeniem udanego builda ani zaliczonego zestawu.

Wykonanie pełnych fixture'ów sceny nie zastępuje osobnego builda Windows x64 ani jego uruchomienia. Test startu natywnego playera i FPS na dziedzińcu nie jest benchmarkiem całego poziomu; automatyczny czas przejścia w edytorze nie jest ręcznym playtestem. Aktualny raport musi osobno wskazywać: dokumentację/kontrakt, testy .NET, EditMode, PlayMode, kompilację playera, jego rzeczywisty start, automatyczny pomiar trasy, przegląd obrazów oraz niewykonane kontrole człowieka i pełnej wydajności playera. Nie oznaczać tych ostatnich jako zaliczone tylko na podstawie testów automatycznych.

## Źródła techniczne

Poniższe odnośniki pochodzą z implementacji #5. Dokumentacja Test Framework 1.4 jest historycznym odniesieniem; projekt używa obecnie 1.8.0. Rzeczywiste raporty z 2026-09-30 dokumentują historyczny zestaw main. Bieżący zestaw połączony dokumentuje [raport integracji](validation/main-merge-2026-10-07.md); odnośniki do API nie są dowodem wykonania testów.

- [Unity Test Framework 1.4: uruchamianie z linii poleceń](https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/reference-command-line.html).
- [Unity Test Framework: assemblies testowe](https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/workflow-create-test-assembly.html).
- [GameCI Test Runner](https://game.ci/docs/github/test-runner/).
- [Format raportu NUnit](https://docs.nunit.org/articles/nunit/technical-notes/usage/Test-Result-XML-Format.html).

## Pełna trasa zamku

Historyczna integracja main dodała AI trzech starć, sesję, HUD, restart i opcjonalny relikt. `FullCastleRouteTests` używa rzeczywistych W/S, A/D, E, LPM oraz R przez Input System w zapisanej scenie; nie ustawia flag postępu ani nie wstrzykuje obrażeń. Zestaw zawiera główną trasę bez sekretu, trasę z reliktem i skrótem, porażkę w każdym z trzech starć oraz zablokowane przejścia. [Historyczny raport main](validation/full-castle-route-2026-09-30.md) oddziela te wyniki od wcześniejszego blockoutu i playera Windows.
