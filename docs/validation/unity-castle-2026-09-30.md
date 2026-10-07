# Unity: walidacja zamku, 2026-09-30

Wykonano rzeczywiste testy Windows Unity **6000.6.3f1 (45d8eee7de74)** przez CLI **1.0.0-beta.11** dołączone do Unity Hub. Izolowany checkout pochodził z `c0676e5e93684d3aea729d88ce70cd7b648e5da6`. Próba 2 zawierała dodatkowo poprawkę dwóch fixture'ów PlayMode zapisaną w tym PR; ich dokładne hashe podano niżej. Kod produkcyjny, geometria, wersja edytora i pakiety nie zostały zmienione w trakcie tej walidacji.

## Wyniki

| Próba | Tryb | Zaliczone/wszystkie | Błędy | Pominięte | Czas procesu CLI |
| --- | --- | --- | --- | --- | --- |
| 1 | editmode | 105/105 | 0 | 0 | 424.64 s |
| 1 | playmode | 34/68 | 34 | 0 | 68.06 s |
| 2 | playmode | 68/68 | 0 | 0 | 32.95 s |
| 2 | editmode | 105/105 | 0 | 0 | 18.11 s |

Pierwszy EditMode obejmował czysty import bez `Library`; dalsze uruchomienia korzystały z tego samego cache. Czas procesu obejmuje start/import/kompilację i zamknięcie edytora, nie czas przejścia gracza przez poziom. Walidator `tools/unity_validation.py results` przyjął oba raporty próby 2 jako kompletne i zaliczone.

EditMode: 32 przypadki matematyki kamery, 36 rdzenia ruchu, 26 rdzenia postępu, 6 bazowych oraz 5 sceny zamku. PlayMode: 2 bazowe, 22 kamery, 17 ruchu, 7 cyklu życia postępu oraz 20 zamku. Ostatnia grupa obejmuje wszystkie 9 przejść w obie strony, skok na dziedzińcu i próbę przejścia/skoku przez ścianę zagadki. Testy używają rzeczywistych scen, Input System i CharacterController; przejścia są sterowane programowo.

## Poprawka po pierwszym PlayMode

34 początkowe błędy dotyczyły 20 przypadków zamku i 14 ruchu. Edytor batch nie miał fokusu Game View, więc Input System 1.20.0 wybierał aktualizację `Editor` i nie wywoływał callbacku gracza. Nie dochodziło nawet do kroku grawitacji, stąd wspólne błędy początkowego podparcia.

Fixture'y zapisują, ustawiają i przywracają `backgroundBehavior=IgnoreFocus` oraz `editorInputBehaviorInPlayMode=AllDeviceInputAlwaysGoesToGameView`. Asercja sprawdza, że początkowe zdarzenie syntetyczne dociera jako `Manual`. Produkcyjna obsługa utraty fokusu pozostaje bez zmian i jej istniejący test również przechodzi. Nie wyłączono asercji, testów ani walidatora wyników.

## Odtworzenie

Na Windows ustaw `$castleProject` na izolowany checkout tego PR, a `$castleResults` na osobny katalog wyników. Zainstalowany i aktywowany edytor wystarcza do lokalnego uruchomienia; nie są potrzebne sekrety GitHub.

```powershell
$unityCli = 'C:\Program Files\Unity Hub\resources\unity.exe'
$editor = 'C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe'
& $unityCli test $castleProject --mode EditMode --editor-path $editor --timeout 1800 --output "$castleResults\editmode-results.xml" --non-interactive --no-log-proxy -- -assemblyNames Shadows.EditMode.Tests -logFile "$castleResults\editmode.log"
& $unityCli test $castleProject --mode PlayMode --editor-path $editor --timeout 900 --output "$castleResults\playmode-results.xml" --non-interactive --no-log-proxy -- -assemblyNames Shadows.PlayMode.Tests -logFile "$castleResults\playmode.log"
python tools/unity_validation.py results --mode editmode "$castleResults\editmode-results.xml"
python tools/unity_validation.py results --mode playmode "$castleResults\playmode-results.xml"
```

Nie dodawano `-quit`, `-nographics` ani instalacji innej wersji edytora. Uruchomienia używały Direct3D 11 i lokalnej licencji Personal. Właściciel jawnie zezwolił na test mimo początkowego przekroczenia limitu 90% C:. Nadzorca kontrolował wspólny budżet wzrostu zajętości 5 GiB oraz minimum 60 GiB wolnego miejsca; nie przerwał żadnej próby. Wzrost wolnego miejsca podczas sesji nie jest przypisywany temu zadaniu.

## Dowody i granice

Pełne XML-e, logi edytora/CLI, polecenia, kody wyjścia, hashe źródeł i patche są zachowane lokalnie w `artifacts/unity-acceptance/results/attempt-1` i `attempt-2` oryginalnego checkoutu. Pierwszy nieudany PlayMode zachowano. Pliki `*-execution.json` wiążą raporty ze źródłami; raport sprzątania jest w `artifacts/unity-acceptance/cleanup.json`. Surowe logi i cache nie są częścią repozytorium.

Po zakończeniu procesów usunięto wyłącznie odtwarzalny `checkout/Library`: 28 681 plików, 1 164 396 233 bajty treści (około 1,08 GiB). Zachowano źródła i Git izolowanego checkoutu, małe logi importu oraz wszystkie raporty. Pomiar C: przed/po tej operacji wykazał wzrost wolnego miejsca o 1 192 017 920 bajtów do 102 348 763 136 bajtów (około 95,32 GiB); równoległe zmiany innych procesów mogą wpływać na różnicę. Nie usuwano danych z dysku Linux ani nie kompaktowano VHDX.

Import nie był wolny od ostrzeżeń: pojawiły się komunikaty pakietów dotyczące ILPP, opóźnień heartbeat i nieużywanych shaderów Terrain/SpeedTree. Nie stwierdzono błędów kompilacji C# ani brakujących komponentów sceny. URP dodał materiałom standardowe ukryte metadane `AssetVersion: 10`; wartości materiałów pozostały takie same. Lokalne patche zachowują te zmiany importera oraz kosmetyczne zmiany serializacji ustawień.

**Nie wykonano** ręcznego przejścia zwykłymi kontrolkami, przeglądu obrazu kamery, rzutu edytora zestawionego z mapą DOCX, player builda ani pomiaru docelowych 2–3 minut rozgrywki. Wyniki nie zamykają automatycznie #8 ani pełnego odbioru #5. Aktywacja GitHub Actions pozostaje oddzielnym warunkiem; lokalny sukces nie zmienia nieudanego statusu CI.

SHA-256 końcowych raportów NUnit:

- `editmode-results.xml`: `9d1a85c6bdfa87c20a844e7dd7851ce100386566ec524c363ac88e5ae61c43a1`
- `playmode-results.xml`: `d3de3bf9f78c1f78702f2680b406475705dc18e6c6bf0726a01137b17b3bc913`

SHA-256 sceny i poprawionych fixture’ów użytych w obu końcowych uruchomieniach:

- `Assets/Scenes/ForsakenCastle.unity`: `0160a1eeb404f64e27d6f57f8e4ce3ac6c0c4b089335aadcfbd22839d38c2a45`
- `Assets/Tests/PlayMode/PlayerMovementTests.cs`: `0db8282c7f9259f27683d2186427ba4af74d6839a3b7b02e67dadbf240ada0bc`
- `Assets/Tests/PlayMode/CastleLayoutTraversalTests.cs`: `61d1348f1135de6008096baf8589c8ba5412d1aa7b1df6c148040710b888087c`
