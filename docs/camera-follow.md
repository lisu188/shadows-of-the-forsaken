# Kamera śledząca — issue #7

Zakres: fundament eksploracji zamku z DOCX §2–4. Rozwinięto istniejący `Assets/CameraFollow.cs`, bez zmiany nazwy, ścieżki, GUID oraz pól `player`, `distance`, `height`, `smoothSpeed`. Domyślne wartości trzech liczb pozostają 5, 2 i 2. W ramach #7 nie zmieniono scen ani wersji pakietów. Kamera nie wymaga Cinemachine ani nowej zależności Unity.

## Podłączenie i wybory implementacyjne

Komponent wymaga `Camera` na tym samym obiekcie. Przypisać `player` do korzenia postaci albo dziecka pod jej `CharacterController`/`Rigidbody`. Tak rozpoznana hierarchia postaci jest pomijana przez zapytania kolizji; nie ignorujemy całego wspólnego korzenia sceny. Najprostszy wariant to przypisanie korzenia gracza.

Pozycja docelowa nadal wynosi `player.position - player.forward * distance + Vector3.up * height`. Nowy `pivotHeight` (domyślnie 1 jednostka) podnosi punkt obserwacji i początek zapytań nad stopy. Jest to ustawienie implementacyjne, nie liczba z DOCX; dla celu ustawionego na wysokości głowy zmniejszyć je odpowiednio.

Wygładzanie wykorzystuje `1 - exp(-smoothSpeed * deltaTime)`. Daje jednakowy zanik odchylenia dla nieruchomego celu przy różnych krokach czasu. Nie oznacza identycznych trajektorii dla dowolnego ruchomego celu próbkowanego z różnymi FPS. Ograniczenie przez przeszkodę następuje po wygładzaniu, natychmiast; odsuwanie po usunięciu przeszkody pozostaje płynne. `smoothSpeed = 0` zatrzymuje zwykłe wygładzanie, nie wyłącza kontroli bezpieczeństwa ani jawnego resetu.

Pierwsze przypisanie, zmiana celu, ponowne włączenie oraz skok pozycji większy niż `teleportDistance` (8 jednostek) resetują śledzenie. `SetTarget(Transform)` jawnie zmienia cel. `SnapToTarget()` natychmiast wyznacza pozycję z kontrolą kolizji i zwraca powodzenie. Dla mniejszych teleportów wywołać tę metodę z kodu restartu #18. `Simulate(float deltaTime)` jest wspólną ścieżką wywoływaną przez `LateUpdate` i testy; nie wywoływać dodatkowo w zwykłej pętli gry.

Bez przypisanego celu opcja `findTaggedPlayer` wyszukuje dokładnie jeden aktywny obiekt z tagiem `Player` w tej samej scenie fizyki, najwyżej co 0,5 s. Dwa pasujące obiekty nie powodują arbitralnego wyboru. Przy restarcie można przypisać nowy cel przez `SetTarget` lub oznaczyć nowo utworzoną postać tagiem. Brak, zniszczenie, nieaktywność albo nieprawidłowa hierarchia celu nie powodują wyjątku co klatkę; ostrzeżenie jest ograniczone do jednego na epizod.

## Kolizje i ograniczenia

Promień bezpieczeństwa to maksimum `collisionRadius` (0,2) i długości wektorów do narożników near plane z `Camera.CalculateFrustumCorners`. To zachowawcza kula obejmująca kamerę i jej bliską płaszczyznę obrazu, również przy zmianie FOV/aspect. Dodatkowy margines to `collisionPadding` (0,05). Szeroka projekcja lub duży near clip wymagają więcej miejsca w korytarzu; nie zmniejszamy samoczynnie near clip, by ukryć problem.

Zapytania wykonują się w scenie fizyki celu. `obstructionMask` wybiera przeszkody, triggery są ignorowane. Sweep kuli sprawdza objętość, raycast chroni widoczność przy początkowym nakładaniu, a overlap potwierdza końcową pozycję. Punkt obserwacji wewnątrz przeszkody jest odrzucany. Przy narożniku wykonywane jest ograniczone szukanie bliższej wolnej pozycji, bez przesuwania gracza ani przeszkód.

Bufory zapytań rosną po nasyceniu (32 → maksymalnie 1024 wyniki), zamiast uznać niepełny zestaw za najbliższe trafienie. Ich nasycenie przy limicie kończy próbę niepowodzeniem. Typowy przebieg wykorzystuje istniejące tablice; powiększenie bufora i wyszukiwanie brakującego celu mogą alokować pamięć.

Jeżeli nie ma bezpiecznej, widocznej pozycji na sprawdzanych odcinkach, `HasSafePose` jest false i renderowanie tej kamery zostaje czasowo zawieszone z jednym ostrzeżeniem. Po odzyskaniu miejsca przywracany jest wcześniejszy stan `Camera.enabled`; kamera już wcześniej wyłączona nie zostaje samowolnie włączona. Nie jest to system wyszukiwania dowolnej trasy kamery ani gwarancja widoczności całej sylwetki w każdej geometrii. Zbyt ciasny korytarz wymaga poprawy geometrii/parametrów w #8. Nie dodano automatycznej przezroczystości ścian lub postaci. Automatyczne testy ustawień i kolizji wykonano w rzeczywistym Unity; ręczny odbiór #7 nadal pozostaje do wykonania.

## Weryfikacja

`CameraMotionTests` zawiera 32 przypadki współdzielone przez NUnit/.NET i EditMode: wygładzanie, powrót od przeszkody i natychmiastowy limit przy 30/60/120 FPS, zmienny krok, wartości brzegowe oraz nieprawidłowe wejście. Runner kompiluje rzeczywisty `Assets/CameraRig/Core/CameraMotion.cs` jako .NET Standard 2.1, nie atrapę silnika.

```sh
dotnet test tests/Camera/Camera.Tests.csproj --configuration Release
```

`CameraFollowTests` sprawdza w PlayMode prawdziwe Camera, CharacterController i zapytania fizyki: brak/zmianę celu, tagi, ścianę, sufit, narożnik, near plane, nasycenie bufora, początkowe nakładanie, teleport, cykl życia oraz wyładowanie sceny. Testy korzystają z odbicia dla istniejącej klasy Assembly-CSharp, bez zmiany jej assembly/GUID. Dnia 2026-09-30 wszystkie 22 przypadki PlayMode oraz 32 przypadki matematyki w EditMode zaliczono przez Windows Unity CLI z edytorem `6000.6.3f1`; zob. [raport pełnych zestawów 105/105 i 68/68](validation/unity-castle-2026-09-30.md).

Raporty rzeczywistego Unity są dostępne; odbiór nadal wymaga ręcznego przejścia ciasnych wnętrz i oceny wizualnej. Brak sekretów aktywacji #5 blokuje GitHub Actions, lecz nie lokalne wykonanie z aktywną licencją. Nie dostarczono player builda ani zrzutów potwierdzających wygląd kamery. Testy matematyki C#, testy silnika i ocena wizualna pozostają oddzielnymi dowodami.

## Sprawdzone odniesienia API

Odnośniki do Unity 6000.0 zachowano jako historyczne źródła implementacji #7. Nie stanowią dowodu zgodności ani wykonania testów po aktualizacji projektu do 6000.6.3f1.

- [SphereCast](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.SphereCast.html): nie wystarcza do wykrywania nakładania w punkcie początkowym.
- [RaycastNonAlloc](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.RaycastNonAlloc.html): pełny bufor nie gwarantuje najbliższych wyników.
- [CalculateFrustumCorners](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Camera.CalculateFrustumCorners.html): wektory narożników projekcji na zadanej głębokości.
- [PhysicsScene.SphereCast](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PhysicsScene.SphereCast.html), [PhysicsScene.Raycast](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PhysicsScene.Raycast.html), [PhysicsScene.OverlapSphere](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PhysicsScene.OverlapSphere.html) oraz [Collider.ClosestPoint](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Collider.ClosestPoint.html).
