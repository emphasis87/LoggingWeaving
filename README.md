# LoggingWeaving

A minimal .NET console application demonstrating the `Microsoft.Extensions.Logging`
API with Serilog as the logging provider. Log events are written to `example.log`.

## Run

```powershell
dotnet run
```

The command creates or appends to `example.log` in the current working directory.
The example writes debug, information, warning, and error events, including an
exception and structured properties such as `ItemNumber`.
