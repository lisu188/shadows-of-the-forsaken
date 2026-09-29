# Testy Unity i integralność źródeł — issue #5

## Stan i granice weryfikacji

Dodano kod **6 przypadków EditMode i 2 PlayMode**, osobne assembly definitions, walidator źródeł i raportów NUnit, lokalny runner oraz workflow `Unity tests`. Istniejące skrypty, sceny, wersje pakietów i GUID-y nie zostały zmienione.

Pierwsza próba CI w [PR #26](https://github.com/lisu188/shadows-of-the-forsaken/pull/26) wykazała **brak konfiguracji aktywacji Unity**. Etap `Unity activation prerequisite (not tests)` zakończył się błędem, a testy edytora nie wystartowały. Nie jest to wynik testów C# ani dowód błędnej kompilacji. Issue #5 pozostaje otwarte do uzyskania rzeczywistych raportów obu trybów i logu czystego importu.

`Validate` sprawdza pliki, dokumentację i narzędzia w Pythonie. Zielony wynik tego workflow **nie potwierdza** importu, kompilacji, działania fizyki, kamery, walki lub grafiki. Syntetyczne XML-e i mock procesu w `tests/test_unity_validation.py` testują tylko zachowanie walidatora/runnera; nie są atrapą uruchomienia gry ani dowodem wykonania testów Unity.

## Zakres testów silnika

| Assembly | Przypadki | Sprawdzane elementy |
| --- | ---: | --- |
| `Shadows.EditMode.Tests` | 6 | Wersja edytora; obecność sceny w Build Settings; import obu skryptów z oryginalnymi GUID-ami; brak Missing Script w scenie; rozwiązywalne shadery materiałów. |
| `Shadows.PlayMode.Tests` | 2 | Załadowanie rzeczywistego `SampleScene.unity`, kilka klatek bez nieoczekiwanych błędów, aktywna główna kamera z AudioListener oraz światło kierunkowe. |

Scena bazowa pozostaje sceną szablonową. Testy nie twierdzą, że zawiera gracza albo gotowy poziom zamku. Testy EditMode sprawdzają import istniejących skryptów przez `MonoScript`, bez przenoszenia ich do nowych assemblies. Assembly testowe nie są dołączane do zwykłych buildów gry; PlayMode nie odwołuje się do `UnityEditor`.

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

`tools/unity_validation.py results --mode editmode <raport.xml>` wymaga rzeczywistego raportu NUnit z wynikiem `Passed`, poprawnymi licznikami i wszystkimi oczekiwanymi przypadkami bazowymi. Sam kod wyjścia zero, pusty raport, brak wyników, częściowy zestaw testów lub wyłącznie testy innego trybu nie wystarczają. Zmieniając nazwy/liczbę testów bazowych, utrzymać spójność `EXPECTED_TESTS`, testów walidatora i tej dokumentacji.

Do zamknięcia #5 należy dołączyć: udany run obu trybów, XML-e, logi importu/kompilacji i potwierdzenie uruchomienia sceny. Przy błędzie aktywacji sprawdzić konfigurację; przy błędzie kompilacji lub asercji poprawić kod. Nie usuwać testów, nie ustawiać `continue-on-error` i nie zmieniać warunku wstępnego na pozorny sukces tylko po to, aby uzyskać zielony status.

Automatyczny build docelowego Windows playera i test całego poziomu pozostają odpowiednio zadaniami #24 i #22.

## Źródła techniczne

- [Unity Test Framework 1.4: uruchamianie z linii poleceń](https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/reference-command-line.html).
- [Unity Test Framework: assemblies testowe](https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/workflow-create-test-assembly.html).
- [GameCI Test Runner](https://game.ci/docs/github/test-runner/).
- [Format raportu NUnit](https://docs.nunit.org/articles/nunit/technical-notes/usage/Test-Result-XML-Format.html).
