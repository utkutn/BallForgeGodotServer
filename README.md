# BallForgeGodotServer

Headless authoritative server project for BallForge.

## Run

```text
godot --headless --path . --match_id=3b1a81fa-3ede-436a-943c-115de712eca0
```

The server does not receive a port from the command line. It calls:

```text
http://localhost:8080/api/match/check-session?matchId=<match_id>
```

and listens on the `serverPort` returned by the API. This is the first separation step.
Move authoritative game code here next:

- `Server.cs`
- server-side physics and game state
- shared data models used by the server
- a server-only scene containing the simulation nodes

The mobile client remains in `BallForgeGodot` and connects to this process.
