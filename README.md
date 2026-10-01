# Shadows of the Forsaken

Przygodowa gra akcji z eksploracją i zagadkami środowiskowymi w mrocznym, upadłym królestwie. Projekt powstaje w Unity i ma **implementować grę oraz poziom opisane w [Shadows of the Forsaken.docx](Shadows%20of%20the%20Forsaken.docx)**.

> **DOCX jest nadrzędną specyfikacją projektu** — obejmuje tekst, referencje graficzne, flow chart i mapę 2D. README opisuje zakres i stan realizacji, ale nie zastępuje dokumentu. W razie rozbieżności obowiązuje DOCX albo późniejsza, wyraźna decyzja właściciela projektu. Prototypy techniczne są etapami realizacji, nie alternatywnym pomysłem na grę.

Zapisany układ sceny porównano z mapą DOCX: [trzy widoki z edytora i zakres weryfikacji](docs/validation/castle-map-2026-10-01.md). Zgodność topologii nie zastępuje ręcznego odbioru geometrii i nawigacji.

## Docelowa gra

Zgodnie z sekcjami 1–2 dokumentu gracz eksploruje ruiny miast i zamków, walczy z demonicznymi stworzeniami oraz skażonymi ludźmi i rozwiązuje zagadki środowiskowe. Pierwszy poziom rozgrywa się w ruinach starożytnego, przeklętego zamku pośród mglistych gór.

Kierunek wizualny to gotycka architektura, wszechobecny cień i skąpe światło pochodni oraz księżyca. Referencje z §5 pokazują monumentalne gotyckie budowle, zrujnowane wnętrza, sklepienia, kolumny i kamienne katakumby z sarkofagami. Są materiałem referencyjnym, a nie zrzutami działającej gry.

## Zakres pierwszego poziomu

| Element | Wymaganie wynikające z DOCX | Źródło |
| --- | --- | --- |
| Wejście / dziedziniec | Kilkadziesiąt sekund spokojnej eksploracji, zapoznanie z zamkiem i atmosferą. | §3, §7 |
| Pierwsze zagrożenie | Stopniowe wprowadzenie zagrożenia przez pojedynczego przeciwnika / demona. | §3, §6, §8 |
| Zagadka środowiskowa | Otwieranie bram i tajnych przejść za pomocą starożytnych run lub dźwigni. | §3, §6, §8 |
| Sala tronowa | Główny punkt orientacyjny; zniszczone kolumny i ślady krwi. Tekst opisuje mini-walkę, a oba diagramy wyraźnie wskazują minibossa. | §4, §6–8 |
| Przeklęta biblioteka | Zakazane księgi i mechanizm blokujący ukryte drzwi. | §4 |
| Katakumby | Mroczne podziemne korytarze ze śladami kultu; sekretne przejście i mechanizm. Mapa oznacza sekretną dźwignię. | §4, §7, §8 |
| Finałowa walka | Flow chart umieszcza finałową walkę w etapie katakumb; mapa oznacza oddzielny końcowy obszar jako „Finałowa Walka/Scena”. | §6, §8 |
| Opcjonalny sekret | Flow chart pokazuje odnogę „Sekretne Wyjście”, a mapa „Sekretną Salę (Bonus)”. Ich związek określają rozstrzygnięcia poniżej. | §3, §6, §8 |
| Gotyckie okna / witraże | Światło księżyca podkreślające pozostałości dawnej świetności zamku. | §4 |
| Zakończenie | Wyjście lub przejście do kolejnego poziomu. | §6, §7 |
| Tempo rozgrywki | Około 2–3 minut przy podstawowej eksploracji; opcjonalne sekrety wydłużają rozgrywkę. | §3 |
| Układ i połączenia | Realizacja przebiegu z flow chartu oraz przestrzennego układu mapy 2D, z jawnym rozstrzygnięciem ich niejednoznaczności. | §6, §8 |

### Przebieg według flow chartu (§6)

Poniższy schemat odwzorowuje etapy i połączenia z ilustracji w dokumencie. Rozbicie pierwszej walki i zagadki na konkretne pokoje wynika z mapy, nie z tego uproszczonego diagramu.

```mermaid
flowchart TD
    A["Dziedziniec — start"] --> B["Pierwsza walka i zagadka"]
    B --> C["Sala tronowa — miniboss"]
    C --> D["Katakumby — finałowa walka"]
    D --> E["Wyjście / kolejny poziom"]
    C --- S["Sekretne wyjście — odnoga opcjonalna"]
```

Mapa z §8 rozwija układ przestrzenny: start znajduje się na dole, pierwsze starcie powyżej niego, zagadka po lewej, sala tronowa po prawej, katakumby dalej u góry. W górnej części są końcowa walka / scena oraz lewa odnoga do sekretnej sali bonusowej. Połączenia i proporcje przestrzeni należy sprawdzać w oryginalnej ilustracji; schemat Mermaid nie zastępuje mapy.

### Przyjęte rozstrzygnięcia — issue #4

[Decyzje projektowe](docs/design-decisions.md) rozdzielają wymagania DOCX od interpretacji przyjętych do implementacji. Zawierają mapowanie obszarów na siatkę mapy, model sterowania i walki oraz wybór pierwszego targetu: Windows x64. Oryginalny DOCX pozostał bez zmian.

- **Biblioteka:** obowiązkowy łącznik między salą tronową a katakumbami; mechanizm ukrytych drzwi otwiera drogę do podziemi.
- **Sekret:** jedna sala bonusowa z dojściem od katakumb oraz ukrytym skrótem od sali tronowej. Oba połączenia odblokowuje dźwignia w katakumbach, dopiero po otwarciu biblioteki. Skrót jest jawną interpretacją połączenia flow chartu, nie korytarzem narysowanym na mapie. Bonus nie jest wymagany do ukończenia.
- **Finał:** górna komnata należy do katakumb; po walce otwiera się wyjście kończące scenariusz. Bez obowiązkowej cutscenki i bez ładowania nieistniejącego następnego poziomu.

[Kontrakt poziomu w JSON](docs/level-contract.json) zapisuje obszary, połączenia i warunki postępu. Walidator sprawdza osiągalność i brak przedwczesnego ukończenia w modelu, z sekretem i bez niego. JSON pozostaje kontraktem projektowym; [runtime C# dla #10](docs/progression-runtime.md) implementuje te reguły osobno i jest porównywany z nim w testach. **Testy modelu i C# są oddzielne od testów sceny w Unity.**

Dokument nie określa również wszystkich parametrów implementacyjnych: klawiszy, statystyk przeciwników, obrażeń, szczegółowych zasad walki czy wymiarów geometrii. Takie decyzje należy oznaczać jako decyzje projektowe / techniczne, a nie dosłowne wymagania DOCX.

## Aktualny stan

Kod i jawny builder sceny `Assets/Scenes/ForsakenCastle.unity` łączą pełną trasę zamku: spokojny dziedziniec, trzy odrębne starcia, runiczną dźwignię, mechanizm biblioteki, opcjonalny relikt i wyjście. [Opis pełnej trasy](docs/full-castle-route.md) podaje sterowanie, parametry AI, połączenia, restart i granice dowodów. Zachowano scenę bazową `SampleScene` oraz oddzielne demonstracje mechanik.

Bieżąca integracja dodaje gotycką architekturę i dekoracje, oryginalne modele postaci z animacją, prezentacje bram/mechanizmów, rozbudowany HUD oraz opcjonalne pomiary wydajności. [Pochodzenie zasobów](docs/art-provenance.md) oddziela wygenerowane tekstury od rzeczywistych ujęć gry. **Baza kamery z PR #35 zaliczyła 173/173 EditMode 9 oraz 194/194 PlayMode 35**, w tym ochronę przed zerowym kierunkiem patrzenia i regresję kadrowania walki. Historyczny build Windows 5 z czystych wejść commita `8602b8e` zakończył się sukcesem; ten kandydat pozostaje nieuruchomiony, a build 6 zakończył się sukcesem (kod 0, bez warunku zatrzymania): zweryfikowano 198 plików silnika / 133 694 160 B. Po trzech osobno przejrzanych zmianach zasobów renderowania graficzny PlayMode 41 i headless 42 zaliczyły po jednym teście głównej trasy; ten zestaw wejść pozostaje odróżniony od pełnych EditMode 9/PlayMode 35. [Raport z galerią PlayMode 14](docs/validation/gothic-presentation-2026-10-01.md#rendered-evidence) zachowuje dowody wcześniejszej korekty emisji, a [macierz DOCX](docs/docx-acceptance.md) wskazuje odroczone testy standalone i oczekujące pierwsze przejście człowieka.

[Przegląd wszystkich 21 otwartych issues](docs/github-issue-review.md) rozdziela dostarczone mechaniki od pozostałego odbioru. Walidator wymaga teraz pełnych, kwalifikowanych nazwami klas zestawów testów Unity. Narzędzia [identyfikacji builda](docs/player-build-identity.md) i [raportowania tempa](docs/timing-validation.md) wiążą przyszłe pomiary z zachowanymi plikami; nie zastępują odroczonych przejść standalone ani odbioru człowieka.

[Poprawka kadrowania walki](docs/validation/combat-camera-2026-10-01.md) ogranicza zasłanianie przeciwników przez model gracza. Rzeczywiste [ujęcia PlayMode 25](docs/validation/combat-camera-2026-10-01.md#rendered-evidence), sprzed dodania ochrony przed zerowym kierunkiem patrzenia, pokazują pełną postać na dziedzińcu, zachowany zarys zamku oraz głowy, tułowia i kończyny atakujące trzech przeciwników; w ujęciu podejścia do finału dolna część gracza nadal wychodzi poza kadr. Regresja mierzy zasłanianie rzeczywistych trójkątów modeli w zaobserwowanym przygotowaniu ataku: co najmniej 12 próbek i 50% widoczności każdej wymaganej części. Jest to próg implementacyjny dla zasłaniania przez gracza i granic kadru; nie zastępuje oceny oświetlenia, przeszkód otoczenia ani czytelności podczas gry przez człowieka.

Dla #21/#24 dodano osobny, ograniczony zapis `room-performance.jsonl` w dotychczasowym trybie opt-in. Każda wizyta zachowuje pokój, token sesji, konfigurację i histogram rzeczywistych odstępów klatek; przejścia, utrata fokusu, pauza oraz pierwsze 5 kwalifikujących sekund wizyty są wyłączane z próbek. Krótkie wizyty mogą nie mieć pomiarów. Dotychczasowy plik sesji `performance.jsonl` zachowuje format. [Opis i granice](docs/room-performance.md) podają zaliczone 7 testów akumulatora .NET i 29 postępu; po rebase ich źródła i konfiguracja projektów pozostały identyczne z testowanym commitem `8bb24ff`. Bieżący commit `8cdba019` zaliczył **173/173 EditMode 10 i 202/202 PlayMode 44**, w tym osiem nowych regresji pokoi. [Raport walidacji](docs/validation/room-performance-2026-10-01.md) zachowuje wcześniejszy PlayMode 43: 200/202, dwa przekroczenia limitu ładowania sceny. Powtórkę wykonano bez zmiany źródeł, limitów i asercji. Build 7 zakończył się sukcesem; zweryfikowano 198 plików playera i osobny BUILD-INFO, a pomiary na wskazanym sprzęcie pozostają odroczone. Nie jest to profilowanie GPU ani kosztów poszczególnych świateł.

| Obszar | Stan |
| --- | --- |
| Specyfikacja i kontrakt (#4) | DOCX i jego hash zachowane; główna trasa oraz opcjonalność sekretu pozostają zgodne z kontraktem. Późniejsze decyzje opisują wydłużone podejście i proste mechanizmy. |
| Postęp i integracja sceny (#10, #18) | Jeden kontroler postępu, przestrzenne triggery pokoi, sesja Running/Defeated/Completed/Resetting, terminalny restart i odtwarzanie świata. |
| Ruch i kamera (#6–7) | Zachowano sterowanie, R, blokadę sesji i ukrywanie własnego modelu przy zbliżeniu kamery. Scena i builder używają `distance=3`, `height=4`, `shoulderOffset=-1.8`, `shoulderAimFraction=0.5`, `lookHeightOffset=1.5` oraz FOV 75°. To decyzje implementacyjne dla DOCX §§1–3 i 6; punkt kolizji pozostaje na wysokości 1 m, a dotychczasowa ochrona uwzględnia szerszą płaszczyznę bliską. |
| Interakcje i bramy (#9, #13, #15–16, #19) | E uruchamia oznaczoną dźwignię, księgę, dźwignię sekretu i relikt. Uszkodzony mechanizm daje nieszkodliwy komunikat. Bramy synchronizują collider, panel i blokadę nawigacji. |
| Walka i starcia (#11–12, #14, #17) | Wspólne zdrowie i melee obsługują jednego demona, wytrzymalszego strażnika i szybszego demona finałowego. AI ma ograniczoną arenę, nawigację, kontrolę przeszkód i zachowuje tokeny oczekującego zaliczenia śmierci. |
| Geometria zamku (#8) | Zachowano dziewięć obszarów, zejście biblioteki i osobny dolny skrót. Podejście wydłużono do 100 m; brak przejścia przez ścianę między zagadką a tronem. |
| HUD i zakończenie (#18) | Nazwa obszaru, pasek zdrowia, osobne panele podpowiedzi/komunikatów i terminalny restart. Ujęcia PlayMode 14 pokazują proporcje HUD i cele w bieżącym pokoju; zaliczono obie trasy oraz porażki/restarty po korekcie emisji materiałów. |
| Walidacja rdzeni i narzędzi | Historycznie zaliczono 160 testów .NET dla niezmienionych rdzeni. W bieżącej walidacji ponownie zaliczono 61 przypadków: 29 postępu i 32 kamery. Po rebase obserwacji pokoi zaliczono 142 testy Python w 20,817 s, w tym odmowę brakujących przypadków pokoju i podstawienia innej klasy testowej. Wynik PR #35 (141 testów w 11,849 s) pozostaje historyczny. Te testy nie zastępują weryfikacji sceny w Unity. |
| Walidacja integracji Unity (#22) | Bieżące EditMode 10: **173/173**, PlayMode 44: **202/202**, oba kod 0; 662 wejścia bez zmian podczas przebiegów, zamrożona delta 64 plików / 249 232 B. Wszystkie 15 przypadków obserwatora zaliczono. Zachowano PlayMode 43 (200/202): dwa błędy limitu 30 s ładowania sceny, bez zmiany limitu lub asercji w powtórce. [Raport pokoi](docs/validation/room-performance-2026-10-01.md) podaje zakres i dowody. Historyczne EditMode 9: 173/173 i PlayMode 35: 194/194 dotyczą bazy PR #35; wcześniejsze Author 17, EditMode 5, PlayMode 14 i build 4 pozostają w raporcie oprawy. Wyłączone audio i użycie cache nie kwalifikują audio ani świeżego checkoutu. |
| Walidacja poprawki kamery (#7, #22) | PlayMode 35 obejmuje bieżącą ochronę przed zerowym kierunkiem i regresję widoczności siatek. Zachowano przerwania PlayMode 27/29/31 przy rezerwie 60 GiB oraz PlayMode 33 przy 50 GiB. Obecne 32 GiB rezerwy i limit 5 GiB przyrostu zadania są ustawieniami wybranymi przez agenta, nie wartościami narzuconymi przez właściciela. Headless 36 zatrzymano po zawieszeniu ILPP przed testami: tylko drzewo tego procesu, kod 1 po 436,91 s, bez XML; 660 wejść pozostało bez zmian. Headless 38 i 40 zakończyły się niepowodzeniem limitu 30 s ładowania zapisanej sceny (każdy: 0/1, kod 8), przed rozpoczęciem trasy; limitu ani asercji nie zmieniono. PlayMode 40: 368,42 s runnera / 154,3470826 s XML; log ładowania: 153,689031 s deserializacji / 154,079391 s łącznie. Późniejszy graficzny PlayMode 41 zaliczył 1/1 (166,36 s runnera / 92,0267627 s XML), a headless 42 zaliczył 1/1 (146,44 s / 93,5338602 s); oba kod 0, bez zmian 660 wejść po buildzie, delta 60 plików / 235 339 B. Pierwsza deserializacja zamku w 42: 5,316521 s. Dokładna przyczyna wcześniejszych powolnych ładowań pozostaje nieudowodniona. Końcowy kolektor dowodów oraz [galeria czterech rzeczywistych ujęć PlayMode 35](docs/validation/combat-camera-2026-10-01.md#rendered-evidence) są gotowe; BUILD-INFO został zapisany i zweryfikowany. Historyczny podgląd PlayMode 25 (193/193, przed ochroną przed zerowym kierunkiem) zachowano oddzielnie. |
| Windows player i ręczny odbiór (#23–24) | **Build 7: sukces, 0 błędów/ostrzeżeń; 198 zweryfikowanych plików silnika / 133 699 472 B.** Osobny `BUILD-INFO.json` wiąże player ze źródłem `8cdba019` i 662 przejrzanymi wejściami, bez zmian podczas budowania. [Raport i galeria](docs/validation/room-performance-2026-10-01.md) zachowują wyniki testów, wcześniejsze niepowodzenie i granice odbioru. Build użył istniejącego cache; wcześniejszy [build 5 bez cache](docs/validation/clean-build-2026-10-01.json) ma osobną proweniencję. Player pozostaje nieuruchomiony, zgodnie z odroczeniem standalone przez właściciela; ręczny odbiór i pomiary sprzętowe pozostają otwarte. |
| Oprawa zamku i postaci (#20–21) | Author 17 zapisał 106 220 trójkątów środowiska / 126 rendererów / 126 siatek bez zmian fizyki. Ujęcia PlayMode 14 i testy trójkątów wspierają poprawkę szczelin podłoga–ściana; widoczna jest poprawiona emisja runy, płomieni i oczu. Test ponownego importu zaliczono. Późniejsze ujęcia PlayMode 25 pokazują poprawę widoczności przeciwników i kadru gracza. Końcowy odbiór człowieka, w tym czytelność walki w ruchu, pozostaje otwarty. |
| Tempo i wydajność (#23–24) | Znane automatyzacji trasy w PlayMode 14 zajęły 81,591 s bez sekretu i 121,163 s z sekretem, kończąc z 200 HP. Nie potwierdza to tempa pierwszej próby ani celu 2–3 minut. Regresje liczników zaliczono; pomiary aktualnego playera na wskazanym sprzęcie pozostają odroczone. |
| Aktywacja Unity w CI (#5) | Brak sekretów GitHub Actions nadal blokuje testy silnika w CI; lokalna aktywacja i wyniki .NET nie usuwają tego ograniczenia. |

Historyczny [raport blockoutu](docs/validation/unity-castle-2026-09-30.md) zawiera 105 EditMode i 68 PlayMode, a [raport wspólnych mechanik i rzeczywistych obrazów](docs/validation/shared-gameplay-2026-09-30.md) — 145 .NET, 153 EditMode, 109 PlayMode oraz 82 Python. Te wyniki opisują wcześniejsze rewizje i nie są deklaracją zaliczenia nowych starć, pełnej trasy ani player builda.

Dodano ignorowanie generowanych plików Unity. `Library`, `Logs` i `UserSettings` nie należą do źródeł; usunięcie ich z bieżącego drzewa Git nie usuwa ich ze starej historii.

## Plan realizacji

1. **Fundamenty techniczne:** kontroler gracza, kamera, niezawodne wejście i testy. Zachować istniejące GUID-y skryptów oraz przypiętą wersję Unity.
2. **Blokowy poziom zamku:** dziedziniec, pierwsze starcie, zagadka, sala tronowa, biblioteka, sekretne przejście, katakumby, finał i wyjście. Odwzorować mapę z rozstrzygnięciami zapisanymi w `docs/design-decisions.md`. Geometria zastępcza nie jest finalną oprawą.
3. **Pełny przebieg rozgrywki:** spokojne wejście, pojedynczy pierwszy przeciwnik, zagadka, miniboss, katakumby, finałowa walka i osiągalne zakończenie. Oddzielić opcjonalny sekret od wymaganej ścieżki.
4. **Atmosfera i czytelność:** ruiny, kolumny, księgi, ślady kultu, gotyckie okna, światło księżyca i pochodni, mgła oraz czytelna prezentacja zagrożeń.
5. **Weryfikacja:** testy logiki, testy w Unity i ręczne przejście poziomu. Sprawdzić brak blokad postępu, możliwość ukończenia bez opcjonalnego sekretu oraz dodatkową zawartość sekretnej trasy. Dopiero po pomiarach deklarować osiągnięcie czasu 2–3 minut.

Każda zmiana powinna wskazywać realizowane wymaganie i aktualizować stan implementacji. Nie dodajemy rozbudowanych systemów niezwiązanych ze specyfikacją zamiast realizować opisany poziom. Zasady dla narzędzi i agentów znajdują się w [AGENTS.md](AGENTS.md).

## Uruchomienie projektu

Wersje zapisane w repozytorium:

| Składnik | Wersja |
| --- | --- |
| Unity Editor | `6000.6.3f1` |
| Universal Render Pipeline | `17.6.0` |
| Input System | `1.20.0` |
| AI Navigation | `2.0.14` |
| Unity UI | `2.6.0` |
| Unity Test Framework | `1.8.0` |

Źródła wersji: [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt) i [manifest.json](Packages/manifest.json). Nie aktualizować edytora ani pakietów przypadkowo podczas otwierania projektu.

Na polecenie właściciela zapisano aktualizację do Unity `6000.6.3f1` oraz pakietów z manifestu i lockfile; dostosowano przypięcia walidatora, testu wersji i CI. Import, kompilację oraz testy bazy kamery potwierdzono lokalnie na Windows: 173 EditMode 9 i 194 PlayMode 35. Po dodaniu obserwacji pokoi zaliczono 173/173 EditMode 10 i 202/202 PlayMode 44; zachowano wcześniejsze dwa błędy ładowania sceny w PlayMode 43. Build 7 zakończył się sukcesem bez błędów/ostrzeżeń; zweryfikowano 198 plików i osobny BUILD-INFO ([raport](docs/validation/room-performance-2026-10-01.md)). Raport prezentacji zachowuje wcześniejsze 173 EditMode / 172 PlayMode po korekcie emisji i udany build 4; osobny build 5 identyfikuje wcześniejszy commit. Headless 38 i 40 nie zaliczyły 30-sekundowego limitu ładowania sceny i nie weszły na trasę. Build 6 zakończył się kodem 0 bez błędów/ostrzeżeń; zweryfikowano 198 plików i trzy zmiany renderowania. Graficzny PlayMode 41 i headless 42 zaliczyły po jednym teście trasy na wejściach po buildzie. Końcowy pakiet dowodów i galeria PlayMode 35 są gotowe; BUILD-INFO został zapisany i zweryfikowany. Kandydat standalone nie został uruchomiony, a ręczny odbiór pozostaje otwarty.

```sh
git clone https://github.com/lisu188/shadows-of-the-forsaken.git
cd shadows-of-the-forsaken
```

Dodaj katalog repozytorium w Unity Hub, otwórz go we wskazanej wersji edytora i zaczekaj na import zasobów oraz odtworzenie `Library`. Otwórz `Assets/Scenes/ForsakenCastle.unity` i włącz Play: W/S porusza, A/D obraca, Spacja skacze, LPM atakuje, E używa mechanizmu. R lub przycisk ekranowy rozpoczyna nową sesję po śmierci albo ukończeniu. Instrukcja trasy i jawnego autorowania jest w [opisie integracji](docs/full-castle-route.md). `SampleScene` pozostaje drugą włączoną sceną dla dotychczasowych testów bazowych. Przed dużym importem obowiązuje limit miejsca na dysku z instrukcji projektu.

[Instrukcja kontrolera #6](docs/player-movement.md) opisuje podłączenie istniejącego assetu wejścia, W/S, A/D, Spację, sygnały LPM/E, blokowanie sterowania i reset. Po przywróceniu fokusu/pauzy należy puścić używane klawisze przed ponownym sterowaniem. Gracz i geometria zamku są zapisane w ForsakenCastle; odbiór sceny w Unity pozostaje częścią #8.

[Instrukcja kamery #7](docs/camera-follow.md) opisuje przypisanie celu, tag Player, maskę przeszkód, parametry kolizji, `SetTarget` i `SnapToTarget`. Ściany muszą mieć collidery na uwzględnianych warstwach. Przy braku bezpiecznej pozycji kamera czasowo wstrzymuje renderowanie zamiast pokazywać wnętrze geometrii; ograniczenia i wymagany odbiór są opisane w instrukcji.

Wspólne mechaniki można sprawdzić w zapisanych scenach [InteractionDemo](Assets/Interactions/Demo/InteractionDemo.unity) (E: dźwignia i brama) oraz [CombatDemo](Assets/Combat/Demo/CombatDemo.unity) (LPM: atak na cel). [Instrukcja interakcji](docs/interactions.md) i [instrukcja walki](docs/combat.md) opisują podłączenie i parametry. Są to oddzielne sceny testowe poza Build Settings. Te same komponenty są używane przez integrację pełnej trasy; przygotowane warunki demonstracji nie są dowodem ukończenia zamku.

## Dokumentacja i testy narzędzi

Eksport tekstu specyfikacji, walidatory i testy narzędzi wymagają Git oraz Pythona 3.9 lub nowszego, bez dodatkowych bibliotek:

```sh
python3 tools/export_design.py
python3 tools/validate_level_contract.py
python3 tools/unity_validation.py project
python3 -m unittest discover -s tests -p 'test_*.py' -v
```

Na Windows użyj `python` zamiast `python3`, jeżeli pod tą nazwą dostępny jest interpreter. Eksport tekstowy **nie zawiera ilustracji ani układu graficznego**. Flow chart, mapę i referencje należy sprawdzać w oryginalnym DOCX.

Workflow `Validate` sprawdza dokumentację, kontrakt poziomu, śledzone źródła Unity i narzędzia. **Nie uruchamia silnika Unity, nie kompiluje gry i nie testuje rozgrywki.**

## Testy runtime C# bez edytora

Wymagany jest .NET SDK 8.0. Projekty testów odwołują się do rzeczywistego kodu rdzeni w `Assets`, nie do kopii lub atrap Unity:

```sh
dotnet test tests/Progression/Progression.Tests.csproj --configuration Release
dotnet test tests/Movement/Movement.Tests.csproj --configuration Release
dotnet test tests/Camera/Camera.Tests.csproj --configuration Release
dotnet test tests/Interactions/Interactions.Tests.csproj --configuration Release
dotnet test tests/Combat/Combat.Tests.csproj --configuration Release
```

Workflow `Progression C# tests` kompiluje rdzeń, uruchamia NUnit, sprawdza raport TRX i publikuje go jako artefakt. Test zgodności z JSON porównuje komendy we wszystkich osiągalnych stanach: 29 bez sekretu i 63 z sekretem, łącznie 1472 porównania. Pokrywa również odrzucane komendy. **Nie zastępuje testów komponentu Unity ani fizycznego przejścia poziomu.** Sposób podłączenia komponentu i granice API opisuje [dokument runtime](docs/progression-runtime.md).

Workflow `Movement C# tests` kompiluje produkcyjny rdzeń ruchu jako .NET Standard 2.1 i wykonuje 36 wspólnych przypadków NUnit. Sprawdza także obecność wszystkich prób przy 30/60/120 FPS w raporcie TRX. Testy matematyki ruchu i blokady wejścia nie potwierdzają fizyki CharacterController ani działania Input System w silniku.

Workflow `Camera C# tests` kompiluje produkcyjną matematykę kamery jako .NET Standard 2.1 i wykonuje 32 wspólne przypadki NUnit, w tym próby 30/60/120 FPS. Nie wykonuje zapytań kolizji, renderowania ani automatycznego odnajdywania celu w Unity.

Workflow `Shared gameplay C# tests` kompiluje produkcyjne rdzenie interakcji i walki. Wymaga niepustych, kompletnie zaliczonych raportów TRX; zestaw zawiera 17 przypadków interakcji oraz 46 walki i cyklu życia starć. Raycasty, fizyczne bramy, CharacterController i wejście są weryfikowane osobno w PlayMode.

## Testy Unity

Osobny workflow `Unity tests` uruchamia EditMode i PlayMode na Unity 6000.6.3f1, każdy tryb z czystego checkoutu bez cache `Library`. Wymaga skonfigurowanej aktywacji; jej brak kończy etap wstępny błędem `Unity tests NOT RUN`, a nie zaliczeniem testów. Raporty NUnit są sprawdzane pod kątem brakujących, pustych, niepełnych lub pominiętych wyników.

[Instrukcja testów i aktywacji CI](docs/unity-testing.md) zawiera polecenia lokalne dla Windows, zakres 8 przypadków bazowych, lokalizację logów oraz warunki zamknięcia #5. Kod testów jest w `Assets/Tests`. Do bazowych zestawów dodano testy rdzeni, cyklu życia, kontrolera, kamery, geometrii, interakcji, walki i demonstracji, a następnie regresje AI/nawigacji, sesji, HUD oraz pełnej trasy zamku. Walidator wymaga wykonania nowych regresji, a nie tylko bazowych przypadków. [Raport blockoutu](docs/validation/unity-castle-2026-09-30.md) zachowuje wyniki 105/105 i 68/68 oraz czysty import; [raport wspólnych mechanik](docs/validation/shared-gameplay-2026-09-30.md) opisuje walidację połączonego zestawu po rebase. Aktywacja CI, ręczny odbiór i automatyczny build gry (#24) pozostają osobnymi zadaniami.

## Struktura repozytorium

```text
Assets/                         Sceny, skrypty, zasoby i powiązane pliki .meta
Assets/CameraRig/Core/          Matematyka wygładzania i limitów kamery bez Unity
Assets/Combat/                  Zdrowie, atak, reguły tożsamości starć i arena testowa
Assets/Encounters/              AI, nawigacja i zaliczanie trzech starć
Assets/LevelSession/            Sesja, obszary, HUD i pełny restart
Assets/Interactions/            Mechanizmy, fizyczne bramy, rdzeń i scena testowa
Assets/Movement/Core/           Rdzeń ruchu i blokada wejścia bez zależności Unity
Assets/Progression/             Rdzeń postępu bez Unity i adapter MonoBehaviour
Assets/Tests/                   Testy EditMode i PlayMode silnika Unity
Packages/                       Zależności Unity
ProjectSettings/                Współdzielona konfiguracja i wersja edytora
docs/                           Decyzje projektowe, kontrakt poziomu i uruchamianie testów
tools/                          Walidacja dokumentacji, źródeł i wyników; lokalny runner Unity
tests/                          Testy narzędzi Pythona i modelu poziomu, nie testy silnika
tests/Camera/                   Runner NUnit/.NET kompilujący matematykę kamery
tests/Combat/                   Runner NUnit/.NET kompilujący rdzeń walki
tests/Interactions/             Runner NUnit/.NET kompilujący reguły interakcji
tests/Movement/                 Runner NUnit/.NET kompilujący produkcyjny rdzeń ruchu
tests/Progression/              Runner NUnit/.NET kompilujący produkcyjny rdzeń C#
.github/workflows/              Automatyzacja CI
Shadows of the Forsaken.docx     Nadrzędna specyfikacja gry i poziomu
README.md                       Zakres, uruchomienie i aktualny stan
AGENTS.md                       Zasady pracy nad projektem
```

Nie usuwać ani nie regenerować istniejących plików `.meta`; zawarte w nich GUID-y wiążą skrypty i zasoby ze scenami. Oryginalny dokument projektowy i referencje graficzne należy zachować bez nieuzgodnionych zmian.
