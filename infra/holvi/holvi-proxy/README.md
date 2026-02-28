# holvi-proxy

Token-suojattu HTTP-proxy, joka:
- vastaanottaa pyynnon appilta (`POST /v1/proxy/http`)
- hakee oikean salaisuuden Infisicalista aliasin perusteella
- tekee upstream-kutsun ja palauttaa statuksen/bodyn appille

## API

### `GET /health`

Vastaus:

```json
{ "ok": true }
```

### `POST /v1/proxy/http`

Headerit:
- `x-proxy-token: <PROXY_SHARED_TOKEN>`
- `content-type: application/json`

Body:

```json
{
  "secret_alias": "sec://openai_api_key",
  "request": {
    "method": "POST",
    "url": "https://api.z.ai/api/coding/paas/v4/chat/completions",
    "headers": { "content-type": "application/json" },
    "body": {
      "model": "glm-5",
      "messages": [{ "role": "user", "content": "hi" }]
    }
  }
}
```

## Alias registry

Aliasit luetaan tiedostosta `aliases.json`.
Esimerkki:

```json
{
  "sec://openai_api_key": {
    "infisical": { "secretName": "OPENAI_API_KEY" },
    "usage": { "type": "bearer" },
    "allow": {
      "hosts": ["api.z.ai", "api.openai.com"],
      "methods": ["POST", "GET"]
    }
  }
}
```

## Environment

Pakolliset:
- `PROXY_SHARED_TOKEN`
- `INFISICAL_BASE_URL`
- `INFISICAL_SERVICE_TOKEN`
- `INFISICAL_PROJECT_ID`
- `INFISICAL_ENV`

Valinnaiset:
- `PORT` (default `8099`)
- `HOLVI_BIND` (default `0.0.0.0`)
- `MAX_BODY_BYTES` (default `262144`)
- `RATE_LIMIT_CAPACITY` (default `100`)
- `RATE_LIMIT_REFILL_RATE` (default `10`)
- `REQUEST_TIMEOUT_MS` (default `30000`)

## Diagnostiikka

Yleiset virheet:
- `401 unauthorized`: `x-proxy-token` ei vastaa `PROXY_SHARED_TOKEN`
- `404 unknown secret_alias`: alias puuttuu `aliases.json`-tiedostosta
- `403 Host not allowed` / `Method not allowed`: aliasin allowlist blokkaa pyynnon
- `502 secret fetch failed`: Infisical token/project/environment virheellinen
- `504 request timeout`: upstream ei vastaa `REQUEST_TIMEOUT_MS` sisalla

Nopea tarkistus:

```bash
curl -sS http://127.0.0.1:8099/health
docker compose -f infra/holvi/compose.yml logs --tail 100 holvi-proxy
```
