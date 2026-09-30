# cqafabric_ArcGISPro

CQA Fabric for ArcGIS Pro is an ArcGIS Pro 3.7 port of the CQA Fabric QGIS
plugin.

Repository: [hchen-cqa/cqafabric_ArcGISPro](https://github.com/hchen-cqa/cqafabric_ArcGISPro)

## Implemented

- ArcGIS Pro ribbon tab and dock pane
- Session-only API key handling (the key is never written to disk)
- API-key validation
- Fabric vintage and layer discovery
- Reusable asynchronous CostQuest API client
- API operations ready for upcoming UI workflows: field metadata, individual data,
  geocoding, matching, locating, and identifier translation

## Requirements

- ArcGIS Pro 3.7
- .NET 10 SDK (required by ArcGIS Pro 3.7)
- Visual Studio 2022 with the .NET desktop development workload (recommended)

## Build

Run `build.ps1`. The add-in is written to `artifacts\CqaFabricPro.esriAddInX`.
The project references the ArcGIS Pro assemblies from the installed Pro directory.

## Install

Double-click `artifacts\CqaFabricPro.esriAddInX`, accept the ArcGIS Pro add-in
installation prompt, restart ArcGIS Pro, and open the **CQA Fabric** ribbon tab.

## Next porting phase

1. Add authenticated Fabric vector-tile layers.
2. Add identify/retrieve map tools.
3. Add locate and match forms with map output.
4. Add chunked CSV batch tools and export.
