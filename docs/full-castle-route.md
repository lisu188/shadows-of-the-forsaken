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

W/S porusza względem postaci, A/D obraca, Spacja skacze, LPM wykonuje pojedynczy atak, a E używa widocznego mechanizmu w zasięgu. Kamera pozostaje za graczem, z przesunięciem nad ramieniem o 1.4 m w scenie zamku. HUD pokazuje zdrowie, bieżący cel, podpowiedź interakcji, reakcję mechanizmu oraz oddzielne ekrany porażki i ukończenia.

R lub przycisk ekranowy rozpoczyna nową sesję wyłącznie po śmierci albo ukończeniu. Restart blokuje akcje i obrażenia, przenosi gracza do bezpiecznego startu, odnawia zdrowie i tokeny życia, odtwarza przeciwników, mechanizmy i bramy, ustawia kamerę i zeruje licznik czasu. Przytrzymane klawisze wymagają zwolnienia przed kolejną akcją. Nie ma trwałego zapisu ani checkpointów.

`CameraPlayerOcclusion` ukrywa wyłącznie przypisane renderery własnej postaci, gdy kamera zbliży się do jej punktu obserwacji poniżej 1,25 m; przywraca je od 1,6 m. Collidery, przeciwnicy i geometria pozostają aktywne. Barwy `CombatFeedback` oznaczają przygotowanie, aktywny atak, trafienie i śmierć. To oprawa zastępcza wymagająca odbioru w grze.

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

**Stan walidacji:** zaliczono 160 testów .NET, 84 Python, 169 Unity EditMode i 162 PlayMode (z wyłączonym audio). Zbudowano i uruchomiono Windows x64; wyniki, zachowane błędy i hashe dowodów opisuje [raport pełnej trasy](validation/full-castle-route-2026-09-30.md). Pełne pomiary standalone wymagają odblokowanego pulpitu Windows. Osobnego odbioru wymagają HUD/kamera, trzy pomiary trasy podstawowej i jeden z sekretem oraz pierwsze przejście człowieka. Cel 2–3 minut i finalna oprawa nie są jeszcze zatwierdzone. Brak sekretów aktywacji GitHub Actions pozostaje oddzielnym ograniczeniem CI.
