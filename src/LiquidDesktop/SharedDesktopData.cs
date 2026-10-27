using System;
using System.Threading.Tasks;
internal sealed class SharedDesktopData : IDisposable {
 readonly object dataGate=new object(),widgetsGate=new object(),quotaGate=new object();readonly SideDockData source;public readonly WidgetBackend Widgets;public readonly QuotaReader Quota;object data,widgets;DateTime dataAt=DateTime.MinValue,widgetsAt=DateTime.MinValue,quotaAt=DateTime.MinValue;Task<QuotaSnapshot> quotaTask;QuotaSnapshot quota;
 public SharedDesktopData(string root){source=new SideDockData(root);Widgets=new WidgetBackend(root);Quota=new QuotaReader(root);quota=Quota.Cached();}
 public object ReadWidgets(){lock(widgetsGate){if(widgets==null||(DateTime.UtcNow-widgetsAt).TotalMilliseconds>=900){widgets=Widgets.Read();widgetsAt=DateTime.UtcNow;}return widgets;}}
 public object ReadData(){lock(dataGate){if(data==null||(DateTime.UtcNow-dataAt).TotalSeconds>=4.5){data=source.Read();dataAt=DateTime.UtcNow;}return data;}}
 public Task<QuotaSnapshot> ReadQuota(bool manual){lock(quotaGate){if(quotaTask!=null&&!quotaTask.IsCompleted)return quotaTask;if((DateTime.UtcNow-quotaAt).TotalSeconds<(manual?15:180))return Task.FromResult(quota);quotaAt=DateTime.UtcNow;quotaTask=ReadQuotaCore();return quotaTask;}}
 async Task<QuotaSnapshot> ReadQuotaCore(){var result=await Quota.ReadAsync();lock(quotaGate){quota=result;}return result;}
 public void Invalidate(){lock(widgetsGate){widgetsAt=DateTime.MinValue;}}
 public void Dispose(){Quota.Dispose();}
}
