# Task 19: VM start/wizard vakaus ja retry-polut

Status: Completed  
Last verified against code: 2026-02-28

## Tavoite

Parantaa wizard-käynnistyksen ennustettavuutta virhetilanteissa niin, että käyttäjä voi jatkaa turvallisesti ilman workspace-identiteetin tai välitilan rikkoutumista.

## Commitit

- `259885b` Preserve workspace identity when retrying wizard start
- `aeec069` Pause wizard startup when provisioning credentials are missing
- `9eef750` Add provisioning secrets pause dialog with settings navigation and retry flow
- `9d4b99a` Clean up workspace artifacts when wizard startup fails
- `5075603` Integrate wizard secret loading with remote fallback and tests
- `3b350dd` Simplify warmup retry cleanup without expanding service API

## Mitä tehtiin

- Retry-polkujen tilanhallintaa korjattiin niin, että workspace-identiteetti säilyy uusintayrityksissä.
- Käynnistys keskeytetään hallitusti, jos provisioning-salaisuudet puuttuvat, ja käyttäjä ohjataan asetuksiin.
- Epäonnistuneiden starttien artefaktit siivotaan, jotta uusi yritys alkaa puhtaasta tilasta.
- Warmup/retry-cleanupin toteutusta yksinkertaistettiin ilman API-laajennusta.

## Vaikutus

- Vähemmän rikkinäisiä välitiloja epäonnistuneen startin jälkeen.
- Selkeämpi käyttäjäpolku "puuttuvat salaisuudet" -tapauksessa.
- Parempi toistettavuus startupin retry-skenaarioissa.
