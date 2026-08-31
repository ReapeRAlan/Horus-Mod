# Runtime Validation Helpers

These scripts operate only on an explicit isolated official dedicated-server installation. They do not download game files, modify Steam credentials, fabricate Steam identities, or control Horus through the official TCP administration endpoint.

## Install a Horus package

```powershell
./build/runtime/install-dedicated-package.ps1 `
  -ServerRoot ../HorusDedicatedTest/server-win `
  -PackagePath ./dist/Horus-Dedicated-v2.0.0-rc.1.zip
```

Existing `Horus.Server.cfg` and administrator allowlist files are preserved.

## Windows smoke or soak

```powershell
./build/runtime/run-windows-dedicated.ps1 `
  -ServerRoot ../HorusDedicatedTest/server-win `
  -ConfigPath ../HorusDedicatedTest/server-win/DedicatedServerConfig.horus.test.json `
  -DurationMinutes 2
```

Use `-DurationMinutes 240` for the four-hour soak. The script starts the server in a hidden window, samples resource use, stops only the process it created, sanitizes the copied configuration, scans logs, and writes evidence below the isolated server root. It also writes `runtime-status.json` and aborts fail-closed if the server does not become ready within 300 seconds or if the Unity log exceeds 16 MiB. Override those safety limits only for a documented investigation with `-ReadyTimeoutSeconds` and `-MaxLogBytes`.

Run only one official Windows dedicated-server process at a time for certification. Concurrent instances in the same Windows/Steam session can leave the game network manager uninitialized even when different game and query ports are configured.

## Linux/WSL smoke or soak

Inside WSL 2:

```bash
chmod +x build/runtime/run-linux-dedicated.sh
./build/runtime/run-linux-dedicated.sh \
  /opt/horus/server-linux \
  /opt/horus/server-linux/DedicatedServerConfig.horus.test.json \
  2
```

Use duration `240` for the four-hour soak. Install BepInEx 5 Unix and verify `run_bepinex.sh` before running the helper. The Linux helper applies the same 300-second readiness and 16 MiB log limits and records `runtime-status.json`. Controlled investigations may override the limits with `HORUS_READY_TIMEOUT_SECONDS` and `HORUS_MAX_LOG_BYTES`.

These helpers prove startup stability and log cleanliness. Steam authorization, visual GM operations, mission changes, normal-client replication, and the functional mutation matrix still require an actual game client and must be recorded in the release matrix.
