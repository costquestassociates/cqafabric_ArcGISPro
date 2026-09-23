using System.Text.Json.Nodes;
using System.Windows;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using CqaFabricPro.Services;

namespace CqaFabricPro;

internal sealed class IdentifyFabricTool : MapTool
{
    public IdentifyFabricTool() { IsSketchTool=true; SketchType=SketchGeometryType.Point; SketchOutputMode=SketchOutputMode.Map; }

    protected override async Task<bool> OnSketchCompleteAsync(Geometry geometry)
    {
        var context=IdentifySession.Current; if(context is null || geometry is not MapPoint point) return false;
        try
        {
            var wgs=await QueuedTask.Run(()=>GeometryEngine.Instance.Project(point,SpatialReferences.WGS84) as MapPoint);
            if(wgs is null)return false;
            var ids=await MvtIdentifyService.FindAsync(context,wgs.X,wgs.Y);
            if(ids.Count==0){ ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show("No Fabric feature was found at that location.","CQA Fabric"); return true; }
            using var api=new CostQuestApiClient(context.ApiKey);
            var fields=await api.GetFieldsAsync(context.Vintage,context.Layer);
            var aliases=fields.OfType<JsonObject>().Where(o=>o["fieldname"] is not null).ToDictionary(o=>o["fieldname"]!.ToString(),o=>o["displayname"]?.ToString() ?? o["fieldname"]!.ToString(),StringComparer.OrdinalIgnoreCase);
            var records=new List<JsonObject>(); foreach(var id in ids.Take(20)){var node=await api.GetIndividualDataAsync(context.Vintage,context.Layer,id); if(node is JsonArray a) records.AddRange(a.OfType<JsonObject>()); else if(node is JsonObject o) records.Add(o);}
            await Application.Current.Dispatcher.InvokeAsync(()=>new FabricPopupWindow(context,records,aliases).Show());
        }
        catch(Exception ex){ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(ex.Message,"CQA Fabric Identify");}
        return true;
    }
}
