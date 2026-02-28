# Task 20: Secrets, asetukset ja porttiallokaatio

Status: Completed  
Last verified against code: 2026-02-28

## Tavoite

Yhtenäistää salaisuuksien käsittelyä sekä porttiallokoinnin lähdearvoja, jotta wizard/runtime-käynnistys toimii samalla logiikalla.

## Commitit

- `94489ca` Secure settings secrets with DPAPI and migration
- `678d249` Harden secret resolution across wizard and runtime startup
- `dbc92a3` Use settings ports as allocator source of truth
- `370cf89` Fix wizard env preflight before docker compose startup
- `bdf1d7d` Fix brittle quote matching in provisioning preflight test

## Mitä tehtiin

- Secret-tallennus kovennettiin DPAPI-pohjaiseksi ja migraatiopolku varmistettiin.
- Secret resolution -polku yhtenäistettiin wizardin ja runtime-startin välillä.
- PortAllocator sidottiin asetusten aloitusportteihin ensisijaisena lähteenä.
- Provisioning preflightin env-tarkistusta korjattiin.

## Vaikutus

- Vähemmän ympäristökohtaisia startup-virheitä.
- Porttien deterministisyys parani useiden workspacejen tilanteessa.
- Salaisuuksien käsittely on robustimpi sekä käyttöönottovaiheessa että ajossa.
