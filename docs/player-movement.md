# Kontroler gracza — issue #6

Implementacja rozwija `Assets/PlayerMovement.cs`, zachowując nazwę, ścieżkę, GUID `963b3f782c6d71942ad1e73d117b2820` oraz pola `speed`, `rotationSpeed`, `gravity`, `jumpForce`. Domyślne wartości pozostają 5, 20, 9.81 i 5. W ramach #6 nie zmieniono sceny, kamery, pakietów ani DOCX.

## Sterowanie i integracja

Zgodnie z `design-decisions.md`: W/S porusza przód/tył względem postaci; A/D obraca bez strafowania; Spacja skacze. `PlayerMovement` wymaga `CharacterController` i automatycznie dodaje go podczas dodawania skryptu. Fizyczne wymiary, `stepOffset`, `skinWidth` i `slopeLimit` pozostają konfiguracją konkretnego controllera/prefabu, a nie uniwersalnymi wartościami narzuconymi przez skrypt.

Pole `inputActions` jest opcjonalną referencją do istniejącego assetu `Assets/InputSystem_Actions.inputactions`. Bez niej kontroler korzysta z projektowego `InputSystem.actions`. Mapa `Player` musi zawierać `Move` (Value/Vector2), `Jump`, `Attack` i `Interact` (Button); scena zamku używa również opcjonalnej akcji `Restart` (Button/R). Brak lub błędny typ zgłasza jeden błąd konfiguracji i wyłącza komponent zamiast generować wyjątek co klatkę.

Kontroler ma prywatną kopię mapy, włącza cztery podstawowe akcje oraz `Restart`, jeśli jest obecna, i zwalnia ją po wyłączeniu. Nie włącza/wyłącza źródłowego assetu ani UI. Kopia zachowuje ograniczenia urządzeń/maski źródła. Kompozyty Dpad/2DVector są lokalnie ustawiane na Digital (mode=1), aby W+D nie zmniejszało prędkości ruchu lub obrotu przez normalizację przekątnej. Klonowanie w runtime nie modyfikuje źródłowych bindingów ani ich GUID-ów; późniejsza integracja sceny dodała do assetu akcję `Restart`.

Wejście jest próbkowane po aktualizacji Input System, a komendy konsumowane raz podczas aktualizacji ruchu. Nie ma odczytów starego `UnityEngine.Input`. Krótkie naciśnięcie i zwolnienie pomiędzy klatkami nie musi być utrzymywane do `Update`. Kilka naciśnięć tej samej akcji przed jedną klatką gry jest celowo scalane do jednego żądania.

`AttackRequested` i `InteractRequested` to sygnały C# bez parametrów, emitowane przy naciśnięciu. LPM oraz E są zdefiniowane w istniejącym assetcie. Interakcja jest natychmiastowym żądaniem naciśnięcia również przy szablonowym Hold, ponieważ odczyt dotyczy progu przycisku, nie ukończenia interakcji Hold. [PlayerCombat (#11)](combat.md) i [PlayerInteractor (#9)](interactions.md) subskrybują te sygnały we własnym OnEnable/OnDisable, bez ponownego odczytu klawiatury. Ich zapisane sceny demo i pełna trasa korzystają z tego samego kontrolera. `RestartRequested` jest trzecim sygnałem, używanym przez `LevelSessionController` do restartu po porażce lub ukończeniu. R wymaga nowego naciśnięcia po zwolnieniu; przytrzymany przycisk nie restartuje sam kolejnej sesji.

`SetControlsEnabled(false)` zatrzymuje ruch, skok oraz sygnały ataku i interakcji. Niezależne `SetSessionControlsEnabled(false)` blokuje te same akcje podczas resetu i w stanie terminalnym, nawet gdy komponent zdrowia przywróci własną zgodę na sterowanie. `SetRestartEnabled` osobno dopuszcza R po porażce/ukończeniu; `ControlsEnabled` jest wynikiem obu blokad ruchu, a `RequestedControlsEnabled` zachowuje zgodę klienta. `ResetMotion()` zeruje prędkość pionową i bufor wejścia, ale nie odblokowuje jawnie wyłączonego sterowania. Po przestawieniu/odtworzeniu gracza należy wywołać `ResetMotion()`; po zakończeniu restartu również `SetControlsEnabled(true)`. Tych metod nie należy wywoływać z innych wątków. `Simulate(deltaTime)` jest krokiem kontrolera używanym przez Update oraz testy; zwykłe komponenty gry nie powinny wywoływać go drugi raz w tej samej klatce.

Utrata fokusu, pauza aplikacji, `timeScale=0`, wyłączenie komponentu/CharacterController i blokada sterowania czyszczą ruch oraz oczekujące akcje. Po wznowieniu wymagany jest neutralny odczyt wszystkich używanych osi/przycisków, a potem nowe naciśnięcie. Jest to celowa polityka bezpieczeństwa wejścia; zawieszenie zeruje także pionową prędkość, więc nie zachowuje pędu skoku sprzed pauzy.

## Ruch i granice implementacji

`Assets/Movement/Core/PlayerMotor.cs` nie zależy od Unity. Integruje tor pionowy z grawitacją od pierwszego kroku skoku, zeruje wznoszenie po kolizji z sufitem oraz stabilizuje kontakt z podłożem. Przytrzymanie Spacji nie ponawia skoku po lądowaniu. Ruch ze stałym skrętem jest integrowany po łuku, a nie według kierunku z końca klatki, co ogranicza różnice pomiędzy 30/60/120 FPS.

Decyzje techniczne, nie liczby z DOCX: docisk do podłoża 2 jednostki/s; maksymalne opadanie 60 jednostek/s; kroki CharacterController najwyżej 1/60 s; po przycięciu klatki przetwarzane jest najwyżej 0.1 s. Przy większym przycięciu symulacja świadomie zwalnia zamiast wykonać nieograniczony skok czasu. Parametry spoza przedziału 0–10000 są ograniczane w adapterze, a NaN/nieskończoność zastępowane wartościami domyślnymi. Rdzeń odrzuca błędne parametry i czas.

Bramy muszą nadal mieć geometrię uniemożliwiającą przeskoczenie ich (#8/#9). Skrypt ruchu nie rozstrzyga postępu poziomu i nie zastępuje colliderów. Nie dodano sprintu, staminy, uników ani nowych pakietów.

## Weryfikacja

```sh
dotnet test tests/Movement/Movement.Tests.csproj --configuration Release
python3 tools/unity_validation.py project
python3 -m unittest discover -s tests -p 'test_*.py' -v
```

Projekt .NET Standard 2.1 kompiluje ten sam produkcyjny rdzeń co Unity, a runner .NET 8 wykonuje te same 36 przypadków NUnit zapisane w `Assets/Tests/EditMode/PlayerMotorTests.cs`. Obejmują skok, sufit, lądowanie, granicę opadania, reset, walidację wejścia, neutralny stan po wznowieniu oraz obliczenia przy 30/60/120 FPS. Workflow `Movement C# tests` wymaga niepustego TRX, wykonania całego zestawu oraz wszystkich trzech częstotliwości dla trzech scenariuszy ruchu.

`Assets/Tests/PlayMode/PlayerMovementTests.cs` tworzy rzeczywiste obiekty CharacterController, podłoże, ściany, sufit, stopnie i wąskie przejście oraz urządzenia testowe Input System. Testy obejmują ruch, obrót, skok, kolizje, fokus, pauzę, blokadę, krótkie naciśnięcia, wyłączenie i izolację mapy wejścia. Refleksja służy dostępowi do istniejącej klasy z Assembly-CSharp, bez przenoszenia skryptu i bez atrap CharacterController.

Dnia 2026-09-30 przez Windows Unity CLI z edytorem `6000.6.3f1` wykonano 36 przypadków `PlayerMotorTests` w EditMode oraz 17 przypadków `PlayerMovementTests` w PlayMode, wszystkie zaliczone w pełnych raportach 105/105 i 68/68. Pierwszy PlayMode ujawnił błąd środowiska testowego: bez fokusu Game View Input System kierował aktualizacje do edytora, mimo wymuszenia fokusu samego komponentu. Fixture zapisuje teraz, ustawia i przywraca `IgnoreFocus` oraz `AllDeviceInputAlwaysGoesToGameView`, a po pierwszej aktualizacji wymaga typu `Manual`. Produkcyjne sterowanie i testy utraty fokusu/pauzy pozostały bez zmian; szczegóły obu prób zawiera [raport](validation/unity-castle-2026-09-30.md).

Lokalny wynik potwierdza kompilację adaptera oraz sprawdzone zachowania fizyki i Input System. Historyczny raport #6 nie obejmował ręcznego przejścia ani builda gry; bieżący stan integracji i Windows playera opisuje [raport pełnej trasy](validation/full-castle-route-2026-09-30.md). Automatyczne sterowanie nie zastępuje ręcznej kontroli #6. Brak sekretów aktywacji nadal blokuje workflow Unity w GitHub Actions (#5), niezależnie od lokalnego wyniku.

[Raport mechanik #9/#11](validation/shared-gameplay-2026-09-30.md) opisuje także testy wejścia i ruchu w zapisanych scenach demonstracyjnych. Po połączeniu z blockoutem ponownie wykonano pełne zestawy: 153 EditMode i 109 PlayMode zaliczonych, bez pominięć.

## Dokumentacja API

- [Input System 1.11: projektowe actions](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.11/api/UnityEngine.InputSystem.InputSystem.html#UnityEngine_InputSystem_InputSystem_actions)
- [InputAction: IsPressed i WasPressedThisFrame](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.11/api/UnityEngine.InputSystem.InputAction.html)
- [InputActionMap: Clone i Dispose](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.11/api/UnityEngine.InputSystem.InputActionMap.html)
- [CharacterController.Move i CollisionFlags](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CharacterController.Move.html)

Odnośniki do Input System 1.11 i Unity 6000.0 zachowano jako historyczne źródła implementacji #6. Projekt używa obecnie przypiętego Input System 1.20.0 i Unity 6000.6.3f1. Te źródła nie potwierdzają zgodności ani wykonania testów silnika po aktualizacji.
