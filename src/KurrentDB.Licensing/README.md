# Eventstore Licensing

Some features of EventStoreDB require a license key to access.

The license key can be provided to EventStoreDB via environment variable or config file in the usual plugin config location

`EventStore__Licensing__LicenseKey`

```
{
  "EventStore": {
    "Licensing": {
      "LicenseKey": "Your key"
    }
  }
}
```

EventStoreDB will not start if features are enabled that require a licence key but the license is not provided or is invalid.
