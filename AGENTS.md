# AGENTS.md

## Perus käyttäytyminen (repository-wide)

- Pidä muutokset pieninä, kohdennettuina ja helposti reviewattavina.
- Varmista aina ennen commitia vähintään:
  - `git status --short`
  - relevantit dokumenttimuutokset, jos commitit muuttavat käyttäytymistä tai ominaisuuksia.
- Älä tee oletuksia "valmiista" dokumentaatiosta, vaan täsmäytä docs lähimpään toteutettuun koodiin.

## README.md kirjaaminen

Päivitä `README.md`, kun jokin näistä toteutuu:
- käyttäjälle näkyvä toiminnallisuus muuttuu,
- runtime/provisioning/hardening-käyttäytyminen muuttuu,
- quality gate / testauspolku muuttuu.

Minimikirjaus:
- lyhyt bullet "mitä muuttui",
- tarvittaessa "Recent changes" -kohta viimeisimpien committien perusteella.

## docs/tasks kirjaaminen

Kun kehitys etenee, pidä `docs/tasks` ajan tasalla:
- Lisää uusi task-dokumentti, jos muutos on oma selkeä kokonaisuus.
- Päivitä `docs/tasks/INDEX.md` aina kun task-lista muuttuu.
- Jos vanhan taskin status muuttuu, päivitä status + puuttuvat kohdat selkeästi.

## Committeihin perustuva dokumentointirutiini

Kun käyttäjä pyytää dokumentit ajan tasalle:
1. Tarkista viimeisimmät commitit (`git log --oneline -n <N>`).
2. Poimi käyttäjälle merkittävät muutokset (ei pelkkiä sisäisiä refaktoreita elleivät vaikuta käytökseen).
3. Päivitä vähintään:
   - `README.md`
   - `docs/tasks/INDEX.md` ja/tai uusi task-tiedosto.

## Kieli

- Käytä ensisijaisesti suomea, jos käyttäjä kirjoittaa suomeksi.
- Pidä teksti tiiviinä ja käytännönläheisenä.
