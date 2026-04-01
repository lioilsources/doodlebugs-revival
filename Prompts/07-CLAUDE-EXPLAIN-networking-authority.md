Server vs Owner Authoritative - jednoduše

  Problém: Kdo má "pravdu" o pozici objektu?

  V multiplayeru má každý hráč svoji kopii hry. Když se letadlo pohne, musí se rozhodnout - kdo určuje pravdu?

  ---
  Server Authoritative (NetworkTransform)

  Klient stiskne šipku
         ↓
  Server: "OK, posunu tě"
         ↓
  Server pošle novou pozici všem
         ↓
  Všichni vidí pohyb

  Výhoda: Server má kontrolu, těžší cheaty
  Nevýhoda: Zpoždění - hráč stiskne tlačítko a čeká na server

  ---
  Owner Authoritative (ClientNetworkTransform)

  Klient stiskne šipku
         ↓
  Klient: "Posunu SE sám"
         ↓
  Klient pošle novou pozici serveru a ostatním
         ↓
  Všichni vidí pohyb

  Výhoda: Okamžitá odezva - žádné čekání
  Nevýhoda: Hráč může cheaty

  ---
  V naší hře:

  | Co             | Kdo rozhoduje | Proč                           |
  |----------------|---------------|--------------------------------|
  | Pohyb letadla  | Owner (hráč)  | Okamžitá odezva na input       |
  | Kolize         | Server        | Spravedlivé rozhodnutí         |
  | Respawn pozice | Owner         | Protože má autoritu nad pozicí |

  ---
  Chyba kterou jsme řešili:

  Dříve: NetworkTransform (server authoritative)
         → Klient nemohl teleportovat SVOJE letadlo
         → Chyba: "Teleporting on non-authoritative side!"

  Teď:   ClientNetworkTransform (owner authoritative)
         → Klient MÁ autoritu nad svým letadlem
         → Může teleportovat = respawn funguje

  ---
  Jednoduché pravidlo:

  - IsOwner = "Jsem majitel tohoto objektu?" → Můžu ho ovládat
  - IsServer = "Jsem server/host?" → Rozhoduji o důležitých věcech (kolize, skóre)