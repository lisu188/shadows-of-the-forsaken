# Runtime postępu poziomu — issue #10

Źródła wymagań: DOCX §3 i §6–8, [decyzje #4](design-decisions.md), [kontrakt JSON](level-contract.json) oraz [issue #10](https://github.com/lisu188/shadows-of-the-forsaken/issues/10). Ta implementacja nie zmienia przebiegu ani oryginalnego dokumentu.

## Dostarczony kod

`Assets/Progression/Core/LevelProgression.cs` zawiera rzeczywiste reguły C# do wykorzystania przez grę. Assembly `Shadows.Progression.Core` nie zależy od Unity. `Assets/Progression/LevelProgressionController.cs` jest cienkim komponentem MonoBehaviour w osobnym assembly, udostępniającym stan i zdarzenia bramom, starciom oraz UI. Istniejące skrypty i GUID-y nie są przenoszone.

Stan to bieżący logiczny obszar, maska ukończonych celów i identyfikator sesji. Obowiązkowe cele: pierwszy przeciwnik, główna zagadka, miniboss, mechanizm biblioteki oraz finałowy przeciwnik. Dźwignia sekretu i odkrycie bonusu są opcjonalne. Biblioteka nie jest opcjonalnym sekretem.

`CanEnter` sprawdza dostępność sąsiedniego obszaru. `TryEnter` aktualizuje logiczne położenie tylko przez dozwolone połączenie. Nie teleportuje Transform i nie otwiera collidera. Powrót z zagadki prowadzi przez pierwszy obszar, nie przez ścianę do tronu. Wyjście jest terminalne i wymaga wszystkich obowiązkowych celów.

`CanComplete` / `TryComplete` sprawdzają właściwy obszar i wymagania konkretnego celu. Druga informacja o tym samym zabójstwie czy mechanizmie nie zmienia stanu ani nie emituje kolejnego zdarzenia. Parametr musi wskazywać jeden cel, nie kombinację flag. Nieznane enumy powodują `ArgumentOutOfRangeException`; poprawne, ale niedostępne przejścia/cele zwracają `false`.

`IsPassageOpen(from, to)` służy bramom: po spełnieniu wymagań stan bramy pozostaje otwarty do resetu, niezależnie od bieżącego pokoju gracza. Brak połączenia nie jest otwartym przejściem. Stan otwarcia nie jest symulacją kolizji.

## Sesja, zdarzenia i reset

Każda komenda zmieniająca pokój lub cel wymaga `SessionId` pobranego przy rozpoczęciu danej akcji. Nowa instancja i każdy reset tworzą inny identyfikator. Opóźniony callback przeciwnika z poprzedniej sesji zostanie odrzucony nawet po powrocie do tego samego pomieszczenia. To token lokalnej sesji, nie zabezpieczenie sieciowe. Reguły przejść są deterministyczne; losowy identyfikator służy wyłącznie izolacji sesji.

`Changed` przekazuje niezmienne migawki `Before` / `After`, rodzaj zmiany i cel. Stan jest już zatwierdzony w chwili powiadomienia. Bramy/UI mogą odczytać pełną migawkę po subskrypcji, a następnie reagować na różnicę. Callbacki obserwatorów nie mogą synchronicznie zmieniać postępu: reentrancja jest odrzucana, aby nie odwracać kolejności zdarzeń. Kolejną akcję należy wykonać po zakończeniu powiadomienia.

W rdzeniu błąd obserwatora nie blokuje innych obserwatorów: po ich powiadomieniu zgłaszany jest `AggregateException`, a zatwierdzona zmiana nie jest cofana. Adapter Unity loguje wyjątki obserwatorów przez `Debug.LogException` i powiadamia pozostałych.

`Reset` rdzenia / `TryResetSession` komponentu czyści wszystkie cele, wraca logicznie do dziedzińca, zamyka bramy w modelu i emituje `SessionReset`. Nie niszczy subskrypcji aktualnych odbiorców, ponieważ potrzebują sygnału do odtworzenia świata. Fizyczny respawn, odtworzenie przeciwników i UI należą do #18 i zadań konkretnych scen.

Komponent jest związany ze sceną, bez singletona, statycznego stanu czy `DontDestroyOnLoad`. `OnDisable` odłącza przekazywanie zdarzeń i blokuje komendy; `OnEnable` przywraca dokładnie jedną subskrypcję bez resetowania postępu. `OnDestroy` usuwa odbiorców i odłącza rdzeń. Odbiorcy z własnym cyklem życia powinni także odpinać swoje subskrypcje w `OnDisable`. API służy głównemu wątkowi Unity, nie obsłudze współbieżnych komend.

## Podłączenie w kolejnych zadaniach

Dodać jeden `LevelProgressionController` do obiektu sesji w scenie poziomu. Komponenty gracza, obszarów, bram, przeciwników i UI powinny wskazywać tę samą instancję, zamiast tworzyć własny model. Trigger obszaru wywołuje `TryEnter`; kontrola bramy używa `IsPassageOpen`; źródło ukończenia celu przechowuje token sesji i wywołuje `TryComplete` z przypisanym celem. Nie pobierać nowego tokenu dopiero w starym opóźnionym callbacku.

Rdzeń zakłada wiarygodne sygnały od komponentów rozgrywki. Nie potwierdza sam, że przeciwnik naprawdę zginął ani że rozwiązano zagadkę; nie wolno wystawiać `TryComplete` jako dowolnego polecenia gracza. Sygnał jest odrzucany poza właściwym logicznym pokojem — starcia i mechanizmy muszą kończyć się w swoim obszarze lub dostarczyć zdarzenie po powrocie gracza. Nie zaliczać celu przez zmianę Inspectora.

`SampleScene` pozostaje sceną bazową. Scena gry `ForsakenCastle` korzysta z tego runtime: `ForsakenLevel` tworzy jeden kontroler, wiąże fizyczne przejścia i bramy, przeciwników, mechanizmy oraz HUD. [Opis implementacji poziomu](level-implementation.md) określa mapowanie do DOCX. Testy samego rdzenia nadal nie stanowią dowodu działania tej integracji.

## Weryfikacja

```sh
dotnet test tests/Progression/Progression.Tests.csproj --configuration Release
python3 -m unittest discover -s tests -p 'test_*.py' -v
python3 tools/unity_validation.py project
python3 tools/validate_level_contract.py
```

Projekt .NET kompiluje dokładnie produkcyjny plik C# jako .NET Standard 2.1 i uruchamia ten sam plik testów NUnit, który należy do EditMode w Unity. Osobny test .NET porównuje wszystkie komendy w 29 osiągalnych stanach bez sekretu i 63 z sekretem z niezależnie odczytanym JSON-em. Dane runtime nie są ładowane z `docs/` w playerze; test parytetu wykrywa rozjazd ręcznie zapisanych reguł z kontraktem.

Testy PlayMode `ProgressionControllerTests` obejmują adapter, wyłączenie/włączenie, odrzucanie starych sesji, zniszczenie obiektu i wyładowanie sceny. Muszą zostać wykonane w rzeczywistym Unity zgodnie z [instrukcją](unity-testing.md). CI `.NET` **nie kompiluje adaptera MonoBehaviour i nie zastępuje tych testów**. Odbiór integracyjny #10 zależy od odblokowania #5 oraz rzeczywistych wyników Unity. Nie deklarujemy czasu przejścia ani poprawności fizycznego poziomu.

Dokumentacja API: [referencje assembly w Unity 6](https://docs.unity3d.com/6000.0/Documentation/Manual/assembly-definitions-referencing.html), [OnDisable](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnDisable.html), [NUnit i dotnet test](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-csharp-with-nunit). Nie aktualizowano pakietów Unity; zależności NuGet dotyczą wyłącznie zewnętrznego runnera testów.
