# Shadows of the Forsaken

Przygodowa gra akcji z eksploracją i zagadkami środowiskowymi w mrocznym, upadłym królestwie. Projekt powstaje w Unity i ma **implementować grę oraz poziom opisane w [Shadows of the Forsaken.docx](Shadows%20of%20the%20Forsaken.docx)**.

> Dokument DOCX jest nadrzędną specyfikacją projektu: obejmuje tekst, referencje graficzne, flow chart i mapę 2D. README opisuje zakres i stan realizacji, ale nie zastępuje specyfikacji. W razie rozbieżności obowiązuje DOCX albo późniejsza, wyraźna decyzja właściciela projektu. Prototypy techniczne są etapami realizacji, nie alternatywnym pomysłem na grę.

## Docelowa gra

Zgodnie z sekcjami 1–2 dokumentu gracz eksploruje ruiny miast i zamków, walczy z demonicznymi stworzeniami oraz skażonymi ludźmi i rozwiązuje zagadki środowiskowe. Pierwszy poziom rozgrywa się w ruinach starożytnego, przeklętego zamku pośród mglistych gór.

Kierunek wizualny to gotycka architektura, wszechobecny cień i skąpe światło pochodni oraz księżyca. Atmosfera ma budować zagrożenie, a nie zastępować czytelność otoczenia i rozgrywki.

## Zakres pierwszego poziomu

| Element | Wymaganie wynikające z DOCX | Źródło |
| --- | --- | --- |
| Wejście / dziedziniec | Kilkadziesiąt sekund spokojnej eksploracji, zapoznanie z zamkiem i atmosferą. | §3, §7 |
| Pierwsze zagrożenie | Stopniowe wprowadzenie zagrożenia przez pojedynczego przeciwnika / demona. | §3 |
| Sala tronowa | Główny punkt orientacyjny i miejsce mini-walki; ruiny, zniszczone kolumny i ślady krwi. | §4, §7 |
| Zagadka środowiskowa | Otwieranie bram i tajnych przejść za pomocą starożytnych run lub dźwigni. | §3 |
| Przeklęta biblioteka | Zakazane księgi i mechanizm blokujący ukryte drzwi. | §4 |
| Katakumby | Mroczne podziemne korytarze ze śladami kultu; sekretne przejście stanowi miejsce nagrody lub wyzwania i klucz do dalszych lokacji. | §4, §7 |
| Gotyckie okna / witraże | Światło księżyca podkreślające pozostałości dawnej świetności zamku. | §4 |
| Zakończenie | Wyjście lub przejście do kolejnego poziomu. | §7 |
| Tempo rozgrywki | Około 2–3 minut podstawowej eksploracji; opcjonalne sekrety wydłużają rozgrywkę. | §3 |
| Układ i połączenia | Zgodność z flow chartem i mapą 2D w dokumencie, nie tylko z powyższą listą pomieszczeń. | §6, §8 |

Nie wszystkie parametry implementacyjne są określone w dokumencie. Konkretne klawisze, statystyki walki, liczby obrażeń, geometria kolizji i narzędzia deweloperskie wymagają jawnie opisanych decyzji technicznych. Nie należy przedstawiać takich decyzji jako dosłownych wymagań DOCX.

## Aktualny stan

Repozytorium jest na początkowym etapie. Zawiera projekt Unity, scenę szablonową `Assets/Scenes/SampleScene.unity`, podstawowe skrypty `PlayerMovement` i `CameraFollow`, konfigurację Input System oraz zasoby nocnego nieba. Sama obecność skryptów i zasobów **nie oznacza**, że opisany w dokumencie poziom jest grywalny.

Walka, zagadka, biblioteka, katakumby, sekrety i zakończenie poziomu nie są jeszcze zaimplementowane. Czas przejścia i docelowa oprawa również nie zostały zweryfikowane.

Dodano ignorowanie generowanych plików Unity oraz wstępną walidację GitHub Actions. `Library`, `Logs` i `UserSettings` nie należą do źródeł; ich usunięcie z bieżącego drzewa Git nie usuwa ich ze starej historii.

## Plan realizacji

1. **Fundamenty techniczne:** kontroler gracza, kamera, niezawodne wejście i testy. Zachować istniejące GUID-y skryptów oraz przypiętą wersję Unity.
2. **Blokowy poziom zamku:** dziedziniec, sala tronowa, biblioteka, sekretne przejście, katakumby i wyjście, z połączeniami wynikającymi z mapy oraz flow chartu DOCX. Geometria zastępcza służy testowaniu, nie jest finalną oprawą.
3. **Pełny przebieg rozgrywki:** spokojne wejście, pierwsza walka, interakcja z runami / dźwignią, odblokowanie przejścia i osiągalne zakończenie; opcjonalny sekret musi być odróżniony od wymaganej ścieżki.
4. **Atmosfera i czytelność:** ruiny, kolumny, księgi, ślady kultu, gotyckie okna, światło księżyca i pochodni, mgła oraz odpowiednia prezentacja zagrożeń.
5. **Weryfikacja:** testy logiki, testy w Unity i ręczne przejście poziomu. Potwierdzić brak blokad postępu i dopiero po pomiarach deklarować docelowe 2–3 minuty rozgrywki.

Każda zmiana powinna wskazywać realizowane wymaganie i uczciwie aktualizować stan implementacji. Nie dodajemy rozbudowanych systemów niezwiązanych ze specyfikacją zamiast realizować opisany poziom.

## Uruchomienie projektu

Wersje zapisane w repozytorium:

| Składnik | Wersja |
| --- | --- |
| Unity Editor | `6000.0.24f1` |
| Universal Render Pipeline | `17.0.3` |
| Input System | `1.11.1` |
| Unity Test Framework | `1.4.5` |

Źródła wersji: `ProjectSettings/ProjectVersion.txt` i `Packages/manifest.json`. Nie aktualizować edytora ani pakietów przypadkowo podczas otwierania projektu.

```sh
git clone https://github.com/lisu188/shadows-of-the-forsaken.git
cd shadows-of-the-forsaken
```

Dodaj katalog repozytorium w Unity Hub, otwórz go we wskazanej wersji edytora i zaczekaj na import zasobów oraz odtworzenie `Library`. Otwórz `Assets/Scenes/SampleScene.unity`. To obecnie scena bazowa, a nie gotowy poziom z dokumentu.

## Dokumentacja i walidacja

Tekst specyfikacji można odczytać bez dodatkowych bibliotek Pythona:

```sh
python3 tools/export_design.py
```

Na Windows polecenie może mieć postać `python tools/export_design.py`. Eksport tekstowy **nie zawiera ilustracji ani układu graficznego**. Flow chart, mapę i referencje należy sprawdzać w oryginalnym DOCX.

Workflow `Validate` eksportuje treść dokumentu i udostępnia ją jako artefakt. Kontrola struktury i testy logiki są uruchamiane dopiero po dodaniu odpowiadających im skryptów / projektów testowych. Zielony wynik samego eksportu nie potwierdza kompilacji ani działania gry w Unity.

## Struktura repozytorium

```text
Assets/                         Sceny, skrypty, zasoby i powiązane pliki .meta
Packages/                       Zależności Unity
ProjectSettings/                Współdzielona konfiguracja i wersja edytora
tools/                          Narzędzia dokumentacji i walidacji
.github/workflows/              Automatyzacja CI
Shadows of the Forsaken.docx     Nadrzędna specyfikacja gry i poziomu
README.md                       Zakres, uruchomienie i aktualny stan
AGENTS.md                       Zasady pracy nad projektem
```

Nie usuwać ani nie regenerować istniejących plików `.meta`; zawarte w nich GUID-y wiążą skrypty i zasoby ze scenami. Oryginalny dokument projektowy i referencje graficzne należy zachować bez nieuzgodnionych zmian.
