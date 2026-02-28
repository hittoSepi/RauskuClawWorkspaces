# Holvi Infra (Infisical + Secret Proxy)

Tama kansio sisaltaa Holvi-stackin, jolla OpenAI-avain haetaan Infisicalista ajonaikaisesti ilman etta avain tallennetaan suoraan appin `.env`:iin.

## Mitä tämä tekee

- Ajaa lokaalisti neljä palvelua:
1. `postgres` (Infisicalin tietokanta)
2. `redis` (Infisicalin cache/queue)
3. `infisical` (salaisuuksien hallinta)
4. `holvi-proxy` (token-suojattu HTTP-proxy, joka hakee salaisuuden Infisicalista aliasin perusteella)

- OpenAI-provider voi käyttää Holvia näin:
1. App kutsuu `holviHttp(...)` (`app/lib/holviClient.js`)
2. Kutsu menee `holvi-proxy`:lle (`POST /v1/proxy/http`)
3. Proxy hakee oikean salaisuuden Infisicalista alias-mappingin mukaan
4. Proxy tekee ulkoisen API-kutsun ja palauttaa statuksen/bodyn appille

## Tiedostot

- `infra/holvi/compose.yml`: Holvi-stackin docker-compose
- `infra/holvi/.env.example`: vaaditut envit
- `infra/holvi/holvi-proxy/server.mjs`: proxyn HTTP-palvelin
- `infra/holvi/holvi-proxy/aliases.json(.example)`: alias -> secretName + allowlist

## Vaatimukset

- Docker + Docker Compose
- Infisical service token ja project/workspace ID
- Taytetty `infra/holvi/.env`

## Kaynnistys

Suorita projektin juuresta:

```bash
cp infra/holvi/.env.example infra/holvi/.env
cp infra/holvi/holvi-proxy/aliases.json.example infra/holvi/holvi-proxy/aliases.json
docker compose -f infra/holvi/compose.yml --env-file infra/holvi/.env up -d --build
docker compose -f infra/holvi/compose.yml ps
```

### Vahvista että palvelut ovat ylhaalla

```bash
curl -sS http://127.0.0.1:8099/health
docker compose -f infra/holvi/compose.yml logs --tail 100 holvi-proxy
docker compose -f infra/holvi/compose.yml logs --tail 100 infisical
```

## Yhteys appiin (OpenAI Holvi mode)

Appin juuressa (`/opt/openclaw/.env`) kayta:

```dotenv
OPENAI_ENABLED=1
OPENAI_SECRET_ALIAS=sec://openai_api_key
# Docker compose -ymparistossa appi on kontissa:
HOLVI_BASE_URL=http://holvi-proxy:8099
HOLVI_PROXY_TOKEN=<sama kuin infra/holvi/.env PROXY_SHARED_TOKEN>
```

Jos ajat appia hostilta ilman Dockeria, silloin `HOLVI_BASE_URL=http://127.0.0.1:8099` on oikein.

### Docker-verkotus (tarkea)

Jotta appi-kontit (`rauskuclaw-api`, `rauskuclaw-worker`) tavoittavat `holvi-proxy`-palvelun nimella `holvi-proxy`,
niiden tulee olla samassa Docker-verkossa (`holvi_holvi_net`).

Varmistus:

```bash
docker ps --format 'table {{.Names}}\t{{.Networks}}'
```

Jos verkotus ei nay oikein, kaynnista appi-kontit uudelleen:

```bash
docker compose up -d --force-recreate rauskuclaw-api rauskuclaw-worker
```

Huom:
- Kun `OPENAI_SECRET_ALIAS` on asetettu, provider menee Holvi-moodiin.
- Jos `OPENAI_SECRET_ALIAS` puuttuu, kaytetaan legacy-moodia (`OPENAI_API_KEY` suoraan envista).

## holvi-proxy API-sopimus

### `GET /health`

Palauttaa:

```json
{ "ok": true }
```

### `POST /v1/proxy/http`

Vaaditut headerit:
- `x-proxy-token: <PROXY_SHARED_TOKEN>`
- `content-type: application/json`

Body:

```json
{
  "secret_alias": "sec://openai_api_key",
  "request": {
    "method": "POST",
    "url": "https://api.openai.com/v1/chat/completions",
    "headers": { "content-type": "application/json" },
    "body": { "model": "gpt-4.1-mini", "messages": [{ "role": "user", "content": "hi" }] }
  }
}
```

Onnistunut vastaus:

```json
{
  "status": 200,
  "headers": { "content-type": "application/json" },
  "body": "{...raw upstream body...}"
}
```

## Turvallisuusmalli (nykytila)

- Jaettu token-auth (`x-proxy-token`)
- Alias-kohtainen allowlist:
  - sallitut hostit
  - sallitut HTTP-metodit
- Header-sanitointi:
  - requestissa vain allowlistatut headerit eteenpain
  - responseista poistetaan herkat headerit (`set-cookie`, jne.)
- Body-kokoraja (`MAX_BODY_BYTES`)
- In-memory token bucket rate limit (`RATE_LIMIT_CAPACITY`, `RATE_LIMIT_REFILL_RATE`)
- Timeoutit:
  - total request timeout (`REQUEST_TIMEOUT_MS`)
- Audit-loki redaktoi tokenit/header-arvot fingerprint-muotoon

## Yleisimmät virheet ja nopea diagnoosi

- `401 unauthorized` proxylta
  - `HOLVI_PROXY_TOKEN` (app) != `PROXY_SHARED_TOKEN` (holvi .env)
- `404 unknown secret_alias`
  - alias puuttuu `infra/holvi/holvi-proxy/aliases.json` tiedostosta
- `403 Host not allowed` tai `403 Method not allowed`
  - aliasin `allow.hosts`/`allow.methods` ei salli pyyntoa
- `502 secret fetch failed`
  - `INFISICAL_SERVICE_TOKEN`, `INFISICAL_PROJECT_ID` tai Infisical endpoint/malli ei vastaa odotettua
- `504 request timeout`
  - ulkoinen API hidas tai timeout-arvot liian pienet
- `PROVIDER_NETWORK` + `fetch failed` appin jobissa
  - appi ei saa yhteytta Holviin; tarkista `HOLVI_BASE_URL` ja Docker-verkotus
- `PROVIDER_NETWORK` + `request timeout` appin jobissa
  - Holvi vastaa, mutta upstream API timeouttaa; nosta `REQUEST_TIMEOUT_MS` tai tarkista upstreamin vaste
- `413 Request body too large`
  - pyynto ylittaa `MAX_BODY_BYTES`

## Rollback (pienin turvallinen palautus)

Jos Holvi-integraatio aiheuttaa tuotanto-ongelman, palauta OpenAI legacy-moodiin:

1. Poista `OPENAI_SECRET_ALIAS` appin `.env`:sta
2. Aseta `OPENAI_API_KEY` suoraan appin `.env`:iin
3. Varmista `OPENAI_ENABLED=1`
4. Kaynnista app-palvelut uudelleen

Tama ohittaa Holvin, mutta palauttaa chatin suoraan OpenAI-kutsuun.

## Tunnetut riskit / TODO

- `holvi-proxy` käyttää tällä hetkellä placeholder-olettamaa Infisical v3 raw endpointista (`/api/v3/secrets/raw`), joka voi vaihdella version mukaan.
- Rate limit on in-memory (ei jaettu usean instanssin valilla).
- Hostilta ulospain ei ole taydennettya mTLS/TLS-terminointia tässä kansiossa; verkon kovennus kannattaa tehdä gateway-/LB-tasolla.
