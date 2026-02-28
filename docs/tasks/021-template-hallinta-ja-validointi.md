# Task 21: Template-hallinta ja validointi

Status: Completed  
Last verified against code: 2026-02-28

## Tavoite

Nostaa template-työnkulku tuotantokelpoiseksi: CRUD, import/export, preview, validointi ja wizard-yhteensopivuus.

## Commitit

- `b3ac771` Add custom template CRUD and unified template source handling
- `0c19339` Fix template JSON parsing for camelCase properties
- `187ad3c` Add template import validation preview and wizard template preview
- `9dfddea` Add template management view with validation and import/export
- `fc1f860` Move template management under general settings and fix host path binding mode
- `e6bf180` Add dedicated Templates section to main navigation
- `ee51e6e` Use wizard-aligned CPU and memory dropdowns in template management
- `d9848af` Improve template ports/services hints and validation UX
- `7403a56` Fix template dropdown theming and populate CPU/memory options
- `de1901c` Improve template details pane text contrast
- `38d5313` Refine template details form layout and labeling

## Mitä tehtiin

- Lisättiin custom template CRUD sekä yhtenäinen template-lähteiden käsittely.
- Tuotiin import/export ja preview/validointi osaksi template-hallintaa.
- Korjattiin parsing-yhteensopivuutta (camelCase JSON).
- Yhtenäistettiin wizardin ja template-hallinnan CPU/RAM-valinnat.
- Parannettiin UX:ää: kontrasti, layout, vihjetekstit ja dropdown-käytös.

## Vaikutus

- Templatejen hallinta on käytännössä end-to-end käytettävä.
- Validaatio estää virheellisten templatejen ajautumisen runtimeen.
- Hallintanäkymä ja wizard puhuvat samaa "resurssikieltä".
