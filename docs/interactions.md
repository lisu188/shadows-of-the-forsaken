# Interakcje i fizyczne bramy — issue #9

Wymagania: DOCX §3–4 (runy, dźwignie, bramy i ukryte drzwi), warunki przejść z §6–8 oraz `docs/level-contract.json`. Te wspólne komponenty obsługują teraz konkretne mechanizmy [pełnej trasy zamku](full-castle-route.md): runiczną dźwignię, księgę biblioteki, dźwignię sekretu i relikt. Reguły pozostają w istniejącym kontrolerze postępu; same komponenty nie dowodzą ukończenia ani czasu przejścia.

## Podłączenie

`PlayerInteractor` należy umieścić na tym samym obiekcie co istniejący `PlayerMovement`. Pole `progression` wskazuje jedną scenową instancję `LevelProgressionController`, wspólną dla mechanizmów i bram. Interaktor subskrybuje wyłącznie `PlayerMovement.InteractRequested`; nie odczytuje ponownie klawiatury i nie dodaje mapy wejścia. Zablokowanie sterowania przez `SetControlsEnabled(false)` wyłącza również używanie mechanizmów.

`InteractionTarget` przypina się do dźwigni/runy. Należy przypisać tę samą instancję postępu oraz `focusCollider` należący do hierarchii tego mechanizmu. Bez jawnego collidera komponent wybiera pierwszy collider z własnej hierarchii podczas włączenia. Opcjonalny `focusPoint` określa miejsce patrzenia; domyślnie służy do tego środek collidera. Collider musi być aktywny. Kilka colliderów jednego mechanizmu nadal tworzy jeden cel.

Decyzje implementacyjne, nie liczby z DOCX: zasięg 2,5 m od punktu jedną jednostkę nad stopami, półkąt 60° względem kierunku postaci. Prawidłowy konfigurowalny zasięg mieści się w `(0, 100]`. Wygrywa najbliższy widoczny cel, potem lepsze skierowanie, mniejszy `selectionOrder`, na końcu kolejność pełnego `EntityId` Unity. Identyfikatory są porównywane przez `EntityId.CompareTo`; rdzeń dostaje unikalną pozycję na tak posortowanej liście, bez skracania identyfikatora lub używania skrótu jako tożsamości. Ostatni remis jest stabilny w obrębie uruchomienia, nie między uruchomieniami; nadaj różne `selectionOrder`, gdy dokładny remis ma mieć autorsko ustalone rozstrzygnięcie. Niedostępny najbliższy cel pozostaje wybrany: E nie uruchamia wtedy innego mechanizmu w tle.

`SelectedTarget`, `CanInteract`, `Prompt` oraz `SelectionChanged` są interfejsem dla UI. `RefreshTarget()` odświeża je także bez czekania na Update. `TryInteract()` ponownie sprawdza zasięg, kierunek, dostępność i przeszkody w chwili akcji, więc stary prompt nie pozwala użyć przesuniętego celu. Zapytania korzystają ze sceny fizyki gracza, synchronizują zmienione Transformy i ignorują jego własne collidery. Bryła zawierająca początek promienia również blokuje użycie. `obstructionMask` musi obejmować ściany i drzwi; triggery nie są przeszkodami. Bufory rosną do 512 wyników; nasycenie limitu bezpiecznie odrzuca wybór.

## Jednorazowe użycie i sesje

`InteractionTarget.available` pozwala mechanizmowi sterować dostępnością bez narzucania jej rozwiązania. `objective = None` emituje tylko jednorazowe zdarzenie `Activated(Guid sessionId)`. Inna pojedyncza wartość `LevelObjective` dodatkowo wymaga `CanComplete` i wywołuje `TryComplete` istniejącego kontrolera. Cel musi więc znajdować się w poprawnym logicznym pokoju; komponent nie teleportuje gracza ani nie zmienia pokoju. Brak kontrolera lub niepoprawna flaga nie pozwala aktywować mechanizmu.

Token sesji jest przechwytywany na początku akcji. `TryActivate(Guid)` jest zaufanym API komponentów, a nie alternatywnym wejściem gracza: samo nie sprawdza odległości. UI/sterowanie powinny używać `PlayerInteractor.TryInteract()`. Opóźniony odbiorca zachowuje pierwotny token i przekazuje go dalej; nie pobiera świeżego tokenu w starym callbacku.

Zdarzenie jest emitowane po zaakceptowanym ukończeniu celu, najwyżej raz na sesję. Powtórzenia E, przytrzymanie, ponowne włączenie oraz ponowne wywołanie przez odbiorcę nie dublują użycia. Wyjątek odbiorcy jest logowany i nie cofa zaakceptowanego użycia. Wyłączenie lub zniszczenie obiektu odpina jego subskrypcje. Reset przy aktywnym komponencie czyści użycie; po resecie wykonanym podczas wyłączenia komponent uzgadnia nową sesję w `OnEnable`. Nie następują mutacje postępu z listenerów `Changed`.

## Bramy i bezpieczne zamykanie

`ProgressionGate` ma `BoxCollider` na własnym obiekcie, parę istniejących pokoi `from`/`to` oraz renderery ruchomego/znikającego panelu w `closedVisuals`. Nie umieszczać tam stałego obramowania ani ścian. Pusta lista jest wypełniana rendererami hierarchii bramy. Komponent odczytuje `IsPassageOpen` istniejącego kontrolera; nie ma drugiego modelu postępu. Otwarcie jednocześnie wyłącza collider i renderery panelu. Zamknięcie jednocześnie przywraca oba. Obiekt komponentu pozostaje aktywny. To natychmiastowe przełączenie, bez niezamówionego systemu animacji.

Przed zamknięciem sprawdzana jest bryła drzwi powiększona o 0,05 m. Aktywny collider `CharacterController`, aktora z `CombatHealth` lub `NavMeshAgent`, albo dynamicznego Rigidbody w otworze utrzymuje już otwartą bramę jako `IsOpen = true`, `ClosePending = true`; panel pozostaje niewidoczny, a przejście fizycznie otwarte. Po wyjściu aktora brama zamyka się w Update. Podejście do już zamkniętej bramy nie odblokowuje jej. Nasycona lista zajętości traktowana jest jako zajęte przejście. Maska zajętości musi zawierać warstwy aktorów. Nie używać ścinających transformacji z obróconymi rodzicami o niejednorodnej skali.

Opcjonalny `NavMeshObstacle` na bramie dostaje wymiary collidera i carving. Jest aktywny tylko przy faktycznym zamknięciu; `ClosePending` pozostawia również nawigację otwartą. Ruch `EnemyEncounter` dodatkowo sprawdza fizyczne przeszkody, więc opóźnienie carvingu nie pozwala przejść przez drzwi.

Reset modelu nie jest respawnem gracza. `LevelSessionController` koordynuje pełne odtworzenie świata i bezpieczny punkt odrodzenia. Brama zapobiega zamknięciu na aktywnym aktorze; nie teleportuje go i nie wybiera za scenę punktu startowego. Koordynator restartu blokuje sterowanie i obrażenia, ustawia gracza poza bramami i dopiero odtwarza świat. Komponent zachowuje fizyczny stan przy wyłączeniu i ponownie uzgadnia postęp po włączeniu.

## Mechanizmy właściwego poziomu

Dźwignia runiczna zalicza `MainPuzzleSolved` dopiero po pierwszym demonie, a księga w bibliotece `LibraryOpened` po strażniku. Uszkodzona dźwignia używa `objective = None` i pokazuje wskazówkę; nie blokuje poprawnego mechanizmu. Nie ma ukrytej sekwencji ani utraty rozwiązania po nieudanej próbie.

Dźwignia katakumb zalicza `SecretLeverPulled`; otwiera tylko opcjonalną odnogę bonusową i dolny skrót. Odkrycie reliktu zalicza `BonusDiscovered`, ukrywa jego model i pokazuje krótki tekst. `MechanismFeedback` obsługuje komunikat, prosty obrót uchwytu/księgi oraz przywrócenie prezentacji po restarcie. Nie przyznaje sam flag ani przedmiotów.

## Oddzielna scena demonstracyjna

Menu `Shadows/Mechanics/Create Interaction Demo` albo jawna metoda batch `InteractionDemoBuilder.BuildForBatch` zapisują `Assets/Interactions/Demo/InteractionDemo.unity` i cztery niewielkie materiały. Narzędzie nie jest uruchamiane podczas importu, nie zmienia SampleScene ani konfiguracji budowania. Ponowne wywołanie nadpisuje wyłącznie tę scenę demonstracyjną i jej materiały.

Demo jest oznaczoną sceną testową: ustawia logiczny pokój zagadki oraz ukończony warunek pierwszego przeciwnika. Nie jest to zaliczenie walki w grze. Złota dźwignia emituje `MainPuzzleSolved` i otwiera niebieską bramę; czerwony mechanizm stoi za przeszkodą. UI pokazuje aktualny cel i dostępność. W/S porusza, A/D obraca, E używa. Przycisk `Restart demo` przenosi postać na bezpieczny start, resetuje sesję i ponownie przygotowuje jawny fixture. Warunki prawdziwego poziomu nie są zmieniane.

Ręczny odbiór: użycie złotej dźwigni, wielokrotne E, fizyczne przejście przez otwartą bramę, brak użycia czerwonego mechanizmu przez ścianę, czytelność komunikatów oraz ponowne użycie po restarcie. Zamknięcie z aktorem w otworze jest osobnym testem PlayMode, ponieważ pełny restart demo celowo przenosi aktora poza bramę.

## Testy i granice dowodów

`Shadows.Interactions.Core` nie zależy od Unity. `tests/Interactions/Interactions.Tests.csproj` kompiluje produkcyjny `InteractionRules.cs` i wspólny zestaw `InteractionRulesTests`: wybór, granice, błędne dane, stabilne remisy, jednorazowość, stare tokeny i oczekiwanie na opuszczenie bramy. Nie testuje raycastów ani colliderów.

```sh
dotnet test tests/Interactions/Interactions.Tests.csproj --configuration Release
python3 tools/validate_level_contract.py
python3 tools/unity_validation.py project
python3 -m unittest discover -s tests -p 'test_*.py' -v
```

`InteractionComponentTests` używa prawdziwego Input System, CharacterController i fizyki Unity. Obejmuje zasięg, kierunek, ścianę, promień zaczynający się wewnątrz ściany, zmianę geometrii przed E, złożone collidery, rywalizujące cele, held/repeated input, ponowne włączenie, zniszczenie, reset przy aktywnym i wyłączonym celu, prawdziwy cel postępu oraz fizycznie zamkniętą/otwartą bramę. Test teleportu do otworu i resetu w tej samej klatce nie wykonuje synchronizacji po stronie wywołującego.

Dodanie testów nie jest potwierdzeniem ich wykonania. Wyniki .NET, Unity EditMode, Unity PlayMode i ręczny odbiór należy raportować osobno. Demo ani testy tych komponentów nie dowodzą grywalności zamku, rozwiązania właściwej zagadki czy osiągnięcia czasu 2–3 minut.

[Historyczny raport wspólnych mechanik](validation/shared-gameplay-2026-09-30.md) zawiera wyniki Windows Unity 6000.6.3f1 i obrazy wersji sprzed integracji zamku. Nowe testy sesji, zajętości przez przeciwnika oraz pełnej trasy rozszerzają ten zakres; stan wykonania aktualnej integracji podaje [bieżący raport](validation/full-castle-route-2026-09-30.md).
