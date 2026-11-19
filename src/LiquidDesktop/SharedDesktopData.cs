using System;
using System.Threading.Tasks;
internal sealed class SharedDesktopData : IDisposable {
 readonly object dataGate=new object(),widgetsGate=new object(),quotaGate=new object();readonly SideDockData source;public readonly WidgetBackend Widgets;public readonly QuotaReader Quota;object data,widgets;DateTime dataAt=DateTime.MinValue,widgetsAt=DateTime.MinValue,quotaAt=DateTime.MinValue;Task<QuotaSnapshot> quotaTask;QuotaSnapshot quota;
 readonly object workGate=new object();Task workTask;volatile bool disposed;
 public SharedDesktopData(string root){source=new SideDockData(root);Widgets=new WidgetBackend(root);Quota=new QuotaReader(root);quota=Quota.Cached();}
 public object ReadWidgets(){lock(widgetsGate){if(disposed)throw new ObjectDisposedException("SharedDesktopData");if(widgets==null||(DateTime.UtcNow-widgetsAt).TotalMilliseconds>=900){widgets=Widgets.Read();widgetsAt=DateTime.UtcNow;}return widgets;}}
 public void SyncWorkWidgets(){lock(workGate){if(disposed||workTask!=null&&!workTask.IsCompleted)return;workTask=Task.Run(()=>{try{lock(widgetsGate){if(disposed)return;var snapshot=ReadWidgets();if(!disposed)Widgets.SyncWorkWidgets(snapshot);}}catch{}});}}
 public object ReadData(){lock(dataGate){if(data==null||(DateTime.UtcNow-dataAt).TotalSeconds>=4.5){data=source.Read();dataAt=DateTime.UtcNow;}return data;}}
 public Task<QuotaSnapshot> ReadQuota(bool manual){lock(quotaGate){if(quotaTask!=null&&!quotaTask.IsCompleted)return quotaTask;if((DateTime.UtcNow-quotaAt).TotalSeconds<(manual?15:180))return Task.FromResult(quota);quotaAt=DateTime.UtcNow;quotaTask=ReadQuotaCore();return quotaTask;}}
 async Task<QuotaSnapshot> ReadQuotaCore(){var result=await Quota.ReadAsync();lock(quotaGate){quota=result;}return result;}
 public void Invalidate(){lock(widgetsGate){widgetsAt=DateTime.MinValue;}}
 public void Dispose(){Task pending;lock(workGate){if(disposed)return;disposed=true;pending=workTask;}if(pending!=null&&!pending.IsCompleted)pending.ContinueWith(_=>{lock(widgetsGate)Widgets.Dispose();},TaskScheduler.Default);else lock(widgetsGate)Widgets.Dispose();Quota.Dispose();}
}
