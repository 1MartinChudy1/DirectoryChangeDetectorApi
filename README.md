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

## Perzistence

Data jsou uložena bez databáze do JSON souboru:

```text
.data/snapshots.json
```

Soubor je lokální pro běžící instanci aplikace a není verzovaný v gitu.

## Princip porovnání

Adresář se prochází rekurzivně. Relativní cesty podadresářů slouží pro detekci přidání a odstranění adresářů. Obsah souborů se porovnává přes SHA-256 hash, ne přes čas poslední změny.

## Omezení

- API analyzuje jen lokální filesystem serveru, na kterém běží aplikace.
- Automatické sledování filesystemu není implementováno; analýza se spouští pouze ručním zavoláním endpointu.
- Při současných requestech je analýza serializovaná v rámci jedné běžící instance aplikace.
- Stav není sdílený mezi více instancemi aplikace.
- Pokud se během analýzy mění analyzovaný adresář, výsledek odpovídá stavu, který se podařilo přečíst během průchodu.
- Očekávané limity zadání jsou soubory do 50 MB a nejvýše 100 položek v jednom adresáři.

## Poznámka k tvorbě

Při tvorbě řešení byl použit AI asistent.
