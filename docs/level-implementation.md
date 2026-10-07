# Implementacja alternatywnej sceny runtime zamku

Źródło: niezmieniony `Shadows of the Forsaken.docx`, tekst §1–8 oraz osobno obejrzane ilustracje §5, §6 i §8. Rozstrzygnięcia z `design-decisions.md` i reguły `level-contract.json` pozostają obowiązujące. Poniższe wymiary, stylizacja proceduralna i liczby balansu są decyzjami implementacyjnymi.

Ta scena zachowuje implementację z gałęzi ukończonej i zweryfikowanej 2026-10-06. Podstawową sceną projektu pozostaje autorska `ForsakenCastle` z main; szczegóły jej trasy opisuje `full-castle-route.md`. Wyniki z Unity6000.0.24f1 w starszym raporcie są historyczne, a bieżący projekt zachowuje Unity6000.6.3f1 i pakiety main.

Scena runtime to `Assets/Scenes/ForsakenRuntimeCastle.unity`. `ForsakenLevel` tworzy geometrię i komponenty w `Awake`, przed pierwszą klatką rozgrywki. Dzięki temu jeden zapis współrzędnych składa tę samą scenę w edytorze, testach i playerze; nie ma drugiego generatora poziomu lub stanu postępu. Scena bazowa `SampleScene` pozostaje fixture testów i nie wchodzi do dostarczanego playera.

## Mapa i zawartość

Jedna komórka mapy §8 odpowiada 12 m. Kolumna 5 jest osią X=0, wiersz 10 osią Z=0. Wyższe wiersze mają większe Z. Jest to wybór skali, nie pomiar z DOCX. Dziedziniec ma wydłużone dojście od Z=-51: spokojne wprowadzenie wynika z drogi i widoków, bez obowiązkowego oczekiwania.

Prędkość chodu w tej scenie wynosi 2 m/s, obrót 110 stopni/s. Samo dojście do pierwszej bramy ma około 69 m; daje kilkadziesiąt sekund bez walki. Historyczne automatyczne przejście z 2026-10-06 w Unity 6000.0.24f1, sprzed integracji z main, zmierzyło 255,44 m i 134,17 s, w tym walki oraz interakcje, przy skonfigurowanej prędkości bez przymusowych pauz. Wynik mieści się w 2–3 minutach dla tej automatycznej trasy; nie zastępuje ręcznego przejścia człowieka lub pomiaru całej trasy w playerze. Metodę i granice dowodu opisuje [raport weryfikacji](level-verification.md).

| DOCX | Realizacja | Przyjęte współrzędne X,Z |
| --- | --- | --- |
| §2–3, §7–8 | Spokojny dziedziniec, parapety, kolumny, pochodnie, nocne niebo i górskie sylwetki; nie atakują tu przeciwnicy. | 0,-51 do 0,18 |
| §3, §6, §8 | Jeden demon; śmierć rzeczywistego przeciwnika otwiera odnogę zagadki. | 0,24 |
| §3, §6, §8 | Lewa odnoga: trzy runy, widoczna inskrypcja „crescent, crown, flame”, dowolna liczba prób. Po rozwiązaniu gracz wraca do dolnego węzła. | -12,36 |
| §4–8 | Prawa trasa do sali tronowej: miniboss, tron, zniszczone kolumny, krew i witraż. | 12,48 |
| §4 | Obowiązkowa biblioteka w istniejącym łączniku: regały, księgi, interakcja z zakazaną księgą otwierająca ukryte drzwi. | 24,60 |
| §4, §6–8 | Katakumby, sarkofagi, czaszki i oznaczenia kultu; dźwignia sekretu nie blokuje finału. | 24,72 do -12,72 |
| §3, §6, §8 | Opcjonalna sala bonusowa przy górnej lewej odnodze, relikt oraz dolny korytarz powrotny do sali tronowej. | -12,84 do -12,96 |
| §6, §8 | Osobna górna arena, demon finałowy, zamknięte wyjście do czasu wszystkich wymaganych celów. | 12,84 do 12,96 |
| §6–7 | Przejście na północ kończy scenariusz, wyświetla wynik i daje ponowną grę. | 12,108 |

Skrót tron–bonus ma własne zejście do Y=-6 i powrót na Y=0. Nie przecina ściany pomiędzy pierwszą zagadką i tronem. Wejście z tronu oraz wyjście przy bonusie są zamknięte do otwarcia biblioteki i użycia dźwigni. `RoomTransition` na końcu skrótu zmienia logiczny pokój dopiero po przejściu do bonusu. Nie tworzy drugiej sali ani alternatywnego zakończenia.

Podziemne położenie katakumb (§4) realizuje skalny nadkład wznoszącego się zbocza ponad ich sklepieniem. Korytarz grobowców ma niższe sklepienie 4,8 m, odróżniające go od sal zamku i większej areny. Posadzka obowiązkowej trasy pozostaje na wysokości biblioteki; droga wchodzi pod wzgórze, bez obowiązkowych schodów. Jest to decyzja przestrzenna implementacji, której mapa 2D nie określa. Osobne zejście do Y=-6 należy wyłącznie do opcjonalnego skrótu.

Geometria wykorzystuje oryginalne, składane z brył detale: ostrołukowe żebra, kolumny, gzymsy, okna, regały, sarkofagi, humanoidalne sylwetki i demoniczne rogi. `CastleSurface.shader` daje kamienną fakturę, oświetlenie URP i mgłę. `WorldText.shader` renderuje inskrypcje z testem głębokości `LEqual`, dzięki czemu nie zdradzają zawartości za ścianami (§4, §7–8). Scena zapisuje referencję materiału, a jej własna kopia śledzi dynamiczny atlas fontu i jest niszczona razem z poziomem. Nie importowano ilustracji referencyjnych jako gotowej grafiki gry. Stylizacja i skala nie oznaczają automatycznie odbioru podobieństwa do referencji; wymagają obejrzenia działającej sceny.

## Sterowanie i sesja

W/S porusza, A/D obraca postać, Spacja skacze, LPM atakuje, E wybiera jedną widoczną interakcję. R i przycisk HUD działają po śmierci albo ukończeniu. Nie ma zapisu, ekwipunku, rozwoju postaci ani ładowania nieistniejącego następnego poziomu. Każda scena ma jeden `LevelProgressionController`, bez singletona lub obiektu przenoszonego między scenami.

Restart przywraca dziedziniec, zdrowie, położenie gracza i przeciwników, bramy, runy, bibliotekę, sekret, czas oraz kamerę. Komponenty sceny obserwują reset i odtwarzają stan fizyczny; żaden obserwator nie wywołuje kolejnej komendy postępu synchronicznie. Stare ataki, przejścia i oczekujący restart zachowują poprzedni token i są odrzucane. Restart klawiaturą wymaga fokusu, braku pauzy i terminalnego stanu; utrata fokusu/pauza usuwa oczekującą komendę. Przycisk terminalny przechwytuje token swojej sesji.

HUD Canvas w przestrzeni kamery skaluje interfejs względem 1280 × 720, ma własny EventSystem i kopię akcji UI, bez wyłączania wejścia gracza. Dolny panel rozciąga się do szerokości Canvas, timer zachowuje prawy margines, a podpowiedzi sterowania pozostawiają osobne miejsce na czas. Historyczne renderowanie z 2026-10-06 w Unity 6000.0.24f1 sprawdzono w proporcjach 4:3, 16:10, 16:9 i ultrawide. Pokazuje zdrowie, bieżący obszar/cel, podpowiedź interakcji, informację o przygotowaniu ataku przeciwnika, wynik końcowy, opcjonalny relikt i czas sesji. Dźwięk wiatru i rezonansu jest oryginalnie syntetyzowany; pochodnie delikatnie migoczą.

## Build i odbiór

Menu `Forsaken/Build runtime castle Windows player` lub metoda `ShadowsOfTheForsaken.Editor.ForsakenBuild.BuildWindows` tworzy player Windows x64 Mono zawierający tylko `ForsakenRuntimeCastle`. Wymaga dokładnie Unity `6000.6.3f1`. Domyślny wynik jest w ignorowanym `Builds/RuntimeCastle-Windows`; raport trafia do ignorowanego `artifacts/level-completion/build`. Przygotowanie `.meta` i pierwszej sceny można odtworzyć poleceniem `python tools/prepare_level_assets.py`; narzędzie nie zastępuje istniejących GUID-ów.

Stan wykonanych testów, builda, oględzin i pomiarów należy odczytać z README i raportu odbioru. Testy modelu Python i rdzeni .NET nie dowodzą działania colliderów ani oprawy. Automatyczne przejście Unity jest odrębnym dowodem od ręcznej rozgrywki. Nie deklarujemy osiągnięcia czasu 2–3 minut bez rzeczywistego pomiaru przejścia.
