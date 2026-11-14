# Per-engine API notes (as implemented in `Booru/Clients.cs`)

Common: every request carries `User-Agent: kurotsuki/<version> (desktop booru client; +https://github.com/FranCasado207/Kurotsuki)`.
`IBooruClient` pages are **0-based**; clients convert. `limit` is 100 per page from the UI. Null query values are omitted from the URL; empty strings are sent.
HTTP status ≥ 400 → `HttpRequestException("HTTP <code> from <host>")`.

| Engine | Search endpoint | Paging | Auth | Notes |
|---|---|---|---|---|
| Gelbooru family | `GET /index.php?page=dapi&s=post&q=index&json=1&tags&limit&pid` | `pid` = page (0-based) | `api_key`, `user_id` query | Response is either an array or `{"@attributes":…,"post":[…]}`. Fields are `preview_url/file_url/sample_url` (snake_case). Tags endpoint (`s=tag`, `name_pattern=<prefix>%`) may answer XML or JSON. Safebooru works anonymously. |
| Danbooru | `GET /posts.json?tags&page&limit` | `page` 1-based | `login` + `api_key` query | Rating letters g/s/q/e → general/sensitive/questionable/explicit. Per-category tag strings give `TagCategories`. Anonymous request returned **503** in one live test. |
| e621 | `GET /posts.json?tags&page&limit` | `page` 1-based | HTTP Basic `login:apikey` | Nested JSON (`file`, `preview`, `sample`, `score.total`, `tags{category:[…]}`, `sources[]`, `pools[]`). `preview.url` can be null (blocked posts). Needs a descriptive UA. |
| Moebooru (yande.re, Konachan) | `GET /post.json?tags&page&limit` | `page` 1-based | none | Tag suggest: `/tag.json?name=<prefix>*&order=count`. Ratings s/q/e. Pools: `/pool.json`, `/pool/show.json?id=` (not ported). |
| Shimmie | `GET /api/danbooru/find_posts?tags&page&limit` (XML) | `page` 1-based | none | No tag suggest. Attributes on `<post>` elements. |
| Szurubooru | `GET /api/posts/?query&offset&limit` | `offset` = page × limit | `Authorization: Token base64(user:token)` | `safety` safe/sketchy/unsafe → general/questionable/explicit. `tags` may be strings (old) or objects with `names` (new) — both handled. |
| Hydrus | `GET /get_files/search_files?tags=<json>` then `/get_files/file_metadata?file_ids=<json>` | client-side slice of an id list; ids fetched in batches of 1000 and the last 4 queries cached | header `Hydrus-Client-API-Access-Key` | Key is never put in URLs (they’re persisted and used as cache keys): `RequestAuth` adds the header for media requests under the booru’s base URL. Tags come from *local tag services* (service type 5) `display_tags`; `creator:`/`series:`/`character:` prefixes map to categories. No tag suggest. |

Ratings are normalised to words (`general|sensitive|questionable|explicit`) at the client edge; the Java app does the same.
Referer for media: the booru’s own site when the URL’s host is a known template domain, else the URL’s origin (`BooruHttp.RefererFor`) — some CDNs reject hotlinks.
