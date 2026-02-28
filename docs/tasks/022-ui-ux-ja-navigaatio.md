# Task 22: UI/UX ja navigaatio

Status: Completed  
Last verified against code: 2026-02-28

## Tavoite

Parantaa löydettävyyttä, luettavuutta ja responsiivisuutta erityisesti workspace- ja asetusnäkymissä.

## Commitit

- `095a058` Move templates/settings from VM tabs to sidebar general section
- `e147476` Improve main window UX discoverability and empty state
- `1295045` Stretch workspace tab content to use full VM view width
- `1a6670d` Add workspace-specific settings tab and view model
- `79df740` Add workspace-specific stretched tab header styles
- `b2195f6` Stretch workspace tab header row across full content width
- `e01218f` Improve workspace tab overflow behavior and header readability
- `c887390` Fix dark theme ComboBox dropdown readability
- `a39d4ef` Style template management filter and list for dark theme

## Mitä tehtiin

- Siirrettiin asetus- ja template-polkuja loogisempaan navigaatiorakenteeseen.
- Parannettiin empty-state- ja discoverability-kokemusta.
- Korjattiin tabien venytys/overflow ja header-luettavuus.
- Tehtiin dark theme -luettavuuskorjauksia (ComboBox/listat/filtterit).

## Vaikutus

- Vähemmän klikkauksia yleisiin asetustoimintoihin.
- Parempi käytettävyys pienillä ja leveillä ikkunoilla.
- Selkeämpi luettavuus tummassa teemassa.
