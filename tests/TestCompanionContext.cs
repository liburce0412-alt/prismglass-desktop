using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

// Every foreground read and dispatch is injected. Never call native Read, Send or SendInput.
internal static class TestCompanionContext {
 static int checks;static readonly JavaScriptSerializer json=new JavaScriptSerializer();
 static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
 static Dictionary<string,object> Map(object value){return json.Deserialize<Dictionary<string,object>>(json.Serialize(value));}
 static CompanionContext.Foreground Window(string name,int handle=101,int process=701,long started=638000000000000000){return new CompanionContext.Foreground{window=new IntPtr(handle),process=process,started=started,name=name};}
 static string Id(CompanionContext context){return (string)Map(context.Snapshot())["id"];}
 static string[] Actions(Dictionary<string,object> snapshot){return ((System.Collections.IList)snapshot["actions"]).Cast<Dictionary<string,object>>().Select(action=>(string)action["id"]).ToArray();}
 static void Throws<T>(Action action,string label) where T:Exception {bool rejected=false;try{action();}catch(T){rejected=true;}Check(rejected,label);}
 static void IdentityChecks(){
  var current=Window("chrome");int sends=0;var context=new CompanionContext(()=>current,(target,keys)=>{sends++;return true;});var snapshot=Map(context.Snapshot());string first=(string)snapshot["id"];
  Check(Regex.IsMatch(first,"\\A[a-f0-9]{32}\\z"),"snapshot identity is an opaque random token");Check(Id(context)==first,"unchanged foreground keeps the action identity stable");
  Check(new HashSet<string>(snapshot.Keys).SetEquals(new[]{"id","scene","appName","available","actions"}),"snapshot exposes only the documented context fields");
  string serialized=json.Serialize(snapshot);Check(!serialized.Contains("window")&&!serialized.Contains("process")&&!serialized.Contains("started")&&!serialized.Contains("title")&&!serialized.Contains("path")&&!serialized.Contains("638000000000000000"),"snapshot does not disclose HWND, PID, process start, window title or path");
  foreach(var action in (System.Collections.IList)snapshot["actions"])Check(new HashSet<string>(((Dictionary<string,object>)action).Keys).SetEquals(new[]{"id","label"}),"remote action exposes only a fixed ID and label");
  Throws<CompanionContextChangedException>(()=>context.Execute(null,"back"),"missing context identity cannot dispatch");Throws<CompanionContextChangedException>(()=>context.Execute("forged-context","back"),"forged context identity cannot dispatch");
  current=Window("chrome",handle:102);Throws<CompanionContextChangedException>(()=>context.Execute(first,"back"),"changed foreground window rejects an old action");string second=Id(context);Check(second!=first,"foreground window change rotates its identity");
  current=Window("chrome",handle:102,process:702);Throws<CompanionContextChangedException>(()=>context.Execute(second,"back"),"reused window with another PID rejects an old action");string third=Id(context);Check(third!=second,"PID change rotates its identity");
  current=Window("chrome",handle:102,process:702,started:638000000000000001);Throws<CompanionContextChangedException>(()=>context.Execute(third,"back"),"reused PID with another process start rejects an old action");string fourth=Id(context);Check(fourth!=third,"process restart rotates its identity");
  current=Window("msedge",handle:102,process:702,started:638000000000000001);Throws<CompanionContextChangedException>(()=>context.Execute(fourth,"back"),"changed process classification rejects an old action");string fifth=Id(context);Check(fifth!=fourth,"process-name change rotates its identity");
  current=null;Throws<CompanionContextChangedException>(()=>context.Execute(fifth,"back"),"missing foreground rejects a previous action");snapshot=Map(context.Snapshot());Check((string)snapshot["id"]==""&&!(bool)snapshot["available"]&&Actions(snapshot).Length==0,"missing foreground publishes no actionable identity");Check(sends==0,"all stale and forged identities avoid the dispatch delegate");
  current=Window("chrome");Check(Id(context)!=first,"returning from no foreground creates a new action identity");
 }
 static void AllowlistChecks(){
  string[] browser={"back","forward","reload","new-tab","previous-tab","next-tab"},editor={"palette","quick-open","find","toggle-sidebar"};
  foreach(string name in new[]{"chrome","msedge","code","cloudmusic","qqmusic","spotify","PRIVATE_UNSUPPORTED_PROCESS"}){
   var current=Window(name);int sends=0;var context=new CompanionContext(()=>current,(target,keys)=>{sends++;return true;});var snapshot=Map(context.Snapshot());string[] expected=name=="chrome"||name=="msedge"?browser:name=="code"?editor:new string[0];Check(Actions(snapshot).SequenceEqual(expected),"exact command allowlist for "+name);
   string id=(string)snapshot["id"];Throws<ArgumentException>(()=>context.Execute(id,"delete-file"),"unlisted command cannot execute in "+name);Check(sends==0,"unlisted command never reaches dispatch in "+name);
   if(expected.Length==0){Throws<ArgumentException>(()=>context.Execute(id,"back"),"browser shortcut cannot execute in "+name);Check(sends==0,"non-browser scene cannot dispatch browser input in "+name);}
   if(name=="PRIVATE_UNSUPPORTED_PROCESS"){string output=json.Serialize(snapshot);Check((string)snapshot["scene"]=="general"&&(string)snapshot["appName"]=="电脑"&&!output.Contains(name),"unknown application name is replaced by the generic label");}
  }
 }
 static void DispatchChecks(){
  var chords=new Dictionary<string,ushort[]>{{"back",new ushort[]{0x12,0x25}},{"forward",new ushort[]{0x12,0x27}},{"reload",new ushort[]{0x11,0x52}},{"new-tab",new ushort[]{0x11,0x54}},{"previous-tab",new ushort[]{0x11,0x10,0x09}},{"next-tab",new ushort[]{0x11,0x09}},{"palette",new ushort[]{0x11,0x10,0x50}},{"quick-open",new ushort[]{0x11,0x50}},{"find",new ushort[]{0x11,0x46}},{"toggle-sidebar",new ushort[]{0x11,0x42}}};
  foreach(string name in new[]{"chrome","msedge","code"}){
   var current=Window(name);int calls=0;ushort[] sent=null;CompanionContext.Foreground destination=null;var context=new CompanionContext(()=>current,(target,keys)=>{calls++;destination=target;sent=(ushort[])keys.Clone();return true;});var snapshot=Map(context.Snapshot());string id=(string)snapshot["id"];
   foreach(string action in Actions(snapshot)){int before=calls;var result=Map(context.Execute(id,action));Check(calls==before+1&&Object.ReferenceEquals(destination,current)&&sent.SequenceEqual(chords[action]),name+" "+action+" dispatches its exact allowed chord once");Check(result.Count==1&&result.ContainsKey("message")&&!json.Serialize(result).Contains("window")&&!json.Serialize(result).Contains("path"),"dispatch reply contains only a public acknowledgement");}
   Throws<ArgumentException>(()=>context.Execute(id,null),"null action cannot select an input chord");
  }
  var foreground=Window("chrome");int attempts=0;var rejected=new CompanionContext(()=>foreground,(target,keys)=>{attempts++;return false;});string rejectedId=Id(rejected);Throws<InvalidOperationException>(()=>rejected.Execute(rejectedId,"reload"),"native dispatch refusal is not reported as success");Check(attempts==1,"a refused non-idempotent dispatch is not automatically repeated");
 }
 static object Field(object value,string field){return value.GetType().GetField(field,BindingFlags.Public|BindingFlags.Instance).GetValue(value);}
 static void InputChecks(){
  var type=typeof(CompanionContext);var input=type.GetNestedType("Input",BindingFlags.NonPublic);var keyboard=type.GetNestedType("Keyboard",BindingFlags.NonPublic);var mouse=type.GetNestedType("Mouse",BindingFlags.NonPublic);var union=type.GetNestedType("InputUnion",BindingFlags.NonPublic);
  Check(Marshal.SizeOf(input)==(IntPtr.Size==8?40:28)&&Marshal.OffsetOf(input,"data").ToInt32()==(IntPtr.Size==8?8:4),"native INPUT size and union offset match this Windows architecture");Check(Marshal.SizeOf(keyboard)==(IntPtr.Size==8?24:16)&&Marshal.SizeOf(mouse)==(IntPtr.Size==8?32:24),"keyboard and mouse layouts preserve native pointer alignment");Check(Marshal.OffsetOf(union,"keyboard")==IntPtr.Zero&&Marshal.OffsetOf(union,"mouse")==IntPtr.Zero,"INPUT union fields begin at the same native offset");
  var key=type.GetMethod("Key",BindingFlags.NonPublic|BindingFlags.Static);foreach(ushort code in new ushort[]{0x11,0x10,0x12,0x09,0x25,0x27,0x50})foreach(bool up in new[]{false,true}){
   object item=key.Invoke(null,new object[]{code,up}),data=Field(Field(item,"data"),"keyboard");uint expected=(up?2u:0u)|(code==0x25||code==0x27?1u:0u);Check(Convert.ToUInt32(Field(item,"type"))==1&&Convert.ToUInt16(Field(data,"key"))==code&&Convert.ToUInt16(Field(data,"scan"))==0&&Convert.ToUInt32(Field(data,"flags"))==expected&&Convert.ToUInt32(Field(data,"time"))==0&&(UIntPtr)Field(data,"extra")==UIntPtr.Zero,"native key flags and reserved fields for "+code+" "+(up?"release":"press"));
  }
  var build=type.GetMethod("BuildInputs",BindingFlags.NonPublic|BindingFlags.Static);if(build==null)throw new Exception("Pure native input sequence builder was not found; never invoke Send from this test.");
  ushort[] chord={0x11,0x10,0x09};foreach(bool releasesOnly in new[]{false,true}){
   var items=(Array)build.Invoke(null,new object[]{chord,releasesOnly});var expected=releasesOnly?new ushort[]{0x09,0x10,0x11}:new ushort[]{0x11,0x10,0x09,0x09,0x10,0x11};bool valid=items.Length==expected.Length;for(int i=0;i<items.Length&&i<expected.Length;i++){object data=Field(Field(items.GetValue(i),"data"),"keyboard");valid&=Convert.ToUInt16(Field(data,"key"))==expected[i]&&Convert.ToUInt32(Field(data,"flags"))==(releasesOnly||i>=chord.Length?2u:0u);}Check(valid,releasesOnly?"partial-send cleanup releases the chord in reverse order":"single native batch presses then releases the full chord in reverse order");
  }
 }
 static void DesktopChecks(){
  var foreground=Window("PRIVATE_APP");ushort[] sent=null;int calls=0;var context=new CompanionContext(()=>foreground,(target,keys)=>{calls++;sent=keys;return true;});string id=Id(context);
  var expected=new Dictionary<string,ushort[]>{{"next-window",new ushort[]{0x12,0x09}},{"task-view",new ushort[]{0x5B,0x09}},{"show-desktop",new ushort[]{0x5B,0x44}},{"maximize",new ushort[]{0x5B,0x26}},{"restore",new ushort[]{0x5B,0x28}},{"previous-desktop",new ushort[]{0x5B,0x11,0x25}},{"next-desktop",new ushort[]{0x5B,0x11,0x27}}};
  foreach(var action in expected){context.ExecuteGlobal(id,action.Key);Check(sent.SequenceEqual(action.Value),"desktop command dispatches fixed chord "+action.Key);}
  Throws<ArgumentException>(()=>context.ExecuteGlobal(id,"shutdown"),"desktop commands cannot expand to power operations");
  foreground=Window("PRIVATE_APP",handle:109);Throws<CompanionContextChangedException>(()=>context.ExecuteGlobal(id,"next-window"),"desktop command rejects stale foreground identity");Check(calls==expected.Count,"rejected desktop commands never inject input");
 }
 static int Main(){try{IdentityChecks();AllowlistChecks();DispatchChecks();InputChecks();DesktopChecks();Console.WriteLine("Passed "+checks+" companion context checks without native input.");return 0;}catch(Exception error){Console.WriteLine("FAIL "+error);return 1;}}
}
