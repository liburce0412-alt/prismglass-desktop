using System;
using System.Collections.Generic;

// In-process presentation events; never launch applications or read notification contents.
internal static class IslandEvents {
 internal sealed class Notice {public long sequence,expiresAt;public string kind,name,icon,key;}
 static readonly object gate=new object();static long sequence;sealed class Launch {public Notice notice;public HashSet<long> previous;}
 static readonly Dictionary<string,Launch> launches=new Dictionary<string,Launch>(StringComparer.OrdinalIgnoreCase);
 public static event Action<Notice> Changed;
 static readonly HashSet<string> attention=new HashSet<string>();sealed class Activation { public long sequence,until;public Action invoke; }
 static readonly Dictionary<string,Activation> actions=new Dictionary<string,Activation>();
 public static void ObserveAttention(string key,bool active,string name,string icon,Action activate){Notice notice;lock(gate){if(!active){attention.Remove(key);return;}if(attention.Contains(key))return;if(attention.Count>=64)attention.Clear();attention.Add(key);notice=new Notice{sequence=++sequence,key=key,name=name??"应用",icon=icon??"",kind="attention",expiresAt=Now()+6000};foreach(var old in new List<string>(actions.Keys))if(actions[old].until<=Now())actions.Remove(old);
 if(actions.Count>=8&&!actions.ContainsKey(key)){string oldest=null;foreach(var item in actions)if(oldest==null||item.Value.sequence<actions[oldest].sequence)oldest=item.Key;actions.Remove(oldest);}
 actions[key]=new Activation{sequence=notice.sequence,until=notice.expiresAt,invoke=activate};}Notify(notice);}
 public static void Open(long noticeSequence){Action action=null;lock(gate){string found=null;foreach(var item in actions)if(item.Value.sequence==noticeSequence){found=item.Key;if(item.Value.until>Now())action=item.Value.invoke;break;}if(found!=null)actions.Remove(found);}if(action!=null)action();}
 static long Now(){return (long)(DateTime.UtcNow-new DateTime(1970,1,1)).TotalMilliseconds;}
 static long publishedSequence;
 static void Notify(Notice notice){lock(gate){if(notice.sequence<=publishedSequence)return;publishedSequence=notice.sequence;}var handlers=Changed;if(handlers==null)return;foreach(Action<Notice> handler in handlers.GetInvocationList())try{handler(notice);}catch{} }
 public static void BeginLaunch(string key,string name,string icon,long[] existing){Notice notice;lock(gate){notice=new Notice{sequence=++sequence,key=key,name=name??"应用",icon=icon??"",kind="launching",expiresAt=Now()+10000};foreach(var old in new List<string>(launches.Keys))if(launches[old].notice.expiresAt<=Now())launches.Remove(old);
 if(launches.Count>=8&&!launches.ContainsKey(key)){string oldest=null;foreach(var item in launches)if(oldest==null||item.Value.notice.sequence<launches[oldest].notice.sequence)oldest=item.Key;launches.Remove(oldest);}
 launches[key]=new Launch{notice=notice,previous=new HashSet<long>(existing??new long[0])};}Notify(notice);}
 public static string DockLaunchState(string id,string path){lock(gate){Launch launch;if((id!=null&&launches.TryGetValue(id,out launch)||path!=null&&launches.TryGetValue(path,out launch))&&launch.notice.expiresAt>Now())return launch.notice.kind;return "";}}
 public static void ObserveWindow(string key,long handle){Notice notice;lock(gate){Launch launch;if(key==null||!launches.TryGetValue(key,out launch)||launch.notice.kind!="launching"||launch.notice.expiresAt<Now()||launch.previous.Contains(handle))return;var current=launch.notice;launches.Remove(key);notice=new Notice{sequence=++sequence,key=key,name=current.name,icon=current.icon,kind="launched",expiresAt=Now()+2200};}Notify(notice);}
 public static void LaunchFailed(string key){Notice notice;lock(gate){Launch launch;if(key==null||!launches.TryGetValue(key,out launch)||launch.notice.kind!="launching")return;var current=launch.notice;if(current.expiresAt<=Now())return;notice=new Notice{sequence=++sequence,key=key,name=current.name,icon=current.icon,kind="launch-failed",expiresAt=Now()+4500};launch.notice=notice;}Notify(notice);}
}
