# Testy Unity i integralność źródeł — issue #5

## Stan i granice weryfikacji

Aktualny komplet źródłowy obejmuje **113 przypadków EditMode i 67 PlayMode**, osobne assembly definitions, walidator źródeł i pełnych raportów NUnit, lokalny runner oraz workflow `Unity tests`. Liczby obejmują rozwinięte warianty `[TestCase]`, a nie tylko nazwy metod. Są to oczekiwane rozmiary zestawów, nie samodzielna deklaracja zaliczenia. Unity `6000.0.24f1`, Input System `1.11.1`, URP `17.0.3`, Test Framework `1.4.5` i oryginalne GUID-y istniejących skryptów pozostają przypięte.

Historyczna pierwsza próba CI w [PR #26](https://github.com/lisu188/shadows-of-the-forsaken/pull/26) wykazała **brak konfiguracji aktywacji Unity**. Etap `Unity activation prerequisite (not tests)` zakończył się błędem, a testy edytora nie wystartowały. Nie był to wynik testów C# ani dowód błędnej kompilacji. Tego historycznego wyniku nie należy przedstawiać jako obecnego wyniku lokalnego importu lub aktywacji. Bieżący zdalny workflow CI nie został wykonany w ramach tej dostawy; lokalne wyniki muszą mieć własne raporty, logi i identyfikację sprawdzonych źródeł.

Lokalna integracyjna weryfikacja końcowego poziomu zaliczyła pełne **113/113 EditMode po czystym imporcie oraz 67/67 PlayMode**, bez pominięć. [Raport poziomu](level-verification.md) zapisuje rzeczywiste wyniki, identyfikację sprawdzonych źródeł i odrębne granice odbioru. Sam wcześniejszy zielony zestaw, wynik 64/65 albo kod zakończenia procesu nadal nie potwierdzają pełnego zaliczenia bieżącego kompletu; niniejsza instrukcja nie zastępuje XML-ów i logów wykonania.

`Validate` sprawdza pliki, dokumentację i narzędzia w Pythonie. Zielony wynik tego workflow **nie potwierdza** importu, kompilacji, działania fizyki, kamery, walki lub grafiki. Syntetyczne XML-e i mock procesu w `tests/test_unity_validation.py` testują tylko zachowanie walidatora/runnera; nie są atrapą uruchomienia gry ani dowodem wykonania testów Unity.

## Zakres testów silnika

| Assembly / fixture | Przypadki | Sprawdzane elementy |
| --- | ---: | --- |
| EditMode: `ProjectBaselineTests` | 6 | Wersja edytora; scena bazowa w Build Settings; import obu istniejących skryptów z oryginalnymi GUID-ami; brak Missing Script; rozwiązywalne shadery materiałów. |
| EditMode: `LevelProgressionTests` | 26 | Wymagana i opcjonalna trasa, bramy, kolejność celów, terminalne wyjście, reset, izolacja sesji, niezmienne migawki i bezpieczeństwo obserwatorów. |
| EditMode: `PlayerMotorTests` | 36 | Ruch i obrót, skok, grawitacja, kolizje jako reguły rdzenia, niezależność od częstotliwości klatek, neutralizacja wejścia i pojedyncze naciśnięcia. |
| EditMode: `CameraMotionTests` | 32 | Tłumienie, ograniczenie dystansu przy przeszkodzie, niezależność czasowa, zakresy i odrzucanie niepoprawnych danych. Nie zastępuje fizyki kamery. |
| EditMode: `CombatRulesTests` | 13 | Zdrowie, śmierć/reset, token sesji ataku, przygotowanie/aktywne okno/odstęp, jeden trafiony cel na zamach i kierunkowy zasięg. |
| **EditMode razem: `Shadows.EditMode.Tests`** | **113** | Pełny zestaw powyższych przypadków. |
| PlayMode: `BaselineSceneTests` | 2 | Rzeczywista `SampleScene.unity`, brak Missing Script, aktywna kamera z AudioListener i światło kierunkowe. |
| PlayMode: `ProgressionControllerTests` | 7 | Rzeczywisty adapter MonoBehaviour: komendy, zdarzenia, wyłączenie/włączenie, reset, stare sesje, zniszczenie i wyładowanie sceny. |
| PlayMode: `PlayerMovementTests` | 18 | Rzeczywisty CharacterController i Input System: podłoże, ściany, schody, skok/sufit, wąskie przejście, utrata fokusu, pauza, wyłączenie, pojedyncze E i LMB. |
| PlayMode: `CameraFollowTests` | 22 | Rzeczywiste zapytania fizyczne kamery, przeszkody, bezpieczna pozycja/frustum, odzyskanie widoku, brak celu, teleport i cykl życia. |
| PlayMode: `CombatControllerTests` | 10 | Rzeczywista walka: okna ataku, compound colliders, ściany/kierunek, LMB jako dostarczona akcja, AI w granicach areny, śmierć, reset i stare tokeny; dwa testy zwykłego Update potwierdzają pościg i obrażenia przy 60 FPS oraz bez limitu klatek. |
| PlayMode: `LevelInteractionTests` | 4 | Runy i ponowienie po błędzie/reset, stan fizycznej bramy, pojedyncza widoczna interakcja E oraz przyjęte/odrzucone callbacki przejścia. |
| PlayMode: `ForsakenLevelTests` | 4 | Rzeczywista `ForsakenCastle.unity`: pełna trasa bez sekretu, opcjonalna sala i fizyczny skrót przez rampy, porażka/reset oraz próba skoku przez zamkniętą bramę. |
| **PlayMode razem: `Shadows.PlayMode.Tests`** | **67** | Pełny zestaw powyższych przypadków. |

Scena bazowa pozostaje sceną szablonową; końcowy poziom jest w `ForsakenCastle.unity`. Testy EditMode sprawdzają import istniejących skryptów przez `MonoScript`, bez przenoszenia ich do nowych assemblies. Rdzenie reguł nie zależą od Unity; osobne testy .NET kompilują te same źródła, ale nie uruchamiają MonoBehaviour, CharacterControllera, renderera ani cyklu życia sceny. Assembly testowe nie są dołączane do zwykłych buildów gry; kod PlayMode nie odwołuje się bezpośrednio do `UnityEditor`.

Fixture'y ruchu, walki, interakcji i całej sceny osadzają oficjalny `InputTestFixture` z przypiętego pakietu. Zapisuje on bieżący Input System, instaluje izolowany runtime i wirtualne urządzenia, a w `TearDown` odtwarza pierwotny stan. W batch mode domyślne `InputSystem.Update()` może oznaczać aktualizację edytora, a brak fokusu Game View może odłożyć zdarzenia klawiatury lub myszy do bufora Editor. Dlatego testy wykonują jawny update `Manual` przez refleksję do wewnętrznego overloadu z wersji 1.11.1, po neutralnym wejściu sprawdzają rzeczywiste dostarczenie do konsumenta i ustawiają fokus wyłącznie dla testowego komponentu. To izolacja testów; produkcyjne warunki fokusu, pauzy, kontroli i odrzucania aktualizacji Editor/BeforeRender pozostają aktywne.

Fixture pełnej sceny porusza rzeczywisty CharacterController przez fizyczne bramy i triggery, zabija przeciwników przez prawdziwe zamachy i wybiera mechanizmy przez zasięg oraz line of sight; nie przyznaje flag przez bezpośrednie `TryComplete`. Podstawowa trasa używa skonfigurowanej prędkości i rzeczywistych klatek, wymaga przeżycia zwykłego podatnego na obrażenia gracza oraz 120–180 sekund czasu gry. Jest to automatyczny pomiar trasy, odrębny od ręcznego playtestu. Szybsza regresja opcjonalnego odgałęzienia nie potwierdza czasu przejścia.

Dowody wizualne testów całej sceny wymagają działającego urządzenia graficznego; nie uruchamiać ich z `-nographics`. Przypięty URP obsługuje `RenderPipeline.SubmitRenderRequest` z `UniversalRenderPipeline.SingleCameraRequest`, renderowanie do `RenderTexture` i odczyt `ReadPixels`. Ten transport zapisuje PNG 1280 × 720, nie używa niedostępnego w batch mode `WaitForEndOfFrame` ani przedwczesnego `ScreenCapture.CaptureScreenshotAsTexture`. Jednolite obrazy są odrzucane. Zrzuty końcowej sceny pokazują kamerę świata wraz z jej rzeczywistym HUD-em Canvas; wcześniejsze zrzuty wersji OnGUI nie obejmowały interfejsu. JSON zapisuje metodę, trasę, czas, zdrowie i listę obrazów. Same zrzuty nie dowodzą ręcznej obsługi interfejsu ani wydajności pełnej trasy w playerze.

## Kontrole bez edytora

Wymagane: Git oraz Python 3.9 lub nowszy, bez dodatkowych bibliotek.

```sh
python3 tools/unity_validation.py project
python3 -m unittest discover -s tests -p 'test_*.py' -v
```

Walidator korzysta z `git ls-files`, więc wykrywa również `.meta` istniejące lokalnie, ale nieśledzone przez Git. Sprawdza brakujące metadane plików i folderów, zerowe/błędne/zdublowane GUID-y, osierocone metadane plików, kolizje wielkości liter, katalogi generowane i zgodność przypiętych wersji z lockfile. Nie naprawia ani nie regeneruje GUID-ów. Lokalne nieśledzone cache nie są uznawane za źródła.

## Rzeczywisty test lokalny

Zainstaluj i aktywuj **Unity 6000.0.24f1**, zamknij projekt w edytorze i uruchom oba tryby oddzielnie. Na Windows, z katalogu repozytorium:

```powershell
python tools/unity_validation.py run --editor "C:\Program Files\Unity\Hub\Editor\6000.0.24f1\Editor\Unity.exe" --mode editmode
python tools/unity_validation.py run --editor "C:\Program Files\Unity\Hub\Editor\6000.0.24f1\Editor\Unity.exe" --mode playmode
```

Na Linux/macOS podaj rzeczywistą ścieżkę do binarnego pliku Unity przez `--editor`. Runner używa już aktywowanego edytora: nie przyjmuje haseł ani nie wykonuje aktywacji. W środowisku Linux bez ekranu potrzebny jest działający serwer wyświetlania, np. Xvfb; same kontrole źródeł tego nie wymagają.

Domyślny limit wynosi 900 sekund (`--timeout`); `--output` pozwala zmienić katalog wyników. Stary XML danego trybu jest usuwany przed uruchomieniem, a brak nowego raportu, błąd procesu, timeout, pominięty test i niepoprawne liczniki kończą polecenie kodem niezerowym. Log edytora i XML trafiają do `artifacts/unity-local`. Runner nie dodaje `-quit` do `-runTests`.

Czysty import należy sprawdzić na **osobnym świeżym checkoutcie**, bez otwierania go wcześniej w Hubie; nie trzeba usuwać `Library` z używanej kopii roboczej. Sam runner lokalny nie kasuje cache i nie deklaruje automatycznie, że uruchomienie było czystym importem.

## Aktywacja GitHub Actions

Postępuj według [instrukcji aktywacji GameCI](https://game.ci/docs/github/activation/) odpowiedniej dla posiadanej licencji. W repozytorium otwórz **Settings → Secrets and variables → Actions**. Skonfiguruj `UNITY_EMAIL`, `UNITY_PASSWORD` oraz **jeden** z sekretów: `UNITY_LICENSE` dla Personal albo `UNITY_SERIAL` dla Pro. Nie wklejaj wartości do README, issue, komentarza PR, pliku repozytorium ani rozmowy.

Po konfiguracji uruchom workflow `Unity tests` przez **Actions → Unity tests → Run workflow**, wskazując aktualne `main`. Można też ponowić nieudany run. Obecność sekretów jest tylko warunkiem wstępnym; poprawność aktywacji potwierdzi dopiero rzeczywisty proces Unity.

Workflow działa na push do `main`, pull requestach i ręcznie. Nie używa `pull_request_target`; fork PR i Dependabot nie uruchamiają zadań z sekretami. Takie pominięcie nie jest dowodem zaliczenia testów. Dla zmian z zewnętrznego źródła testy z aktywacją uruchamiać dopiero po przeglądzie na zaufanej gałęzi.

Testy obu trybów są wykonywane kolejno, każdy na świeżym runnerze bez cache `Library`. GameCI Test Runner jest przypięty do commita `0ff419b913a3630032cbe0de48a0099b5a9f0ed9` (v4.3.1), z Unity 6000.0.24f1. Raporty `editmode-results.xml` / `playmode-results.xml` i logi edytora są publikowane jako osobne artefakty z identyfikatorem commita. Nie jest wymagany token z prawem zapisu checks; poświadczenia checkoutu nie są utrwalane.

## Zasady odbioru

`tools/unity_validation.py results --mode editmode <raport.xml>` i odpowiednik `--mode playmode` wymagają rzeczywistego raportu NUnit z wynikiem `Passed`, zgodnymi licznikami, zerową liczbą failed/skipped/inconclusive i **całym oczekiwanym zestawem danego trybu**. `EXPECTED_TESTS` zawiera jawne nazwy `Fixture.Metoda` oraz liczby wariantów: obecnie 113 EditMode i 67 PlayMode. Walidator nie wylicza wymagań z właśnie ocenianego raportu, więc zielony stary zestaw bazowy nie może zastąpić pełnej integracji końcowego poziomu. Sam kod wyjścia zero, pusty raport, brak wyników, częściowy zestaw lub wyłącznie testy innego trybu nie wystarczają. Przy uzasadnionej zmianie źródłowych nazw/liczb utrzymać spójność `EXPECTED_TESTS`, testów walidatora i dokumentacji; nie zmniejszać wymagań, aby zaakceptować niepełny wynik.

Do zamknięcia #5 należy dołączyć: udany run obu trybów, XML-e, logi importu/kompilacji i potwierdzenie uruchomienia sceny. Przy błędzie aktywacji sprawdzić konfigurację; przy błędzie kompilacji lub asercji poprawić kod. Nie usuwać testów, nie ustawiać `continue-on-error` i nie zmieniać warunku wstępnego na pozorny sukces tylko po to, aby uzyskać zielony status.

Wykonanie pełnych fixture'ów sceny nie zastępuje osobnego builda Windows x64 ani jego uruchomienia. Test startu natywnego playera i FPS na dziedzińcu nie jest benchmarkiem całego poziomu; automatyczny czas przejścia w edytorze nie jest ręcznym playtestem. Aktualny raport musi osobno wskazywać: dokumentację/kontrakt, testy .NET, EditMode, PlayMode, kompilację playera, jego rzeczywisty start, automatyczny pomiar trasy, przegląd obrazów oraz niewykonane kontrole człowieka i pełnej wydajności playera. Nie oznaczać tych ostatnich jako zaliczone tylko na podstawie testów automatycznych.

## Źródła techniczne

- [Unity Test Framework 1.4: uruchamianie z linii poleceń](https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/reference-command-line.html).
- [Unity Test Framework: assemblies testowe](https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/workflow-create-test-assembly.html).
- [GameCI Test Runner](https://game.ci/docs/github/test-runner/).
- [Format raportu NUnit](https://docs.nunit.org/articles/nunit/technical-notes/usage/Test-Result-XML-Format.html).
