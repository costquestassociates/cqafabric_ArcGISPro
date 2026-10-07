[![GitHub release](https://img.shields.io/badge/release-v1.0.0-blue)](https://github.com/hchen-cqa/cqafabric_ArcGISPro/releases/latest)

# CQA Fabric Add-in for ArcGIS Pro

CQA Fabric for ArcGIS Pro provides access to CostQuest Fabric APIs directly in
ArcGIS Pro. The add-in is based on the workflows available in the
[CQA Fabric QGIS plugin](https://github.com/costquestassociates/cqafabric_QGIS).

## Requirements

- Windows 10 or 11
- ArcGIS Pro 3.7 or later
- A CostQuest API key with access to the required Fabric vintages and layers

To request an API key, contact [APISupport@costquest.com](mailto:APISupport@costquest.com).

## Installation

1. Download `CqaFabricPro.esriAddInX` from the latest release.
2. Close ArcGIS Pro.
3. Double-click `CqaFabricPro.esriAddInX` and accept the ArcGIS Pro add-in prompt.
4. Reopen ArcGIS Pro.
5. Open the **CQA Fabric** group on the **Add-In** tab, or add its commands to the Quick Access Toolbar.

To upgrade, close ArcGIS Pro and install the newer `.esriAddInX` file over the existing version.

## User authentication

Select **Log In**, enter your CostQuest API key, and optionally select
**Remember API key securely**. Remembered keys are encrypted in Windows
Credential Manager and are not written to the ArcGIS project or layer URL.

The Fabric commands remain disabled until login succeeds. Select **Log Out** to
end the session and remove the saved API key.

## Features
<img width="311" height="99" alt="image" src="https://github.com/user-attachments/assets/3431c5a1-4047-4e5e-a261-557ae77e8bd1" />

### Add Fabric Tiles

Select an available vintage and Fabric layer returned by the API, then add its
authenticated vector tiles to the active map. Adding a layer preserves the current map extent.

### Retrieve Fabric

Use a polygon feature layer in the active map as the **Define Layer**. The tool
can use every polygon or only the selected polygons.

- Select a vintage and any layer available to the API key.
- Select one or more output fields.
- Retrieve Fabric records intersecting the Define Layer.
- Preview the returned attributes.
- Export results to CSV.
- Add retrieved locations, footprints, or parcels to the active map.

The current map extent is preserved when retrieved records are added.

### Identify Fabric

Activate **Identify Fabric**, then click the map to identify Fabric features at
that location. Use the command's drop-down to search the topmost layer, all
visible layers, or Fabric layers selected in the Contents pane. The tool uses
the ArcGIS Pro selection tolerance, so the search area remains consistent on
screen as the map scale changes. It shows one attribute window at a time.
Select the command again to deactivate it.

### Locate Fabric

Locate Fabric locations by coordinate or address. Results can include all
available location attributes, are displayed in a table, and can be exported to
CSV. Single searches automatically navigate the map to returned locations.

The tool also supports full CSV batch processing. The add-in reads the API's
current maximum request size and automatically divides the input into valid
requests; the API batch size does not limit the total number of CSV rows.

Coordinate CSV format:

```csv
sourcekey,latitude,longitude
1,36.660,-80.924
```

Address CSV format:

```csv
sourcekey,address
1,"1600 Pennsylvania Avenue NW, Washington, DC"
```

The interface previews up to 500 batch results for responsiveness. **Save CSV**
exports the complete result set, including unmatched source keys.

## Uninstallation

Close ArcGIS Pro and remove **CQA Fabric for ArcGIS Pro** from the ArcGIS Pro
Add-In Manager, then restart ArcGIS Pro.

## Development

The add-in targets ArcGIS Pro 3.7 and .NET 10. Building requires the .NET 10 SDK,
ArcGIS Pro 3.7 SDK assemblies, and preferably Visual Studio 2022 with the .NET
desktop development workload.

Run:

```powershell
.\build.ps1
```

The packaged add-in is created at `artifacts\CqaFabricPro.esriAddInX`.

## Support

For API access or CostQuest service questions, contact
[APISupport@costquest.com](mailto:APISupport@costquest.com).

