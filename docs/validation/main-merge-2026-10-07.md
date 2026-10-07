# Integracja gałęzi runtime z main — 2026-10-07

Punkt startowy main: `8bf556de69a0bcd1f30de215f5c35b9ca8a5b1c1`; gałąź `codex/complete-castle-level`: `6183cde2c543723d0b0894dbe1226072d189ebe7`. Main zawierał 17 niezależnych commitów, a gałąź 5. Rozwiązanie zachowuje obie historie i zawartość obu poziomów.

Main zachowuje podstawową autorską scenę `ForsakenCastle` i jej GUID. Dawna scena runtime jest zapisana jako `ForsakenRuntimeCastle` z własnym dotychczasowym GUID-em, osobnym scene-owned kontrolerem i builderem. Każdy z dwóch jawnych builderów wybiera jedną właściwą scenę; ogólne Build Settings zawierają kolejno autorską scenę, alternatywną scenę runtime i SampleScene. Nie dodano globalnego stanu postępu. Wspólny rdzeń i oba kontrakty sesji pozostają testowane.

Przestrzenie nazw `Combat.CastleRules` i `LevelCombat.PlayerCombat` rozdzielają równoległe implementacje bez zmiany nazw plików i GUID-ów komponentów. Zachowano aktualne API main, prezentację, kamerę, nawigację, dowody i narzędzia walidacji. Migracja mapuje dotychczasowe komponenty runtime i ich odwołania testowe na nowe pełne nazwy. Połączone assemblies i projekty .NET kompilują oba zestawy. Unity 6000.6.3 wymaga `EntityId`; runtime przechowuje pełne klucze w `MeleeAttack<EntityId>`. Regresja kolizji hashy sprawdza, że dwa odrębne cele mogą otrzymać po jednym trafieniu.

Wersje po integracji są wersjami main: Unity `6000.6.3f1`, Input System `1.20.0`, URP `17.6.0`, Test Framework `1.8.0`. To integracja z już wykonaną migracją main, nie cofnięcie do wersji użytej w starszej paczce gałęzi.

## Bieżąca weryfikacja

Sprawdzone wejścia silnika: drzewo Git `63615d12d3cd860629b9affd54c014d25d49d130`, poddrzewo Assets `db5273f5ecb335ecf375aff3364bedfdab8a4410`. Końcowe zmiany raportów nie zmieniają tych wejść kodu/scen. Oryginalny DOCX ma nadal SHA-256 `9baa88bd8a9c26fc6adcbcdc9032266096716a7475969c5a6d793356cf15ed31`.

| Kontrola | Bieżący wynik | Dowód lokalny |
| --- | --- | --- |
| Kontrakt DOCX/modelu | 29 stanów bez sekretu / 63 z sekretem, bez zmiany DOCX | `artifacts/main-merge-20261007/source-audit.json` |
| Python | 147/147, bez pominięć | `python3 -m unittest discover -s tests -p 'test_*.py' -q` przez WSL |
| Integralność źródeł Unity | 325 assetów / 854 śledzone pliki / 369 unikalnych GUID-ów | `python tools/unity_validation.py project` |
| Rdzenie .NET | 186/186, bez pominięć | `artifacts/main-merge-20261007/headless-input-reuse.json` |
| Unity 6000.6.3f1 EditMode | 187/187, bez pominięć, kod procesu 0 | `artifacts/main-merge-20261007/editmode-entity-id/results.xml` |
| Unity 6000.6.3f1 PlayMode | Pierwsza pełna próba 227/228; powtórka bez zmiany źródeł trwa | `artifacts/main-merge-20261007/playmode-merged-1/` i `playmode-merged-3/` |

.NET: Progression 29, Movement 36, Camera 32, Combat 60, Interactions 17, Performance 7, Puzzles 5. Combat wykonano ponownie po zmianie tożsamości celów; pozostałe sześć zestawów zachowuje identyczne 26 wejść źródeł/testów/projektów/danych względem pierwszego uruchomienia integracji. Raport reużycia zapisuje bloby wejściowe, ścieżki, SHA-256 TRX i rzeczywiste liczniki wyników.

Wyniki silnika po poprawkach pochodzą z zachowanego cache projektu weryfikacyjnego. Dwa wcześniejsze zimne importy zakończyły się błędami kompilacji: kolizją aliasu klasy i odrzuconym `GetInstanceID`. Zachowano ich logi w `editmode/` i `retry-1/editmode/`; poprawiono przyczyny przed zaliczonym EditMode. Nie są to zaliczone zimne importy końcowych źródeł. Przed PlayMode zachowano i przejrzano diff zapisany przez edytor: metadane wersji materiałów URP, drobne różnice serializacji kolorów i whitespace. Odtworzono tylko te pliki w izolowanym projekcie testowym; główny checkout i autorska scena nie zostały zmienione przez edytor. Patch i przegląd są w `editmode-entity-id/editor-written-source*.json` oraz `.patch`.

Dokładne, kwalifikowane nazwy klas i wszystkie argumenty NUnit są sprawdzane walidatorem; sam licznik testów nie wystarcza. Historyczne 113/74 gałęzi i 173/202 main pozostają historyczne.

Ręczne przejścia człowieka, odbiór oprawy przez właściciela i benchmark całej trasy playera pozostają odrębnymi niewykonanymi kontrolami. Istniejąca awaria zdalnego workflow Unity przy aktywacji nie jest wynikiem testów silnika; dotychczasowe main miało zielone kontrole źródeł/C# i czerwony warunek aktywacji bez uruchomienia testów Unity.

Pierwszy pełny PlayMode wykonał wszystkie 228 przypadków bez pominięć; 227 zaliczono. `CastleLockedMainGatesRejectWalkingAndJumping` otrzymał nieoczekiwany log błędu FMOD przy zmianie domyślnego urządzenia audio Windows. Nie był to błąd asercji rozgrywki. Zachowano XML, log, wykonanie, wygenerowane obrazy oraz diff serializacji w `playmode-merged-1/`. Powtórka całego zestawu używa identycznego drzewa źródeł, asercji, limitów testów i aktywnego audio; niczego nie wyciszono ani nie pominięto.
