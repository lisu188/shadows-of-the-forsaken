# Pełna trasa zamku

Źródło: DOCX §1, §3–4 i §6–8 oraz [przyjęte decyzje](design-decisions.md). Kod i jawny builder łączą dotychczasową geometrię, postęp, ruch, kamerę, interakcje i walkę w jednym poziomie. Parametry poniżej są wyborami implementacyjnymi, nie liczbami narzuconymi przez DOCX. Oryginalny dokument i jego hash pozostają bez zmian.

## Trasa i mechanizmy

| Etap | Rozgrywka i warunek dalszej drogi | Źródło |
| --- | --- | --- |
| Dziedziniec | Spokojne podejście od `(0, 0.05, -88)` do granicy pierwszego starcia na `z=12`; bez wrogów i wymuszonego oczekiwania. 100 m przy 5 m/s to założenie geometryczne, nie pomiar przejścia. | §3, §7; późniejsza decyzja właściciela |
| Pierwsze starcie | Jeden demon; jego śmierć otwiera lewą odnogę zagadki. | §3, §6, §8 |
| Zagadka | Oznaczona runą dźwignia otwiera drogę do sali tronowej. Uszkodzona dźwignia daje nieszkodliwy komunikat i nie zużywa poprawnego rozwiązania. Powrót prowadzi przez dolny węzeł. | §3, §6, §8 |
| Sala tronowa | Jeden wolniejszy, wytrzymalszy skażony strażnik. Jego pokonanie odblokowuje bibliotekę. | §4, §6–8 |
| Biblioteka | Wysunięta, oznaczona księga otwiera obowiązkowe ukryte drzwi do katakumb. | §4; interpretacja połączenia z §6–8 |
| Katakumby i finał | Droga do ostatniego demona jest niezależna od sekretu. Pokonanie demona otwiera końcową bramę; wejście żywego gracza do wyjścia kończy sesję. | §4, §6–8 |
| Opcjonalny sekret | Dźwignia katakumb otwiera górną salę bonusową i dolny skrót do tronu. Relikt daje jednorazowy tekst fabularny oraz wzmiankę na ekranie ukończenia; bez leczenia i ekwipunku. | §3, §6, §8; jawne rozstrzygnięcie niejednoznaczności mapy |

Wymagane cele pozostają takie same: `FirstEnemyDefeated`, `MainPuzzleSolved`, `MinibossDefeated`, `LibraryOpened`, `FinalEnemyDefeated`. `SecretLeverPulled` i `BonusDiscovered` są opcjonalne. Skrót nie omija biblioteki ani finałowej walki. Poziom nie ładuje nieistniejącej kolejnej sceny i nie wymaga cutscenki.

## Sterowanie, oprawa i restart

W/S porusza względem postaci, A/D obraca, Spacja skacze, LPM wykonuje pojedynczy atak, a E używa widocznego mechanizmu w zasięgu. Kamera pozostaje za graczem; scena zamku używa przesunięcia nad ramieniem −1,8 m, odległości 3 m, wysokości 4 m i pola widzenia 75°. Szczegóły celowania i zakres weryfikacji opisuje [kamera](camera-follow.md). HUD pokazuje zdrowie, bieżący cel, podpowiedź interakcji, reakcję mechanizmu oraz oddzielne ekrany porażki i ukończenia.

R lub przycisk ekranowy rozpoczyna nową sesję wyłącznie po śmierci albo ukończeniu. Restart blokuje akcje i obrażenia, przenosi gracza do bezpiecznego startu, odnawia zdrowie i tokeny życia, odtwarza przeciwników, mechanizmy i bramy, ustawia kamerę i zeruje licznik czasu. Przytrzymane klawisze wymagają zwolnienia przed kolejną akcją. Nie ma trwałego zapisu ani checkpointów.

`CameraPlayerOcclusion` ukrywa wyłącznie przypisane renderery własnej postaci, gdy kamera zbliży się do jej punktu obserwacji poniżej 1,25 m; przywraca je od 1,6 m. Collidery, przeciwnicy i geometria pozostają aktywne. Barwy `CombatFeedback` oznaczają przygotowanie, aktywny atak, trafienie i śmierć. Oryginalne modele postaci mają animację obserwującą te stany; korekta importu emisji przeszła regresję i aktualne ujęcia, a końcowa czytelność nadal wymaga odbioru człowieka.

## Starcia i integracja

| Aktor | Zdrowie | Obrażenia | Przygotowanie / aktywne okno / odstęp | Prędkość |
| --- | ---: | ---: | --- | ---: |
| Gracz w zamku | 200 | 10 | 0,20 / 0,12 / 0,38 s | 5 m/s |
| Pierwszy demon | 50 | 12 | 0,55 / 0,12 / 0,80 s | 2,8 m/s |
| Skażony strażnik | 125 | 22 | 0,85 / 0,18 / 1,00 s | 2,2 m/s |
| Demon finałowy | 100 | 18 | 0,40 / 0,12 / 0,60 s | 3,4 m/s |

Scena zamku nadpisuje domyślne 100 zdrowia / 25 obrażeń gracza na 200 / 10: dłuższe starcia zachowują większy margines na błędy początkującego. Rdzeń i osobne demonstracje zachowują swoje dotychczasowe wartości. Ten dobór wymaga pomiaru i ręcznego odbioru; nie stanowi potwierdzenia celu 2–3 minut.

Wrogowie używają zasięgu ataku 1,8 m i łuku 80°; gracz 2 m i 90°. Zasięg trafienia jest mierzony do powierzchni collidera. AI zatrzymuje podejście około 1,5 m od celu, wykrywa go w promieniu 9 m przy widoczności, a podczas rozpoczętego ataku nie obraca się za graczem. Nie dodano osobnej akcji uniku; cofnięcie i ustawienie postaci wykorzystują zwykłe sterowanie.

`EnemyEncounter` aktywuje istniejącego aktora po dozwolonym wejściu do jego logicznego pokoju. Nie tworzy kolejnych wrogów przy ponownym wejściu. Opuszczenie areny pozwala przeciwnikowi wrócić na start, ale nie odnawia jego zdrowia. Każda arena ogranicza ruch do własnego obszaru; pełna ścieżka NavMesh jest wymagana, a ruch dodatkowo sprawdza rzeczywiste collidery. Brak ścieżki, utrata celu, pauza, utrata fokusu, śmierć i stan terminalny nie uruchamiają ruchu przez przeszkody.

Scena używa jednego `LevelProgressionController`. `LevelRoomTrigger` sprawdza rzeczywistą pozycję gracza także w osi Y, co oddziela dolny skrót od sal nad nim. `LevelSessionController` zarządza stanami `Running`, `Defeated`, `Completed`, `Resetting`; nie kopiuje flag postępu. `PlayerMovement.RestartRequested` korzysta z tej samej mapy Input System co pozostałe akcje.

`CombatHealth.SetDamageEnabled` jest blokadą sesji, a `SetEncounterDamageEnabled` niezależną blokadą nieaktywnego wroga. `MeleeCombat` respektuje obie. Zdarzenie śmierci zachowuje oryginalne `SessionId` i `LifeId`. Jeżeli ostatni cios zabije wroga po wycofaniu się gracza z logicznego pokoju, `EnemyEncounter` zachowuje oczekujący cel do powrotu. Zaliczenie następuje poza callbackiem `Changed`; restart odrzuca stary zapis.

`ProgressionGate` synchronizuje panel, collider i przypisany `NavMeshObstacle` z fizycznym stanem bramy. Aktor w otworze opóźnia zamknięcie także wtedy, gdy ma NavMeshAgent lub kinematyczne ciało wroga. Samo opóźnienie aktualizacji carvingu nie pozwala AI przejść przez collider drzwi.

## Autorowanie i zakres weryfikacji

`CastleLayoutBuilder.BuildForBatch` lub menu **Shadows → Level → Rebuild Castle Layout** jawnie odtwarza scenę, mechanizmy, starcia i nawigację. Zbiera geometrię do NavMesh z fizycznych colliderów i zapisuje `Assets/LevelLayout/CastleNavigation.asset`; postacie i dynamiczne bramy nie są wypiekane jako nieruchome przeszkody. Nie jest uruchamiany przy imporcie ani w playerze. Przebudowa nadpisuje wygenerowaną scenę, materiały i nawigację, więc autorskie zmiany należy wprowadzać w źródle układu lub builderze.

`CastlePlayerBuild.BuildForBatch` buduje wyłącznie scenę zamku dla Windows x64. Wymaga zmiennej `SHADOWS_PLAYER_OUTPUT` z absolutną ścieżką pliku `.exe` poza `Assets`. Wywołanie narzędzia i obecność kodu nie dowodzą udanego builda ani uruchomienia playera.

Rdzenie C# zaliczyły 160 testów .NET, w tym 46 w zestawie Combat: 31 reguł walki i 15 reguł tożsamości/starć. Dodano `EnemyEncounterTests` z rzeczywistą fizyką i niewielkim NavMesh, `LevelSessionTests`, testy ukrywania własnej postaci oraz `FullCastleRouteTests`, które wykonują próbę przejścia zapisanego poziomu przez wejście gracza. Testy samej geometrii jawnie izolują bramy i aktorów; nie zastępują testu pełnej rozgrywki.

**Stan walidacji:** bieżące źródła zaliczyły **173/173 EditMode 9 oraz 194/194 PlayMode 35**, bez błędów i pominięć. Pełny PlayMode zakończył się kodem 0: 732,23 s runnera, 605,1941159 s zestawu XML; zamrożono 660 niezmienionych podczas przebiegu wejść oraz deltę 57 plików / 222 672 B. [Raport kamery](validation/combat-camera-2026-10-01.md) publikuje rzeczywiste ujęcia PlayMode 35 i zachowuje oddzielną historyczną galerię PlayMode 25, sprzed dodania ochrony przed zerowym kierunkiem. Najnowszy pełny zestaw narzędzi zaliczył 141 testów Python w 11,849 s; ponownie zaliczono 61 przypadków C# postępu/kamery, a wcześniejsze 160 testów rdzeni dotyczy ich niezmienionych źródeł.

Przerwania PlayMode 27/29/31 przy rezerwie 60 GiB i PlayMode 33 przy 50 GiB zachowano. Obecne 32 GiB rezerwy oraz limit 5 GiB przyrostu zadania wybrał agent; nie są to wartości narzucone przez właściciela. Headless 36 zawiesił się w ILPP przed testami; zatrzymano tylko jego drzewo procesów, uzyskując kod 1 po 436,91 s, bez XML i bez zmian 660 wejść. Headless 38 i 40 nie zaliczyły niezmienionego limitu 30 s ładowania zapisanej sceny: każdy zakończył się wynikiem 0/1 i kodem 8 przed rozpoczęciem trasy. PlayMode 40: 368,42 s runnera / 154,3470826 s XML; 153,689031 s deserializacji / 154,079391 s łącznego ładowania sceny. To zachowane niepowodzenia testów; nie obniżono wymagań ani progów kadrowania. Build Windows 6 zakończył się kodem 0 bez warunku zatrzymania po 877,67 s; log podaje `Succeeded`, 0 błędów/ostrzeżeń, 133 694 160 B i czas `00:11:39.8806601`. Zweryfikowano 198 plików silnika / 133 694 160 B i 660 wejść po buildzie. Trzy zasoby renderowania zmienione podczas budowania mają dokładne pary hashy znane z przeglądu builda 5; końcowe 54 pary importu są odrębne od 51 w EditMode 9/PlayMode 35. Graficzny PlayMode 41 zaliczył 1/1 (166,36 s runnera / 92,0267627 s XML), a headless 42 zaliczył 1/1 (146,44 s / 93,5338602 s): kod 0, bez zmian 660 wejść po buildzie, delta 60 plików / 235 339 B. Pierwsza deserializacja zamku w 42: 5,316521 s. Dokładna przyczyna wcześniejszych niepowodzeń ładowania pozostaje nieudowodniona; limity i asercje zachowano. Końcowy kolektor dowodów oraz [galeria czterech rzeczywistych ujęć PlayMode 35](validation/combat-camera-2026-10-01.md#rendered-evidence) są gotowe; BUILD-INFO został zapisany i zweryfikowany, a nowy player nie został uruchomiony. Podgląd PlayMode 25 zachowano jako materiał historyczny.

Historyczny [raport pełnej trasy](validation/full-castle-route-2026-09-30.md) zachowuje 169 EditMode / 162 PlayMode, a [raport oprawy](validation/gothic-presentation-2026-10-01.md) wcześniejsze 173 EditMode / 172 PlayMode po korekcie emisji i udany build 4. Oddzielny [build 5](validation/clean-build-2026-10-01.json) identyfikuje commit `8602b8e` i czyste wejścia; kandydat pozostaje nieuruchomiony. Bieżące testy edytora używały wyłączonego audio i natywnego cache, więc nie kwalifikują audio ani świeżego checkoutu. Właściciel odroczył standalone i wybrał kontynuowanie implementacji. Trzy podstawowe przejścia, jedno z sekretem, pierwsza próba człowieka, wydajność i cel 2–3 minut pozostają do odbioru. Aktywacja CI jest osobnym ograniczeniem.
