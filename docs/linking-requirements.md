# Wymagania: łączenie kont z członkami sojuszu

## 1. Pojęcia

- **Członek sojuszu (`Player`)**: osoba na liście sojuszu, zarządzana przez liderów. Nie musi mieć konta w aplikacji.
- **Użytkownik (`User`)**: zarejestrowane konto w aplikacji.
- **Członkostwo (`AllianceMember`)**: przynależność konta do sojuszu wraz z rolą (`Owner`, `Leader`, `Member`).
- **Powiązanie**: przypisanie konta użytkownika do konkretnego członka w danym sojuszu.

## 2. Zarządzanie członkami (bez kont)

- **R1.** Owner i Leader mogą dodawać, edytować, dezaktywować i usuwać członków bez względu na to, czy członek ma powiązane konto.
- **R2.** Nagrody, aktywność i statystyki są zawsze przypisane do członka (`Player`), nigdy bezpośrednio do konta. Dzięki temu historia przetrwa podłączenie i odłączenie konta.
- **R3.** Lista członków pokazuje przy każdym, czy ma powiązane konto (tak/nie, nazwa użytkownika).
- **R4.** Tylko Owner i Leader mogą zmieniać dane członka (nazwę, aktywność, status). Użytkownik powiązany z członkiem nie może ich edytować, w tym zmienić nazwy.

## 3. Model powiązania

- **R5.** Członek może mieć najwyżej jedno powiązane konto (`Player.UserId`, pole opcjonalne).
- **R6.** Jedno konto może być powiązane z najwyżej jednym członkiem w danym sojuszu. W różnych sojuszach może mieć różnych członków. Wymaga to unikalnego indeksu `(AllianceId, UserId)` na `Player`, z filtrem na wartości różne od null.
- **R7.** Powiązanie z członkiem automatycznie tworzy członkostwo konta w sojuszu (`AllianceMember` z rolą `Member`), jeśli konto jeszcze go nie ma. Jeśli konto jest już członkiem sojuszu, jego rola się nie zmienia.
- **R8.** Owner i Leader również mogą powiązać swoje konto z własnym członkiem.

## 4. Sposoby łączenia

### A. Kod powiązania

- **R9.** Owner lub Leader generuje jednorazowy kod (np. `K7QX-92PM`) dla wybranego członka, który nie ma powiązanego konta.
- **R10.** Kod jest ważny przez ograniczony czas (domyślnie 7 dni) i wygasa po pierwszym użyciu. Wygenerowanie nowego kodu dla tego samego członka unieważnia poprzedni.
- **R11.** Zalogowany użytkownik wpisuje kod (`POST /api/players/claim`), a system tworzy powiązanie zgodnie z R5–R7.
- **R12.** Kod jest odrzucany, jeśli:
  - wygasł albo został już użyty,
  - członek ma już powiązane konto,
  - użytkownik jest już powiązany z innym członkiem w tym sojuszu.

### B. Bezpośrednie powiązanie przez lidera (zaproszenie)

- **R13.** Owner lub Leader może powiązać członka z zarejestrowanym kontem, wskazując je po adresie e-mail lub nazwie użytkownika.
- **R14.** Zaproszenie zawsze wskazuje członka (`playerId` jest wymagane). Obecny endpoint `POST /api/alliances/{id}/invite` bez `playerId` zostaje usunięty: każde konto dołączające do sojuszu musi mieć przypisanego członka.
- **R15.** Przy zaproszeniu Owner może nadać rolę `Leader` albo `Member`, tak jak dziś.

### C. Wniosek użytkownika

- **R16.** Zalogowany użytkownik może złożyć wniosek o powiązanie: wskazuje sojusz i członka („to ja”). Dopisuje opcjonalną wiadomość dla lidera.
- **R17.** Użytkownik wyszukuje sojusz po nazwie. Przed złożeniem wniosku widzi wyłącznie listę nazw członków bez powiązanego konta i nic więcej z danych sojuszu.
- **R18.** Owner i Leader widzą listę oczekujących wniosków i mogą każdy zaakceptować albo odrzucić. Akceptacja tworzy powiązanie zgodnie z R5–R7.
- **R19.** Użytkownik może mieć najwyżej jeden oczekujący wniosek na sojusz i może go wycofać.
- **R20.** Wniosek jest automatycznie odrzucany, jeśli w międzyczasie członek dostał powiązanie innym sposobem albo został usunięty.
- **R21.** Użytkownik widzi status swoich wniosków: oczekujący, zaakceptowany lub odrzucony.

## 5. Odłączanie

- **R22.** Owner lub Leader może odłączyć konto od członka. Członek i jego historia pozostają bez zmian.
- **R23.** Usunięcie konta z sojuszu odłącza je od członka.
- **R24.** Usunięcie członka, który ma powiązane konto, wymaga jawnego potwierdzenia. Usuwa wtedy również członkostwo tego konta w sojuszu, chyba że konto jest Ownerem. Wynika to z R14: konto w sojuszu musi mieć członka.
- **R25.** Konta Ownera nie można odłączyć od sojuszu (można jedynie odłączyć je od członka).

## 6. Uprawnienia i widoczność

- **R26.** Generowanie kodów, zaproszenia, obsługa wniosków oraz łączenie i odłączanie kont: Owner i Leader. Zmiana ról: tylko Owner.
- **R27.** Użytkownik z rolą `Member` widzi wyłącznie:
  - swoje sojusze oraz członka, z którym jest powiązany,
  - nagrody przypisane do tego członka.

  Nie widzi innych członków, wydarzeń, statystyk ani rekomendacji.
- **R28.** Uprawnienia `Member` muszą być rozszerzalne, bo w przyszłości dojdą kolejne funkcje (np. ranking, wydarzenia). Sprawdzanie dostępu powinno odbywać się per funkcja w `AllianceAccessService`, a nie przez jeden warunek „rola różna od `Member`”.
- **R29.** Każde powiązanie i odłączenie trafia do logu: kto, kiedy, którego członka i którego konta dotyczy, oraz jaką metodą (kod, zaproszenie, wniosek).

## 7. Wymagania nietechniczne

- **R30.** Endpoint `claim` oraz składanie wniosków podlegają rate limitingowi, żeby utrudnić zgadywanie kodów i spam.
- **R31.** Kody są generowane kryptograficznie bezpiecznym generatorem i mają co najmniej 40 bitów entropii. W bazie przechowywany jest wyłącznie hash kodu.
- **R32.** Migracja nie zmienia istniejących danych: wszyscy obecni członkowie startują bez powiązanego konta. Istniejące członkostwa bez członka (np. obecni liderzy) pozostają ważne do czasu powiązania. Wymóg z R14 dotyczy tylko nowych dołączeń.

## 8. Poza zakresem (na później)

- Dodatkowe funkcje widoczne dla `Member` (ranking, wydarzenia itp.), patrz R28.
- Powiadomienia (e-mail, w aplikacji) o nowych wnioskach i ich rozpatrzeniu.
