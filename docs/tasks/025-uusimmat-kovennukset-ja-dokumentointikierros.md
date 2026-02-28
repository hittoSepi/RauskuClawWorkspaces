# Task 25: Uusimmat kovennukset ja dokumentointikierros

Status: Completed  
Last verified against code: 2026-02-28

## Tavoite

Koostaa viimeisin hardening-vaihe sekä siihen liittynyt dokumentaation päivityskierros.

## Commitit

- `ca991f8` Refactor wizard startup fallback, workspace settings UI, and Holvi connectivity
- `4c3d7b3` Security hardening wave 1: path policy, ssh host key tofu, secret resilience, startup reason codes
- `5c31217` Docs: add AGENTS.md, add Task 18, and refresh README/INDEX

## Mitä tehtiin

- Refaktoroitiin startup fallback -päätöksentekoa.
- Kovennettiin host-path policyä, SSH host key TOFU -hallintaa ja secret-resilienssiä.
- Lisättiin startupin reason code -diagnostiikkaa.
- Synkattiin README/docs/tasks uuden toteutuksen mukaiseksi.

## Vaikutus

- Parempi diagnosoitavuus startup-failure -tilanteissa.
- Pienempi riski väärään host-polkuun tai host key drift -tilanteisiin.
- Dokumentaatio seuraa tuoreinta toteutusta.
