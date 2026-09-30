# Wspólna walka wręcz — issue #11

Źródło: DOCX §1, §3 i §6 oraz minimalny model z `design-decisions.md`.
Wspólne mechaniki gracza, początkowego demona, minibossa i finału nie zmieniają
kontraktu poziomu. Nie dodano AI, ekwipunku, uników, staminy ani nowych pakietów.
Rozmieszczenie i sterowanie przeciwnikami należą do późniejszych zadań.

## Podłączenie i parametry

`CombatHealth` oraz `MeleeCombat` należy umieścić na korzeniu postaci; collidery
ciała mogą być jego dziećmi. `PlayerCombat` na tym samym korzeniu subskrybuje
wyłącznie istniejący `PlayerMovement.AttackRequested` i odpina się przy wyłączeniu.
Nie odczytuje ponownie LPM ani nie tworzy drugiej mapy wejścia. Przeciwnik może
wywoływać `MeleeCombat.TryAttack()` z przyszłego AI. `CanAttack` uwzględnia również
gotowość po odstępie; atakowanie nie wymaga kopiowania komponentu dla różnych starć.

Domyślne wartości są decyzjami implementacyjnymi do późniejszego balansu:
100 zdrowia, 25 obrażeń, 2 m zasięgu od punktu 1 m nad korzeniem, przedni łuk 90°,
0.20 s przygotowania, 0.12 s aktywnego okna i 0.38 s odpoczynku. Nowy atak jest
możliwy po całym cyklu 0.70 s. Zasięg jest odległością do najbliższego punktu
collidera; kierunek i widoczność sprawdzane są w chwili rozstrzygnięcia trafienia.
Parametry czasu, obrażeń i geometrii są przechwytywane przy rozpoczęciu akcji.

`Simulate(dt)` służy zwykłemu `Update` oraz testom; nie wywoływać obu ścieżek
dwukrotnie w tej samej klatce. Krok przecinający aktywne okno wykonuje jeden
odczyt bieżącej fizyki, nawet jeśli duży `dt` obejmuje całe okno. Nie odtwarza
historycznych pozycji ani nie odkłada obrażeń do callbacka animacji. Kolejne
klatki i dodatkowe collidery nie powtarzają obrażeń tego samego celu w tej akcji.
Spam i przytrzymanie LPM nie resetują przygotowania ani odpoczynku.

Zapytania działają w scenie fizyki atakującego. Cel musi mieć aktywny, żywy
`CombatHealth`, leżeć w zasięgu i przednim łuku oraz nie być własną postacią.
Triggery nie są hitboxami. `targetMask` wybiera cele; wszystkie nie-triggerowe
warstwy nadal blokują widoczność. Uwzględniono także ścianę zawierającą punkt
początku promienia. Wypełniony bufor jest powiększany do 1024, a nasycenie limitu
odrzuca niepełne zapytanie zamiast trafiać przez niewykrytą przeszkodę.

## Śmierć, przerwanie i sesja

Zdrowie ma granice 0–maximum. `TryDamage(amount, expectedLife, expectedSession)`
odrzuca martwy cel, stare identyfikatory i niedodatnie obrażenia. `Died` emituje
jedną niezmienną `HealthChange` dla danej śmierci; zawiera ona oryginalne
`LifeId` i `SessionId`. Nawet reset z obserwatora nie zastępuje tokenów starego
powiadomienia. Przyszły konsument postępu ma użyć tego zapisanego `SessionId`
w `TryComplete`, nigdy aktualnego tokenu pobranego ze starego callbacka.

W scenie poziomu wszystkie postacie muszą wskazywać ten sam scene-owned
`LevelProgressionController` przez `CombatHealth.progression`, przypisany przed
włączeniem komponentu. Bez niego arena techniczna używa pustego identyfikatora
sesji, nadal z oddzielnymi tokenami życia. Postacie z różnych sesji nie mogą
zadawać sobie obrażeń. `SessionReset` odtwarza zdrowie i tworzy nowy token życia;
obserwator nie zmienia synchronicznie postępu. Wyłączenie odpina subskrypcję,
a ponowne włączenie synchronizuje pominięty reset sesji.

Przerwanie/wyłączenie ataku, utrata fokusu gracza, pauza, blokada sterowania,
wyłączenie `PlayerMovement` i śmierć anulują oczekujące trafienie. Przerwanie
nie skraca pozostałego odstępu; jawny reset zdrowia rozpoczyna nowe życie
i czyści starą akcję. Śmierć gracza blokuje jego `PlayerMovement`. Po przywróceniu
zdrowia mostek oddaje sterowanie tylko wtedy, gdy sam zablokował wcześniej
włączony kontroler. Ponowne włączenie mostka uwzględnia zmiany zdrowia z czasu,
gdy był wyłączony. Pozycja, kamera, bramy i pełny restart gry należą do #18;
`ResetHealth()` odtwarza tylko mechaniki zdrowia/ataku.

`CombatFeedback` używa bloków właściwości materiału: przygotowanie jest
pomarańczowe, aktywne zagrożenie jasnoczerwone, trafienie chwilowo czerwone,
a pokonana postać szara. Nie tworzy kopii materiałów i przy wyłączeniu odtwarza
pierwotne bloki. Jest to czytelna oprawa zastępcza, nie finalna animacja.

## Arena i weryfikacja

Jawne **Shadows → Demos → Create Combat Arena** lub batch
`-executeMethod CombatDemoBuilder.BuildForBatch` zapisuje
`Assets/Combat/Demo/CombatDemo.unity` przez prawdziwe API edytora. Nie zmienia
`SampleScene`, blockoutu ani Build Settings. Arena zawiera gracza, nieruchomy
cel, słup do prób zasłonięcia, kamerę, oświetlenie i panel zdrowia/ataków.
Przycisk „Target strikes” uruchamia zwykły atak celu, bez AI; „Reset arena”
odtwarza sesję i pozycję gracza wyłącznie w tym technicznym przykładzie.

```sh
dotnet test tests/Combat/Combat.Tests.csproj --configuration Release
```

Projekt kompiluje produkcyjny rdzeń `CombatState.cs` jako .NET Standard 2.1
i uruchamia 31 wspólnych przypadków NUnit z `CombatStateTests`. Obejmują
zdrowie, śmierć, tokeny, okna, odstęp, duży krok, duplikaty, przerwanie,
reset i 30/60/120 FPS. Nie symulują fizyki Unity.

`CombatPhysicsTests` zawiera 24 przypadki prawdziwego PlayMode: zasięg i łuk,
ściana i punkt wewnątrz ściany, izolacja lokalnej sceny fizyki, wiele colliderów,
własna postać, czas ataku, śmierć, stare tokeny, reset sesji, sterowanie LPM,
przytrzymanie, przerwanie, blokady i cykl życia mostka. Testowe urządzenia są
kierowane do aktualizacji gracza w batch editorze, bez zmieniania produkcyjnej
polityki fokusu. Dodanie tych testów nie jest deklaracją ich wykonania.
Rzeczywiste wyniki Unity i ręczny odbiór prezentacji należy raportować osobno
od testów rdzenia; arena nie dowodzi ukończenia poziomu ani celu 2–3 minut.

Zapisane sceny i komponenty sprawdzono w rzeczywistym Windows Unity 6000.6.3f1; [raport lokalnej walidacji](validation/shared-gameplay-2026-09-30.md) zawiera wyniki, obrazy oraz niewykonane punkty odbioru.
