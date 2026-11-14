# Shared-file contract with the JavaFX app

Both apps may run against the same user data. Treat every item below as a public format.

## Locations (`AppPaths`)
- **Linux:** config `$XDG_CONFIG_HOME|~/.config` `/kurotsuki/config.json`; data `$XDG_DATA_HOME|~/.local/share` `/kurotsuki/kurotsuki.db`;
  cache `$XDG_CACHE_HOME|~/.cache` `/kurotsuki/`. Relative XDG values are ignored (spec).
- **Windows/macOS:** everything in `~/kurotsuki`, cache in `~/kurotsuki/.cache` (Java TODO wants `%APPDATA%` etc. — change both apps together).
- `config.cacheFolder` overrides the cache location. Thumbnail file name = lowercase hex SHA-256 of the URL, no extension.

## config.json (`ConfigManager`, camelCase, 2-space indent, nulls omitted)
Gson wrote Java field names, so: `booruConfigurations[]`, `downloadFolder`, `cacheFolder`, `language`, `panelDocks`, `panelSizes`,
`favoriteTags`, `favoritePosts[]`, `postFolders[]`, `blacklistedTags`, `poolArchiveFormat`, `readerMode`, `readerFit`,
`readerRightToLeft`, `readerSpread`, `readerCacheMegabytes`, `readerHonorBlacklist`, `downloadTemplate`, `downloadSidecar`, `redownloadKnown`.
- **Enums** serialise as Java constant names: `JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper)` (`GelbooruFamily` ⇄ `GELBOORU_FAMILY`).
  Renaming a C# enum member changes the file format — don’t. The test `EnumNamesMatchTheJavaOnes` guards three of them; extend it when adding members.
- **Booru entries** are either template-based (`{"template":"GELBOORU","apiKey":..,"userId":..}` — *no* engine field) or custom
  (`{"engine":..,"flavor":..,"customBaseUrl":..}`), or the pre-0.5 legacy form (`booruType`, `login`) which `BooruConfigurationConverter` migrates on read.
  Entries with an unknown template/engine or an invalid URL are dropped on load (as in Java); saving then removes them from the file.
- `userId` is the login/username for Danbooru, e621 and Szurubooru, and the numeric user id for Gelbooru. `apiKey` is the key/token (Hydrus: access key).
- `Post` JSON (in `favoritePosts`, `postFolders[].posts`, and `download_queue.payload`): `id, score, width, height, image, rating, source, tags,
  previewUrl, fileUrl, sampleUrl, booruBaseUrl, poolIds[], tagCategories{tag: "ARTIST"|…}`. Post key = `booruBaseUrl + "#" + id`.
- `[JsonExtensionData] Extra` keeps unknown top-level fields across load/save.
- Writes: temp file `config.json.tmp` created mode 600, then atomic move; config dir set to 700.
- Values the base does not use but must keep: everything in `UserConfiguration` that no ViewModel touches (panels, reader, favourites, folders…).

## Layout keys written by this app
Window and panel sizes are stored in `panelSizes` as `next.window.width`, `next.window.height`, `next.window.maximized` (0/1), `next.sidebar.width`, `next.queue.width`,
`next.sidebar.visible`, `next.queue.visible` (0/1). The Java app loads that whole map and writes it back, and ignores unknown `panelDocks` keys, so these survive it.
Favourites/folders no longer live in config.json: the Java app imports them into the DB once (`meta.migrated_from_json`) and empties the fields; this app does the same if it runs first.

## kurotsuki.db (SQLite)
Session 2: `LibraryStore` now creates and uses the **whole** schema below (identical to Java, `CREATE TABLE IF NOT EXISTS`, `reading_progress` columns added if missing).
`download_queue.kind` is `post` or `pool` (both handled now); unknown kinds are never deleted. Tag category names are stored as enum names (`ARTIST`).
Original text of the two tables this app used in session 1:
```sql
downloaded(item_key TEXT PRIMARY KEY, saved_at INTEGER NOT NULL)            -- item_key = Post.Key (pools use their own key)
download_queue(position INTEGER PRIMARY KEY AUTOINCREMENT, item_key TEXT NOT NULL UNIQUE, kind TEXT NOT NULL, payload TEXT NOT NULL)
```
`kind` is `post` or `pool`. `SaveQueue` rewrites **only `post` rows**, so queued pools from the Java app survive.
Other Java tables (`meta, posts, post_tags, tag_categories, favorite_tags, favorite_posts, reading_progress, tag_groups, tag_group_tags, folders,
folder_posts`) exist only if the Java app created them; schema in Java `storage/LibraryStore.java#createSchema`. When porting library features, copy the
DDL verbatim (`CREATE TABLE IF NOT EXISTS`) and keep `reading_progress`’s `addColumnIfMissing` migrations.

## Not shared / not ported yet
`~/kurotsuki` legacy migration, log archive (`kurotsuki-logs.tar`, `logs/`), backup files (`docs/backup-format.md` in the Java repo).
