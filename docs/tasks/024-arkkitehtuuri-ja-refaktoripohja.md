# Task 24: Arkkitehtuuri- ja refaktoripohja

Status: Completed  
Last verified against code: 2026-02-28

## Tavoite

Parantaa ylläpidettävyyttä pilkkomalla ViewModel- ja service-vastuita selkeämpiin osiin.

## Commitit

- `c726aea` Refactor viewmodels with startup/warmup and provisioning services
- `e035b29` Fix refactor regressions and harden service abstractions
- `43b91e7` Resolve merge conflicts with main after ViewModel folder refactor
- `2de960e` Fix MainViewModel constructor merge conflict and dependency wiring
- `2b13763` Continue ViewModel split into partial files for maintainability

## Mitä tehtiin

- Pilkottiin monoliittisia ViewModel-rakenteita osiin (partialit / palvelurajat).
- Korjattiin refaktoroinnin regressioita ja dependency wiring -ongelmia.
- Kovennettiin palvelu-abstraktioiden käyttöä.

## Vaikutus

- Koodi on helpompi testata ja ylläpitää.
- Startup/warmup/provisioning-vastuut ovat selkeämmin eriytetty.
