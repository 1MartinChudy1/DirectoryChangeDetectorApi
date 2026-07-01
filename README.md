# Directory Change Detector API

Jednoduchá ASP.NET Core REST API aplikace pro ruční detekci změn v lokálním adresáři.

## Spuštění

```bash
dotnet run
```

Swagger UI je dostupné na `/swagger`. Root `/` na něj přesměruje.

## Endpoint

```http
POST /api/directory-analysis/analyze?path=/absolute/or/relative/path
```

První spuštění pro danou cestu vytvoří baseline snapshot. Každé další spuštění porovná aktuální stav adresáře s posledním uloženým snapshotem a vrátí:

- nové soubory a podadresáře,
- změněné soubory,
- odstraněné soubory a podadresáře,
- aktuální verze všech existujících souborů.

Verze souboru začíná na `1`. Při detekované změně obsahu se navýší o `1`. Nový soubor začíná opět verzí `1`.

## Chybové odpovědi

API vrací `ProblemDetails` se stručným popisem problému:

- `400 Bad Request`: prázdná/nevalidní cesta nebo cesta ukazuje na soubor místo adresáře,
- `404 Not Found`: adresář neexistuje,
- `403 Forbidden`: aplikace nemá právo adresář nebo soubor přečíst,
- `409 Conflict`: jiná analýza právě běží; počkejte na její dokončení a spusťte request znovu,
- `500 Internal Server Error`: IO chyba při čtení adresáře/souboru nebo při ukládání snapshotu.

## Perzistence

Data jsou uložena bez databáze do JSON souboru:

```text
.data/snapshots.json
```

Soubor je lokální pro běžící instanci aplikace a není verzovaný v gitu.

## Princip porovnání

Adresář se prochází rekurzivně. Relativní cesty podadresářů slouží pro detekci přidání a odstranění adresářů. Obsah souborů se porovnává přes SHA-256 hash, ne přes čas poslední změny.

## Ošetřená úskalí

- Neplatná nebo prázdná cesta je odmítnuta jako validační chyba.
- Cesta na existující soubor je odmítnuta, protože endpoint očekává adresář.
- Souběžné spuštění analýzy v jedné instanci aplikace není povoleno; druhý request dostane `409 Conflict`.
- Opakovaný běh bez změn nevrací falešné změny a zachová verze.
- Změna obsahu souboru se stejnou velikostí je detekována přes SHA-256 hash.
- Odstranění složky s obsahem reportuje odstraněnou složku i soubory pod ní.
- Přejmenování souboru je vyhodnoceno jako odstranění původní cesty a přidání nové cesty.
- Nahrazení souboru adresářem na stejné relativní cestě je reportováno jako odstraněný soubor a nový adresář.

## Zbývající omezení

- API analyzuje jen lokální filesystem serveru, na kterém běží aplikace.
- Automatické sledování filesystemu není implementováno; analýza se spouští pouze ručním zavoláním endpointu.
- Stav souběhu je chráněn jen v rámci jedné běžící instance aplikace.
- Stav není sdílený mezi více instancemi aplikace.
- Pokud se během analýzy mění analyzovaný adresář, výsledek odpovídá stavu, který se podařilo přečíst během průchodu.
- Symlinky a hardlinky nemají speciální zacházení; aplikace používá standardní .NET enumeraci filesystemu.
- Chyba při přečtení libovolného souboru ukončí celou analýzu, aby nevznikl částečný a matoucí snapshot.
- Očekávané limity zadání jsou soubory do 50 MB a nejvýše 100 položek v jednom adresáři.

## Poznámka k tvorbě

Při tvorbě řešení byl použit AI asistent.
