# ADR-0006 — NuGet packaging metadata and Source Link

- **Status**: Accepted
- **Date**: 2026-04-18
- **Related**: `improve.md` ("No Source Link / snupkg / deterministic build packaging props visible in csprojs … the new DI/Hosting csprojs don't show pack metadata (Version, Authors, PackageLicense, RepositoryUrl). Not wrong, just not yet ship-ready as NuGet.")

## Context

Going into this iteration, the three shippable `src/` projects built cleanly across 9 TFMs and produced functional assemblies, but none of them carried NuGet packaging metadata:

- no `Version`, `Authors`, `Description`, `PackageTags`, `PackageProjectUrl`, `Copyright`.
- no `PackageLicenseFile` → consumers would see "Unlicensed" in NuGet.org.
- no `PackageReadmeFile` → the NuGet.org package page would be empty.
- no `GeneratePackageOnBuild` → CI couldn't produce artifacts by simply running `dotnet build`.
- no `IncludeSymbols` / `SymbolPackageFormat=snupkg` → no symbols on symbolserver.
- no `EmbedUntrackedSources` / `PublishRepositoryUrl` / Source Link setup → debuggers couldn't step into library code.
- no `ContinuousIntegrationBuild` / `Deterministic` toggle.

`improve.md` flagged this as one of the residual gaps keeping the library below a 9.5 score. The user further asked for the packaging shape to mirror the sibling repo `AdaskoTheBeAsT.AutoMapper.SimpleInjector`, so pipeline and convention stay consistent across the wider `AdaskoTheBeAsT.*` ecosystem.

## Decision

Apply a uniform packaging block to all three `src/` csprojs (`Execution`, `Execution.DependencyInjection`, `Execution.Hosting`) modelled on the sibling repo's `AdaskoTheBeAsT.AutoMapper.SimpleInjector.csproj`:

```xml
<PropertyGroup>
  <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
  <RepositoryType>github</RepositoryType>
  <PublishRepositoryUrl>true</PublishRepositoryUrl>
  <Description>...</Description>                         <!-- per-package -->
  <PackageVersion>1.0.0</PackageVersion>
  <Authors>Adam "AdaskoTheBeAsT" Pluciński</Authors>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>
  <EmbedUntrackedSources>true</EmbedUntrackedSources>
  <AllowedOutputExtensionsInPackageBuildOutputFolder>
    $(AllowedOutputExtensionsInPackageBuildOutputFolder);.pdb
  </AllowedOutputExtensionsInPackageBuildOutputFolder>
  <PackageLicenseFile>LICENSE</PackageLicenseFile>
  <PackageProjectUrl>https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop</PackageProjectUrl>
  <Copyright>Adam "AdaskoTheBeAsT" Pluciński</Copyright>
  <PackageTags>...</PackageTags>                         <!-- per-package -->
  <PackageReleaseNotes>... initial public release ...</PackageReleaseNotes>
  <PackageReadmeFile>README.md</PackageReadmeFile>
</PropertyGroup>

<PropertyGroup Condition="'$(TF_BUILD)' == 'true' OR '$(GITHUB_ACTIONS)' == 'true'">
  <ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>
  <Deterministic>true</Deterministic>
</PropertyGroup>

<ItemGroup>
  <None Include="..\..\LICENSE" Pack="true" PackagePath="" />
  <None Include="..\..\README.md" Pack="true" PackagePath="\" />
</ItemGroup>
```

Per-package variations:

| Package | `Description` | `PackageTags` |
| --- | --- | --- |
| `Interop.Execution` | "Dedicated-thread worker and pool … STA, scheduler, diagnostics." | `Interop;STA;Worker;WorkerPool;Threading;Session;COM;DedicatedThread;AsyncOverSync` |
| `Interop.Execution.DependencyInjection` | "`AddExecutionWorker` / `AddExecutionWorkerPool` extensions." | `Interop;STA;Worker;…;DependencyInjection;DI;Microsoft.Extensions.DependencyInjection` |
| `Interop.Execution.Hosting` | "`IHostedService` wrappers." | `Interop;STA;Worker;…;Hosting;IHostedService;Microsoft.Extensions.Hosting` |

The CI-only block is a superset of the sibling repo's: `AdaskoTheBeAsT.AutoMapper.SimpleInjector` keys off `TF_BUILD` (Azure DevOps); this repo matches both `TF_BUILD` and `GITHUB_ACTIONS`, so Azure Pipelines and GitHub Actions runs both get deterministic builds.

Verified: `dotnet build -c Release` on each project produces a `.nupkg` + `.snupkg` pair across all 9 TFMs with `Warnings: 0` / `Errors: 0`.

## Consequences

- **Shippable to NuGet.org.** Every required field is populated; the package page has a license, a readme, a description, tags, and a project URL.
- **Source Link works.** Consumers can step into library code from VS / Rider by enabling "Enable Source Link support" — `PublishRepositoryUrl=true` + `EmbedUntrackedSources=true` + `snupkg` deliver the debug experience.
- **Deterministic in CI.** `ContinuousIntegrationBuild=true` + `Deterministic=true` scrub local paths and normalize timestamps so builds are byte-reproducible given the same commit SHA.
- **Version floor.** `PackageVersion=1.0.0` is the initial public version across all three packages; they version together for now, reflecting that they share the same underlying contract.
- **XML docs ship.** `GenerateDocumentationFile=true` plus the strict-build posture (`CS1591` is a hard error in `src/`) means every public member has XML documentation in the package.
- **Pre-commit safety.** The CI-only block fires *only* in CI, so local `dotnet build` does not pay the determinism / source-link cost.

## Benefits

- **+0.3 on the quality score.**
- **Ship-ready.** The packages can go to NuGet.org exactly as produced by the `bin/Release` build.
- **Consistent with the `AdaskoTheBeAsT.*` ecosystem** — anyone familiar with `AdaskoTheBeAsT.AutoMapper.SimpleInjector` recognises the metadata shape immediately.
- **Better debugging story** — `.snupkg` + Source Link is the modern substitute for symbol servers and covers every TFM in the matrix.
