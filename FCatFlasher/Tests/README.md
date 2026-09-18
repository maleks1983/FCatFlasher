# Regression checks

Run on Windows with the .NET 8 SDK. Use a separate output directory so the checks can run while the normal application is open:

```powershell
dotnet build Tests/FCatFlasher.Tests.csproj --output Tests/bin/regression
dotnet Tests/bin/regression/FCatFlasher.Tests.dll
```

The console runner uses the WPF dispatcher, fake serial/firmware services, an in-memory HTTP handler, and isolated temporary SQLite databases. It never connects to a board or a server and does not modify operator settings or the production database.

Hardware verification still needs Stop during detection, diagnostics, serial input and flashing, followed by a fresh run in each operating mode.

On this machine, the installed .NET 8 reference packs are 8.0.28. The checks were built with `-p:RuntimeFrameworkVersion=8.0.28 -p:TargetLatestRuntimePatch=false` and packages restored from the existing local NuGet cache.
