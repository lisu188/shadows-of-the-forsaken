# Weryfikacja dostarczonego poziomu — 2026-10-05

Scena `Assets/Scenes/ForsakenCastle.unity` implementuje jeden grywalny poziom: spokojny dziedziniec → pojedynczy demon → lewa zagadka i powrót do węzła → miniboss w sali tronowej → obowiązkowa biblioteka → katakumby → finałowa walka → wyjście. Dźwignia, sala bonusowa, relikt i ukryty powrót do tronu pozostają opcjonalne. Źródłowy DOCX, jego flow chart, mapa i referencje zostały przeczytane/obejrzane; oryginalny blob `8c5b63bdf8f054a60b024407641921caa8055e75` oraz wcześniejsze decyzje i kontrakt pozostały bez zmian.

Implementację i przypisanie do sekcji DOCX opisuje [mapowanie poziomu](level-implementation.md). Poniżej są wyniki rzeczywiście wykonanych kontroli, a nie plan testów lub deklaracja ręcznego odbioru właściciela.

## Wyniki i dowody

Wyniki lokalne znajdują się w ignorowanym `artifacts/level-completion`. Nie dołączono cache, logów, raportów ani builda do źródeł Git.

| Kontrola | Rzeczywisty wynik | Lokalny dowód |
| --- | --- | --- |
| Kontrakt i DOCX | PASS; 29 stanów bez sekretu, 63 z sekretem, niezmieniony hash DOCX | `tools/validate_level_contract.py` |
| Narzędzia Python | 73/73 PASS w WSL, w tym odrzucenie niepełnego zielonego zestawu Unity | `python3 -m unittest discover -s tests -p 'test_*.py' -v` |
| Integralność źródeł Unity | PASS; komplet nowych `.meta`, 98 unikalnych GUID-ów, przypięte wersje, brak generowanych źródeł | `tools/unity_validation.py project` |
| Rdzenie .NET / NUnit | 115/115 PASS, bez pominięć: postęp 29, ruch 36, kamera 32, walka 13, runy 5 | `final-headless/<zestaw>/*.trx` |
| Unity EditMode po czystym imporcie | 113/113 PASS, 0 failed/skipped; rzeczywisty edytor i urządzenie graficzne | `final-cold-editmode-probe/editmode-results.xml`, `editmode.log`, `source-snapshot.json` |
| Unity PlayMode | 67/67 PASS, 0 failed/skipped; cały oczekiwany zestaw, w tym 4 przypadki rzeczywistej sceny | `final-playmode/playmode-results.xml`, `playmode.log`, `source-snapshot.json` |
| Zwykły Update przeciwnika | Pościg i obrażenia przy 60 FPS oraz bez limitu; przypadki zawarte w 67 PlayMode | `focused-native-pursuit-retry-1`, końcowy raport 67 |
| Główna trasa z pomiarem | Ukończona bez sekretu: 134,1875 s czasu gry / 134,1916 s rzeczywistego czasu, 255,44 m, zdrowie 100/100 | `final-traversal/MainRouteWinsWithoutSecretAndVictoryRestartRestoresTheWholeScene.json` |
| Sekret i ukryty powrót | Górna odnoga, relikt, fizyczne zejście do Y=-6 i powrót do tronu; finał nadal wymagany | `final-traversal/OptionalRelicAndPhysicalConcealedRampReturnStillRequireTheFinalFight.json` |
| Porażka, restart i zamknięte bramy | Śmierć od ataków przeciwnika, pełny reset sceny, przycisk/R, stare callbacki, fokus/pauza i blokada skoku przez bramę | Pozostałe 2 JSON-y `final-traversal`, raport 67 |
| Renderowanie sceny i HUD | 10 PNG 1280 × 720: dziedziniec, pierwsza sala, tron, biblioteka, katakumby, bonus, powrót oraz oba zakończenia i porażka; obrazy obejrzane | `final-traversal/*.png` |
| Windows x64 Mono | SUCCEEDED, 114 699 995 B, 38,40 s według BuildPipeline, 0 błędów, 2 ostrzeżenia przypiętego URP; proces exit 0 | `build-final/build-summary.json`, `editor.log`, `execution.json`, `player-file-manifest.json` |
| Natywny player i HUD | PASS, exit 0: Unity 6000.0.24f1, RTX 4060 Ti / D3D11, 1280 × 720, dziedziniec, 3 przeciwników, 9 przejść, zdrowie 100, bezpieczna kamera oraz rzeczywiste piksele Canvas | `player-smoke-final/startup.json`, `courtyard.png`, `player.log`, `execution.json` |
| Paczka Windows | 169 wymaganych plików playera; ZIP 36 619 044 B, wszystkie CRC oraz SHA-256 zawartości zweryfikowane | `package-summary.json`, `Builds/ShadowsOfTheForsaken-Windows-x64.zip` |

Rdzenie .NET kompilują te same produkcyjne źródła co Unity. Nie symulują MonoBehaviour. XML-e Unity zostały sprawdzone bieżącym walidatorem, który wymaga jawnie zapisanych nazw `Fixture.Metoda`, wszystkich wariantów oraz zerowej liczby pominiętych przypadków; samo exit 0 lub wcześniejszy mniejszy zestaw nie wystarcza.

## Tożsamość źródeł i środowiska

- Przypięty edytor: `6000.0.24f1` (`11fa355cd605`), faktycznie uruchomiony z `C:\Users\andrz\AppData\Local\Unity\Editors\6000.0.24f1\Editor\Unity.exe`. Lokalna licencja działała podczas testów i builda; nie zmieniano wersji projektu.
- Pakiety: URP `17.0.3`, Input System `1.11.1`, Unity Test Framework `1.4.5`; manifest i lockfile zachowane. Testy renderujące miały rzeczywiste Direct3D11, bez `-nographics`.
- Pełne 67 PlayMode sprawdziło niezmienny snapshot drzewa Git `c861c3461365fc175bbf6570992643e530165cee`. Następny snapshot `91cd3fc63aca1772197e8e0c5ad3c4ca004e32a7` zmienia wyłącznie `LevelDeliveryProbe.cs`: wiązanie docelowej tekstury opt-in zrzutu natywnego. Żaden komponent rozgrywki ani test nie zmienił się między nimi; probe nie jest tworzony podczas tych testów.
- Oba dokładnie sprawdzone drzewa zachowano jako commity: rozgrywka `b5ee9cbec42e9b74977096e056e96f3f42ba2f34`, poprawka zrzutu/build natywny `47ef1e40c7a3d628b0ef0425fad098aecc796853`. Późniejszy commit dokumentacji nie zastępuje ich historycznym green ani nie zmienia źródeł runtime.
- Końcowy czysty import i 113 EditMode wykonano na `91cd3fc63aca1772197e8e0c5ad3c4ca004e32a7`. Przed uruchomieniem w osobnym checkoutcie nie istniały `Library`, `Temp`, `Logs`, `Obj` ani `UserSettings`; wcześniejszej używanej kopii i jej cache nie usuwano.
- Końcowy subtree `Assets` ma identyfikator Git `bf81918a24cf14e48c2e1564e809aca627348b89`. Późniejsze korekty raportu/README opisują dowody; nie zmieniają sprawdzonych skryptów, geometrii, sceny, testów lub pakietów. Różnica opt-in probe jest weryfikowana osobnym natywnym uruchomieniem.

## Metoda pomiaru i granice wyniku

Główna fixture porusza rzeczywisty CharacterController przez collidery i triggery z krokiem `speed * Time.fixedDeltaTime` przy 2 m/s. Gracz pozostaje podatny na obrażenia; test po każdym zamachu cofa się z zasięgu przeciwnika. Produkcyjne Update wykonują okna ataków gracza i przeciwników. Interakcje używają rzeczywistego zasięgu i line of sight. Test nie przyznaje flag przez `TryComplete`, nie przeskakuje biblioteką ani finałem, nie dodaje obowiązkowych pauz i wymaga wszystkich trzech zabitych przeciwników oraz 120–180 s czasu gry.

Zmierzony wynik około **2:14** spełnia zakres 2–3 minut dla tej automatycznej podstawowej trasy. Fixture steruje drogą i obrotem bez decyzji człowieka, więc nie dowodzi czasu pierwszego przejścia nowego gracza ani ręcznej jakości walki. Trasa opcjonalna używa szybszych kroków testowych (maksymalnie 0,35 m na FixedUpdate); jej 28,65 s nie jest czasem rzeczywistej rozgrywki. Dystans około 444,30 m potwierdza większą fizyczną trasę z sekretem i powrotem.

PNG-y pochodzą z rzeczywistej kamery świata i jej Canvas w przestrzeni kamery: URP `SingleCameraRequest` → RenderTexture → `ReadPixels`. Zrzut jest odrzucany, jeśli pozostaje jednolity; dla żywego gracza weryfikowane są piksele rzeczywistego paska zdrowia. Obejrzano czytelność HUD-u, biblioteki, katakumb, tronu, bonusu oraz ekranów zakończenia/śmierci. Jest to przegląd obrazów przez agenta, nie odrębny odbiór artystyczny właściciela lub obsługa przycisku myszą przez człowieka. Przycisk testowany jest przez jego rzeczywisty listener; R przez dostarczoną akcję wejścia.

## Windows i uruchomienie

Końcowy build zawiera tylko `ForsakenCastle`; używa Windows x64 Mono i źródeł `91cd3fc63aca1772197e8e0c5ad3c4ca004e32a7` po czystym imporcie. Normalny player (bez `-batchmode`) został uruchomiony z `WindowStyle Hidden` i jawnym opt-in `-forsakenSmokeOutput`, wykonał test oraz sam zakończył się kodem 0 po 16,81 s. Obraz `player-smoke-final/courtyard.png` został obejrzany: świat, gracz, zdrowie, cel, sterowanie i czas są widoczne. Piksel czerwonego paska wyniósł RGB około (0,678; 0,165; 0,133), a oba warunki `cameraCaptureSaved` i `hudCaptureSaved` są prawdziwe. Zwykłe uruchomienie bez tego argumentu nie tworzy komponentu probe, nie zapisuje dowodów i nie kończy gry automatycznie.

Dwa ostrzeżenia builda pochodzą z przypiętego URP 17.0.3: `ScreenSpaceAmbientOcclusion.shader` wiersze 167/211, niejawne skrócenie wektora na D3D11. Nie było błędów kompilacji ani wyjątków w końcowym logu playera; wersji pakietu nie aktualizowano dla wyciszenia ostrzeżeń.

Paczka `Builds/ShadowsOfTheForsaken-Windows-x64.zip` zawiera EXE, `UnityPlayer.dll`, crash handler, Data, `MonoBleedingEdge`, `D3D12`, krótką instrukcję `PLAY.txt` i manifest `SHA256SUMS.json`. Wykluczono wygenerowany katalog `BurstDebugInformation_DoNotShip`; oryginał builda, wcześniejszy build oraz dowody zostały zachowane. SHA-256 archiwum:

```text
de378c8bdec493cde2d3f8baef6adea702f13d2f20ea9f65a992cc6ec9955311
```

Assembly rozgrywki końcowego playera ma SHA-256 `0c7052138bdfcc87d89c5221a2e5ad85b42037b8a28673e71f3bd4474f0ee4d9`. Aby grać, rozpakuj cały ZIP i uruchom `ShadowsOfTheForsaken.exe`. W/S porusza, A/D obraca, Spacja skacze, LPM atakuje, E obsługuje mechanizmy. R lub przycisk rozpoczyna nową sesję po śmierci albo zwycięstwie.

## Zachowane ograniczenia i wcześniejsze nieudane próby

Nie wykonano ręcznej rozgrywki człowieka, ręcznego pomiaru kilku przejść, oceny podobieństwa do referencji przez właściciela, benchmarku całej trasy natywnego playera ani nowego zdalnego runu CI. Lokalny green nie zamyka automatycznie issues/PR i nie dowodzi działania GitHub secrets. Częstotliwość Update w ukrytym oknie nie jest prezentowanym FPS; jawny offscreen render dowodzi obrazu z GPU, a nie płynności całej trasy w widocznym oknie.

Nieudane i pośrednie wyniki pozostają w `baseline`, `integrated-playmode*`, `focused-native-pursuit` oraz wcześniejszych katalogach smoke. Wykryto i poprawiono: izolację wejścia w batch fixture, przypadkowe wyłączenie TestRunnera przez fixture, przedwczesny zrzut, zbyt krótki testowy budżet dojścia do finałowego przeciwnika oraz rzeczywisty błąd pościgu przy wysokiej częstotliwości Update. Domyślne `CharacterController.minMoveDistance` odrzucało małe kroki; produkcyjny Configure ustawia 0, a oba regresyjne przypadki NativeUpdate sprawdzają ten fix. Nie zmniejszano wymaganej liczby testów ani warunków ukończenia.

Wczesne zrzuty natywnego backbuffera były czarne pomimo niepustego pliku, a wcześniejsze zrzuty kamery wykluczały OnGUI. Te próby nie stanowią odbioru HUD-u. Końcowe zrzuty Canvas wymagają rzeczywistych pikseli jego paska zdrowia; starszych dowodów nie nadpisano ani nie przedstawiono jako końcowych.
