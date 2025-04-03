---
order: 4
---

# Default directories

The default directories used by KurrentDB vary by platform to fit with the common practices each platform.

## Linux

When you install KurrentDB on Linux, the following locations apply:

- **Application:** `/usr/bin`
- **Content:** `/usr/share/kurrentdb`
- **Configuration:** `/etc/kurrentdb/`
- **Data:** `/var/lib/kurrentdb`
- **Server logs:** `/var/log/kurrentdb`
- **Test client logs:** `./testclientlog`
- **Web content:** `./clusternode-web` then `{Content}/clusternode-web`
- **Projections:** `./projections` then `{Content}/projections`
- **Prelude:** `./Prelude` then `{Content}/Prelude`

## Windows

- **Configuration:** `./`
- **Data:** `./data`
- **Server logs:** `./logs`
- **Test client log:** `./testclientlogs`
- **Web content:** `./clusternode-web`
- **Projections:** `./projections`
- **Prelude:** `./Prelude`

## Local binaries

When running KurrentDB using local binaries, either downloaded or built from source, the server will use its location and place the necessary files inside it:

- **Configuration:** `./`
- **Data:** `./data`
- **Server logs:** `./logs`
- **Test client log:** `./testclientlogs`
- **Web content:** `./clusternode-web`
- **Projections:** `./projections`
- **Prelude:** `./Prelude`

Depending on the platform and installation type, the location of KurrentDB executables, configuration and other necessary files vary.
