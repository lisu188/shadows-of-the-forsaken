# Przechodni układ zamku — issue #8

Źródła: DOCX §2–4 i §6–8, obejrzane ilustracje `media/image5.png` oraz `media/image6.png`, decyzje #4 i niezmieniony kontrakt poziomu. Ta iteracja realizuje wybrany przez właściciela **walkable layout first**. Scena `Assets/Scenes/ForsakenCastle.unity` jest zapisana w repozytorium i włączona jako pierwsza w Build Settings. `SampleScene` pozostaje włączona jako druga dla istniejących testów.

## Stan i granice

Scena zawiera dziewięć obszarów, fizyczne podłoże, ściany, stropy, rampy, wspólne materiały URP, proste gotyckie punkty orientacyjne, jawnie podłączony kontroler gracza i kamerę oraz znaczniki późniejszych starć i interakcji. Wszystkie przejścia są otwarte do przeglądu geometrii. Napis w Game View wyjaśnia, że rozgrywka nie została podłączona.

Nie ma przeciwników, aktywnych zagadek, nagród, zakończenia, restartu ani komponentu `LevelProgressionController`. Scena nie zalicza celów, nie zmienia kontraktu i nie wprowadza drugiego systemu postępu. Późniejsza integracja ma użyć istniejącego kontrolera i jego tokenów sesji. Otwarte skróty są wyłącznie trybem odbioru układu; docelowe wymagania biblioteki, dźwigni i finału nadal obowiązują.

Zapisany YAML powstał małym eksporterem źródeł, bez uruchomienia Unity. Poprawność struktury YAML, referencji i geometrii modelu **nie potwierdza importu, fizyki ani obrazu silnika**. Nie wykonano jeszcze nowej sceny w EditMode/PlayMode, player builda ani ręcznego przejścia. Odbiór #8 pozostaje otwarty.

## Skala i zgodność mapy

Wymiary są decyzjami implementacyjnymi, nie liczbami z DOCX:

- Jedna komórka mapy = 8 m; środki mają `x=(kolumna−5)*8`, `z=(10−wiersz)*8`. Dziedziniec jest początkiem układu współrzędnych.
- Pokoje mają zasadniczo 8 × 8 m; główne korytarze zachowują 4 m wolnej szerokości. Otwarte ramy bram mają 4 m światła, czyli więcej niż przyjęte minimum 3 m.
- Ściany mają 4.5 m wysokości i 0.4 m grubości; płyty podłogowe 0.5 m. Stropy pozostawiają co najmniej 4 m wysokości. Początek i pierwsze starcie pozostają pod nocnym niebem.
- Biblioteka zajmuje wschodni łącznik i schodzi z 0 do −4 m na odcinku 8 m. Jej środek znajduje się na −2 m; regały zostawiają 4 m wolnego przejścia. Katakumby, bonus i finał leżą na −4 m.
- Powrotny skrót spod tronu schodzi do −12 m, obiega główny układ od zachodu i wraca rampą do bonusu. Wszystkie trzy rampy mają nachylenie około 26.565°, bez obowiązkowego skoku. Jest to jawna interpretacja połączenia z §6, a nie dodatkowy korytarz narysowany w §8.
- Wyjście leży za północną ścianą areny finału; jego środek to `(8, −4, 74)`. Nie dodaje kolejnego poziomu ani cutscenki.

Zajęte komórki oryginalnej mapy: rząd 10: c5; rząd 9: c5; rząd 8: c4–6; rząd 7: c4 i c6; rząd 6: c6–7; rząd 5: c7; rząd 4: c4–7; rzędy 3 i 2: c4 i c6. Geometria zachowuje lewą ślepą odnogę zagadki i powrót przez dolny węzeł. Nie ma przejścia przez ścianę między zagadką a tronem. Z tronu główna trasa wychodzi na wschód, a następnie na północ do biblioteki.

Wspólne materiały i lokalne bryły tworzą tron, uszkodzone kolumny, regały i księgi, sarkofagi, ślady kultu, plamy krwi oraz ostre łuki okienne. Referencyjne obrazy DOCX nie są użyte jako zasoby gry. Nie dodano pakietów, zakupionych modeli ani nowych wejść.

## Autorowanie i przyszłe połączenia

`Assets/LevelLayout/Editor/CastleLayout.json` opisuje geometrię, materiały, pokoje, przebiegi przejść i znaczniki. To wejście narzędzi edytora, nie ładowany w playerze system reguł. Główne grupy sceny to `Geometry`, `Rooms`, `Passages`, `Anchors` i `Lighting`.

- `Room_<LevelRoom>` wskazuje środek obszaru, zgodnie z enumem istniejącego rdzenia.
- Każdy z dziewięciu `Passage_<From>_<To>` ma `Waypoint_00…` na trasie oraz `GateAnchor` na poziomie podłogi. Znaczniki są pustymi Transform, bez skryptów postępu i colliderów blokujących.
- `Anchors` zawiera `Spawn`, `FirstEnemy`, `MainPuzzle`, `ThroneMiniboss`, `LibraryMechanism`, `SecretLever`, `BonusDiscovery`, `FinalEnemy` i `Exit`.
- Gracz używa istniejącego `PlayerMovement` i assetu Input System. CharacterController: wysokość 2 m, promień 0.3 m, środek `(0,1,0)`, krok 0.3 m, skin 0.02 m. Scena ustawia obrót 120°/s; domyślne pola skryptu pozostają bez zmian. Kamera ma jawny cel i uwzględnia wszystkie collidery architektury.

Jawna komenda **Shadows → Level → Rebuild Castle Layout** odtwarza scenę i jej materiały przez natywne API Unity. Pyta o zapis zmodyfikowanych scen i zastąpienie wygenerowanego układu. Nie działa podczas importu, uruchomienia gry ani automatycznie. Metoda batch: `CastleLayoutBuilder.BuildForBatch`. Regeneracja zastępuje ręczne zmiany sceny; trwałe zmiany układu należy wprowadzać w jego generatorze/JSON.

Mały eksporter bez silnika służy bootstrapowi i przeglądowi źródeł:

```sh
python3 tools/castle_layout.py
python3 tools/export_castle_scene.py
python3 tools/castle_layout.py --check
python3 tools/export_castle_scene.py --check
```

Natywny zapis Unity może inaczej uporządkować YAML; porównanie bajtowe eksportera nie jest warunkiem odbioru sceny po zapisie w edytorze. Testy porównują znaczenie geometrii i referencji. Oba sposoby autorowania zachowują istniejące GUID-y skryptów gracza i kamery. Nowe GUID-y są stałe; metadane są wersjonowane.

## Weryfikacja

Testy Pythona niezależnie sprawdzają komórki mapy, zgodność połączeń z kontraktem, nachylenia ramp, podparcie i prześwit w trzech pasach korytarzy oraz referencje i transformacje zapisane w scenie. To kontrole matematyczne źródeł, nie symulacja CharacterController.

Testy EditMode sprawdzają import sceny, Build Settings, brakujące komponenty, przypięcie gracza i kamery, pokoje i połączenia wobec istniejącego modelu oraz zgodność znaczników z JSON. Testy PlayMode prowadzą rzeczywisty CharacterController przez wszystkie dziewięć przejść w obie strony, używając istniejących akcji wejścia; sprawdzają również skok i granicę odnogi zagadki. Ich dodanie nie oznacza wykonania.

Do odbioru nadal potrzebne są:

1. Obowiązkowe testy dokumentacji, kontraktu, integralności źródeł oraz trzy zestawy rdzeni C#.
2. Rzeczywiste raporty obu trybów Unity `6000.6.3f1`, bez pominiętych nowych testów, oraz log importu/kompilacji.
3. Ręczne przejście zwykłymi W/S, A/D i Spacją przez główną trasę, powrót z zagadki, odnogę bonusu i dolny skrót; próby ścian, narożników, ramp i miejsc przyszłych bram. Kamera ma stale pokazywać postać i przejście.
4. Rzut z edytora zestawiony z mapą DOCX, kilka reprezentatywnych widoków oraz krótki raport z rewizją, poleceniami i wynikami. Rysunek źródeł lub test modelu nie zastępuje zrzutu Unity.

Czas przejścia samego blockoutu można zanotować pomocniczo. Cel 2–3 minut wymaga późniejszego przejścia z walką i zagadkami.

Podczas implementacji C: przekraczał próg 90%, dlatego nie uruchomiono dużego importu ani player builda. Odbiór w silniku należy wznowić po uzyskaniu miejsca i uzgodnieniu dostępu do aktywnego edytora. Lokalna licencja i aktywacja GitHub Actions są niezależne; brak sekretów CI nadal blokuje tam testy Unity. Przy uruchamianiu Windows Unity z WSL użyć Windows Python lub prawidłowych ścieżek Windows, zamiast przekazywać `/tmp` czy `/mnt/c` jako `-projectPath`.
