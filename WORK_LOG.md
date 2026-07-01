# Work Log

## 2026-07-01

- Created ASP.NET Core Web API project targeting `net10.0`.
- Added Swagger UI through `Swashbuckle.AspNetCore`.
- Removed template weather forecast sample files.
- Implemented manual directory analysis endpoint: `POST /api/directory-analysis/analyze?path=...`.
- Implemented JSON file persistence in `.data/snapshots.json` without a database.
- Implemented recursive snapshot comparison using SHA-256 file content hashes.
- Added file version tracking: new files start at version 1, changed files increment by 1, removed files report their last known version.
- Added xUnit test project covering initial baseline and subsequent new/changed/removed detection.
- Updated the HTTP scratch file to target the real analysis endpoint and mapped `/` to Swagger UI.
- Configured API JSON serialization to return enum values as strings for more readable REST responses.
- Removed template HTTPS redirection middleware to avoid local HTTP-only warning noise during manual testing.
